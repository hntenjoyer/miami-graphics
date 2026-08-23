using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Parser;
using ImageMagick;
using RageLib.GTA5.ResourceWrappers.PC.Particles;
using RageLib.Helpers;
using RageLib.ResourceWrappers;

using CwGameFiles = global::CodeWalker.GameFiles;

namespace MiamiGraphics.Core.Services
{
    public enum TracerScenario
    {

        NoOpSaveTest,

        CwNoOpSaveTest,

        ColorOnly,

        MasterTexture,

        CrossInject
    }

    public sealed class TracerCustomizationRequest
    {
        public TracerScenario Scenario { get; init; }

        public string PatchRootDirectory { get; init; } = "";

        public DiffManifest Manifest { get; init; } = null!;

        public string GtaRootPath { get; init; } = "";

        public byte Red { get; init; }
        public byte Green { get; init; }
        public byte Blue { get; init; }

        public string? ModelDirectory { get; init; }

        public string? DonorReduxPath { get; init; }

        public bool InjectDonorBlood { get; init; }

        public bool UseCleanBaseline { get; init; }

        public bool IncludeQualityVariants { get; init; }

        public bool OverrideColorWithPicker { get; init; }
    }

    public class TracerBuilderService
    {
        private const string ComponentKey   = "tracers";
        private const string ComponentMapFn = "component_map.json";
        private const string PatchFilesDir  = "patch_files";

        private const string MainTextureName = "ptfx_bullet_tracer";
        private const string HeatTextureName = "ptfx_bullet_tracer_heat";
        private const string RgTextureName   = "ptfx_bullet_tracer_rg";
        private const string BloodTextureName = "ptfx_blood_spray";

