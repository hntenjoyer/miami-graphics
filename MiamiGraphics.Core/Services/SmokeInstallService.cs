using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.Parser;

namespace MiamiGraphics.Core.Services
{
    public sealed class SmokeInstallService
    {
        public static readonly string[] CoreYptTargets =
        {
            "x64/patch/data/effects/ptfx.rpf/core.ypt",
            "x64/patch/data/effects/ptfx_hi.rpf/core.ypt",
            "x64/patch/data/effects/ptfx_lo.rpf/core.ypt",
        };

        public sealed class Result
        {
            public bool Success { get; init; }
            public string ErrorMessage { get; init; } = "";
            public int Patched { get; init; }
        }

        private static List<string> CoreYptCandidates(string gtaRoot, Func<string, byte[]?> bytesForPath,
            out Dictionary<string, byte[]> loaded)
        {
            loaded = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var hit = new List<string>();
            foreach (var p in CoreYptTargets)
            {
                var b = bytesForPath(p);
                if (b is null) continue;
                loaded[p] = b;
                hit.Add(p);
            }
            if (hit.Count > 0) return hit;

            foreach (var p in PatchCustomizationSupport.FindInternalPathsDeepWhere(
                         gtaRoot, name => name.Equals("core.ypt", StringComparison.OrdinalIgnoreCase), maxHits: 24))
            {
                var b = bytesForPath(p);
                if (b is null) continue;
                loaded[p] = b;
                hit.Add(p);
            }
            return hit;
        }

        public Result Apply(string gtaRoot, string smokeDdsDir,
            Func<string, byte[]?> cleanBytesForPath)
        {
            if (string.IsNullOrWhiteSpace(gtaRoot))
                return new Result { Success = false, ErrorMessage = Loc.T("error.gtaNotFoundShort") };
            if (!Directory.Exists(smokeDdsDir))
                return new Result { Success = false, ErrorMessage = Loc.T("error.smokeDirNotFound", ("path", smokeDdsDir)) };

            string workDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics",
                "smoke_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string patchFilesDir = Path.Combine(workDir, "patch_files");
            Directory.CreateDirectory(patchFilesDir);

            var manifest = new DiffManifest
            {
                ReduxName = "smoke",
                ParsedAt = DateTime.Now,
                Actions = new List<PatchAction>(),
            };

            Dictionary<string, byte[]> smokePixels;
            var smokeSources = new Dictionary<string, CoreYptTexturePatcher.TextureSource>(StringComparer.OrdinalIgnoreCase);
            try
            {
                smokePixels = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var name in TracerBuilderService.SmokeTextureNames)
                {
                    var dds = Path.Combine(smokeDdsDir, name + ".dds");
                    if (!File.Exists(dds)) continue;
                    var bytes = File.ReadAllBytes(dds);
                    smokePixels[name] = CoreYptTexturePatcher.DdsPixelData(bytes);
                    smokeSources[name] = CoreYptTexturePatcher.ParseDds(bytes);
                }
            }
            catch (Exception ex)
            {
                return new Result { Success = false, ErrorMessage = Loc.T("error.smokeReadFailed", ("reason", ex.Message)) };
            }
            if (smokePixels.Count == 0)
                return new Result { Success = false, ErrorMessage = Loc.T("error.smokeNoTexturesInDir", ("path", smokeDdsDir)) };

