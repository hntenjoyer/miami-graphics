using System.Text.Json;
using System.Xml.Linq;
using MiamiGraphics.Core.Parser;

namespace MiamiGraphics.DlcLab;

public static class ReduxAnalyzer
{
    private enum Bucket
    {
        MetaRoot,
        LooseRootRpf,
        X64,
        Common,
        DlcPatch,
        OtherRootFile,
    }

    public static void Analyze(string manifestPath, string patchFilesDir)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        opts.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        var manifest = JsonSerializer.Deserialize<DiffManifest>(File.ReadAllText(manifestPath), opts)
                       ?? throw new InvalidOperationException("manifest.json unreadable");

        Console.WriteLine($"Redux: {manifest.ReduxName}   parsed {manifest.ParsedAt}");
        Console.WriteLine($"Actions: {manifest.Actions.Count}   patch size: {manifest.TotalPatchSize / 1024.0 / 1024.0:F1} MB\n");

        var byBucket = new Dictionary<Bucket, List<PatchAction>>();
        foreach (Bucket b in Enum.GetValues<Bucket>()) byBucket[b] = new();

        foreach (var a in manifest.Actions)
            byBucket[Classify(a.TargetPath)].Add(a);

        Console.WriteLine("── change buckets (Import / Replace / Delete / bytes) ──");
        foreach (Bucket b in Enum.GetValues<Bucket>())
        {
            var list = byBucket[b];
            if (list.Count == 0) continue;
            int imp = list.Count(x => x.Type == ActionType.Import);
            int rep = list.Count(x => x.Type == ActionType.Replace);
            int del = list.Count(x => x.Type == ActionType.Delete);
            long bytes = list.Where(x => x.Type != ActionType.Delete).Sum(x => x.Size);
            Console.WriteLine($"  {b,-14} total={list.Count,-4} I={imp,-4} R={rep,-4} D={del,-4} {bytes / 1024.0 / 1024.0,7:F1} MB   {Verdict(b)}");
        }

        DumpBucket(byBucket, Bucket.LooseRootRpf, "LOOSE ROOT RPFs (need dlc device mapping)");
        DumpBucket(byBucket, Bucket.DlcPatch,     "DLC_PATCH targets (update.rpf-only mechanic — cannot go in a plain dlcpack)");
        DumpBucket(byBucket, Bucket.MetaRoot,     "ROOT META");
        DumpBucket(byBucket, Bucket.OtherRootFile,"OTHER ROOT FILES");

        AnalyzeContentXml(Path.Combine(patchFilesDir, "content.xml"));
    }

    private static Bucket Classify(string targetPath)
    {
        string p = targetPath.Replace('\\', '/').TrimStart('/');
        string lower = p.ToLowerInvariant();
        string first = lower.Contains('/') ? lower[..lower.IndexOf('/')] : lower;
        bool loose = !p.Contains('/');

        if (loose)
        {
            if (lower is "content.xml" or "setup2.xml" or "assembly.xml") return Bucket.MetaRoot;
            if (lower.EndsWith(".rpf")) return Bucket.LooseRootRpf;
            return Bucket.OtherRootFile;
        }
        return first switch
        {
            "x64" => Bucket.X64,
            "common" => Bucket.Common,
            "dlc_patch" => Bucket.DlcPatch,
            _ => Bucket.OtherRootFile,
        };
    }

    private static string Verdict(Bucket b) => b switch
    {
        Bucket.X64 => "→ dlc_XXX:/%PLATFORM%/…",
        Bucket.Common => "→ dlc_XXX:/common/…",
        Bucket.LooseRootRpf => "→ place under x64/ + content.xml RPF_FILE overlay",
        Bucket.DlcPatch => "⚠ needs update.rpf dlc_patch OR per-dlc handling",
        Bucket.MetaRoot => "handled by generated setup2/content",
        Bucket.OtherRootFile => "⚠ inspect individually",
        _ => "",
    };

    private static void DumpBucket(Dictionary<Bucket, List<PatchAction>> map, Bucket b, string title)
    {
        var list = map[b];
        if (list.Count == 0) return;
        Console.WriteLine($"\n── {title} ({list.Count}) ──");
        foreach (var a in list.OrderBy(x => x.TargetPath, StringComparer.OrdinalIgnoreCase).Take(40))
            Console.WriteLine($"  [{a.Type,-7}] {a.TargetPath}");
        if (list.Count > 40) Console.WriteLine($"  … +{list.Count - 40} more");
    }

    private static void AnalyzeContentXml(string contentXmlPath)
    {
        Console.WriteLine("\n── redux content.xml ──");
        if (!File.Exists(contentXmlPath)) { Console.WriteLine("  (no content.xml in patch_files)"); return; }

        XDocument doc;
        try { doc = XDocument.Parse(File.ReadAllText(contentXmlPath)); }
        catch (Exception ex) { Console.WriteLine($"  parse failed: {ex.Message}"); return; }

        var dataItems = doc.Descendants("dataFiles").Elements("Item").ToList();
        Console.WriteLine($"  dataFiles: {dataItems.Count}");

        var devices = dataItems
            .Select(i => i.Element("filename")?.Value ?? "")
            .Select(fn => fn.Contains(":/") ? fn[..fn.IndexOf(":/")] : "(none)")
            .GroupBy(d => d, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count());
        Console.WriteLine("  device prefixes used:");
        foreach (var g in devices) Console.WriteLine($"    {g.Key,-28} × {g.Count()}");

        var fileTypes = dataItems
            .Select(i => i.Element("fileType")?.Value ?? "(none)")
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count());
        Console.WriteLine("  fileTypes:");
        foreach (var g in fileTypes) Console.WriteLine($"    {g.Key,-28} × {g.Count()}");

        var changeSets = doc.Descendants("contentChangeSets").Elements("Item").ToList();
        int enable = doc.Descendants("filesToEnable").Elements("Item").Count();
        Console.WriteLine($"  contentChangeSets: {changeSets.Count}   filesToEnable entries: {enable}");
    }
}
