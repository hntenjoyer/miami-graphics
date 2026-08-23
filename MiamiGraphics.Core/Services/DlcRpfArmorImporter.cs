#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CodeWalker.GameFiles;
using MiamiGraphics.Core.I18n;
using DbgWriter = System.Diagnostics.Debug;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{

    public static class DlcRpfArmorImporter
    {
        private static void Log(string s) => DbgWriter.WriteLine("[DlcImport] " + s);

        public static ArmorInspectionResult Inspect(string dlcRpfPath, string previewOutputDir, CancellationToken ct = default)
        {
            var report = new ArmorInspectionResult { DlcRpfPath = dlcRpfPath };
            if (string.IsNullOrWhiteSpace(dlcRpfPath))
            {
                report.ErrorMessage = Loc.T("error.rpfPathEmpty");
                return report;
            }
            if (!File.Exists(dlcRpfPath))
            {
                report.ErrorMessage = Loc.T("error.fileNotFoundAt", ("path", dlcRpfPath));
                return report;
            }

            try { Directory.CreateDirectory(previewOutputDir); }
            catch (Exception ex)
            {
                report.ErrorMessage = Loc.T("error.previewDirCreateFailed", ("reason", ex.Message));
                return report;
            }

            try
            {
                Log($"open RPF: {dlcRpfPath}");
                using var stream = File.Open(dlcRpfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var arc = RageArchiveWrapper7.Open(stream, Path.GetFileName(dlcRpfPath), true);

                var ydds = new List<ResourceCandidate>();
                var ytds = new List<ResourceCandidate>();
                CollectResourceCandidates(arc.Root, "", ydds, ytds);
                Log($"found YDD={ydds.Count} YTD={ytds.Count}");

                if (ydds.Count == 0)
                {
                    report.Warnings.Add(
                        Loc.T("error.rpfNoYdd"));
                    return report;
                }

                var armorYdds = ydds
                    .Where(c => YddNameRegex.IsMatch(Path.GetFileName(c.Path)))
                    .ToList();
                if (armorYdds.Count == 0)
                {

                    Log("no canonical task_*_u.ydd found - falling back to all YDDs");
                    armorYdds = ydds;
                    report.Warnings.Add(
                        Loc.T("error.rpfNoCanonicalYdd"));
                }

                static string FolderKeyOf(string path)
                {
                    var slash = path.LastIndexOf('/');
                    var dir = slash < 0 ? "" : path.Substring(0, slash);
                    var parts = dir.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    return parts.Length > 0 ? parts[parts.Length - 1] : "";
                }
                var byFolder = armorYdds
                    .GroupBy(c => FolderKeyOf(c.Path), StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.ToList())
                    .ToList();
                var ordered = new List<ResourceCandidate>(armorYdds.Count);
                int maxRound = byFolder.Max(g => g.Count);
                for (int round = 0; round < maxRound; round++)
                {
                    foreach (var grp in byFolder)
                    {
                        if (round < grp.Count) ordered.Add(grp[round]);
                    }
                }
                Log($"round-robin order across {byFolder.Count} folder(s): " +
                    string.Join(", ", byFolder.Select(g => $"{FolderKeyOf(g[0].Path)}={g.Count}")));

                int maxParallel = Math.Max(1, Math.Min(4, Environment.ProcessorCount));
                Log($"parallel GLB generation: {ordered.Count} candidates × {maxParallel} workers");
                var resultsLock = new object();
                bool cancelled = false;
                try
                {
                    Parallel.ForEach(
                        ordered,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = maxParallel,
                            CancellationToken      = ct,
                        },
                        yddCand =>
                        {
                            try
                            {
                                var candidate = InspectYddCandidate(
                                    yddCand, ytds, previewOutputDir, generatePreview: true);
                                if (candidate != null)
                                {
                                    lock (resultsLock) report.Candidates.Add(candidate);
                                }
                            }
                            catch (Exception ex)
                            {
                                lock (resultsLock)
                                {
                                    report.Warnings.Add(
                                        Loc.T("error.inspectFailed",
                                            ("path", yddCand.Path), ("reason", ex.GetType().Name + ": " + ex.Message)));
                                }
                                Log($"yddCandidate fail: {yddCand.Path}: {ex}");
                            }
                        });
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }

                report.Candidates.Sort((a, b) =>
                    string.Compare(a.YddInternalPath, b.YddInternalPath, StringComparison.OrdinalIgnoreCase));

                if (cancelled)
                {
                    report.Warnings.Add(
                        Loc.T("error.inspectCancelled",
                            ("done", report.Candidates.Count), ("total", armorYdds.Count)));
                    Log($"inspection cancelled after {report.Candidates.Count}/{armorYdds.Count} candidates");
                }
            }
            catch (Exception ex)
            {
                report.ErrorMessage = Loc.T("error.rpfOpenFailed", ("reason", ex.GetType().Name + ": " + ex.Message));
                Log($"open fail: {ex}");
            }

            return report;
        }

        private static ArmorCandidateReport InspectYddCandidate(
            ResourceCandidate yddCand,
            List<ResourceCandidate> allYtds,
            string previewOutputDir,
            bool generatePreview)
        {

            byte[] yddBytes = yddCand.Bytes;
            if (yddBytes == null || yddBytes.Length == 0)
            {
                return new ArmorCandidateReport
                {
                    YddInternalPath = yddCand.Path,
                    YddName         = Path.GetFileName(yddCand.Path),
                    ParseError      = Loc.T("error.yddBytesExtractFailed"),
                };
            }

            var ydd = new YddFile();
            try { ydd.Load(yddBytes); }
            catch (Exception ex)
            {
                return new ArmorCandidateReport
                {
                    YddInternalPath = yddCand.Path,
                    YddName         = Path.GetFileName(yddCand.Path),
                    ParseError      = Loc.T("error.yddParseFailed", ("reason", ex.GetType().Name + ": " + ex.Message)),
                };
            }

            var drawables = ydd.Drawables;
            if (drawables == null || drawables.Length == 0)
            {
                return new ArmorCandidateReport
                {
                    YddInternalPath = yddCand.Path,
                    YddName         = Path.GetFileName(yddCand.Path),
                    ParseError      = Loc.T("error.yddNoDrawable"),
                };
            }

            var picked = drawables.FirstOrDefault(d =>
                d?.DrawableModels?.High != null && d.DrawableModels.High.Length > 0);
            if (picked == null)
            {
                return new ArmorCandidateReport
                {
                    YddInternalPath = yddCand.Path,
                    YddName         = Path.GetFileName(yddCand.Path),
                    ParseError      = Loc.T("error.yddNoHighLod"),
                };
            }

            var report = new ArmorCandidateReport
            {
                YddInternalPath      = yddCand.Path,
                YddName              = Path.GetFileName(yddCand.Path),
                DrawableInternalName = picked.Name ?? "<unnamed>",
            };

            var shaders = picked.ShaderGroup?.Shaders?.data_items;
            if (shaders != null)
            {
                for (int si = 0; si < shaders.Length; si++)
                {
                    var sh = shaders[si];
                    if (sh?.ParametersList?.Parameters == null || sh.ParametersList.Hashes == null) continue;
                    for (int p = 0; p < sh.ParametersList.Parameters.Length; p++)
                    {
                        var param = sh.ParametersList.Parameters[p];
                        if (param == null) continue;
                        uint paramHash = (uint)sh.ParametersList.Hashes[p];

                        if (param.Data is TextureBase tb && !string.IsNullOrEmpty(tb.Name))
                        {

                            string samplerName = ResolveSamplerName(paramHash)
                                ?? $"param_{paramHash:X8}";
                            report.SamplerExpectations.Add(new SamplerExpectation
                            {
                                SamplerName        = samplerName,
                                ExpectedTextureName = tb.Name,
                            });
                        }
                    }
                }
            }

            string yddDir = GetDirPart(yddCand.Path);
            var allInnerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ytdCand in allYtds)
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(GetDirPart(ytdCand.Path), yddDir)) continue;
                var ytdReport = ReadYtdInfo(ytdCand);
                report.CandidateYtds.Add(ytdReport);
                foreach (var n in ytdReport.InnerTextureNames) allInnerNames.Add(n);
            }

            var embeddedTd = picked.ShaderGroup?.TextureDictionary;
            if (embeddedTd?.Textures?.data_items != null)
            {
                foreach (var tex in embeddedTd.Textures.data_items)
                {
                    if (tex == null || string.IsNullOrEmpty(tex.Name)) continue;
                    allInnerNames.Add(tex.Name);
                }
            }

            var expectedNames = report.SamplerExpectations
                .Select(s => s.ExpectedTextureName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var missing = expectedNames
                .Where(name => !allInnerNames.Contains(name))
                .ToList();
            report.MissingExpectedDiffuses = missing;

            var realMissing = missing.Where(n => !LooksLikePlaceholderName(n)).ToList();
            report.HasNameMismatch = realMissing.Count > 0;

            if (report.HasNameMismatch
                && missing.Count >= 1
                && report.CandidateYtds.Count >= 1)
            {
                var diffuseExpected = PickLikelyDiffuse(missing);
                var firstYtd = report.CandidateYtds.FirstOrDefault(y =>
                    y.InnerTextureNames.Count > 0);
                var firstInner = firstYtd?.InnerTextureNames.FirstOrDefault();

                if (firstYtd != null
                    && !string.IsNullOrEmpty(firstInner)
                    && !string.IsNullOrEmpty(diffuseExpected)
                    && !LooksLikePlaceholderName(diffuseExpected))
                {
                    report.SuggestedRename = new TextureRenameSuggestion
                    {
                        YtdInternalPath = firstYtd.InternalPath,
                        OldTextureName  = firstInner,
                        NewTextureName  = diffuseExpected,
                    };
                }
            }

            if (!generatePreview)
            {
                Log($"preview SKIPPED (over budget) for {yddCand.Path}");
                return report;
            }

            try
            {

                string? renameOld = null;
                string? renameNew = null;
                if (report.HasNameMismatch
                    && missing.Count >= 1
                    && report.CandidateYtds.Count > 0)
                {

                    var sampleInner = report.CandidateYtds
                        .Select(y => y.InnerTextureNames.FirstOrDefault())
                        .FirstOrDefault(n => !string.IsNullOrEmpty(n));
                    var diffuseExpected = PickLikelyDiffuse(missing);
                    if (!string.IsNullOrEmpty(sampleInner)
                        && !string.IsNullOrEmpty(diffuseExpected)
                        && !LooksLikePlaceholderName(diffuseExpected))
                    {
                        renameOld = sampleInner;
                        renameNew = diffuseExpected;
                    }
                }

                var ytdBytesList = new List<byte[]>();
                foreach (var ytdCand in allYtds)
                {
                    if (!StringComparer.OrdinalIgnoreCase.Equals(GetDirPart(ytdCand.Path), yddDir)) continue;
                    if (ytdCand.Bytes == null || ytdCand.Bytes.Length == 0) continue;

                    var bytesToUse = ytdCand.Bytes;
                    if (renameOld != null && renameNew != null)
                    {
                        var renamed = YtdRenamer.RenameInnerTexture(bytesToUse, renameOld, renameNew);
                        if (renamed.Success && renamed.OutputBytes != null)
                            bytesToUse = renamed.OutputBytes;
                    }
                    ytdBytesList.Add(bytesToUse);
                }

                string safeName = SanitizeForFile(yddCand.Path) + ".glb";
                string outGlb = Path.Combine(previewOutputDir, safeName);
                bool ok = YddToGltfConverter.ConvertBytesAsync(yddBytes, ytdBytesList, outGlb)
                    .GetAwaiter().GetResult();
                if (ok && File.Exists(outGlb))
                {
                    report.PreviewGlbPath = outGlb;
                    Log($"preview ok: {outGlb}");
                }
                else
                {
                    Log($"preview NOT generated for {yddCand.Path}");
                }
            }
            catch (Exception ex)
            {
                Log($"preview fail {yddCand.Path}: {ex.GetType().Name}: {ex.Message}");
            }

            return report;
        }

        private static YtdReport ReadYtdInfo(ResourceCandidate ytdCand)
        {
            var report = new YtdReport
            {
                InternalPath = ytdCand.Path,
                FileName     = Path.GetFileName(ytdCand.Path),
            };
            if (ytdCand.Bytes == null || ytdCand.Bytes.Length == 0)
            {
                report.ParseError = "no bytes (export failed during walk)";
                return report;
            }
            try
            {
                var ytd = new YtdFile();
                ytd.Load(ytdCand.Bytes);
                var items = ytd.TextureDict?.Textures?.data_items;
                if (items != null)
                {
                    foreach (var tex in items)
                    {
                        if (tex == null || string.IsNullOrEmpty(tex.Name)) continue;
                        report.InnerTextureNames.Add(tex.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                report.ParseError = $"{ex.GetType().Name}: {ex.Message}";
            }
            return report;
        }

        private static readonly Dictionary<uint, string> SamplerHashToName = new()
        {
            { 0x4D52C5FF, "DiffuseSampler" },
            { 0xCBE9F8B9, "DiffuseSampler2" },
            { 0xB7E29C93, "DiffuseSamplerPhase2" },
            { 0xE43CDB14, "DiffuseSamplerPoint" },
            { 0xC1F22D69, "DiffuseTexSampler" },
            { 0x47BC2E18, "BumpSampler" },
            { 0x16FA5DCE, "SpecSampler" },
            { 0x1F436FA1, "TextureSamplerDiffPal" },
            { 0xCA39C002, "VolumeSampler" },
        };

        private static string ResolveSamplerName(uint hash)
            => SamplerHashToName.TryGetValue(hash, out var n) ? n : null;

        private static bool IsDiffuseSampler(string name)
            => name != null && name.StartsWith("Diffuse", StringComparison.OrdinalIgnoreCase);

        private static readonly string[] PlaceholderTextureNames =
        {
            "givemechecker", "checker", "checkertexture",
            "default", "none", "null", "missing", "blank",
            "todo", "fixme",
        };

        private static bool LooksLikePlaceholderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            foreach (var p in PlaceholderTextureNames)
                if (lower == p || lower.Contains(p)) return true;
            return false;
        }

        private static string PickLikelyDiffuse(List<string> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            string[] nonDiffuseSuffixes = {
                "_map", "_normal", "_nrm", "_n",
                "_spec", "_specular", "_s",
                "_bump", "_bumpmap",
                "_glow", "_emissive", "_em",
                "_alpha",
            };

            bool LooksLikeNonDiffuse(string n)
            {
                if (string.IsNullOrEmpty(n)) return false;
                var lower = n.ToLowerInvariant();
                foreach (var suf in nonDiffuseSuffixes)
                    if (lower.EndsWith(suf, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }

            var diffuseCandidates = candidates.Where(c => !LooksLikeNonDiffuse(c)).ToList();
            if (diffuseCandidates.Count == 0)
                return candidates[0];

            return diffuseCandidates.OrderBy(c => c.Length).First();
        }

        private sealed class ResourceCandidate
        {
            public string Path  { get; set; }

            public byte[] Bytes { get; set; }
        }

        private static readonly Regex YddNameRegex = new Regex(
            @"^task_\d+_u\.ydd$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static void CollectResourceCandidates(
            IArchiveDirectory dir, string prefix,
            List<ResourceCandidate> ydd, List<ResourceCandidate> ytd)
        {
            foreach (var f in dir.GetFiles())
            {
                string childPath = string.IsNullOrEmpty(prefix) ? f.Name : prefix + "/" + f.Name;

                if (f.Name.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = TryExportBytes(f, childPath);
                    if (bytes != null) ydd.Add(new ResourceCandidate { Path = childPath, Bytes = bytes });
                }
                else if (f.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = TryExportBytes(f, childPath);
                    if (bytes != null) ytd.Add(new ResourceCandidate { Path = childPath, Bytes = bytes });
                }

                if (f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && f is IArchiveBinaryFile binRpf)
                {
                    try
                    {
                        using var stream = binRpf.GetStream();
                        using var nestedArc = RageArchiveWrapper7.Open(stream, binRpf.Name, true);
                        CollectResourceCandidates(nestedArc.Root, childPath, ydd, ytd);
                    }
                    catch (Exception ex)
                    {
                        Log($"  nested RPF {childPath} не открылся: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            foreach (var sub in dir.GetDirectories())
            {
                string subPath = string.IsNullOrEmpty(prefix) ? sub.Name : prefix + "/" + sub.Name;
                CollectResourceCandidates(sub, subPath, ydd, ytd);
            }
        }

        private static byte[] TryExportBytes(IArchiveFile f, string pathForLog)
        {
            try
            {
                using var ms = new MemoryStream();
                f.Export(ms);
                return ms.ToArray();
            }
            catch (Exception ex)
            {
                Log($"  export fail {pathForLog}: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static string GetDirPart(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int slash = path.LastIndexOf('/');
            return slash < 0 ? "" : path.Substring(0, slash);
        }

        private static string SanitizeForFile(string s)
        {
            if (string.IsNullOrEmpty(s)) return "candidate";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = s.Select(c =>
                invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray();
            return new string(chars);
        }
    }

    public sealed class ArmorInspectionResult
    {
        public string DlcRpfPath { get; set; }
        public List<ArmorCandidateReport> Candidates { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public string ErrorMessage { get; set; }
    }

    public sealed class ArmorCandidateReport
    {
        public string YddInternalPath { get; set; }
        public string YddName { get; set; }
        public string DrawableInternalName { get; set; }
        public string ParseError { get; set; }
        public List<SamplerExpectation> SamplerExpectations { get; set; } = new();
        public List<YtdReport> CandidateYtds { get; set; } = new();
        public List<string> MissingExpectedDiffuses { get; set; } = new();
        public bool HasNameMismatch { get; set; }
        public TextureRenameSuggestion SuggestedRename { get; set; }

        public string PreviewGlbPath { get; set; }
    }

    public sealed class SamplerExpectation
    {
        public string SamplerName { get; set; }
        public string ExpectedTextureName { get; set; }
    }

    public sealed class YtdReport
    {
        public string InternalPath { get; set; }
        public string FileName { get; set; }
        public List<string> InnerTextureNames { get; set; } = new();
        public string ParseError { get; set; }
    }

    public sealed class TextureRenameSuggestion
    {
        public string YtdInternalPath { get; set; }
        public string OldTextureName { get; set; }
        public string NewTextureName { get; set; }
    }
}
