#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.Parser;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services;

public static class InteriorPatchBuilder
{
    public const string MountPath = "x64/data/cdimages/moviesubs.rpf";

    public sealed record Result(
        bool Success,
        string? ErrorMessage,
        string? PatchDirectory,
        IReadOnlyList<string> Changed,
        IReadOnlyList<string> Skipped);

    public static Result Build(string gtaRoot, string workDir,
                               IReadOnlyList<string>? ytyps = null,
                               string? namesCachePath = null)
    {
        var changed = new List<string>();
        var skipped = new List<string>();
        var wanted = ytyps ?? InteriorClutterDetector.DefaultYtyps;

        Directory.CreateDirectory(workDir);
        var filesDir = Path.Combine(workDir, "patch_files");
        var actions = new List<PatchAction>();

        foreach (var name in wanted)
        {
            var vanilla = FindVanilla(gtaRoot, name);
            if (vanilla is null) { skipped.Add($"{name}: ванильного файла нет в архивах игры"); continue; }

            var hashes = InteriorClutterStripper.HashesOf(vanilla);
            if (hashes.Count == 0) { skipped.Add($"{name}: интерьер пуст либо не разобрался"); continue; }

            var names = InteriorNameResolver.Resolve(gtaRoot, hashes, namesCachePath);
            var strip = InteriorClutterStripper.Strip(vanilla, names);
            if (strip.Data is null) { skipped.Add($"{name}: {strip.ErrorMessage}"); continue; }

            var target = MountPath + "/" + name;
            var dest = Path.Combine(filesDir, target.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, strip.Data);

            actions.Add(new PatchAction
            {
                Type = ActionType.Import,
                TargetPath = target,
                SourcePath = "patch_files/" + target,
                Size = strip.Data.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(strip.Data)).ToLowerInvariant(),
                IsWholeReplaceNestedRpf = false,
            });

            var resolved = hashes.Count(h => names.ContainsKey(h));
            changed.Add($"{name}: объектов {strip.Before} -> {strip.After}, " +
                        $"имён развёрнуто {resolved}/{hashes.Count}");
        }

        if (actions.Count == 0)
            return new Result(true, null, null, changed, skipped);

        var manifest = new DiffManifest
        {
            ReduxName = "interiors",
            ParsedAt = DateTime.UtcNow,
            TotalPatchSize = actions.Sum(a => a.Size),
            Actions = actions,
        };
        File.WriteAllText(
            Path.Combine(workDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            }));

        return new Result(true, null, workDir, changed, skipped);
    }

    private static byte[]? FindVanilla(string gtaRoot, string name)
    {
        foreach (var archive in Directory.EnumerateFiles(gtaRoot, "x64*.rpf")
                                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var arc = RageArchiveWrapper7.OpenRead(archive);
                var found = Search(arc.Root, name, 3);
                if (found is not null) return found;
            }
            catch {}
        }
        return null;
    }

    private static byte[]? Search(IArchiveDirectory dir, string name, int depth)
    {
        foreach (var f in dir.GetFiles())
        {
            if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return Read(f);
            if (depth > 0 && f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = Read(f);
                if (bytes is null) continue;
                try
                {
                    using var ms = new MemoryStream(bytes, writable: false);
                    using var nested = RageArchiveWrapper7.Open(ms, f.Name, leaveOpen: true);
                    var found = Search(nested.Root, name, depth - 1);
                    if (found is not null) return found;
                }
                catch { }
            }
        }
        foreach (var d in dir.GetDirectories())
        {
            var found = Search(d, name, depth);
            if (found is not null) return found;
        }
        return null;
    }

    private static byte[]? Read(IArchiveFile f)
    {
        try
        {
            using var ms = new MemoryStream();
            if (f is IArchiveBinaryFile b) b.Export(ms);
            else if (f is IArchiveResourceFile r) r.Export(ms);
            else return null;
            return ms.ToArray();
        }
        catch { return null; }
    }
}
