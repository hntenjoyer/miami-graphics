#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeWalker.GameFiles;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services;

public static class InteriorClutterDetector
{
    public enum State
    {
        Vanilla,
        Stripped,
        Custom,
        Unknown,
    }

    public sealed record Finding(
        string Ytyp,
        string Source,
        int Objects,
        int VanillaObjects,
        bool SameAsVanilla);

    public sealed record Report(
        State State,
        IReadOnlyList<Finding> Findings,
        string? Note = null);

    private sealed class Cache
    {
        public Dictionary<string, VanillaEntry> Vanilla { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SourceEntry> Sources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class VanillaEntry
    {
        public int Objects { get; set; }
        public string Sha { get; set; } = "";
    }

    private sealed class SourceEntry
    {
        public string Stamp { get; set; } = "";
        public List<Hit> Hits { get; set; } = new();
    }

    private sealed class Hit
    {
        public string Ytyp { get; set; } = "";
        public int Objects { get; set; }
        public string Sha { get; set; } = "";
    }

    private const double StrippedBelow = 0.90;

    public static readonly IReadOnlyList<string> DefaultYtyps = new[]
    {
        "v_int_10.ytyp",
        "v_int_51.ytyp",
        "v_int_66.ytyp",
    };

    public static readonly IReadOnlyList<string> DefaultModels = new[]
    {
        "v_10_liquorstore.ydr",
        "prop_bar_beerfridge_01.ydr",
    };

    public static Report Detect(string gtaRoot, IReadOnlyList<string>? ytyps = null,
                                string? cachePath = null, IReadOnlyList<string>? models = null)
    {
        var wanted = (ytyps ?? DefaultYtyps)
            .Concat(models ?? DefaultModels)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var cache = LoadCache(cachePath);
        var dirty = false;

        var vanilla = wanted.All(cache.Vanilla.ContainsKey)
            ? cache.Vanilla
            : null;
        if (vanilla is null)
        {
            vanilla = ReadVanilla(gtaRoot, wanted);
            if (vanilla.Count > 0) { cache.Vanilla = vanilla; dirty = true; }
        }
        if (vanilla.Count == 0)
            return new Report(State.Unknown, Array.Empty<Finding>(),
                              "ванильные интерьеры не прочитаны - сравнивать не с чем");

        var sources = OverrideSources(gtaRoot).ToList();
        var perSource = new SourceEntry?[sources.Count];
        Parallel.For(0, sources.Count, i =>
        {
            var (_, path) = sources[i];
            var stamp = Stamp(path);
            if (stamp is null) return;
            lock (cache)
                if (cache.Sources.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                { perSource[i] = cached; return; }

            var entry = new SourceEntry { Stamp = stamp };
            foreach (var (name, bytes) in FindAll(path, wanted))
            {
                var n = IsObjectList(name) ? CountObjects(bytes) : 0;
                if (n < 0) continue;
                entry.Hits.Add(new Hit { Ytyp = name, Objects = n, Sha = Sha(bytes) });
            }
            perSource[i] = entry;
            lock (cache) { cache.Sources[path] = entry; dirty = true; }
        });

        var findings = new List<Finding>();
        for (var i = 0; i < sources.Count; i++)
        {
            if (perSource[i] is not { } entry) continue;
            foreach (var h in entry.Hits)
            {
                if (!vanilla.TryGetValue(h.Ytyp, out var van)) continue;
                findings.Add(new Finding(h.Ytyp, sources[i].Source, h.Objects, van.Objects,
                                         string.Equals(h.Sha, van.Sha, StringComparison.Ordinal)));
            }
        }

        if (dirty) SaveCache(cachePath, cache);

        return Classify(findings);
    }

    internal static Report Classify(IReadOnlyList<Finding> findings)
    {
        if (findings.Count == 0)
            return new Report(State.Vanilla, findings, "перекрытий интерьеров нет");

        if (findings.Any(f => f.VanillaObjects > 0 && f.Objects < f.VanillaObjects * StrippedBelow))
            return new Report(State.Stripped, findings);

        if (findings.All(f => f.SameAsVanilla))
            return new Report(State.Vanilla, findings, "перекрытие есть, но файл ванильный");

        return new Report(State.Custom, findings);
    }

    private static Dictionary<string, VanillaEntry> ReadVanilla(
        string gtaRoot, HashSet<string> wanted)
    {
        var result = new Dictionary<string, VanillaEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(gtaRoot, "x64*.rpf")
                                   .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var (name, bytes) in FindAll(f, wanted))
            {
                if (result.ContainsKey(name)) continue;
                var n = IsObjectList(name) ? CountObjects(bytes) : 0;
                if (n >= 0) result[name] = new VanillaEntry { Objects = n, Sha = Sha(bytes) };
            }
            if (result.Count == wanted.Count) break;
        }
        return result;
    }

