using System.Text;
using MiamiGraphics.DlcLab;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

Console.OutputEncoding = Encoding.UTF8;

string keysDir = ResolveKeysDir();
try
{
    GTA5Constants.LoadFromPath(keysDir);
    int loaded = GTA5Constants.PC_NG_KEYS?.Count(k => k is { Length: > 0 }) ?? 0;
    Console.WriteLine($"[keys] loaded from {keysDir} — NG keys: {loaded}");
}
catch (Exception ex)
{
    Console.WriteLine($"[keys] FAILED from {keysDir}: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine("       Fix the keys path before opening any rpf.");
}

if (args.Length == 0) { PrintHelp(); return; }

try
{
    switch (args[0].ToLowerInvariant())
    {
        case "keys":
            break;

        case "ls" when args.Length >= 2:
            {
                int ai = Array.IndexOf(args, "--as");
                string? logical = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : null;
                string inner = args.Length >= 3 && args[2] != "--as" ? args[2] : "";
                Ls(args[1], inner, logical);
            }
            break;

        case "cat" when args.Length >= 3:
            Cat(args[1], args[2]);
            break;

        case "dlclist" when args.Length >= 2:
            Cat(args[1], "common/data/dlclist.xml");
            break;

        case "parse" when args.Length >= 4:
            Parse(args[1], args[2], args[3]);
            break;

        case "pack" when args.Length >= 3:
            Pack(args[1], args[2], args.Contains("-v"));
            break;

        case "analyze" when args.Length >= 3:
            ReduxAnalyzer.Analyze(args[1], args[2]);
            break;

        case "snapshot" when args.Length >= 3:
            Snapshot(args);
            break;

        case "storeopen" when args.Length >= 2:
            StoreOpener.Open(args[1]);
            break;

        case "mgstore" when args.Length >= 4:
            InPlaceInjector.BuildStore(args[1], args[2], args[3]);
            break;

        case "apply" when args.Length >= 4:
            InPlaceInjector.Apply(args[1], args[2], args[3]);
            break;

        case "storeextract" when args.Length >= 3:
            StoreExtract.Extract(args[1], args[2]);
            break;

        case "rpfcmp" when args.Length >= 3:
            {
                int ai = Array.IndexOf(args, "--as");
                string? logical = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : null;
                RpfCompare.Run(args[1], args[2], logical, logical);
            }
            break;

        case "sames" when args.Length >= 3:
            {
                int ai = Array.IndexOf(args, "--as");
                string? logical = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : null;
                int oi = Array.IndexOf(args, "-o");
                string? outFile = oi >= 0 && oi + 1 < args.Length ? args[oi + 1] : null;
                RpfCompare.RunSame(args[1], args[2], logical, logical, outFile);
            }
            break;

        case "cleanmanifest" when args.Length >= 3:
            {
                int vi = Array.IndexOf(args, "--ver");
                string? ver = vi >= 0 && vi + 1 < args.Length ? args[vi + 1] : null;
                if (ver is null || !System.Text.RegularExpressions.Regex.IsMatch(ver, @"^\d+\.\d+\.\d+\.\d+$"))
                {
                    Console.WriteLine("[cleanmanifest] обязателен --ver <exeVersion>, например --ver 1.0.3889.0");
                    Environment.ExitCode = 2;
                    break;
                }
                int ai = Array.IndexOf(args, "--as");
                string logical = ai >= 0 && ai + 1 < args.Length ? args[ai + 1] : "update.rpf";
                CleanManifest(args[1], args[2], ver, logical, !args.Contains("--no-blobs"));
            }
            break;

        case "track" when args.Length >= 4:
            Snapshotter.Track(args[1], args.Skip(2).ToList());
            break;

        case "diff" when args.Length >= 3:
            {
                int oi = Array.IndexOf(args, "-o");
                string? outJson = oi >= 0 && oi + 1 < args.Length ? args[oi + 1] : null;
                Snapshotter.Diff(args[1], args[2], outJson);
            }
            break;

        default:
            PrintHelp();
            break;
    }
}
catch (Exception ex)
{
    Console.WriteLine($"[error] {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

return;

static void Ls(string rpfPath, string innerDir, string? logicalName = null)
{
    using var arc = RpfUtil.OpenRpf(rpfPath, logicalName);
    var dir = string.IsNullOrEmpty(innerDir) ? arc.Root : RpfUtil.NavigateDir(arc.Root, innerDir);
    if (dir is null) { Console.WriteLine($"[ls] path not found: {innerDir}"); return; }
    RpfUtil.PrintDir(dir, $"{Path.GetFileName(rpfPath)}:/{innerDir}");
}

static void Cat(string rpfPath, string innerPath)
{
    using var arc = RageArchiveWrapper7.Open(rpfPath);
    var file = RpfUtil.FindFile(arc.Root, innerPath);
    if (file is null) { Console.WriteLine($"[cat] not found: {innerPath}"); return; }

    byte[] real = RpfUtil.ReadRealBytes(file);
    string text = RpfUtil.DecodeXml(real);
    Console.WriteLine($"[cat] {innerPath} — {real.Length} bytes (real)");
    Console.WriteLine("──────────────────────────────────────────────────");
    Console.WriteLine(string.IsNullOrEmpty(text) ? "(not text / could not decode)" : text);
}

static void Snapshot(string[] args)
{
    string outJson = args[1];
    long hashCapBytes = 16L * 1024 * 1024;
    int hi = Array.IndexOf(args, "--hashcap");
    if (hi >= 0 && hi + 1 < args.Length && long.TryParse(args[hi + 1], out var mb)) hashCapBytes = mb * 1024 * 1024;

    var roots = new List<string>();
    for (int i = 2; i < args.Length; i++)
    {
        if (args[i] == "--common") { roots.AddRange(Snapshotter.CommonRoots()); continue; }
        if (args[i] == "--hashcap") { i++; continue; }
        roots.Add(Path.GetFullPath(args[i]));
    }
    roots = roots.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    if (roots.Count == 0) { Console.WriteLine("[snapshot] no roots given"); return; }

    Console.WriteLine($"[snapshot] roots ({roots.Count}):");
    foreach (var r in roots) Console.WriteLine($"    {r}");
    Snapshotter.Take(outJson, roots, hashCapBytes);
}

static void Pack(string srcFolder, string outRpf, bool verbose)
{
    Console.WriteLine($"[pack] {srcFolder}\n    -> {outRpf}");
    var s = Packer.Pack(srcFolder, outRpf, verbose);
    Console.WriteLine($"[pack] done: dirs={s.Dirs} files={s.Files} (nested rpf={s.Rpfs}, resources={s.Resources}) size={s.Size:N0} bytes");

    using (var hs = File.OpenRead(outRpf))
    {
        var hb = new byte[16];
        hs.ReadExactly(hb, 0, 16);
        uint enc = BitConverter.ToUInt32(hb, 12);
        Console.WriteLine($"[pack] header: ident=0x{BitConverter.ToUInt32(hb, 0):X8} entries={BitConverter.ToUInt32(hb, 4)} enc=0x{enc:X8} (OPEN=0x4E45504F)");
    }
    try
    {
        using var arc = RageArchiveWrapper7.Open(outRpf);
        int rootDirs = arc.Root.GetDirectories().Length;
        int rootFiles = arc.Root.GetFiles().Length;
        Console.WriteLine($"[pack] reopen OK — root: {rootDirs} dirs, {rootFiles} files");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[pack] REOPEN FAILED: {ex.GetType().Name}: {ex.Message}");
    }
}

static void Parse(string cleanRpf, string moddedRpf, string reduxName)
{
    var pipeline = new MiamiGraphics.Core.Parser.ReduxParserPipeline();
    string outDir = pipeline.ParseRedux(cleanRpf, moddedRpf, reduxName);
    Console.WriteLine($"[parse] result: {outDir}");
}

static void CleanManifest(string rpfPath, string outDir, string ver, string logical, bool blobs)
{
    Console.WriteLine($"[cleanmanifest] {rpfPath}");
    Console.WriteLine($"    -> {outDir} (ver={ver}, as={logical}, blobs={(blobs ? "on" : "off")})");

    var builder = new MiamiGraphics.Core.Parser.CleanManifestBuilder();
    var r = builder.Build(rpfPath, ver, outDir, blobs, logical, s => Console.WriteLine($"    {s}"));
    var m = r.Manifest;

    Console.WriteLine($"[cleanmanifest] done in {r.Elapsed.TotalSeconds:F1}s");
    Console.WriteLine($"    источник: {m.SourceFileSize:N0} B, sha256={m.SourceFileSha256}");
    Console.WriteLine($"    листов {m.LeafCount:N0} (+{m.NestedRpfCount} вложенных rpf), real {m.TotalRealBytes:N0} B, stored {m.TotalStoredBytes:N0} B");
    Console.WriteLine($"    блобов {r.UniqueBlobs:N0} уникальных, {r.BlobBytes:N0} B на проводе; дубликатов листов {r.DuplicateLeaves:N0}");
    Console.WriteLine($"    манифест: {r.ManifestPath} ({new FileInfo(r.ManifestPath).Length:N0} B, sha256={r.ManifestSha256})");

    var uniq = m.Entries.Where(e => !e.IsNestedRpf)
        .GroupBy(e => e.Sha256).Select(g => g.First()).ToList();
    long Wire(MiamiGraphics.Core.Parser.CleanManifestEntry e) => e.BlobSize > 0 ? e.BlobSize : e.RealSize;
    (string Label, long Max)[] buckets =
    {
        ("<4КБ", 4096), ("4-64КБ", 64 * 1024), ("64КБ-1МБ", 1024 * 1024),
        ("1-16МБ", 16 * 1024 * 1024), (">=16МБ", long.MaxValue),
    };
    Console.WriteLine("    размеры блобов (на проводе):");
    long prev = 0;
    foreach (var (label, max) in buckets)
    {
        var grp = uniq.Where(e => Wire(e) > prev && Wire(e) <= max).ToList();
        Console.WriteLine($"      {label,-9} {grp.Count,6:N0} шт  {grp.Sum(Wire),15:N0} B");
        prev = max;
    }
}

static string ResolveKeysDir()
{
    var dir = AppContext.BaseDirectory;
    for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
    {
        var cand = Path.Combine(dir, "additionals", "keys", "gtav_ng_key.dat");
        if (File.Exists(cand)) return Path.GetDirectoryName(cand)!;
        dir = Path.GetDirectoryName(dir);
    }
    return Path.Combine(AppContext.BaseDirectory, "additionals", "keys");
}

static void PrintHelp()
{
    Console.WriteLine("""
        MiamiGraphics DLC Lab — testbed for the dlcpack-based install approach.

        Usage: dlclab <command> [args]

          keys                                  verify GTA5 crypto keys load
          ls   <rpf> [innerDir]                 list a directory inside an rpf
          cat  <rpf> <innerFilePath>            dump a text file (decoded) from an rpf
          dlclist <update.rpf>                  dump common/data/dlclist.xml
          parse <clean.rpf> <modded.rpf> <name> run the real Core redux parser
          pack <srcFolder> <outRpf> [-v]        build an rpf from a folder tree
          analyze <manifest.json> <patchDir>    classify redux changes
          cleanmanifest <clean.rpf> <outDir> --ver V [--as NAME] [--no-blobs]
                                                per-entry manifest of a clean update.rpf + blobs
                                                (--no-blobs => stats-only update_manifest.noblobs.json)
          snapshot <out.json> <root...> [--common] [--hashcap MB]   record fs state
          diff <before.json> <after.json> [-o out.json]             compare snapshots

        Examples:
          dlclab snapshot before.json "E:\GTAV" --common
          (run Network Graphics)
          dlclab snapshot after.json "E:\GTAV" --common
          dlclab diff before.json after.json -o changes.json
          dlclab dlclist "C:\...\update\update.rpf"
          dlclab ls      "C:\...\update\update.rpf" common/data
          dlclab cat     "C:\...\dlcpacks\patchday18ng\dlc.rpf" content.xml
        """);
}
