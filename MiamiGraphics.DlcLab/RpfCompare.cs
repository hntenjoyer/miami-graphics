using System.Security.Cryptography;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.DlcLab;

public static class RpfCompare
{
    private const long HashSizeCap = 200L * 1024 * 1024;

    private static readonly List<string> Added = new();
    private static readonly List<string> Removed = new();
    private static readonly List<string> Changed = new();
    private static readonly List<string> Identical = new();
    private static int same;

    public static void RunSame(string aPath, string bPath, string? logicalA, string? logicalB, string? outFile)
    {
        Added.Clear(); Removed.Clear(); Changed.Clear(); Identical.Clear(); same = 0;
        Console.WriteLine($"[same] baseline = {aPath}  (as {logicalA ?? Path.GetFileName(aPath)})");
        Console.WriteLine($"[same] target   = {bPath}  (as {logicalB ?? Path.GetFileName(bPath)})\n");

        using (var a = RpfUtil.OpenRpf(aPath, logicalA))
        using (var b = RpfUtil.OpenRpf(bPath, logicalB))
            Walk(a.Root, b.Root, "");

        Identical.Sort(StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"[same] {Identical.Count} file(s) present in target but IDENTICAL to baseline " +
                          $"(changed={Changed.Count}, only-in-target={Added.Count})\n");
        foreach (var p in Identical) Console.WriteLine("  " + p);

        if (outFile != null)
        {
            File.WriteAllLines(outFile, Identical);
            Console.WriteLine($"\n[same] list saved -> {outFile} ({Identical.Count} paths)");
        }
    }

    public static void Run(string aPath, string bPath, string? logicalA = null, string? logicalB = null)
    {
        Added.Clear(); Removed.Clear(); Changed.Clear(); same = 0;

        Console.WriteLine($"[rpfcmp] A (baseline) = {aPath}  (as {logicalA ?? Path.GetFileName(aPath)})");
        Console.WriteLine($"[rpfcmp] B (modded)   = {bPath}  (as {logicalB ?? Path.GetFileName(bPath)})\n");

        using var a = RpfUtil.OpenRpf(aPath, logicalA);
        using var b = RpfUtil.OpenRpf(bPath, logicalB);
        Walk(a.Root, b.Root, "");

        Added.Sort(StringComparer.OrdinalIgnoreCase);
        Removed.Sort(StringComparer.OrdinalIgnoreCase);
        Changed.Sort(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"════ RPF DIFF ════  +{Added.Count} added   ~{Changed.Count} changed   -{Removed.Count} removed   ={same} identical\n");
        Dump("ADDED (only in modded)", Added);
        Dump("REMOVED (only in baseline)", Removed);
        Dump("CHANGED (content differs)", Changed);
    }

    private static void Dump(string title, List<string> list)
    {
        if (list.Count == 0) return;
        Console.WriteLine($"── {title} ({list.Count}) ──");
        foreach (var s in list.Take(300)) Console.WriteLine($"  {s}");
        if (list.Count > 300) Console.WriteLine($"  … +{list.Count - 300} more");
        Console.WriteLine();
    }

    private static void Walk(IArchiveDirectory da, IArchiveDirectory db, string prefix)
    {
        var filesA = da.GetFiles().ToDictionary(f => f.Name.ToLowerInvariant());
        var filesB = db.GetFiles().ToDictionary(f => f.Name.ToLowerInvariant());

        foreach (var (name, fa) in filesA)
        {
            if (!filesB.TryGetValue(name, out var fb)) { Removed.Add(prefix + name); continue; }

            bool isRpf = name.EndsWith(".rpf");
            if (isRpf)
            {
                if (!TryRecurseNested(fa, fb, prefix + name + "/"))
                {
                    CompareLeaf(fa, fb, prefix + name);
                }
            }
            else
            {
                CompareLeaf(fa, fb, prefix + name);
            }
        }
        foreach (var (name, fb) in filesB)
            if (!filesA.ContainsKey(name)) Added.Add(prefix + name);

        var dirsA = da.GetDirectories().ToDictionary(d => d.Name.ToLowerInvariant());
        var dirsB = db.GetDirectories().ToDictionary(d => d.Name.ToLowerInvariant());
        foreach (var (name, sa) in dirsA)
        {
            if (dirsB.TryGetValue(name, out var sb)) Walk(sa, sb, prefix + name + "/");
            else MarkTreeRemoved(sa, prefix + name + "/");
        }
        foreach (var (name, sb) in dirsB)
            if (!dirsA.ContainsKey(name)) MarkTreeAdded(sb, prefix + name + "/");
    }

    private static bool TryRecurseNested(IArchiveFile fa, IArchiveFile fb, string prefix)
    {
        if (fa is not IArchiveBinaryFile ba || fb is not IArchiveBinaryFile bb) return false;
        try
        {
            using var sa = ba.GetStream();
            using var sb = bb.GetStream();
            using var na = RageArchiveWrapper7.Open(sa, fa.Name, true);
            using var nb = RageArchiveWrapper7.Open(sb, fb.Name, true);
            Walk(na.Root, nb.Root, prefix);
            return true;
        }
        catch { return false; }
    }

    private static void CompareLeaf(IArchiveFile fa, IArchiveFile fb, string path)
    {
        try
        {
            long sa = LeafSize(fa), sb = LeafSize(fb);
            if (sa > HashSizeCap || sb > HashSizeCap)
            {
                if (sa != sb) Changed.Add($"{path}  (size {sa:N0} -> {sb:N0}, too big to hash)");
                else same++;
                return;
            }
            byte[] ba = RpfUtil.ReadRealBytes(fa);
            byte[] bb = RpfUtil.ReadRealBytes(fb);
            if (ba.Length != bb.Length) { Changed.Add($"{path}  ({ba.Length:N0} -> {bb.Length:N0} bytes)"); return; }
            if (!Sha(ba).SequenceEqual(Sha(bb))) { Changed.Add($"{path}  ({ba.Length:N0} bytes, content differs)"); return; }
            same++;
            Identical.Add(path);
        }
        catch (Exception ex) { Changed.Add($"{path}  (compare error: {ex.GetType().Name})"); }
    }

    private static long LeafSize(IArchiveFile f) =>
        f is IArchiveBinaryFile b ? (b.UncompressedSize > 0 ? b.UncompressedSize : b.Size) : f.Size;

    private static byte[] Sha(byte[] data) { using var s = SHA256.Create(); return s.ComputeHash(data); }

    private static void MarkTreeRemoved(IArchiveDirectory d, string prefix)
    {
        foreach (var f in d.GetFiles()) Removed.Add(prefix + f.Name.ToLowerInvariant());
        foreach (var s in d.GetDirectories()) MarkTreeRemoved(s, prefix + s.Name.ToLowerInvariant() + "/");
    }
    private static void MarkTreeAdded(IArchiveDirectory d, string prefix)
    {
        foreach (var f in d.GetFiles()) Added.Add(prefix + f.Name.ToLowerInvariant());
        foreach (var s in d.GetDirectories()) MarkTreeAdded(s, prefix + s.Name.ToLowerInvariant() + "/");
    }
}