        private static readonly string[] CanonicalTextureNames =
            { MainTextureName, HeatTextureName, RgTextureName, BloodTextureName };

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public void Customize(TracerCustomizationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            ValidateRequest(request);

            string debugLogDir = Path.Combine(
                MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir,
                "_DebugLogs",
                "Tracers_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(debugLogDir);
            Console.WriteLine($"[TracerBuilder] Scenario={request.Scenario} DebugLogs={debugLogDir}");
            Console.WriteLine($"[TracerBuilder] Redux: {request.PatchRootDirectory}");

            var componentMap = TryLoadComponentMap(request.PatchRootDirectory);
            if (componentMap == null)
            {
                Console.WriteLine("[TracerBuilder] component_map.json не найден или некорректен - выходим.");
                return;
            }

            if (componentMap.Components == null ||
                !componentMap.Components.TryGetValue(ComponentKey, out ComponentInfo? tracersInfo) ||
                tracersInfo == null ||
                !tracersInfo.IsFound ||
                tracersInfo.InternalPaths == null ||
                tracersInfo.InternalPaths.Count == 0)
            {
                Console.WriteLine("[TracerBuilder] В редуксе нет tracers-компонента - выходим.");
                return;
            }

            Console.WriteLine($"[TracerBuilder] tracers.InternalPaths ({tracersInfo.InternalPaths.Count}):");
            foreach (string p in tracersInfo.InternalPaths)
                Console.WriteLine($"   - {p}");

            if (request.Scenario == TracerScenario.CrossInject)
            {
                bool wholeOk = ProcessCrossInjectWholeFile(request, tracersInfo, debugLogDir);
                if (!wholeOk)
                {
                    Console.WriteLine("[TracerBuilder] CrossInject whole-file: изменений нет.");
                    return;
                }
                PatchCustomizationSupport.RecalculateTotalPatchSize(request.Manifest);
                Console.WriteLine("[TracerBuilder] Готово (whole-file). Manifest пересчитан.");
                return;
            }

            var targetPaths = new List<string>(tracersInfo.InternalPaths);
            if (request.IncludeQualityVariants)
                AppendQualityVariantTargets(request, targetPaths);

            DonorTextures? donorDds = null;

            bool anyModified = false;

            foreach (string internalPath in targetPaths)
            {
                string normalized = NormalizePath(internalPath);
                string physicalPath = Path.Combine(
                    request.PatchRootDirectory,
                    PatchFilesDir,
                    normalized.Replace('/', Path.DirectorySeparatorChar));

                Console.WriteLine($"[TracerBuilder] === Target: {normalized} ===");
                Console.WriteLine($"[TracerBuilder] Physical: {physicalPath}");

                string perTargetDebugDir = Path.Combine(debugLogDir, SanitizePathSegment(normalized));
                Directory.CreateDirectory(perTargetDebugDir);

                if (!File.Exists(physicalPath))
                {
                    Console.WriteLine("[TracerBuilder] Файл не существует в patch_files - skip (не меняем что нет).");
                    continue;
                }

                if (request.Scenario == TracerScenario.CwNoOpSaveTest)
                {
                    bool cwOk = ProcessTargetWithCodeWalker(normalized, physicalPath, perTargetDebugDir);
                    if (cwOk) anyModified = true;
                    continue;
                }

                if (request.Scenario == TracerScenario.ColorOnly)
                {
                    bool colorOk = ProcessTargetColorPatch(request, normalized, physicalPath);
                    if (colorOk) anyModified = true;
                    continue;
                }

                bool ok = ProcessTarget(
                    request,
                    normalized,
                    physicalPath,
                    perTargetDebugDir,
                    donorDds);

                if (ok) anyModified = true;
            }

            if (!anyModified)
            {
                Console.WriteLine("[TracerBuilder] Ни один core.ypt не обновлён.");
                return;
            }

            PatchCustomizationSupport.RecalculateTotalPatchSize(request.Manifest);
            Console.WriteLine("[TracerBuilder] Готово. Manifest пересчитан.");
        }

        public static readonly string[] SmokeTextureNames =
            { "ptfx_smoke_new_plumes", "ptfx_smoke_wispy_anim" };

        public static readonly string[] NoTracerTextureNames =
            { MainTextureName, HeatTextureName, RgTextureName };

        public static bool ReplaceSmokeTextures(string coreYptPath, string smokeDdsDir)
            => ReplaceNamedTextures(coreYptPath, smokeDdsDir, SmokeTextureNames, "Smoke");

        public static int ExtractSmokeDds(string cleanCoreYptPath, string outDir)
            => ExtractNamedDds(cleanCoreYptPath, outDir, SmokeTextureNames);

        public static bool ReplaceNamedTextures(string coreYptPath, string ddsDir, string[] textureNames, string logTag = "CoreTex")
        {
            if (!File.Exists(coreYptPath)) return false;

            string tempDir = Path.Combine(Path.GetTempPath(), logTag + "Patch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                var ypt = new ParticlesFileWrapper_GTA5_pc();
                ypt.Load(coreYptPath);

                var textures = ypt.Particles?.TextureDictionary?.Textures;
                if (textures == null || textures.Count == 0)
                {
                    Console.WriteLine($"[{logTag}] {coreYptPath}: TextureDictionary пустой - skip.");
                    return false;
                }

                var texList = new List<ITexture>();
                foreach (var t in textures) texList.Add(t);

                bool anyChange = false;
                foreach (var name in textureNames)
                    anyChange |= ReplaceFromFileAsIs(texList, ddsDir, name, tempDir);

                if (!anyChange)
                {
                    Console.WriteLine($"[{logTag}] Ни одна текстура не заменена (нет в core.ypt или нет .dds) - skip.");
                    return false;
                }

                string modded = Path.Combine(tempDir, "core_patched.ypt");
                if (!TrySaveVia2015Writer(texList, coreYptPath, modded, tempDir))
                {
                    Console.WriteLine($"[{logTag}] WARNING: 2015-writer недоступен - fallback на форк .Save (игра может забраковать).");
                    ypt.Save(modded);
                }
                VerifyRsc7HeaderOrWarn(modded);
                AssertReparseOrThrow(modded);

                File.Copy(modded, coreYptPath, overwrite: true);
                Console.WriteLine($"[{logTag}] → {coreYptPath} ({new FileInfo(coreYptPath).Length} bytes)");
                return true;
            }
            finally { try { Directory.Delete(tempDir, recursive: true); } catch { } }
        }

        public static int ExtractNamedDds(string coreYptPath, string outDir, string[] textureNames)
        {
            if (!File.Exists(coreYptPath)) return 0;
            Directory.CreateDirectory(outDir);

            var ypt = new ParticlesFileWrapper_GTA5_pc();
            ypt.Load(coreYptPath);
            var textures = ypt.Particles?.TextureDictionary?.Textures;
            if (textures == null || textures.Count == 0) return 0;

            var texList = new List<ITexture>();
            foreach (var t in textures) texList.Add(t);

            int n = 0;
            foreach (var name in textureNames)
            {
                var tex = FindTexture(texList, name);
                if (tex == null) continue;
                try
                {
                    RageLib.Helpers.DDSIO.SaveTextureData(tex, Path.Combine(outDir, name + ".dds"));
                    n++;
                }
                catch (Exception ex) { Console.WriteLine($"[CoreTex] export {name} failed: {ex.Message}"); }
            }
            return n;
        }

        private static readonly string[] QualityVariantTargets =
        {
            "x64/patch/data/effects/ptfx_hi.rpf/core.ypt",
            "x64/patch/data/effects/ptfx_lo.rpf/core.ypt",
        };

        private static void AppendQualityVariantTargets(TracerCustomizationRequest request, List<string> targetPaths)
        {
            if (string.IsNullOrWhiteSpace(request.GtaRootPath))
            {
                Console.WriteLine("[TracerBuilder][quality] GtaRootPath пуст - ptfx_hi/ptfx_lo пропущены " +
                                  "(нужен путь к игре для baseline). Перекрашен будет только core редукса.");
                return;
            }

            foreach (string variant in QualityVariantTargets)
            {
                if (targetPaths.Any(p => NormalizePath(p).Equals(variant, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.WriteLine($"[TracerBuilder][quality] '{variant}' уже есть в целях редукса - skip stage.");
                    continue;
                }

                string parentRpf = GetParentRpfPath(variant);
                bool baseDeletesParent = request.Manifest.Actions.Any(a =>
                    a.Type == ActionType.Delete &&
                    NormalizePath(a.TargetPath).Equals(parentRpf, StringComparison.OrdinalIgnoreCase));
                if (baseDeletesParent)
                {
                    Console.WriteLine($"[TracerBuilder][quality] база удаляет '{parentRpf}' - stage '{variant}' не нужен (фолбэк на ptfx.rpf покрывает все качества).");
                    continue;
                }

                string physicalPath = Path.Combine(
                    request.PatchRootDirectory,
                    PatchFilesDir,
                    variant.Replace('/', Path.DirectorySeparatorChar));

                if (!File.Exists(physicalPath))
                {
                    byte[]? clean = PatchCustomizationSupport.GetCleanBytesForExactPath(request.GtaRootPath, variant);
                    if (clean == null || clean.Length == 0)
                    {
                        Console.WriteLine($"[TracerBuilder][quality] чистый baseline для '{variant}' не найден в update.rpf - skip.");
                        continue;
                    }

                    string? dir = Path.GetDirectoryName(physicalPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllBytes(physicalPath, clean);
                    Console.WriteLine($"[TracerBuilder][quality] staged baseline '{variant}' из update.rpf ({clean.Length} b).");
                }
                else
                {
                    Console.WriteLine($"[TracerBuilder][quality] '{variant}' уже в patch_files - перекрашиваем существующий.");
                }

                targetPaths.Add(variant);
            }
        }

        private static string GetParentRpfPath(string internalPath)
        {
            string norm = NormalizePath(internalPath);
            int idx = norm.LastIndexOf('/');
            return idx > 0 ? norm.Substring(0, idx) : norm;
        }

        private static bool ProcessCrossInjectWholeFile(
            TracerCustomizationRequest request,
            ComponentInfo tracersInfo,
            string debugLogDir)
        {
            string donorRoot = request.DonorReduxPath!;
            Console.WriteLine($"[TracerBuilder][CrossInject] Donor: {donorRoot}");

            string? donorCore = ResolveDonorCoreYpt(donorRoot);
            if (donorCore == null)
            {
                donorCore = TryExtractDonorCoreFromNested(donorRoot);
            }
            if (donorCore == null || !File.Exists(donorCore))
                throw new FileNotFoundException(
                    Loc.T("error.donorCoreYptNotFound", ("root", donorRoot)));

            long donorSize = new FileInfo(donorCore).Length;
            Console.WriteLine($"[TracerBuilder][CrossInject] донорский core.ypt целиком: {donorCore} ({donorSize} b)");
            if (request.UseCleanBaseline)
                Console.WriteLine("[TracerBuilder][CrossInject] UseCleanBaseline: при целиковом переносе эффекты = эффекты донора (для трейсер-паков это ваниль+трейсер).");

            bool any = false;
            foreach (string internalPath in tracersInfo.InternalPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string normalized = NormalizePath(internalPath);
                string physicalPath = Path.Combine(
                    request.PatchRootDirectory,
                    PatchFilesDir,
                    normalized.Replace('/', Path.DirectorySeparatorChar));

                Console.WriteLine($"[TracerBuilder] === Target (whole-file): {normalized} ===");
                bool existed = File.Exists(physicalPath);

                TextureSnapshot? baseBlood = null;
                if (!request.InjectDonorBlood)
                {
                    Console.WriteLine($"[TracerBuilder][CrossInject] возврат крови базы отключён (пересборка ломает донорский файл) - кровь донора остаётся.");
                }

                string? physDir = Path.GetDirectoryName(physicalPath);
                if (!string.IsNullOrEmpty(physDir)) Directory.CreateDirectory(physDir);
                File.Copy(donorCore, physicalPath, overwrite: true);
                Console.WriteLine($"[TracerBuilder][CrossInject] донор → {normalized} (целиком, {donorSize} b)");

                if (baseBlood != null)
                {
                    bool bloodOk = TryApplyTextureSnapshotVia2015(physicalPath, baseBlood);
                    Console.WriteLine(bloodOk
                        ? $"[TracerBuilder][CrossInject] кровь базы возвращена поверх донора ({BloodTextureName})."
                        : $"[TracerBuilder][CrossInject] WARNING: вернуть кровь базы не удалось - остаётся донорская.");
                }

                if (request.OverrideColorWithPicker)
                {
                    try
                    {
                        int n = TracerColorPatcher.PatchTracerColor(physicalPath, request.Red, request.Green, request.Blue);
                        Console.WriteLine($"[TracerBuilder][CrossInject] пикер: цвет → RGB({request.Red},{request.Green},{request.Blue}), keyframes={n}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[TracerBuilder][CrossInject] WARNING: перекраска пикером не удалась ({ex.Message}) - цвет донора сохранён.");
                    }
                }

                TrySaveDiagSnapshot(debugLogDir, "04_final_wholefile.ypt", physicalPath);

                PatchCustomizationSupport.UpsertPatchAction(
                    request.Manifest,
                    request.PatchRootDirectory,
                    new PatchWorkspaceFile
                    {
                        TargetPath   = normalized,
                        PhysicalPath = physicalPath,
                        ActionType   = existed ? ActionType.Replace : ActionType.Import
                    });

                any = true;
            }

            if (any)
                EnsureQualityVariantDeletes(request);

            return any;
        }

        private static string? TryExtractDonorCoreFromNested(string donorRoot)
        {
            try
            {
                var donorMap = TryLoadComponentMap(donorRoot);
                if (donorMap?.Components == null ||
                    !donorMap.Components.TryGetValue(ComponentKey, out ComponentInfo? info) ||
                    info?.InternalPaths == null || info.InternalPaths.Count == 0)
                    return null;

                string fileName = Path.GetFileName(NormalizePath(info.InternalPaths[0]).Replace('/', Path.DirectorySeparatorChar));
                var donorComponentDir = Path.Combine(donorRoot, "components", ComponentKey);
                var nestedBytes = PatchCustomizationSupport.TryFindFileBytesInDir(donorComponentDir, fileName);
                if (nestedBytes == null) return null;

                var extractDir = Path.Combine(Path.GetTempPath(), "TracerDonorExtract_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(extractDir);
                string outPath = Path.Combine(extractDir, fileName);
                File.WriteAllBytes(outPath, nestedBytes);
                Console.WriteLine($"[TracerBuilder][CrossInject] донорский {fileName} извлечён из nested rpf ({nestedBytes.Length} b)");
                return outPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder][CrossInject] nested-извлечение донора не удалось: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void EnsureQualityVariantDeletes(TracerCustomizationRequest request)
        {
            foreach (string variant in QualityVariantTargets)
            {
                string parentRpf = GetParentRpfPath(variant);
                string parentPrefix = parentRpf + "/";

                var nested = request.Manifest.Actions
                    .Where(a => NormalizePath(a.TargetPath).StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var a in nested)
                {
                    request.Manifest.Actions.Remove(a);
                    Console.WriteLine($"[TracerBuilder][CrossInject] удалён конфликтующий экшен {a.Type} '{a.TargetPath}' (родитель {parentRpf} удаляется).");
                }

                string stagedPhysical = Path.Combine(
                    request.PatchRootDirectory,
                    PatchFilesDir,
                    NormalizePath(variant).Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(stagedPhysical))
                {
                    try { File.Delete(stagedPhysical); } catch { }
                }

                bool hasDelete = request.Manifest.Actions.Any(a =>
                    a.Type == ActionType.Delete &&
                    NormalizePath(a.TargetPath).Equals(parentRpf, StringComparison.OrdinalIgnoreCase));
                bool hasOther = request.Manifest.Actions.Any(a =>
                    a.Type != ActionType.Delete &&
                    NormalizePath(a.TargetPath).Equals(parentRpf, StringComparison.OrdinalIgnoreCase));

                if (!hasDelete && !hasOther)
                {
                    request.Manifest.Actions.Add(new PatchAction
                    {
                        Type = ActionType.Delete,
                        TargetPath = parentRpf
                    });
                    Console.WriteLine($"[TracerBuilder][CrossInject] добавлен Delete '{parentRpf}' (фолбэк игры на ptfx.rpf при любом Particle Quality).");
                }
                else if (hasOther)
                {
                    Console.WriteLine($"[TracerBuilder][CrossInject] WARNING: база имеет не-Delete экшен на '{parentRpf}' - оставлен как есть, донорский трейсер может не отображаться на этом Particle Quality.");
                }
            }
        }

        private sealed class TextureSnapshot
        {
            public string Name = "";
            public int Width;
            public int Height;
            public int MipMapLevels;
            public int Stride;
            public uint Format;
            public byte[] Data = Array.Empty<byte>();
        }

        private static TextureSnapshot? TrySnapshotTexture(string yptPath, string textureName)
        {
            try
            {
                var ypt = new ParticlesFileWrapper_GTA5_pc();
                ypt.Load(yptPath);
                var textures = ypt.Particles?.TextureDictionary?.Textures;
                if (textures == null) return null;
                var list = new List<ITexture>();
                foreach (var t in textures) list.Add(t);
                var tex = FindTexture(list, textureName);
                if (tex == null) return null;
                byte[] data = tex.GetTextureData();
                if (data == null || data.Length == 0) return null;
                return new TextureSnapshot
                {
                    Name = textureName,
                    Width = tex.Width,
                    Height = tex.Height,
                    MipMapLevels = tex.MipMapLevels,
                    Stride = tex.Stride,
                    Format = (uint)tex.Format,
                    Data = data,
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder][CrossInject] снапшот '{textureName}' из {yptPath} не удался: {ex.Message}");
                return null;
            }
        }

        private static TextureSnapshot? TrySnapshotCleanTexture(string? gtaRootPath, string internalPath, string textureName)
        {
            if (string.IsNullOrWhiteSpace(gtaRootPath)) return null;
            try
            {
                byte[]? clean = PatchCustomizationSupport.GetCleanBytesForExactPath(gtaRootPath, internalPath);
                if (clean == null || clean.Length == 0) return null;
                string tmp = Path.Combine(Path.GetTempPath(), "TracerCleanSnap_" + Guid.NewGuid().ToString("N") + ".ypt");
                File.WriteAllBytes(tmp, clean);
                try { return TrySnapshotTexture(tmp, textureName); }
                finally { try { File.Delete(tmp); } catch { } }
            }
            catch { return null; }
        }

        private static bool TryApplyTextureSnapshotVia2015(string yptPath, TextureSnapshot snap)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "TracerBloodRestore_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string specDir = Path.Combine(tempDir, "spec2015");
                Directory.CreateDirectory(specDir);

                File.WriteAllText(
                    Path.Combine(specDir, snap.Name + ".meta"),
                    string.Join("\n", new[] { snap.Width, snap.Height, snap.MipMapLevels, snap.Stride, (int)snap.Format }));
                File.WriteAllBytes(Path.Combine(specDir, snap.Name + ".bin"), snap.Data);

                string outYpt = Path.Combine(tempDir, "out.ypt");
                if (!RunWriter2015(yptPath, outYpt, specDir)) return false;

                AssertReparseOrThrow(outYpt);
                File.Copy(outYpt, yptPath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder][CrossInject] возврат '{snap.Name}' не удался: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        }

        private static void ValidateRequest(TracerCustomizationRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.PatchRootDirectory))
                throw new ArgumentException("PatchRootDirectory is required.");
            if (!Directory.Exists(r.PatchRootDirectory))
                throw new DirectoryNotFoundException($"PatchRootDirectory not found: {r.PatchRootDirectory}");
            if (r.Manifest == null)
                throw new ArgumentException("Manifest is required.");

            switch (r.Scenario)
            {
                case TracerScenario.NoOpSaveTest:

                    break;

                case TracerScenario.CwNoOpSaveTest:

                    break;

                case TracerScenario.ColorOnly:
                    break;

                case TracerScenario.MasterTexture:
                    if (string.IsNullOrWhiteSpace(r.ModelDirectory) || !Directory.Exists(r.ModelDirectory))
                        throw new DirectoryNotFoundException(
                            $"MasterTexture: ModelDirectory not found: {r.ModelDirectory}");
                    string mainDds = Path.Combine(r.ModelDirectory!, MainTextureName + ".dds");
                    if (!File.Exists(mainDds))
                        throw new FileNotFoundException(
                            $"MasterTexture: main DDS not found: {mainDds}");
                    break;

                case TracerScenario.CrossInject:
                    if (string.IsNullOrWhiteSpace(r.DonorReduxPath) || !Directory.Exists(r.DonorReduxPath))
                        throw new DirectoryNotFoundException(
                            $"CrossInject: DonorReduxPath not found: {r.DonorReduxPath}");
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(r.Scenario));
            }
        }

        private static ResolvedComponentMap? TryLoadComponentMap(string reduxRoot)
        {
            string path = Path.Combine(reduxRoot, ComponentMapFn);
            if (!File.Exists(path)) return null;

            try
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<ResolvedComponentMap>(json, JsonOpts);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder] Ошибка чтения {path}: {ex.Message}");
                return null;
            }
        }