            int patched = 0;
            int seen = 0, withNames = 0, lenMismatch = 0;
            try
            {
                var candidates = CoreYptCandidates(gtaRoot, cleanBytesForPath, out var loaded);
                foreach (var internalPath in candidates)
                {
                    if (!loaded.TryGetValue(internalPath, out var cur) || cur is null) continue;
                    seen++;

                    string staged = Path.Combine(patchFilesDir, internalPath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                    File.WriteAllBytes(staged, cur);

                    bool changed;
                    try
                    {
                        changed = CoreYptTexturePatcher.ReplacePixelDataInPlace(
                            staged, smokePixels, out var names, out var skipped, smokeSources) > 0;
                        if (names > 0) withNames++;
                        if (skipped > 0) lenMismatch++;
                    }
                    catch (Exception ex)
                    {
                        return new Result { Success = false, ErrorMessage = Loc.T("error.patchFileFailed", ("path", internalPath), ("reason", ex.Message)) };
                    }
                    if (!changed) continue;

                    PatchCustomizationSupport.UpsertPatchAction(manifest, workDir, new PatchWorkspaceFile
                    {
                        TargetPath = internalPath,
                        PhysicalPath = staged,
                        ActionType = ActionType.Replace,
                    });
                    patched++;
                }

                if (patched == 0)
                {
                    if (seen == 0)
                        return new Result { Success = false, ErrorMessage = Loc.T("error.smokeNoCoreYpt") };
                    if (withNames == 0)
                        return new Result { Success = false, ErrorMessage = Loc.T("error.smokeNoNamesInYpt", ("count", seen)) };
                    if (lenMismatch > 0)
                        return new Result { Success = false, ErrorMessage = Loc.T("error.smokeLengthMismatch") };
                    return new Result { Success = false, ErrorMessage = Loc.T("error.smokeTexturesNotFoundAnywhere") };
                }

                PatchCustomizationSupport.RecalculateTotalPatchSize(manifest);
                File.WriteAllText(Path.Combine(workDir, "manifest.json"),
                    global::System.Text.Json.JsonSerializer.Serialize(manifest,
                        new global::System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                var engine = new RpfInjectEngine(gtaRoot);
                if (!engine.InjectPatch(workDir))
                    return new Result { Success = false, ErrorMessage = string.IsNullOrWhiteSpace(engine.LastError) ? Loc.T("error.injectPatchFalseShort") : engine.LastError! };

                return new Result { Success = true, Patched = patched };
            }
            catch (Exception ex)
            {
                return new Result { Success = false, ErrorMessage = ex.Message };
            }
            finally { try { Directory.Delete(workDir, recursive: true); } catch { } }
        }

        public Result Restore(string gtaRoot,
            Func<string, byte[]?> liveBytesForPath,
            Func<string, byte[]?> stockBytesForPath)
        {
            if (string.IsNullOrWhiteSpace(gtaRoot))
                return new Result { Success = false, ErrorMessage = Loc.T("error.gtaNotFoundShort") };

            string workDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics",
                "smoke_restore_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string patchFilesDir = Path.Combine(workDir, "patch_files");
            string stockDir = Path.Combine(workDir, "stock");
            Directory.CreateDirectory(patchFilesDir);
            Directory.CreateDirectory(stockDir);

            var manifest = new DiffManifest { ReduxName = "smoke_restore", ParsedAt = DateTime.Now, Actions = new List<PatchAction>() };
            int patched = 0;
            try
            {
                var candidates = CoreYptCandidates(gtaRoot, liveBytesForPath, out var liveLoaded);
                foreach (var internalPath in candidates)
                {
                    if (!liveLoaded.TryGetValue(internalPath, out var live) || live is null) continue;
                    var stock = stockBytesForPath(internalPath);
                    if (stock is null) continue;

                    string stockYpt = Path.Combine(stockDir, internalPath.Replace('/', '_'));
                    File.WriteAllBytes(stockYpt, stock);
                    var vanillaPixels = CoreYptTexturePatcher.ExtractPixelData(
                        stockYpt, TracerBuilderService.SmokeTextureNames);
                    if (vanillaPixels.Count == 0) continue;
                    var vanillaSources = CoreYptTexturePatcher.ExtractSources(
                        stockYpt, TracerBuilderService.SmokeTextureNames);

                    string staged = Path.Combine(patchFilesDir, internalPath.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                    File.WriteAllBytes(staged, live);
                    if (CoreYptTexturePatcher.ReplacePixelDataInPlace(staged, vanillaPixels, out _, out _, vanillaSources) == 0) continue;

                    PatchCustomizationSupport.UpsertPatchAction(manifest, workDir, new PatchWorkspaceFile
                    {
                        TargetPath = internalPath,
                        PhysicalPath = staged,
                        ActionType = ActionType.Replace,
                    });
                    patched++;
                }

                if (patched == 0)
                    return new Result { Success = false, ErrorMessage = Loc.T("error.noCoreYptForSmokeRestore") };

                PatchCustomizationSupport.RecalculateTotalPatchSize(manifest);
                File.WriteAllText(Path.Combine(workDir, "manifest.json"),
                    global::System.Text.Json.JsonSerializer.Serialize(manifest,
                        new global::System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

                var engine = new RpfInjectEngine(gtaRoot);
                if (!engine.InjectPatch(workDir))
                    return new Result { Success = false, ErrorMessage = string.IsNullOrWhiteSpace(engine.LastError) ? Loc.T("error.injectPatchFalseShort") : engine.LastError! };

                return new Result { Success = true, Patched = patched };
            }
            catch (Exception ex) { return new Result { Success = false, ErrorMessage = ex.Message }; }
            finally { try { Directory.Delete(workDir, recursive: true); } catch { } }
        }
    }
}
