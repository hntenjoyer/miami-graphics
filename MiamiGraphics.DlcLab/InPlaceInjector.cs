using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.Parser;
using RageLib.Archives;
using RageLib.GTA5.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.DlcLab;

public static class InPlaceInjector
{
    private static readonly JsonSerializerOptions JsonOpts = MakeOpts();
    private static JsonSerializerOptions MakeOpts()
    {
        var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public static void BuildStore(string manifestPath, string patchFilesDir, string outRpf)
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mg_store_build_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            File.Copy(manifestPath, Path.Combine(tmp, "manifest.json"), true);
            CopyDir(patchFilesDir, Path.Combine(tmp, "patch_files"));
            var s = Packer.Pack(tmp, outRpf);
            Console.WriteLine($"[mgstore] built {outRpf}");
            Console.WriteLine($"[mgstore]   {s.Files} files, {s.Rpfs} nested rpf, {s.Size:N0} bytes");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    public static void Apply(string containerPath, string vanillaUpdate, string outUpdate)
    {
        var sw = Stopwatch.StartNew();

        Console.WriteLine($"[apply] clean base: copy vanilla -> {Path.GetFileName(outUpdate)}");
        File.Copy(vanillaUpdate, outUpdate, true);
        var tCopy = sw.Elapsed;

        using var container = RpfUtil.OpenRpf(containerPath);
        var mfFile = RpfUtil.FindFile(container.Root, "manifest.json")
                     ?? throw new InvalidOperationException("manifest.json not found in container");
        var manifest = JsonSerializer.Deserialize<DiffManifest>(RpfUtil.ReadRealBytes(mfFile), JsonOpts)!;

        var actions = manifest.Actions
            .Where(a => RawRoot(a.TargetPath) == "update.rpf")
            .Select(a => (path: Norm(a.TargetPath), a))
            .ToList();
        int skipped = manifest.Actions.Count - actions.Count;
        Console.WriteLine($"[apply] {actions.Count} actions target update.rpf" + (skipped > 0 ? $" ({skipped} skipped: other root rpf)" : ""));

        byte[] ReadSrc(PatchAction a)
        {
            var src = a.SourcePath.Replace('\\', '/');
            var f = RpfUtil.FindFile(container.Root, src)
                    ?? throw new FileNotFoundException("source not in container: " + src);
            return RpfUtil.ReadRealBytes(f);
        }

        int nestedFixed;
        using (var arc = RpfUtil.OpenRpf(outUpdate, "update.rpf", write: true))
        {
            var stats = new Stats();
            ApplyRecursive(arc, actions, ReadSrc, stats);
            arc.archive_.Encryption = RageArchiveEncryption7.None;
            Console.WriteLine($"[apply] flushing update.rpf …");
            arc.Flush();
            nestedFixed = stats.NestedRpf;
            Console.WriteLine($"[apply] applied: {stats.Replaced} replaced, {stats.Imported} imported, {stats.Deleted} deleted, {stats.NestedRpf} nested rpf edited");
        }
        var tApply = sw.Elapsed;

        Console.WriteLine($"[apply] ArchiveFix update.rpf …");
        ArchiveFixer.Fix(outUpdate, "update.rpf");
        sw.Stop();

        Console.WriteLine($"\n[apply] DONE in {sw.Elapsed.TotalSeconds:F1}s  " +
                          $"(copy {tCopy.TotalSeconds:F1}s | apply {(tApply - tCopy).TotalSeconds:F1}s | fix {(sw.Elapsed - tApply).TotalSeconds:F1}s)");
    }

    private sealed class Stats { public int Replaced, Imported, Deleted, NestedRpf; }

    private static void ApplyRecursive(RageArchiveWrapper7 arc, List<(string path, PatchAction a)> actions,
                                       Func<PatchAction, byte[]> readSrc, Stats stats)
    {
        var direct = new List<(string path, PatchAction a)>();
        var nested = new Dictionary<string, List<(string inner, PatchAction a)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, a) in actions)
        {
            var (rpf, inner) = SplitFirstNestedRpf(path);
            if (rpf == null) direct.Add((path, a));
            else
            {
                if (!nested.TryGetValue(rpf, out var list)) { list = new(); nested[rpf] = list; }
                list.Add((inner, a));
            }
        }

        foreach (var (rpfPath, subs) in nested)
            ProcessNested(arc, rpfPath, subs, readSrc, stats);