        private sealed class DonorTexture
        {
            public string Name { get; init; } = "";
            public string? DdsPath { get; init; }
            public int Width { get; init; }
            public int Height { get; init; }
            public int MipMapLevels { get; init; }
            public int Stride { get; init; }
            public TextureFormat Format { get; init; }
            public byte[] RawData { get; init; } = Array.Empty<byte>();
        }

        private sealed class DonorTextures
        {
            public DonorTexture? Main  { get; init; }
            public DonorTexture? Heat  { get; init; }
            public DonorTexture? Rg    { get; init; }
            public DonorTexture? Blood { get; init; }

            public string? CoreYptPath { get; set; }
        }

        private static string? ResolveDonorCoreYpt(string donorRoot)
        {
            try
            {
                string compDir = Path.Combine(donorRoot, "components", ComponentKey);
                string? coreYpt = Directory.Exists(compDir)
                    ? Directory.EnumerateFiles(compDir, "*.ypt", SearchOption.AllDirectories).FirstOrDefault()
                    : null;

                if (coreYpt == null)
                {
                    var map = TryLoadComponentMap(donorRoot);
                    if (map?.Components != null &&
                        map.Components.TryGetValue(ComponentKey, out ComponentInfo? info) &&
                        info?.InternalPaths != null)
                    {
                        foreach (var ip in info.InternalPaths)
                        {
                            string p = Path.Combine(donorRoot, PatchFilesDir,
                                NormalizePath(ip).Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(p)) { coreYpt = p; break; }
                        }
                    }
                }

                if (coreYpt != null && File.Exists(coreYpt))
                {
                    Console.WriteLine($"[TracerBuilder][CrossInject] донорский core.ypt для переноса: {coreYpt}");
                    return coreYpt;
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder][CrossInject] резолв донорского core.ypt не удался: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static DonorTextures PrepareDonorTextures(
            TracerCustomizationRequest request,
            string debugLogDir)
        {
            string donorRoot = request.DonorReduxPath!;
            Console.WriteLine($"[TracerBuilder][CrossInject] Donor: {donorRoot}");

            var donorCoreYpt = ResolveDonorCoreYpt(donorRoot);

            string donorDumpDir = Path.Combine(donorRoot, "components", ComponentKey);
            var dumped = TracerComponentTextures.TryReadDumped(donorDumpDir);
            if (dumped != null)
            {
                Console.WriteLine($"[TracerBuilder][CrossInject] донор имеет пред-извлечённые DDS ({dumped.Count}) - читаю из tracers.zip");
                var fromDump = BuildDonorTexturesFromDump(dumped);
                if (fromDump.Main == null)
                    throw new InvalidOperationException(
                        Loc.T("error.donorDumpNoTexture", ("texture", MainTextureName)));
                fromDump.CoreYptPath = donorCoreYpt;
                return fromDump;
            }

            Console.WriteLine("[TracerBuilder][CrossInject] пред-извлечённых DDS нет - fallback на live-извлечение из core.ypt (старый редукс)");

            var donorMap = TryLoadComponentMap(donorRoot);
            if (donorMap?.Components == null ||
                !donorMap.Components.TryGetValue(ComponentKey, out ComponentInfo? donorInfo) ||
                donorInfo == null ||
                !donorInfo.IsFound ||
                donorInfo.InternalPaths == null ||
                donorInfo.InternalPaths.Count == 0)
            {
                throw new InvalidOperationException(
                    Loc.T("error.donorNoTracersComponent", ("root", donorRoot)));
            }

            string donorInternal = NormalizePath(donorInfo.InternalPaths[0]);
            string donorPhysical = Path.Combine(
                donorRoot,
                PatchFilesDir,
                donorInternal.Replace('/', Path.DirectorySeparatorChar));

            Console.WriteLine($"[TracerBuilder][CrossInject] Donor core.ypt: {donorPhysical}");

            string? donorPhysicalForLoad = File.Exists(donorPhysical) ? donorPhysical : null;
            if (donorPhysicalForLoad is null)
            {
                Console.WriteLine($"[TracerBuilder][CrossInject] flat donor core.ypt не найден, ищу в nested rpf {donorRoot}/components/tracers");
                var donorComponentDir = Path.Combine(donorRoot, "components", ComponentKey);
                var fileName = Path.GetFileName(donorInternal.Replace('/', Path.DirectorySeparatorChar));
                var nestedBytes = PatchCustomizationSupport.TryFindFileBytesInDir(donorComponentDir, fileName);
                if (nestedBytes is null)
                {
                    throw new FileNotFoundException(
                        Loc.T("error.donorFileNotFoundAnywhere",
                            ("file", fileName), ("flat", donorPhysical), ("nested", donorComponentDir)));
                }
                var donorExtractDir = Path.Combine(Path.GetTempPath(),
                    "TracerDonorExtract_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(donorExtractDir);
                donorPhysicalForLoad = Path.Combine(donorExtractDir, fileName);
                File.WriteAllBytes(donorPhysicalForLoad, nestedBytes);
                Console.WriteLine($"[TracerBuilder][CrossInject] extracted donor {fileName} from nested rpf to {donorPhysicalForLoad} ({nestedBytes.Length} b)");
            }

            var donorYpt = new ParticlesFileWrapper_GTA5_pc();
            donorYpt.Load(donorPhysicalForLoad);

            var donorTextures = donorYpt.Particles?.TextureDictionary?.Textures;
            if (donorTextures == null || donorTextures.Count == 0)
                throw new InvalidOperationException(
                    Loc.T("error.donorCoreYptEmptyTextures", ("path", donorPhysical)));

            string donorTempDir = Path.Combine(Path.GetTempPath(),
                "TracerDonor_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(donorTempDir);

            var donorList = new List<ITexture>();
            foreach (var t in donorTextures) donorList.Add(t);

            var main  = ExtractDonorTexture(donorList, MainTextureName,  donorTempDir, debugLogDir, required: true);
            var heat  = ExtractDonorTexture(donorList, HeatTextureName,  donorTempDir, debugLogDir, required: false);
            var rg    = ExtractDonorTexture(donorList, RgTextureName,    donorTempDir, debugLogDir, required: false);
            var blood = ExtractDonorTexture(donorList, BloodTextureName, donorTempDir, debugLogDir, required: false);

            if (main == null)
                throw new InvalidOperationException(
                    Loc.T("error.donorNoTexture", ("texture", MainTextureName)));

            return new DonorTextures
            {
                Main  = main,
                Heat  = heat,
                Rg    = rg,
                Blood = blood,
                CoreYptPath = donorCoreYpt,
            };
        }

        private static DonorTextures BuildDonorTexturesFromDump(List<TracerComponentTextures.DumpedTexture> dumped)
        {
            DonorTexture? Map(string name)
            {
                var d = dumped.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (d == null) return null;
                return new DonorTexture
                {
                    Name = d.Name,
                    DdsPath = d.DdsPath,
                    Width = d.Width,
                    Height = d.Height,
                    MipMapLevels = d.MipMapLevels,
                    Stride = d.Stride,
                    Format = d.Format,
                    RawData = d.RawData,
                };
            }

            return new DonorTextures
            {
                Main  = Map(MainTextureName),
                Heat  = Map(HeatTextureName),
                Rg    = Map(RgTextureName),
                Blood = Map(BloodTextureName),
            };
        }

        private static DonorTexture? ExtractDonorTexture(
            List<ITexture> donorTextures,
            string name,
            string donorTempDir,
            string debugLogDir,
            bool required)
        {
            var tex = FindTexture(donorTextures, name);
            if (tex == null)
            {
                string lvl = required ? "WARNING" : "info";
                Console.WriteLine($"[TracerBuilder][CrossInject] {lvl}: donor не имеет '{name}' - skip");
                return null;
            }

            byte[] raw;
            try { raw = tex.GetTextureData(); }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder][CrossInject] WARNING: '{name}' GetTextureData упал ({ex.GetType().Name}) - skip");
                return null;
            }

            string? ddsPath = null;
            if (IsTextureDataSane(tex))
            {
                try
                {
                    string p = Path.Combine(donorTempDir, name + ".dds");
                    DDSIO.SaveTextureData(tex, p);
                    ddsPath = p;
                    CopyDebugArtifact(p, debugLogDir, "donor_" + name + ".dds");
                    Console.WriteLine($"[TracerBuilder][CrossInject] extracted donor '{name}' → DDS " +
                                      $"(W={tex.Width} H={tex.Height} {tex.Format} mips={tex.MipMapLevels})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TracerBuilder][CrossInject] WARNING: '{name}' SaveTextureData упал " +
                                      $"({ex.GetType().Name}) - оставляем только сырые байты для fallback");
                }
            }
            else
            {
                Console.WriteLine($"[TracerBuilder][CrossInject] WARNING: '{name}' мип-чейн битый " +
                                  $"(данных {raw.LongLength} б) - DDS не пишем, только сырые байты для fallback");
            }

            return new DonorTexture
            {
                Name = name,
                DdsPath = ddsPath,
                Width = tex.Width,
                Height = tex.Height,
                MipMapLevels = tex.MipMapLevels,
                Stride = tex.Stride,
                Format = tex.Format,
                RawData = raw,
            };
        }

        private static bool ProcessTarget(
            TracerCustomizationRequest request,
            string internalPath,
            string physicalPath,
            string debugLogDir,
            DonorTextures? donorDds)
        {
            string tempDir = Path.Combine(Path.GetTempPath(),
                "TracerPatch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                if (request.UseCleanBaseline && request.Scenario == TracerScenario.CrossInject)
                    TryStageCleanBaseline(request, internalPath, physicalPath);

                var ypt = new ParticlesFileWrapper_GTA5_pc();
                ypt.Load(physicalPath);

                var textures = ypt.Particles?.TextureDictionary?.Textures;
                if (textures == null || textures.Count == 0)
                {
                    Console.WriteLine(
                        $"[TracerBuilder] {internalPath}: TextureDictionary пустой - skip. " +
                        "Возможно файл битый (известный баг RpfDiffEngine на RSC7 - отдельная задача).");
                    return false;
                }

                var texList = new List<ITexture>();
                foreach (var t in textures) texList.Add(t);

                WriteSimpleTextureDump(debugLogDir, internalPath, texList);

                TrySaveDiagSnapshot(debugLogDir, "01_source.ypt", physicalPath);

                try
                {
                    string diagNoOpPath = Path.Combine(debugLogDir, "02_ragelib_noop.ypt");
                    ypt.Save(diagNoOpPath);
                    Console.WriteLine($"[TracerBuilder][DIAG] saved 02_ragelib_noop.ypt ({new FileInfo(diagNoOpPath).Length} bytes)");

                }
                catch (Exception diagEx)
                {
                    Console.WriteLine($"[TracerBuilder][DIAG] 02_ragelib_noop save failed: {diagEx.Message}");
                }

                bool anyChange = ApplyScenario(request, texList, tempDir, debugLogDir, donorDds);

                if (!anyChange)
                {
                    Console.WriteLine("[TracerBuilder] Сценарий не внёс изменений - skip save.");
                    return false;
                }

                Console.WriteLine($"[TracerBuilder][DIAG] ApplyScenario returned anyChange={anyChange}. Next: Save → 04_final.ypt");

                string moddedYpt = Path.Combine(tempDir, "core_modded.ypt");

                bool saved2015 = TrySaveVia2015Writer(texList, physicalPath, moddedYpt, tempDir);
                if (!saved2015)
                {
                    Console.WriteLine("[TracerBuilder] WARNING: 2015-writer недоступен/упал - fallback на форк .Save. " +
                                      "ИГРА МОЖЕТ ЗАБРАКОВАТЬ core.ypt (ERR_GEN_INVALID)! Проверь staging YptWriter2015.");
                    ypt.Save(moddedYpt);
                }
                VerifyRsc7HeaderOrWarn(moddedYpt);

                AssertReparseOrThrow(moddedYpt);

                File.Copy(moddedYpt, physicalPath, overwrite: true);
                long sz = new FileInfo(physicalPath).Length;
                Console.WriteLine($"[TracerBuilder] → {physicalPath} ({sz} bytes)");

                ApplyImportTracerColorIfNeeded(request, physicalPath, donorDds);

                TrySaveDiagSnapshot(debugLogDir, "04_final.ypt", physicalPath);
                Console.WriteLine($"[TracerBuilder][DIAG] snapshots saved to {debugLogDir}");

                PatchCustomizationSupport.UpsertPatchAction(
                    request.Manifest,
                    request.PatchRootDirectory,
                    new PatchWorkspaceFile
                    {
                        TargetPath   = internalPath,
                        PhysicalPath = physicalPath,
                        ActionType   = ActionType.Replace
                    });

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[TracerBuilder] Ошибка обработки {internalPath}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        }

        private static bool ProcessTargetColorPatch(
            TracerCustomizationRequest request,
            string internalPath,
            string physicalPath)
        {
            try
            {
                int patched = TracerColorPatcher.PatchTracerColor(
                    physicalPath, request.Red, request.Green, request.Blue);
                if (patched == 0)
                {
                    Console.WriteLine($"[TracerBuilder] ColorOnly: {internalPath} - нечего красить, skip.");
                    return false;
                }

                PatchCustomizationSupport.UpsertPatchAction(
                    request.Manifest,
                    request.PatchRootDirectory,
                    new PatchWorkspaceFile
                    {
                        TargetPath   = internalPath,
                        PhysicalPath = physicalPath,
                        ActionType   = ActionType.Replace
                    });
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[TracerBuilder] ColorOnly: ошибка перекраски {internalPath}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static void ApplyImportTracerColorIfNeeded(
            TracerCustomizationRequest request, string physicalPath, DonorTextures? donorDds)
        {
            if (request.Scenario != TracerScenario.CrossInject) return;

            string? donorYpt = donorDds?.CoreYptPath;

            if (!string.IsNullOrEmpty(donorYpt) && File.Exists(donorYpt))
            {
                try { TracerColorPatcher.TransferDonorTracerRules(physicalPath, donorYpt!); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TracerBuilder] CrossInject: перенос плотности донора не удался " +
                                      $"({ex.GetType().Name}: {ex.Message}) - текстуры/цвет применятся, плотность базовая.");
                }
            }

            (byte r, byte g, byte b)? color = request.OverrideColorWithPicker
                ? (request.Red, request.Green, request.Blue)
                : (!string.IsNullOrEmpty(donorYpt) && File.Exists(donorYpt)
                    ? TracerColorPatcher.TryReadDominantTracerColor(donorYpt!)
                    : null);

            if (color != null)
            {
                try
                {
                    int n = TracerColorPatcher.PatchTracerColor(physicalPath, color.Value.r, color.Value.g, color.Value.b);
                    Console.WriteLine($"[TracerBuilder] CrossInject: цвет всех трейсеров → RGB({color.Value.r},{color.Value.g},{color.Value.b}) " +
                                      $"({(request.OverrideColorWithPicker ? "пикер" : "донор")}), keyframes={n}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TracerBuilder] CrossInject: гарантированная перекраска не удалась ({ex.GetType().Name}: {ex.Message}).");
                }
            }
            else
            {
                Console.WriteLine("[TracerBuilder] CrossInject: цвет не определён (нет донорского ypt, override off) - цвет базовый.");
            }
        }

        private static bool ProcessTargetWithCodeWalker(
            string internalPath,
            string physicalPath,
            string debugLogDir)
        {
            try
            {
                Console.WriteLine($"[TracerBuilder][CW] Load (CodeWalker): {physicalPath}");
                byte[] inputBytes = File.ReadAllBytes(physicalPath);
                long inputSize = inputBytes.LongLength;

                var ypt = new CwGameFiles.YptFile();
                ypt.Load(inputBytes);

                var ptfx = ypt.PtfxList;
                var td = ptfx?.TextureDictionary;
                int texCount = td?.Textures?.data_items?.Length ?? -1;
                Console.WriteLine($"[TracerBuilder][CW] PtfxList != null: {ptfx != null}, " +
                                  $"TextureDictionary.Textures.Count: {texCount}");

                Console.WriteLine("[TracerBuilder][CW] Save без изменений...");
                byte[] outputBytes = ypt.Save();
                long outputSize = outputBytes.LongLength;

                File.WriteAllBytes(physicalPath, outputBytes);
                Console.WriteLine($"[TracerBuilder][CW] Written: {physicalPath} " +
                                  $"(in={inputSize} bytes, out={outputSize} bytes)");

                VerifyRsc7HeaderOrWarn(physicalPath);

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[TracerBuilder][CW] Ошибка при CodeWalker round-trip {internalPath}: " +
                    $"{ex.GetType().Name}: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return false;
            }
        }

        private static bool ApplyScenario(
            TracerCustomizationRequest request,
            List<ITexture> textures,
            string tempDir,
            string debugLogDir,
            DonorTextures? donorDds)
        {
            return request.Scenario switch
            {
                TracerScenario.NoOpSaveTest  => ApplyNoOp(textures),
                TracerScenario.ColorOnly     => ApplyColorOnly(request, textures, tempDir, debugLogDir),
                TracerScenario.MasterTexture => ApplyMasterTexture(request, textures, tempDir, debugLogDir),
                TracerScenario.CrossInject   => ApplyCrossInject(request, textures, tempDir, debugLogDir, donorDds!),
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        private static bool ApplyNoOp(List<ITexture> textures)
        {
            Console.WriteLine($"[TracerBuilder][NoOpSaveTest] Textures in ypt: {textures.Count}. " +
                              "Save будет вызван БЕЗ модификации текстур.");
            return true;
        }

        private static bool ApplyColorOnly(
            TracerCustomizationRequest request,
            List<ITexture> textures,
            string tempDir,
            string debugLogDir)
        {
            var mainTex = FindTexture(textures, MainTextureName);
            if (mainTex == null)
            {
                Console.WriteLine($"[TracerBuilder] ColorOnly: {MainTextureName} не найдена - skip");
                return false;
            }

            string sourceDds = Path.Combine(tempDir, MainTextureName + "_src.dds");
            DDSIO.SaveTextureData(mainTex, sourceDds);
            CopyDebugArtifact(sourceDds, debugLogDir, MainTextureName + "_0_source.dds");

            string recoloredDds = RecolorDds(
                sourceDds, mainTex.Format.ToString(), tempDir, debugLogDir,
                MainTextureName, request.Red, request.Green, request.Blue);

            DDSIO.LoadTextureData(mainTex, recoloredDds);
            Console.WriteLine($"[TracerBuilder] ColorOnly: {MainTextureName} recolored → RGB({request.Red},{request.Green},{request.Blue})");
            return true;
        }

        private static bool ApplyMasterTexture(
            TracerCustomizationRequest request,
            List<ITexture> textures,
            string tempDir,
            string debugLogDir)
        {
            bool anyChange = false;

            string? mainSrc = ResolveModelTexturePath(request.ModelDirectory!, MainTextureName);
            if (mainSrc != null)
            {
                var mainTex = FindTexture(textures, MainTextureName);
                if (mainTex == null)
                {
                    Console.WriteLine($"[TracerBuilder] MasterTexture: {MainTextureName} нет в core.ypt - skip");
                }
                else
                {
                    string recolored = RecolorDds(
                        mainSrc, mainTex.Format.ToString(), tempDir, debugLogDir,
                        MainTextureName, request.Red, request.Green, request.Blue);
                    DDSIO.LoadTextureData(mainTex, recolored);
                    Console.WriteLine($"[TracerBuilder] MasterTexture: {MainTextureName} ← {mainSrc} + recolor");
                    anyChange = true;
                }
            }

            anyChange |= ReplaceFromFileAsIs(textures, request.ModelDirectory!, HeatTextureName, debugLogDir);
            anyChange |= ReplaceFromFileAsIs(textures, request.ModelDirectory!, RgTextureName,   debugLogDir);

            return anyChange;
        }

        private static bool ApplyCrossInject(
            TracerCustomizationRequest request,
            List<ITexture> textures,
            string tempDir,
            string debugLogDir,
            DonorTextures donorDds)
        {
            bool anyChange = false;

            if (donorDds.Main != null)
            {
                var mainTex = FindTexture(textures, MainTextureName);
                if (mainTex == null)
                {
                    Console.WriteLine($"[TracerBuilder] CrossInject: {MainTextureName} нет в target core.ypt - skip");
                }
                else
                {
                    if (donorDds.Main.DdsPath != null)
                    {
                        string recolored = RecolorDds(
                            donorDds.Main.DdsPath, mainTex.Format.ToString(), tempDir, debugLogDir,
                            MainTextureName, request.Red, request.Green, request.Blue);
                        if (InjectFromDds(mainTex, recolored, donorDds.Main, MainTextureName + " + recolor"))
                            anyChange = true;
                    }
                    else
                    {
                        Console.WriteLine($"[TracerBuilder] CrossInject: {MainTextureName} донорский DDS недоступен - " +
                                          "прямое копирование без перекраски (fallback)");
                        if (InjectDirect(mainTex, donorDds.Main, MainTextureName))
                            anyChange = true;
                    }
                }
            }

            anyChange |= InjectAsIs(textures, HeatTextureName, donorDds.Heat);
            anyChange |= InjectAsIs(textures, RgTextureName,   donorDds.Rg);

            if (request.InjectDonorBlood)
                anyChange |= InjectAsIs(textures, BloodTextureName, donorDds.Blood);
            else if (donorDds.Blood != null)
                Console.WriteLine($"[TracerBuilder] CrossInject: {BloodTextureName} у донора есть, но InjectDonorBlood=false - не трогаем кровь базы");

            return anyChange;
        }

        private static bool InjectAsIs(List<ITexture> textures, string name, DonorTexture? donor)
        {
            if (donor == null) return false;
            var tex = FindTexture(textures, name);
            if (tex == null)
            {
                Console.WriteLine($"[TracerBuilder] CrossInject: {name} нет в target core.ypt - skip");
                return false;
            }
            if (donor.DdsPath != null)
                return InjectFromDds(tex, donor.DdsPath, donor, name);
            Console.WriteLine($"[TracerBuilder] CrossInject: {name} донорский DDS недоступен - прямое копирование (fallback)");
            return InjectDirect(tex, donor, name);
        }

        private static bool InjectFromDds(ITexture targetTex, string ddsPath, DonorTexture donor, string label)
        {
            try
            {
                DDSIO.LoadTextureData(targetTex, ddsPath);
                Console.WriteLine($"[TracerBuilder] CrossInject: {label} ← donor (DDS)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder] CrossInject: {label} DDS load FAILED " +
                                  $"({ex.GetType().Name}: {ex.Message}) - прямое копирование в память");
                return InjectDirect(targetTex, donor, label);
            }
        }

        private static bool InjectDirect(ITexture targetTex, DonorTexture donor, string label)
        {
            try
            {
                targetTex.Reset(donor.Width, donor.Height, donor.MipMapLevels, donor.Stride, donor.Format);
                targetTex.SetTextureData(donor.RawData);
                Console.WriteLine($"[TracerBuilder] CrossInject: {label} ← donor (direct memory copy, {donor.RawData.Length} b)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder] CrossInject: {label} direct copy FAILED ({ex.GetType().Name}: {ex.Message}) - skip");
                return false;
            }
        }

        private static bool IsTextureDataSane(ITexture tex)
        {
            long expected = 0;
            int w = tex.Width, h = tex.Height;
            for (int m = 0; m < tex.MipMapLevels; m++)
            {
                long lvl = tex.Format switch
                {
                    TextureFormat.D3DFMT_DXT1 => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8L,
                    TextureFormat.D3DFMT_DXT3 or TextureFormat.D3DFMT_DXT5 or
                    TextureFormat.D3DFMT_ATI2 or TextureFormat.D3DFMT_BC7
                        => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 16L,
                    TextureFormat.D3DFMT_ATI1 => Math.Max(1, (w + 3) / 4) * Math.Max(1, (h + 3) / 4) * 8L,
                    TextureFormat.D3DFMT_A8R8G8B8 => w * (long)h * 4,
                    TextureFormat.D3DFMT_L8 or TextureFormat.D3DFMT_A8 => w * (long)h,
                    _ => -1,
                };
                if (lvl < 0) return true;
                expected += lvl;
                w = Math.Max(1, w / 2);
                h = Math.Max(1, h / 2);
            }
            try { return tex.GetTextureData().LongLength == expected; }
            catch { return false; }
        }

        private static bool ReplaceFromFileAsIs(
            List<ITexture> textures,
            string sourceDir,
            string textureName,
            string debugLogDir)
        {
            string? src = ResolveModelTexturePath(sourceDir, textureName);
            if (src == null) return false;

            var tex = FindTexture(textures, textureName);
            if (tex == null)
            {
                Console.WriteLine($"[TracerBuilder] {textureName} нет в core.ypt - skip");
                return false;
            }

            DDSIO.LoadTextureData(tex, src);
            CopyDebugArtifact(src, debugLogDir, textureName + "_used.dds");
            Console.WriteLine($"[TracerBuilder] {textureName} ← {src} (as-is)");
            return true;
        }

        private static string RecolorDds(
            string sourceDdsPath,
            string originalFormat,
            string tempDir,
            string debugLogDir,
            string textureName,
            byte red, byte green, byte blue)
        {
            string pngPath      = Path.Combine(tempDir, textureName + "_1_original.png");
            string recoloredPng = Path.Combine(tempDir, textureName + "_2_recolored.png");
            string finalDdsPath = Path.Combine(tempDir, textureName + "_3_final.dds");

            using (var image = new MagickImage(sourceDdsPath))
                image.Write(pngPath);
            CopyDebugArtifact(pngPath, debugLogDir, textureName + "_1_original.png");

            var targetColor = global::System.Drawing.Color.FromArgb(255, red, green, blue);
            using (var bmp = new global::System.Drawing.Bitmap(pngPath))
            {
                for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    var p = bmp.GetPixel(x, y);
                    if (p.A == 0) continue;
                    float intensity = (p.R + p.G + p.B) / (3.0f * 255.0f);
                    int nr = ClampToByte(targetColor.R * intensity);
                    int ng = ClampToByte(targetColor.G * intensity);
                    int nb = ClampToByte(targetColor.B * intensity);
                    bmp.SetPixel(x, y, global::System.Drawing.Color.FromArgb(p.A, nr, ng, nb));
                }
                bmp.Save(recoloredPng, global::System.Drawing.Imaging.ImageFormat.Png);
            }
            CopyDebugArtifact(recoloredPng, debugLogDir, textureName + "_2_recolored.png");

            using (var image = new MagickImage(recoloredPng))
            {
                string compression = PickDdsCompression(originalFormat);
                image.Settings.SetDefine(MagickFormat.Dds, "compression", compression);
                image.Write(finalDdsPath);
            }
            CopyDebugArtifact(finalDdsPath, debugLogDir, textureName + "_3_final.dds");

            return finalDdsPath;
        }

        private static string PickDdsCompression(string originalFormat)
        {
            if (originalFormat.IndexOf("DXT1", StringComparison.OrdinalIgnoreCase) >= 0) return "dxt1";
            if (originalFormat.IndexOf("DXT5", StringComparison.OrdinalIgnoreCase) >= 0) return "dxt5";
            if (originalFormat.IndexOf("A8R8G8B8", StringComparison.OrdinalIgnoreCase) >= 0 ||
                originalFormat.IndexOf("ARGB", StringComparison.OrdinalIgnoreCase) >= 0) return "none";
            return "dxt5";
        }

        private static ITexture? FindTexture(IEnumerable<ITexture> textures, string name)
            => textures.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        private static string? ResolveModelTexturePath(string modelDir, string textureName)
        {
            string direct = Path.Combine(modelDir, textureName + ".dds");
            if (File.Exists(direct)) return direct;

            if (textureName.Equals(HeatTextureName, StringComparison.OrdinalIgnoreCase))
            {
                string typo = Path.Combine(modelDir, "ptfx_bullet_tacer_heat.dds");
                if (File.Exists(typo)) return typo;
            }
            return null;
        }

        private static string NormalizePath(string path)
            => (path ?? "").Replace('\\', '/').TrimStart('/');

        private static int ClampToByte(double v)
            => v < 0 ? 0 : (v > 255 ? 255 : (int)v);

        private static void CopyDebugArtifact(string sourcePath, string debugLogDir, string fileName)
        {
            if (!File.Exists(sourcePath)) return;
            try { File.Copy(sourcePath, Path.Combine(debugLogDir, fileName), overwrite: true); }
            catch { }
        }

        private static string SanitizePathSegment(string v)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            return string.Concat(v.Select(ch => invalid.Contains(ch) ? '_' : ch));
        }

        private static void TryDeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;
            try { Directory.Delete(path, true); } catch { }
        }

        private static void TrySaveDiagSnapshot(string debugLogDir, string snapshotName, string sourcePath)
        {
            try
            {
                if (!File.Exists(sourcePath)) return;
                string dest = Path.Combine(debugLogDir, snapshotName);
                File.Copy(sourcePath, dest, overwrite: true);
                long sz = new FileInfo(dest).Length;
                Console.WriteLine($"[TracerBuilder][DIAG] {snapshotName}: {sz} bytes  ({sourcePath})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder][DIAG] failed to save {snapshotName}: {ex.Message}");
            }
        }

        private static void TryStageCleanBaseline(
            TracerCustomizationRequest request, string internalPath, string physicalPath)
        {
            if (string.IsNullOrWhiteSpace(request.GtaRootPath))
            {
                Console.WriteLine("[TracerBuilder][baseline] GtaRootPath пуст - чистый baseline недоступен, " +
                                  "используем текущую цель из patch_files.");
                return;
            }
            try
            {
                byte[]? clean = PatchCustomizationSupport.GetCleanBytesForExactPath(request.GtaRootPath, internalPath);
                if (clean == null || clean.Length == 0)
                    clean = PatchCustomizationSupport.GetCleanOriginalBytes(request.GtaRootPath, new List<string> { internalPath });
                if (clean == null || clean.Length == 0)
                {
                    Console.WriteLine($"[TracerBuilder][baseline] чистый core.ypt не найден в update.rpf для '{internalPath}' - " +
                                      "используем текущую цель из patch_files.");
                    return;
                }
                File.WriteAllBytes(physicalPath, clean);
                Console.WriteLine($"[TracerBuilder][baseline] цель заменена на чистый baseline из update.rpf ({clean.Length} b)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder][baseline] не удалось получить чистый baseline " +
                                  $"({ex.GetType().Name}: {ex.Message}) - используем текущую цель.");
            }
        }

        private static bool TrySaveVia2015Writer(
            List<ITexture> textures, string originalYptPath, string outYpt, string tempDir)
        {
            string? exe = ResolveWriter2015Exe();
            if (exe == null)
            {
                Console.WriteLine("[TracerBuilder][2015] YptWriter2015.exe не найден в output - fallback на форк .Save");
                return false;
            }

            string specDir = Path.Combine(tempDir, "spec2015");
            Directory.CreateDirectory(specDir);

            int specCount = 0;
            foreach (var name in CanonicalTextureNames)
            {
                var tex = FindTexture(textures, name);
                if (tex == null) continue;
                byte[] data;
                try { data = tex.GetTextureData(); }
                catch { continue; }
                if (data == null || data.Length == 0) continue;

                File.WriteAllText(
                    Path.Combine(specDir, name + ".meta"),
                    string.Join("\n", new[] { tex.Width, tex.Height, tex.MipMapLevels, tex.Stride, (int)(uint)tex.Format }));
                File.WriteAllBytes(Path.Combine(specDir, name + ".bin"), data);
                specCount++;
            }
            if (specCount == 0)
            {
                Console.WriteLine("[TracerBuilder][2015] нет канонических текстур для spec - fallback");
                return false;
            }

            bool ok = RunWriter2015(originalYptPath, outYpt, specDir);
            if (ok) Console.WriteLine($"[TracerBuilder][2015] OK ({specCount} текстур)");
            return ok;
        }

        private static bool RunWriter2015(string originalYptPath, string outYpt, string specDir)
        {
            string? exe = ResolveWriter2015Exe();
            if (exe == null)
            {
                Console.WriteLine("[TracerBuilder][2015] YptWriter2015.exe не найден в output - fallback");
                return false;
            }

            try
            {
                var psi = new global::System.Diagnostics.ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add(originalYptPath);
                psi.ArgumentList.Add(outYpt);
                psi.ArgumentList.Add(specDir);

                using var p = global::System.Diagnostics.Process.Start(psi)!;
                string so = p.StandardOutput.ReadToEnd();
                string se = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(120000))
                {
                    try { p.Kill(true); } catch { }
                    Console.WriteLine("[TracerBuilder][2015] хелпер завис (>120с) - fallback");
                    return false;
                }

                if (p.ExitCode != 0)
                {
                    Console.WriteLine($"[TracerBuilder][2015] хелпер FAILED exit={p.ExitCode}: {se.Trim()} {so.Trim()}");
                    return false;
                }
                if (!File.Exists(outYpt) || new FileInfo(outYpt).Length == 0)
                {
                    Console.WriteLine("[TracerBuilder][2015] хелпер exit=0 но out.ypt пуст/нет - fallback");
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(so)) Console.WriteLine($"[TracerBuilder][2015] {so.Trim()}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder][2015] запуск хелпера упал: {ex.GetType().Name}: {ex.Message} - fallback");
                return false;
            }
        }

        private static string? ResolveWriter2015Exe()
        {
            foreach (var cand in new[]
            {
                Path.Combine(AppContext.BaseDirectory, "YptWriter2015", "YptWriter2015.exe"),
                Path.Combine(AppContext.BaseDirectory, "Tools", "YptWriter2015", "YptWriter2015.exe"),
            })
            {
                if (File.Exists(cand)) return cand;
            }
            return null;
        }

        private static void AssertReparseOrThrow(string yptPath)
        {
            int n;
            try
            {
                var check = new ParticlesFileWrapper_GTA5_pc();
                check.Load(yptPath);
                n = check.Particles?.TextureDictionary?.Textures?.Count ?? 0;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    Loc.T("error.postSaveReparseFailed", ("reason", ex.GetType().Name + ": " + ex.Message)), ex);
            }
            if (n == 0)
                throw new InvalidOperationException(
                    Loc.T("error.postSaveReparseEmptyTextures"));
            Console.WriteLine($"[TracerBuilder] post-save reparse OK: textures={n}");
        }