    private static IEnumerable<(string Source, string Path)> OverrideSources(string gtaRoot)
    {
        var update = Path.Combine(gtaRoot, "update", "update.rpf");
        if (File.Exists(update)) yield return ("update.rpf", update);

        var packs = Path.Combine(gtaRoot, "update", "x64", "dlcpacks");
        if (!Directory.Exists(packs)) yield break;
        foreach (var dir in Directory.EnumerateDirectories(packs))
            foreach (var rpf in Directory.EnumerateFiles(dir, "*.rpf"))
                yield return ("dlcpacks/" + Path.GetFileName(dir) + "/" + Path.GetFileName(rpf), rpf);
    }

    private static List<(string Name, byte[] Bytes)> FindAll(string archivePath, HashSet<string> wanted)
    {
        var found = new List<(string, byte[])>();
        try
        {
            using var arc = OpenMaybeObfuscated(archivePath);
            Walk(arc.Root, wanted, found, 3);
        }
        catch
        {
        }
        return found;
    }

    private static void Walk(IArchiveDirectory dir, HashSet<string> wanted,
                             List<(string, byte[])> into, int depth)
    {
        foreach (var f in dir.GetFiles())
        {
            if (wanted.Contains(f.Name))
            {
                var b = Read(f);
                if (b is not null) into.Add((f.Name, b));
            }

            if (depth > 0 && f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                var b = Read(f);
                if (b is null) continue;
                try
                {
                    Deobfuscate(b);
                    using var ms = new MemoryStream(b, writable: false);
                    using var nested = RageArchiveWrapper7.Open(ms, f.Name, leaveOpen: true);
                    Walk(nested.Root, wanted, into, depth - 1);
                }
                catch {}
            }
        }
        foreach (var d in dir.GetDirectories()) Walk(d, wanted, into, depth);
    }

    private static RageArchiveWrapper7 OpenMaybeObfuscated(string path)
    {
        var head = new byte[12];
        using (var fs = File.OpenRead(path)) fs.ReadExactly(head, 0, 12);
        if ((BitConverter.ToUInt32(head, 8) & 0x80000000u) == 0)
            return RageArchiveWrapper7.OpenRead(path);

        var stream = new MemoryStream();
        using (var fs = File.OpenRead(path)) fs.CopyTo(stream);
        var buf = stream.GetBuffer();
        Deobfuscate(buf);
        stream.Position = 0;
        return RageArchiveWrapper7.Open(stream, Path.GetFileName(path));
    }

    private static void Deobfuscate(byte[] data)
    {
        if (data.Length < 12) return;
        var v = BitConverter.ToUInt32(data, 8);
        if ((v & 0x80000000u) != 0) BitConverter.GetBytes(v & 0x7FFFFFFFu).CopyTo(data, 8);
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

    private static bool IsObjectList(string name)
        => name.EndsWith(".ytyp", StringComparison.OrdinalIgnoreCase);

    internal static int CountObjects(byte[] ytyp)
    {
        try
        {
            var y = new YtypFile();
            y.Load(ytyp);
            var mlo = (y.AllArchetypes ?? Array.Empty<Archetype>()).OfType<MloArchetype>();
            return mlo.Sum(m => m.entities?.Length ?? 0);
        }
        catch { return -1; }
    }

    private static string Sha(byte[] data)
        => Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(data));

    private static string? Stamp(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? $"{fi.Length}:{fi.LastWriteTimeUtc.Ticks}" : null;
        }
        catch { return null; }
    }

    private static Cache LoadCache(string? path)
    {
        if (path is null || !File.Exists(path)) return new Cache();
        try
        {
            return global::System.Text.Json.JsonSerializer.Deserialize<Cache>(File.ReadAllText(path))
                   ?? new Cache();
        }
        catch { return new Cache(); }
    }

    private static void SaveCache(string? path, Cache cache)
    {
        if (path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, global::System.Text.Json.JsonSerializer.Serialize(cache));
            File.Move(tmp, path, overwrite: true);
        }
        catch {}
    }
}
