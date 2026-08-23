#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeWalker.GameFiles;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services;

public static class InteriorNameResolver
{
    public static IReadOnlyDictionary<uint, string> Resolve(
        string gtaRoot, IReadOnlyCollection<uint> hashes, string? cachePath = null)
    {
        var known = LoadCache(cachePath);
        var need = new HashSet<uint>(hashes.Where(h => !known.ContainsKey(h)));
        if (need.Count == 0) return known;

        var found = new Dictionary<uint, string>();
        foreach (var archive in Directory.EnumerateFiles(gtaRoot, "x64*.rpf")
                                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var arc = RageArchiveWrapper7.OpenRead(archive);
                Walk(arc.Root, need, found, 2);
            }
            catch
            {
            }
            if (found.Count == need.Count) break;
        }

        if (found.Count > 0)
        {
            foreach (var (h, n) in found) known[h] = n;
            SaveCache(cachePath, known);
        }
        return known;
    }

    private static void Walk(IArchiveDirectory dir, HashSet<uint> need,
                             Dictionary<uint, string> found, int depth)
    {
        foreach (var f in dir.GetFiles())
        {
            var stem = Path.GetFileNameWithoutExtension(f.Name);
            if (stem.Length > 0)
            {
                var lower = stem.ToLowerInvariant();
                var h = JenkHash.GenHash(lower);
                if (need.Contains(h) && !found.ContainsKey(h)) found[h] = lower;
            }

            if (depth > 0 && f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = Read(f);
                if (bytes is null) continue;
                try
                {
                    using var ms = new MemoryStream(bytes, writable: false);
                    using var nested = RageArchiveWrapper7.Open(ms, f.Name, leaveOpen: true);
                    Walk(nested.Root, need, found, depth - 1);
                }
                catch {}
            }
        }
        foreach (var d in dir.GetDirectories()) Walk(d, need, found, depth);
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

    private static Dictionary<uint, string> LoadCache(string? path)
    {
        var map = new Dictionary<uint, string>();
        if (path is null || !File.Exists(path)) return map;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var sep = line.IndexOf(' ');
                if (sep <= 0) continue;
                if (uint.TryParse(line.AsSpan(0, sep), out var h))
                    map[h] = line[(sep + 1)..];
            }
        }
        catch { map.Clear(); }
        return map;
    }

    private static void SaveCache(string? path, Dictionary<uint, string> map)
    {
        if (path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, map.Select(kv => kv.Key + " " + kv.Value));
            File.Move(tmp, path, overwrite: true);
        }
        catch {}
    }
}