        private static void VerifyRsc7HeaderOrWarn(string path)
        {
            try
            {
                byte[] hdr = new byte[16];
                using (var fs = File.OpenRead(path)) fs.Read(hdr, 0, 16);
                uint magic = BitConverter.ToUInt32(hdr, 0);
                uint version = BitConverter.ToUInt32(hdr, 4);
                if (magic != 0x37435352u)
                    Console.WriteLine($"[TracerBuilder] WARNING: {path} magic 0x{magic:X8} != RSC7");
                else
                    Console.WriteLine($"[TracerBuilder] Header OK: magic=RSC7 version={version}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TracerBuilder] Header check failed: {ex.Message}");
            }
        }

        private static void WriteSimpleTextureDump(
            string debugLogDir, string logicalPath, IReadOnlyList<ITexture> textures)
        {
            try
            {
                Directory.CreateDirectory(debugLogDir);
                using var w = new StreamWriter(Path.Combine(debugLogDir, "texture_dictionary.txt"), false);
                w.WriteLine("Target: " + logicalPath);
                w.WriteLine("Source: redux patch_files/ (via component_map.json)");
                w.WriteLine("Texture count: " + textures.Count);
                int i = 0;
                foreach (var t in textures)
                {
                    w.WriteLine($"[{i}] Name={t.Name} Format=0x{(uint)t.Format:X8} ({t.Format}) " +
                                $"W={t.Width} H={t.Height} Levels={t.MipMapLevels} Stride={t.Stride}");
                    i++;
                }
            }
            catch { }
        }
    }
}
