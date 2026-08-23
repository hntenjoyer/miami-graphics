using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace MiamiGraphics.DlcLab;

public static class Snapshotter
{
    private sealed class Entry
    {
        public long Size { get; set; }
        public long Mtime { get; set; }
        public string? Sha { get; set; }
    }

    private sealed class Snap
    {
        public DateTime TakenUtc { get; set; }
        public List<string> Roots { get; set; } = new();
        public long HashCapBytes { get; set; }
        public Dictionary<string, Entry> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static IEnumerable<string> CommonRoots()
    {
        string? Env(string v) => Environment.GetEnvironmentVariable(v);
        var cands = new[]
        {
            Env("LOCALAPPDATA"),
            Env("APPDATA"),
            Env("PROGRAMDATA"),
            Env("TEMP"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Env("PUBLIC") is { } pub ? Path.Combine(pub, "Documents") : null,
        };
        return cands.Where(c => !string.IsNullOrEmpty(c) && Directory.Exists(c))!.Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static void Take(string outJson, IReadOnlyList<string> roots, long hashCapBytes)
    {
        var snap = new Snap { Roots = roots.ToList(), HashCapBytes = hashCapBytes };
        var sw = Stopwatch.StartNew();
        int count = 0, hashed = 0;
        string outFull = Path.GetFullPath(outJson);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) { Console.WriteLine($"[snapshot] skip (missing): {root}"); continue; }
            Console.WriteLine($"[snapshot] scanning {root} …");
            foreach (var file in SafeWalk(root))
            {
                if (string.Equals(file, outFull, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var fi = new FileInfo(file);
                    var e = new Entry { Size = fi.Length, Mtime = fi.LastWriteTimeUtc.Ticks };
                    if (fi.Length <= hashCapBytes)
                    {
                        e.Sha = TrySha(file);
                        if (e.Sha != null) hashed++;
                    }
                    snap.Files[fi.FullName] = e;
                    if (++count % 5000 == 0) Console.WriteLine($"  … {count} files ({sw.Elapsed.TotalSeconds:F0}s)");
                }
                catch {}
            }
        }

        File.WriteAllText(outJson, JsonSerializer.Serialize(snap, Json));
        Console.WriteLine($"[snapshot] {count} files ({hashed} hashed ≤{hashCapBytes / 1024 / 1024}MB) in {sw.Elapsed.TotalSeconds:F1}s -> {outJson} ({new FileInfo(outJson).Length / 1024}KB)");
    }

    public static void Track(string substr, IReadOnlyList<string> snapJsons)
    {
        var snaps = snapJsons.Select(j => (name: Path.GetFileNameWithoutExtension(j),
                                           snap: JsonSerializer.Deserialize<Snap>(File.ReadAllText(j))!)).ToList();

        var paths = snaps.SelectMany(s => s.snap.Files.Keys)
            .Where(p => p.Contains(substr, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"[track] '{substr}': {paths.Count} matching path(s) across {snaps.Count} snapshot(s)\n");
        foreach (var p in paths)
        {
            Console.WriteLine(p);
            foreach (var (name, snap) in snaps)
            {
                if (snap.Files.TryGetValue(p, out var e))
                {
                    var t = new DateTime(e.Mtime, DateTimeKind.Utc).ToLocalTime();
                    Console.WriteLine($"    {name,-12} size={e.Size,13:N0}  mtime={t:yyyy-MM-dd HH:mm:ss}  sha={(e.Sha ?? "(none)")[..Math.Min(12, (e.Sha ?? "(none)").Length)]}");
                }
                else
                {
                    Console.WriteLine($"    {name,-12} (absent)");
                }
            }
            Console.WriteLine();
        }
    }

    public static void Diff(string beforeJson, string afterJson, string? outJson)
    {
        var a = JsonSerializer.Deserialize<Snap>(File.ReadAllText(beforeJson)) ?? throw new("bad before json");
        var b = JsonSerializer.Deserialize<Snap>(File.ReadAllText(afterJson)) ?? throw new("bad after json");

        var added = new List<string>();
        var modified = new List<string>();
        var removed = new List<string>();

        foreach (var (path, be) in b.Files)
        {
            if (!a.Files.TryGetValue(path, out var ae)) { added.Add(path); continue; }
            bool changed = ae.Size != be.Size || ae.Mtime != be.Mtime
                        || (ae.Sha != null && be.Sha != null && ae.Sha != be.Sha);
            if (changed) modified.Add(path);
        }
        foreach (var path in a.Files.Keys)
            if (!b.Files.ContainsKey(path)) removed.Add(path);

        added.Sort(StringComparer.OrdinalIgnoreCase);
        modified.Sort(StringComparer.OrdinalIgnoreCase);
        removed.Sort(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"\n════ DIFF ════  +{added.Count} added   ~{modified.Count} modified   -{removed.Count} removed\n");
        Report("ADDED", added, b);
        Report("MODIFIED", modified, b, a);
        Report("REMOVED", removed, a);

        if (outJson != null)
        {
            var result = new { added, modified, removed };
            File.WriteAllText(outJson, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"\n[diff] full lists saved -> {outJson}");
        }
    }

    private static void Report(string title, List<string> paths, Snap primary, Snap? other = null)
    {
        if (paths.Count == 0) return;
        Console.WriteLine($"── {title} ({paths.Count}) ──");
        foreach (var p in paths.Take(200))
        {
            if (title == "MODIFIED" && other != null && primary.Files.TryGetValue(p, out var np) && other.Files.TryGetValue(p, out var op))
                Console.WriteLine($"  {p}   ({op.Size:N0} -> {np.Size:N0} bytes)");
            else if (primary.Files.TryGetValue(p, out var e))
                Console.WriteLine($"  {p}   ({e.Size:N0} bytes)");
            else
                Console.WriteLine($"  {p}");
        }
        if (paths.Count > 200) Console.WriteLine($"  … +{paths.Count - 200} more (see -o json)");
        Console.WriteLine();
    }

    private static string? TrySha(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(fs));
        }
        catch { return null; }
    }

    private static IEnumerable<string> SafeWalk(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            string[] subDirs = Array.Empty<string>();
            try { subDirs = Directory.GetDirectories(dir); } catch { }
            foreach (var d in subDirs) stack.Push(d);

            string[] files = Array.Empty<string>();
            try { files = Directory.GetFiles(dir); } catch { }
            foreach (var f in files) yield return f;
        }
    }
}
