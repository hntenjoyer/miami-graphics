#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.Parser;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Services;

public sealed class ModTextPatchBuilder
{
    public sealed record Edit(string Key, string Value);

    public sealed record FilePlan(
        string TargetPath,
        IReadOnlyList<Edit> Edits,
        string? ReplacementText = null);

    public sealed record Result(
        bool Success,
        string? ErrorMessage,
        string? PatchDirectory,
        IReadOnlyList<string> Changed,
        IReadOnlyList<string> Skipped);

    private readonly string _gtaRoot;

    public ModTextPatchBuilder(string gtaRoot) => _gtaRoot = gtaRoot;

    public Result Build(string archiveRelPath, IReadOnlyList<FilePlan> plans, string workDir,
                        string? readFrom = null)
    {
        var changed = new List<string>();
        var skipped = new List<string>();
        var actions = new List<PatchAction>();

        var archivePath = readFrom ?? Path.Combine(_gtaRoot, archiveRelPath);
        if (!File.Exists(archivePath))
            return new Result(false, $"архив не найден: {archivePath}", null, changed, skipped);

        Directory.CreateDirectory(workDir);
        var filesDir = Path.Combine(workDir, "patch_files");
        Directory.CreateDirectory(filesDir);

        try
        {
            using var archive = RageArchiveWrapper7.Open(archivePath);

            foreach (var plan in plans)
            {
                var entry = FindFile(archive.Root, plan.TargetPath);
                if (entry is null)
                {
                    skipped.Add($"{plan.TargetPath}: записи нет в архиве");
                    continue;
                }

                var original = ReadBytes(entry);
                if (original is null)
                {
                    skipped.Add($"{plan.TargetPath}: не удалось прочитать");
                    continue;
                }

                byte[] patched;
                int applied;
                if (plan.ReplacementText is { } whole)
                {
                    patched = Encoding.Latin1.GetBytes(whole);
                    applied = 1;
                }
                else
                {
                    patched = ApplyEdits(original, plan.Edits, out applied, out var missing);
                    foreach (var m in missing)
                        skipped.Add($"{plan.TargetPath}: ключа '{m}' в файле нет");
                }

                if (applied == 0)
                {
                    skipped.Add($"{plan.TargetPath}: нечего менять");
                    continue;
                }

                if (patched.Length == original.Length && patched.AsSpan().SequenceEqual(original))
                {
                    skipped.Add($"{plan.TargetPath}: значения уже стоят");
                    continue;
                }

                var dest = Path.Combine(filesDir, plan.TargetPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.WriteAllBytes(dest, patched);

                actions.Add(new PatchAction
                {
                    Type = ActionType.Replace,
                    TargetPath = plan.TargetPath,
                    SourcePath = "patch_files/" + plan.TargetPath,
                    Size = patched.Length,
                    Sha256 = Sha256(patched),
                    IsWholeReplaceNestedRpf = false,
                });
                changed.Add(plan.ReplacementText is null
                    ? $"{plan.TargetPath}: {applied} {Plural(applied)}"
                    : $"{plan.TargetPath}: файл заменён целиком");
            }
        }
        catch (Exception ex)
        {
            return new Result(false, $"чтение архива: {ex.GetType().Name}: {ex.Message}", null, changed, skipped);
        }

        if (actions.Count == 0)
            return new Result(true, null, null, changed, skipped);

        var manifest = new DiffManifest
        {
            ReduxName = "optimization",
            ParsedAt = DateTime.UtcNow,
            TotalPatchSize = actions.Sum(a => a.Size),
            Actions = actions,
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        });
        File.WriteAllText(Path.Combine(workDir, "manifest.json"), json);

        return new Result(true, null, workDir, changed, skipped);
    }

    private static string Plural(int n)
    {
        var m10 = n % 10; var m100 = n % 100;
        if (m10 == 1 && m100 != 11) return "строка";
        if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return "строки";
        return "строк";
    }