        foreach (var (path, a) in direct)
        {
            if (a.Type == ActionType.Delete) { if (DeleteRaw(arc, path)) stats.Deleted++; }
            else { WriteFile(arc, path, readSrc(a)); if (a.Type == ActionType.Import) stats.Imported++; else stats.Replaced++; }
        }
    }

    private static void WriteFile(RageArchiveWrapper7 arc, string path, byte[] data)
    {
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var parent = segs[..^1];
        string name = segs[^1];

        var dir = NavigateOrCreateDir(arc.Root, parent);
        var rawDir = NavigateRaw(arc.archive_.Root, parent);
        if (rawDir != null)
        {
            var of = rawDir.Files.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (of != null) rawDir.Files.Remove(of);
        }

        bool isRpf = name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);
        bool isRsc7 = data.Length >= 4 && data[0] == 0x52 && data[1] == 0x53 && data[2] == 0x43;

        if (isRsc7 && !isRpf)
        {
            var f = dir.CreateResourceFile();
            f.Name = name;
            f.Import(new MemoryStream(data));
        }
        else
        {
            var f = dir.CreateBinaryFile();
            f.Name = name;
            if (!isRpf)
            {
                using var ms = new MemoryStream();
                using (var def = new DeflateStream(ms, CompressionMode.Compress, true)) def.Write(data, 0, data.Length);
                f.UncompressedSize = data.Length;
                f.IsCompressed = true;
                f.IsEncrypted = false;
                f.Import(new MemoryStream(ms.ToArray()));
            }
            else
            {
                f.IsCompressed = false;
                f.IsEncrypted = false;
                f.UncompressedSize = data.Length;
                f.Import(new MemoryStream(data));
            }
        }
    }

    private static void ProcessNested(RageArchiveWrapper7 arc, string rpfPath,
                                      List<(string inner, PatchAction a)> subs,
                                      Func<PatchAction, byte[]> readSrc, Stats stats)
    {
        string name = LastSeg(rpfPath);
        var container = NavigateFile(arc.Root, rpfPath) as IArchiveBinaryFile;
        if (container == null) { Console.WriteLine($"[apply]   ! nested rpf not found: {rpfPath}"); return; }

        byte[] nestedBytes = RpfUtil.ReadRealBytes(container);
        string tmpDir = Path.Combine(Path.GetTempPath(), "mg_nest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        string tmp = Path.Combine(tmpDir, name);
        try
        {
            File.WriteAllBytes(tmp, nestedBytes);
            using (var nestedArc = RpfUtil.OpenRpf(tmp, name, write: true))
            {
                ApplyRecursive(nestedArc, subs.Select(s => (s.inner, s.a)).ToList(), readSrc, stats);
                nestedArc.archive_.Encryption = RageArchiveEncryption7.None;
                nestedArc.Flush();
            }
            ArchiveFixer.Fix(tmp, name);
            WriteFile(arc, rpfPath, File.ReadAllBytes(tmp));
            stats.NestedRpf++;
        }
        finally { try { Directory.Delete(tmpDir, true); } catch { } }
    }

    private static bool DeleteRaw(RageArchiveWrapper7 arc, string path)
    {
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var dir = NavigateRaw(arc.archive_.Root, segs[..^1]);
        if (dir == null) return false;
        string name = segs[^1];
        var f = dir.Files.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (f != null) { dir.Files.Remove(f); return true; }
        var sd = dir.Directories.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (sd != null) { dir.Directories.Remove(sd); return true; }
        return false;
    }

    private static RageArchiveDirectory7? NavigateRaw(RageArchiveDirectory7 root, string[] dirs)
    {
        var cur = root;
        foreach (var d in dirs)
        {
            cur = cur.Directories.FirstOrDefault(x => x.Name.Equals(d, StringComparison.OrdinalIgnoreCase))!;
            if (cur == null) return null;
        }
        return cur;
    }

    private static IArchiveDirectory NavigateOrCreateDir(IArchiveDirectory root, string[] dirs)
    {
        var cur = root;
        foreach (var d in dirs)
        {
            var nx = cur.GetDirectories().FirstOrDefault(x => x.Name.Equals(d, StringComparison.OrdinalIgnoreCase));
            if (nx == null) { nx = cur.CreateDirectory(); nx.Name = d; }
            cur = nx;
        }
        return cur;
    }

    private static IArchiveFile? NavigateFile(IArchiveDirectory root, string path)
    {
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cur = root;
        for (int i = 0; i < segs.Length - 1; i++)
        {
            cur = cur.GetDirectories().FirstOrDefault(x => x.Name.Equals(segs[i], StringComparison.OrdinalIgnoreCase))!;
            if (cur == null) return null;
        }
        return cur.GetFiles().FirstOrDefault(x => x.Name.Equals(segs[^1], StringComparison.OrdinalIgnoreCase));
    }

    private static string Norm(string p)
    {
        p = p.Replace('\\', '/');
        int c = p.IndexOf(":/", StringComparison.Ordinal);
        if (c >= 0) p = p[(c + 1)..];
        return p.TrimStart('/');
    }
    private static string RawRoot(string targetPath)
    {
        var t = targetPath.Replace('\\', '/');
        int c = t.IndexOf(":/", StringComparison.Ordinal);
        if (c >= 0 && t[..c].Equals("common", StringComparison.OrdinalIgnoreCase)) return "common.rpf";
        return "update.rpf";
    }
    private static string LastSeg(string p) { int i = p.LastIndexOf('/'); return i < 0 ? p : p[(i + 1)..]; }

    private static (string? rpf, string inner) SplitFirstNestedRpf(string path)
    {
        var segs = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segs.Length - 1; i++)
        {
            if (segs[i].EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                return (string.Join('/', segs[..(i + 1)]), string.Join('/', segs[(i + 1)..]));
        }
        return (null, path);
    }

    private static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, d)));
        foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
        {
            var t = Path.Combine(dst, Path.GetRelativePath(src, f));
            Directory.CreateDirectory(Path.GetDirectoryName(t)!);
            File.Copy(f, t, true);
        }
    }
}