    internal static byte[] ApplyEdits(byte[] original, IReadOnlyList<Edit> edits,
                                      out int applied, out List<string> missing)
    {
        var enc = Encoding.Latin1;
        var text = enc.GetString(original);
        var map = edits.ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder(text.Length + 64);
        applied = 0;
        var i = 0;
        while (i < text.Length)
        {
            var nl = text.IndexOf('\n', i);
            var end = nl < 0 ? text.Length : nl + 1;
            var line = text.Substring(i, end - i);
            i = end;

            var body = line.TrimEnd('\r', '\n');
            var eol = line.Substring(body.Length);
            var trimmed = body.TrimStart();

            if (trimmed.Length == 0 || trimmed.StartsWith("#"))
            {
                sb.Append(line);
                continue;
            }

            var sp = trimmed.IndexOfAny(new[] { ' ', '\t' });
            var key = sp < 0 ? trimmed : trimmed.Substring(0, sp);

            if (!map.TryGetValue(key, out var value))
            {
                sb.Append(line);
                continue;
            }

            var indent = body.Substring(0, body.Length - trimmed.Length);
            var sep = sp < 0 ? "\t" : trimmed.Substring(sp, trimmed.Length - sp - trimmed.Substring(sp).TrimStart().Length);
            var rest = sp < 0 ? "" : trimmed.Substring(sp + sep.Length);
            var tail = rest.Substring(rest.TrimEnd(' ', '\t').Length);
            sb.Append(indent).Append(key).Append(sep.Length > 0 ? sep : "\t")
              .Append(value).Append(tail).Append(eol);
            applied++;
            seen.Add(key);
        }

        missing = map.Keys.Where(k => !seen.Contains(k)).ToList();
        return enc.GetBytes(sb.ToString());
    }

    public static string? TryReadText(IArchiveDirectory root, string path)
    {
        var entry = FindFile(root, path);
        if (entry is null) return null;
        var bytes = ReadBytes(entry);
        return bytes is null ? null : Encoding.Latin1.GetString(bytes);
    }

    public static Dictionary<string, string> ParseKeyValues(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var sp = line.IndexOfAny(new[] { ' ', '\t' });
            if (sp < 0) continue;
            var key = line.Substring(0, sp);
            var value = line.Substring(sp).Trim();
            if (value.Length > 0) map[key] = value;
        }
        return map;
    }

    private static IArchiveFile? FindFile(IArchiveDirectory dir, string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        IArchiveDirectory? cur = dir;
        for (var i = 0; i < parts.Length - 1 && cur is not null; i++)
            cur = cur.GetDirectories().FirstOrDefault(d =>
                string.Equals(d.Name, parts[i], StringComparison.OrdinalIgnoreCase));
        return cur?.GetFiles().FirstOrDefault(f =>
            string.Equals(f.Name, parts[^1], StringComparison.OrdinalIgnoreCase));
    }

    private static byte[]? ReadBytes(IArchiveFile file)
    {
        if (file is not IArchiveBinaryFile bin) return null;
        using var ms = new MemoryStream();
        bin.Export(ms);
        var buf = ms.ToArray();

        if (bin.IsEncrypted)
        {
            var hash = GTA5Hash.CalculateHash(bin.Name);
            var keyIdx = (hash + (uint)bin.UncompressedSize + (101 - 40)) % 0x65;
            var keys = GTA5Constants.PC_NG_KEYS;
            if (keys is { Length: > 0 } && keys.Length > keyIdx)
            {
                var key = keys[keyIdx];
                if (key is { Length: > 0 }) buf = GTA5Crypto.Decrypt(buf, key);
            }
        }

        if (!bin.IsCompressed) return buf;

        try
        {
            using var src = new MemoryStream(buf);
            using var inflate = new DeflateStream(src, CompressionMode.Decompress);
            using var outMs = new MemoryStream((int)Math.Max(bin.UncompressedSize, 1024));
            inflate.CopyTo(outMs);
            return outMs.ToArray();
        }
        catch
        {
            return buf;
        }
    }

    private static string Sha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
    }
}
