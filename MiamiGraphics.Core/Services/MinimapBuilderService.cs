using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Parser;

namespace MiamiGraphics.Core.Services
{
    public readonly record struct MinimapCustomizeResult(int FilesFound, int FilesRecolored);

    public class MinimapBuilderService
    {
        public static readonly string[] DefaultTargetPaths =
        {
            "x64/patch/data/cdimages/scaleform_minimap.rpf/minimap.gfx",
            "x64/data/cdimages/scaleform_minimap.rpf/minimap.gfx"
        };

        public static readonly string[] RpfNameHints = { "minimap", "scaleform" };

        public MinimapCustomizeResult Customize(
            string patchRootDirectory,
            DiffManifest manifest,
            string gtaRootPath,
            byte healthR,
            byte healthG,
            byte healthB,
            byte armourR,
            byte armourG,
            byte armourB,
            string? customHitmarkerPath,
            string? javaExePath = null,
            string? ffdecJarPath = null,
            string? swfmillExePath = null,
            MinimapTweaks? tweaks = null,
            bool applyColors = true,
            (int Width, int Height, byte[] Rgba)? hitImage = null,
            IReadOnlyList<int>? rangeRingsMeters = null,
            (int Width, int Height, byte[] Rgba)? arrowImage = null,
            (int Width, int Height, byte[] Rgba)? gpsImage = null)
        {

            if (hitImage is { Width: > 0, Height: > 0 } && tweaks is not null)
                tweaks = tweaks with { HitNoRedTint = true };

            var bundledFfdec   = ResolveAdditionalsFile("minimap", "ffdec.jar");
            var bundledSwfmill = ResolveAdditionalsFile("minimap", "swfmill.exe");
            string ffdecJar   = ffdecJarPath   ?? (File.Exists(bundledFfdec)   ? bundledFfdec   : "ffdec.jar");
            string javaExe    = javaExePath    ?? "java";
            string swfmillExe = swfmillExePath ?? (File.Exists(bundledSwfmill) ? bundledSwfmill : "swfmill");

            var minimapFiles = PatchCustomizationSupport.FindExistingFiles(patchRootDirectory, manifest, "minimap.gfx");
            if (minimapFiles.Count == 0)
            {
                Console.WriteLine("[MinimapBuilder] minimap.gfx не найден в патче. Ищем оригинал в update.rpf (content.xml → вложенные rpf → дефолт)...");
                minimapFiles.AddRange(PatchCustomizationSupport.EnsureOriginalsImported(
                    patchRootDirectory,
                    manifest,
                    gtaRootPath,
                    "minimap.gfx",
                    DefaultTargetPaths,
                    RpfNameHints));
            }

            int recolored = 0;
            foreach (PatchWorkspaceFile minimapFile in minimapFiles)
            {
                Console.WriteLine($"[MinimapBuilder] Патчим: {minimapFile.TargetPath}");
                if (PatchMinimapFile(
                    minimapFile.PhysicalPath,
                    healthR,
                    healthG,
                    healthB,
                    armourR,
                    armourG,
                    armourB,
                    customHitmarkerPath,
                    ffdecJar,
                    javaExe,
                    swfmillExe,
                    tweaks,
                    applyColors,
                    hitImage,
                    arrowImage,
                    gpsImage))
                {
                    recolored++;
                }

                if (rangeRingsMeters is { Count: > 0 })
                {
                    try
                    {
                        bool ringsOk = MinimapRangeRingsService.Apply(minimapFile.PhysicalPath, rangeRingsMeters);
                        Console.WriteLine(ringsOk
                            ? $"[MinimapBuilder] Круги дальности нанесены ({string.Join(",", rangeRingsMeters)} м)."
                            : "[MinimapBuilder] Круги дальности не легли на эту миникарту - пропущено.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MinimapBuilder] Круги дальности не нанесены: {ex.Message}");
                    }
                }

                PatchCustomizationSupport.UpsertPatchAction(
                    manifest,
                    patchRootDirectory,
                    minimapFile);
            }

            PatchCustomizationSupport.RecalculateTotalPatchSize(manifest);
            return new MinimapCustomizeResult(minimapFiles.Count, recolored);
        }

        public static byte[]? ApplyTweaksToGfxBytes(
            byte[] gfxBytes, MinimapTweaks tweaks, string javaExe, string ffdecJar,
            out string? error, out bool alreadyTweaked, out string? hitNote,
            (int Width, int Height, byte[] Rgba)? hitImage = null,
            (int Width, int Height, byte[] Rgba)? arrowImage = null,
            (int Width, int Height, byte[] Rgba)? gpsImage = null)
        {
            error = null;
            alreadyTweaked = false;
            hitNote = null;
            if (gfxBytes is null || gfxBytes.Length < 1000) { error = Loc.T("error.minimapGfxEmpty"); return null; }
            if (!(gfxBytes[0] == (byte)'G' && gfxBytes[1] == (byte)'F' && gfxBytes[2] == (byte)'X'))
            { error = Loc.T("error.minimapGfxCompressed"); return null; }

            Console.WriteLine("[Минимапа] Графика: " + MinimapTextureCheck.Describe(gfxBytes));

            bool wantPng = hitImage is { Width: > 0, Height: > 0 } img && img.Rgba is not null;
            if (wantPng) tweaks = tweaks with { HitNoRedTint = true, HitArtBaked = true };

            bool wantArrow = arrowImage is { Width: > 0, Height: > 0 } ai && ai.Rgba is not null;
            bool wantGps = gpsImage is { Width: > 0, Height: > 0 } gi && gi.Rgba is not null;

            string temp = Path.Combine(Path.GetTempPath(), "MinimapTweaks_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                string swf = Path.Combine(temp, "t.swf");
                var work = (byte[])gfxBytes.Clone();
                work[0] = (byte)'F'; work[1] = (byte)'W'; work[2] = (byte)'S';
                File.WriteAllBytes(swf, work);

                string scripts = Path.Combine(temp, "scripts");
                Directory.CreateDirectory(scripts);

                string shaKey;
                using (var shaAlg = global::System.Security.Cryptography.SHA256.Create())
                    shaKey = Convert.ToHexString(shaAlg.ComputeHash(gfxBytes)).Substring(0, 16);
                string asCacheRoot = Path.Combine(
                    MiamiGraphics.Core.System.AppDataRoot.Dir("ffdec_minimap_as"), shaKey);

                string? asPath = null;
                try
                {
                    if (Directory.Exists(asCacheRoot))
                    {
                        var cached = Directory.GetFiles(asCacheRoot, "MINIMAP.as", SearchOption.AllDirectories).FirstOrDefault();
                        if (cached is not null)
                        {
                            string rel = Path.GetRelativePath(asCacheRoot, cached);
                            asPath = Path.Combine(scripts, rel);
                            Directory.CreateDirectory(Path.GetDirectoryName(asPath)!);
                            File.Copy(cached, asPath, true);
                        }
                    }
                }
                catch { asPath = null; }

                if (asPath is null)
                {
                    var run = RunProcessChecked(javaExe, $"-XX:TieredStopAtLevel=1 -jar \"{ffdecJar}\" -export script \"{scripts}\" \"{swf}\"");
                    asPath = Directory.GetFiles(scripts, "MINIMAP.as", SearchOption.AllDirectories).FirstOrDefault();
                    if (asPath is null)
                    {
                        error = run.Error.Length > 0
                            ? Loc.T("error.minimapExportFailed", ("reason", run.Error))
                            : Loc.T("error.minimapAsNotFound");
                        return null;
                    }
                    try
                    {
                        string rel = Path.GetRelativePath(scripts, asPath);
                        string dst = Path.Combine(asCacheRoot, rel);
                        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                        File.Copy(asPath, dst, true);
                    }
                    catch (Exception cacheEx)
                    {
                        Console.WriteLine($"[MinimapTweaks] Кэш AS не записан ({cacheEx.GetType().Name}: {cacheEx.Message}) - не страшно, но экспорт не ускорится.");
                    }
                    foreach (var extraAs in Directory.GetFiles(scripts, "*.as", SearchOption.AllDirectories))
                        if (!string.Equals(extraAs, asPath, StringComparison.OrdinalIgnoreCase))
                            try { File.Delete(extraAs); } catch { }
                }

                string text = File.ReadAllText(asPath);
                if (MinimapScriptTweaksService.AlreadyTweaked(text))
                {
                    alreadyTweaked = true;
                    error = Loc.T("error.minimapAlreadyTweaked");
                    return null;
                }

                var notes = new List<string>();
                text = MinimapScriptTweaksService.Apply(text, tweaks, notes, out bool changed);
                foreach (var n in notes) Console.WriteLine("[MinimapTweaks] " + n);
                if (!changed && !wantPng && !tweaks.HideNorth && !wantArrow && !wantGps)
                { error = Loc.T("error.minimapNoTweakApplied"); return null; }

                byte[] result;
                if (changed)
                {
                    File.WriteAllText(asPath, text);
                    string outSwf = Path.Combine(temp, "out.swf");
                    RunProcess(javaExe, $"-XX:TieredStopAtLevel=1 -jar \"{ffdecJar}\" -importScript \"{swf}\" \"{outSwf}\" \"{scripts}\"");
                    if (!File.Exists(outSwf)) { error = Loc.T("error.ffdecImportScriptNoFile"); return null; }
                    result = File.ReadAllBytes(outSwf);
                    if (result.Length < 10_000) { error = Loc.T("error.builtGfxTooSmall", ("bytes", result.Length)); return null; }
                    result[0] = (byte)'G'; result[1] = (byte)'F'; result[2] = (byte)'X';
                }
                else
                {
                    result = (byte[])gfxBytes.Clone();
                }

                result = ApplyBinarySplices(result, tweaks, hitImage, arrowImage, gpsImage, temp, javaExe, ffdecJar, out hitNote);
                if (tweaks is not null)
                {
                    var fontNotes = new List<string>();
                    result = MinimapFontRetarget.Apply(result, tweaks.DigitsFont, fontNotes);
                    foreach (var n in fontNotes) Console.WriteLine(n);
                }
                return result;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }
            finally { TryDeleteDirectory(temp); }
        }

        internal static byte[] ApplyBinarySplices(
            byte[] result,
            MinimapTweaks tweaks,
            (int Width, int Height, byte[] Rgba)? hitImage,
            (int Width, int Height, byte[] Rgba)? arrowImage,
            (int Width, int Height, byte[] Rgba)? gpsImage,
            string tempDir,
            string javaExe,
            string ffdecJar,
            out string? hitNote)
        {
            hitNote = null;
            bool wantPng = hitImage is { Width: > 0, Height: > 0 } img && img.Rgba is not null;

            if (tweaks is not null &&
                (tweaks.BarHpColor is not null || tweaks.BarArmorColor is not null || tweaks.BarHpGradient
                 || tweaks.BarHpTroughColor is not null || tweaks.BarArmorTroughColor is not null))
            {
                bool hpFill = tweaks.BarHpColor is not null || tweaks.BarHpGradient;
                bool arFill = tweaks.BarArmorColor is not null;
                bool hpTrough = hpFill || tweaks.BarHpTroughColor is not null;
                bool arTrough = arFill || tweaks.BarArmorTroughColor is not null;
                var opaque = MinimapBarFillAlphaService.RaiseFillAlpha(
                    result, hpFill, arFill, hpTrough, arTrough, out var alphaErr, out int fillsPatched);
                if (opaque is not null)
                {
                    result = opaque;
                    Console.WriteLine(fillsPatched > 0
                        ? $"[MinimapTweaks] Альфа заливок бара поднята до 255 (стилей: {fillsPatched})."
                        : alphaErr is null
                            ? "[MinimapTweaks] Заливки бара уже непрозрачны - альфа не менялась."
                            : $"[MinimapTweaks] Альфа заливок бара не менялась: {alphaErr}.");
                }
                else
                {
                    Console.WriteLine($"[MinimapTweaks] Альфа заливок бара не поднята: {alphaErr} - цвет полос может выйти тусклее выбранного.");
                }
            }

            if (wantPng)
            {
                string hitSwf = Path.Combine(tempDir, "hit_probe.swf");
                var probe = (byte[])result.Clone();
                probe[0] = (byte)'F'; probe[1] = (byte)'W'; probe[2] = (byte)'S';
                File.WriteAllBytes(hitSwf, probe);
                string hitXml = Path.Combine(tempDir, "hit_probe.xml");
                RunProcess(javaExe, $"-jar \"{ffdecJar}\" -swf2xml \"{hitSwf}\" \"{hitXml}\"");

                var hitIds = new HashSet<int>();
                int maxId = 0;
                if (File.Exists(hitXml))
                {
                    var doc = new global::System.Xml.XmlDocument();
                    doc.Load(hitXml);
                    foreach (global::System.Xml.XmlElement el in doc.SelectNodes(
                        "//item[(@type='PlaceObject2Tag' or @type='PlaceObject3Tag') and @name='healthHitMC']")!)
                    {
                        if (int.TryParse(el.GetAttribute("characterId"), out var cid) && cid > 0)
                            hitIds.Add(cid);
                    }
                    foreach (Match m in Regex.Matches(
                        File.ReadAllText(hitXml), "(?:spriteId|shapeId|characterID|characterId|fontId)=\"(\\d+)\""))
                        maxId = Math.Max(maxId, int.Parse(m.Groups[1].Value));
                }

                if (hitIds.Count == 0)
                {
                    hitNote = Loc.T("misc.hitFlashNotFound");
                    Console.WriteLine("[MinimapTweaks] healthHitMC не найден - картинка вспышки пропущена.");
                }
                else
                {
                    var withPng = MinimapHitBitmapService.ReplaceHitArt(
                        result, hitIds, maxId + 1, maxId + 2,
                        hitImage!.Value.Width, hitImage.Value.Height, hitImage.Value.Rgba, out var hitErr,
                        tweaks?.HitScale ?? 100, tweaks?.HitX, tweaks?.HitY);
                    if (withPng is not null)
                    {
                        Console.WriteLine($"[MinimapTweaks] Картинка вспышки вшита бинарным сплайсом ({result.Length:N0} → {withPng.Length:N0} б, спрайты: {string.Join(",", hitIds)}).");
                        result = withPng;
                    }
                    else
                    {
                        hitNote = Loc.T("misc.hitFlashNotEmbedded", ("reason", hitErr));
                        Console.WriteLine($"[MinimapTweaks] Картинка вспышки НЕ вшита: {hitErr} - твики применены без неё.");
                    }
                }
            }

            if (MinimapScriptTweaksService.BarMoveRequested(tweaks))
            {
                var wrappedBytes = MinimapBarShadowService.WrapShadow(result, out var shadowErr, out int wrappedCount);
                if (wrappedBytes is not null)
                {
                    result = wrappedBytes;
                    Console.WriteLine(wrappedCount > 0
                        ? $"[MinimapTweaks] Подложка бара обёрнута в mgShadowMC (контейнеров: {wrappedCount})."
                        : "[MinimapTweaks] Кандидат подложки бара не найден - перенос без неё.");
                }
                else
                {
                    Console.WriteLine($"[MinimapTweaks] Обёртка подложки бара не удалась: {shadowErr} - перенос без неё.");
                }
            }

            if (tweaks.HideNorth)
            {
                var noN = MinimapNorthBlipService.HideNorth(result, out var nErr, out bool nFound);
                if (noN is not null)
                {
                    result = noN;
                    Console.WriteLine(nFound
                        ? "[MinimapTweaks] Буква N убрана (radar_north опустошён)."
                        : $"[MinimapTweaks] Буква N: {nErr} - пропущено.");
                }
                else Console.WriteLine($"[MinimapTweaks] Буква N не убрана: {nErr}.");
            }

            var blipJobs = new ((int Width, int Height, byte[] Rgba)? Img, string[] Exports, string What, string Tag)[]
            {
                (arrowImage, MinimapBlipArtService.PlayerArrowExports, Loc.T("misc.blipPlayerArrow"), "стрелка игрока"),
                (gpsImage, MinimapBlipArtService.GpsBlipExports, Loc.T("misc.blipGps"), "метка GPS"),
            };
            foreach (var job in blipJobs)
            {
                if (!(job.Img is { Width: > 0, Height: > 0 } bi && bi.Rgba is not null)) continue;
                var withBlip = MinimapBlipArtService.ReplaceBlipArt(
                    result, job.Exports, bi.Width, bi.Height, bi.Rgba, out var bErr, out int cnt);
                if (withBlip is not null && cnt > 0)
                {
                    result = withBlip;
                    Console.WriteLine($"[MinimapTweaks] {job.Tag}: картинка вшита (спрайтов: {cnt}).");
                }
                else
                {
                    var note = Loc.T("misc.blipNotReplaced",
                        ("what", job.What), ("reason", bErr ?? Loc.T("misc.reasonUnknown")));
                    hitNote = hitNote is null ? note : $"{hitNote}; {note}";
                    Console.WriteLine($"[MinimapTweaks] {job.Tag} НЕ заменена: {bErr ?? "неизвестная причина"}.");
                }
            }

            return result;
        }

        private static string ResolveAdditionalsFile(params string[] segments)
        {
            var direct = Path.Combine(new[] { AppDomain.CurrentDomain.BaseDirectory, "additionals" }.Concat(segments).ToArray());
            if (File.Exists(direct)) return direct;

            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var candidate = Path.Combine(new[] { dir, "additionals" }.Concat(segments).ToArray());
                if (File.Exists(candidate)) return candidate;
                dir = Path.GetDirectoryName(dir);
            }

            var local = Path.Combine(
                new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MiamiGraphics",
                    "additionals",
                }.Concat(segments).ToArray());
            return local;
        }

        private bool PatchMinimapFile(
            string minimapPath,
            byte healthR,
            byte healthG,
            byte healthB,
            byte armourR,
            byte armourG,
            byte armourB,
            string? customHitmarkerPath,
            string ffdecJar,
            string javaExe,
            string swfmillExe,
            MinimapTweaks? tweaks = null,
            bool applyColors = true,
            (int Width, int Height, byte[] Rgba)? hitImage = null,
            (int Width, int Height, byte[] Rgba)? arrowImage = null,
            (int Width, int Height, byte[] Rgba)? gpsImage = null)
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "MinimapPatch_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            try
            {
                string workingMinimapPath = Path.Combine(tempDirectory, "minimap.gfx");
                File.Copy(minimapPath, workingMinimapPath, true);

                string swfForFfdec = Path.Combine(tempDirectory, "minimap_ffdec.swf");
                File.Copy(workingMinimapPath, swfForFfdec, true);
                SwapGfxHeader(swfForFfdec, toSwf: true);

                string exportScriptDirectory = Path.Combine(tempDirectory, "scripts");
                Directory.CreateDirectory(exportScriptDirectory);

                RunProcess(javaExe, $"-jar \"{ffdecJar}\" -export script \"{exportScriptDirectory}\" \"{swfForFfdec}\"");

                bool filePatched = false;
                foreach (string asFile in Directory.GetFiles(exportScriptDirectory, "*.as", SearchOption.AllDirectories))
                {
                    string content = File.ReadAllText(asFile);
                    bool changed = false;

                    string patternHealth = @"([a-zA-Z0-9_\.]*colourHealth)\s*=\s*\{[^\}]+\};";
                    string patternArmour = @"([a-zA-Z0-9_\.]*colourArmour)\s*=\s*\{[^\}]+\};";

                    if (!applyColors)
                    {
                    }
                    else if (Regex.IsMatch(content, patternHealth) || Regex.IsMatch(content, patternArmour))
                    {
                        content = Regex.Replace(content, patternHealth, $"$1 = {{r:{healthR},g:{healthG},b:{healthB}}};");
                        content = Regex.Replace(content, patternArmour, $"$1 = {{r:{armourR},g:{armourG},b:{armourB}}};");
                        changed = true;
                    }
                    else if (content.Contains("function SETUP_HEALTH_ARMOUR", StringComparison.Ordinal))
                    {
                        string setupPattern = @"(function\s+SETUP_HEALTH_ARMOUR\s*\([^\)]*\)\s*\{)";
                        string injection = "$1\n" +
                            $"         this.colourHealth = {{r:{healthR}, g:{healthG}, b:{healthB}}};\n" +
                            $"         this.colourArmour = {{r:{armourR}, g:{armourG}, b:{armourB}}};\n";
                        content = Regex.Replace(content, setupPattern, injection);

                        content = Regex.Replace(
                            content,
                            @"([^\r\n]*setHudColour\([^)]*HUD_COLOUR_GREEN\s*,\s*this\.colourHealth\s*\)\s*;)",
                            "$1\n            " + $"this.colourHealth = {{r:{healthR}, g:{healthG}, b:{healthB}}};");
                        content = Regex.Replace(
                            content,
                            @"([^\r\n]*setHudColour\([^)]*HUD_COLOUR_BLUE\s*,\s*this\.colourArmour\s*\)\s*;)",
                            "$1\n            " + $"this.colourArmour = {{r:{armourR}, g:{armourG}, b:{armourB}}};");
                        changed = true;
                    }

                    if (tweaks is not null
                        && MinimapScriptTweaksService.AnyRequested(tweaks)
                        && content.Contains("function SETUP_HEALTH_ARMOUR", StringComparison.Ordinal))
                    {
                        var tweakNotes = new List<string>();
                        content = MinimapScriptTweaksService.Apply(content, tweaks, tweakNotes, out bool tweaksChanged);
                        foreach (var n in tweakNotes) Console.WriteLine("[MinimapBuilder] " + n);
                        if (tweaksChanged) changed = true;
                    }

                    if (changed)
                    {
                        File.WriteAllText(asFile, content);
                        filePatched = true;
                    }
                }

                bool recolorApplied = false;
                if (filePatched)
                {
                    string outSwf = Path.Combine(tempDirectory, "minimap_patched.swf");
                    RunProcess(javaExe, $"-jar \"{ffdecJar}\" -importScript \"{swfForFfdec}\" \"{outSwf}\" \"{exportScriptDirectory}\"");

                    if (File.Exists(outSwf))
                    {
                        SwapGfxHeader(outSwf, toSwf: false);
                        File.Copy(outSwf, workingMinimapPath, true);
                        recolorApplied = true;
                    }
                    else
                    {
                        Console.WriteLine("[MinimapBuilder] Предупреждение: ffdec importScript не выдал файл - цвета не записаны.");
                    }
                }
                else if (applyColors)
                {
                    recolorApplied = PatchSpritesFallback(
                        workingMinimapPath,
                        tempDirectory,
                        swfmillExe,
                        healthR,
                        healthG,
                        healthB,
                        armourR,
                        armourG,
                        armourB);

                    if (!recolorApplied)
                        Console.WriteLine("[MinimapBuilder] Предупреждение: не удалось изменить цвета minimap.gfx.");
                }

                if (!string.IsNullOrWhiteSpace(customHitmarkerPath) && File.Exists(customHitmarkerPath))
                {
                    ReplaceHitImage(workingMinimapPath, customHitmarkerPath, tempDirectory, swfmillExe, ffdecJar, javaExe);
                }

                bool wantSplices = tweaks is not null &&
                    (hitImage is { Width: > 0, Height: > 0 }
                     || tweaks.HideNorth
                     || MinimapScriptTweaksService.BarMoveRequested(tweaks)
                     || tweaks.BarHpColor is not null
                     || tweaks.BarArmorColor is not null
                     || tweaks.BarHpGradient
                     || tweaks.BarHpTroughColor is not null
                     || tweaks.BarArmorTroughColor is not null
                     || arrowImage is { Width: > 0, Height: > 0 }
                     || gpsImage is { Width: > 0, Height: > 0 });
                if (wantSplices)
                {
                    try
                    {
                        var spliced = ApplyBinarySplices(
                            File.ReadAllBytes(workingMinimapPath), tweaks!, hitImage, arrowImage, gpsImage,
                            tempDirectory, javaExe, ffdecJar, out var spliceNote);
                        File.WriteAllBytes(workingMinimapPath, spliced);
                        if (spliceNote is not null)
                            Console.WriteLine($"[MinimapBuilder] Бинарные твики: {spliceNote}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MinimapBuilder] Бинарные твики не применились: {ex.Message}");
                    }
                }

                if (tweaks?.DigitsFont is not null)
                {
                    try
                    {
                        var fontNotes = new List<string>();
                        File.WriteAllBytes(workingMinimapPath, MinimapFontRetarget.Apply(
                            File.ReadAllBytes(workingMinimapPath), tweaks.DigitsFont, fontNotes));
                        foreach (var n in fontNotes) Console.WriteLine(n);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MinimapBuilder] Шрифт не применился: {ex.Message}");
                    }
                }

                File.Copy(workingMinimapPath, minimapPath, true);
                return recolorApplied;
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }
        }

        private bool PatchSpritesFallback(
            string gfxPath,
            string tempDirectory,
            string swfmillPath,
            byte healthR,
            byte healthG,
            byte healthB,
            byte armourR,
            byte armourG,
            byte armourB)
        {
            string xmlPath = Path.Combine(tempDirectory, "minimap_export.xml");
            SwapGfxHeader(gfxPath, toSwf: true);

            try
            {
                RunProcess(swfmillPath, $"swf2xml \"{gfxPath}\" \"{xmlPath}\"");
                if (!File.Exists(xmlPath))
                    return false;

                XDocument document = XDocument.Load(xmlPath);
                bool changed = false;

                var placeObjects = document.Descendants()
                    .Where(e => e.Name.LocalName == "PlaceObject2" || e.Name.LocalName == "PlaceObject3");

                foreach (XElement placeObject in placeObjects)
                {
                    XAttribute? nameAttribute = placeObject.Attribute("name");
                    if (nameAttribute == null)
                        continue;

                    string name = nameAttribute.Value.ToLowerInvariant();
                    if (name.Contains("health", StringComparison.Ordinal))
                    {
                        ApplyColorTransform(placeObject, healthR, healthG, healthB);
                        changed = true;
                    }
                    else if (name.Contains("armou", StringComparison.Ordinal) || name.Contains("armor", StringComparison.Ordinal))
                    {
                        ApplyColorTransform(placeObject, armourR, armourG, armourB);
                        changed = true;
                    }
                }

                var healthColors = new[]
                {
                    new { R = "114", G = "204", B = "114" },
                    new { R = "171", G = "237", B = "171" }
                };
                var armourColors = new[]
                {
                    new { R = "93", G = "182", B = "229" },
                    new { R = "101", G = "180", B = "212" }
                };

                foreach (XElement colorNode in document.Descendants("Color"))
                {
                    XAttribute? red = colorNode.Attribute("red");
                    XAttribute? green = colorNode.Attribute("green");
                    XAttribute? blue = colorNode.Attribute("blue");

                    if (red == null || green == null || blue == null)
                        continue;

                    if (healthColors.Any(c => c.R == red.Value && c.G == green.Value && c.B == blue.Value))
                    {
                        red.Value = healthR.ToString();
                        green.Value = healthG.ToString();
                        blue.Value = healthB.ToString();
                        changed = true;
                    }
                    else if (armourColors.Any(c => c.R == red.Value && c.G == green.Value && c.B == blue.Value))
                    {
                        red.Value = armourR.ToString();
                        green.Value = armourG.ToString();
                        blue.Value = armourB.ToString();
                        changed = true;
                    }
                }

                if (!changed)
                    return false;

                document.Save(xmlPath);

                string outGfx = Path.Combine(tempDirectory, "minimap_patched_fallback.gfx");
                RunProcess(swfmillPath, $"xml2swf \"{xmlPath}\" \"{outGfx}\"");

                if (!File.Exists(outGfx))
                    return false;

                SwapGfxHeader(outGfx, toSwf: false);
                File.Copy(outGfx, gfxPath, true);
                return true;
            }
            finally
            {
                SwapGfxHeader(gfxPath, toSwf: false);
            }
        }

        private void ReplaceHitImage(
            string gfxPath,
            string customImagePath,
            string tempDirectory,
            string swfmillPath,
            string ffdecPath,
            string javaExe)
        {
            string xmlPath = Path.Combine(tempDirectory, "minimap_struct.xml");
            string tempInputSwf = Path.Combine(tempDirectory, "temp_in.swf");
            string cleanSwf = Path.Combine(tempDirectory, "clean_in.swf");

            File.Copy(gfxPath, tempInputSwf, true);
            SwapGfxHeader(tempInputSwf, toSwf: true);

            RunProcess(swfmillPath, $"swf2xml \"{tempInputSwf}\" \"{xmlPath}\"");
            if (!File.Exists(xmlPath))
                return;

            XDocument document = XDocument.Load(xmlPath);

            XElement? hitMarkerPlace = document.Descendants()
                .FirstOrDefault(e =>
                    (e.Name.LocalName == "PlaceObject2" || e.Name.LocalName == "PlaceObject3") &&
                    e.Attribute("name")?.Value == "healthHitMC");

            if (hitMarkerPlace == null)
                return;

            string? spriteId = hitMarkerPlace.Attribute("objectID")?.Value;
            XElement? defineSprite = document.Descendants("DefineSprite")
                .FirstOrDefault(e => e.Attribute("objectID")?.Value == spriteId);

            if (defineSprite == null)
                return;

            XElement? shapePlace = defineSprite.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "PlaceObject2" || e.Name.LocalName == "PlaceObject3");

            if (shapePlace == null)
                return;

            string? shapeId = shapePlace.Attribute("objectID")?.Value;
            if (string.IsNullOrWhiteSpace(shapeId))
                return;

            shapePlace.Element("colorTransform")?.Remove();
            hitMarkerPlace.Element("colorTransform")?.Remove();

            string targetId = shapeId;
            XElement? defineShape = document.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.StartsWith("DefineShape", StringComparison.Ordinal) &&
                                     e.Attribute("objectID")?.Value == shapeId);

            if (defineShape != null)
            {
                XElement? fillWithImage = defineShape.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "FillStyle" && e.Attribute("objectID") != null);

                if (fillWithImage != null)
                {
                    targetId = fillWithImage.Attribute("objectID")!.Value;
                }
                else
                {
                    XElement? fallbackReference = defineShape.Descendants()
                        .FirstOrDefault(e => e.Attribute("objectID") != null || e.Attribute("bitmapID") != null);

                    if (fallbackReference != null)
                        targetId = fallbackReference.Attribute("objectID")?.Value ?? fallbackReference.Attribute("bitmapID")?.Value ?? targetId;
                }
            }

            document.Save(xmlPath);
            RunProcess(swfmillPath, $"xml2swf \"{xmlPath}\" \"{cleanSwf}\"");

            if (!File.Exists(cleanSwf))
                return;

            string extension = Path.GetExtension(customImagePath);
            string targetImagePath = Path.Combine(tempDirectory, $"custom_hit_{targetId}{extension}");
            File.Copy(customImagePath, targetImagePath, true);

            string outputSwf = Path.Combine(tempDirectory, "minimap_patched_image.swf");
            RunProcess(javaExe, $"-jar \"{ffdecPath}\" -replace \"{cleanSwf}\" \"{outputSwf}\" {targetId} \"{targetImagePath}\"");

            if (!File.Exists(outputSwf))
                return;

            SwapGfxHeader(outputSwf, toSwf: false);
            File.Copy(outputSwf, gfxPath, true);
        }

        private static void ApplyColorTransform(XElement placeObject, byte red, byte green, byte blue)
        {
            XElement? colorTransformNode = placeObject.Element("colorTransform");
            if (colorTransformNode == null)
            {
                colorTransformNode = new XElement("colorTransform");
                placeObject.AddFirst(colorTransformNode);
            }

            XElement? colorTransform = colorTransformNode.Element("ColorTransform");
            if (colorTransform == null)
            {
                colorTransform = new XElement("ColorTransform");
                colorTransformNode.Add(colorTransform);
            }

            colorTransform.SetAttributeValue("redMultTerm", "0");
            colorTransform.SetAttributeValue("greenMultTerm", "0");
            colorTransform.SetAttributeValue("blueMultTerm", "0");
            colorTransform.SetAttributeValue("redAdd", red.ToString());
            colorTransform.SetAttributeValue("greenAdd", green.ToString());
            colorTransform.SetAttributeValue("blueAdd", blue.ToString());
        }

        private static void SwapGfxHeader(string filePath, bool toSwf)
        {
            if (!File.Exists(filePath))
                return;

            byte[] header = new byte[3];
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);
            if (stream.Length < 3)
                return;

            stream.Read(header, 0, 3);

            if (toSwf)
            {
                if (header[0] == 0x43 && header[1] == 0x46 && header[2] == 0x58)
                {
                    stream.Position = 1;
                    stream.WriteByte(0x57);
                    stream.WriteByte(0x53);
                }
                else if (header[0] == 0x47 && header[1] == 0x46 && header[2] == 0x58)
                {
                    stream.Position = 0;
                    stream.WriteByte(0x46);
                    stream.WriteByte(0x57);
                    stream.WriteByte(0x53);
                }
            }
            else
            {
                if (header[0] == 0x43 && header[1] == 0x57 && header[2] == 0x53)
                {
                    stream.Position = 1;
                    stream.WriteByte(0x46);
                    stream.WriteByte(0x58);
                }
                else if (header[0] == 0x46 && header[1] == 0x57 && header[2] == 0x53)
                {
                    stream.Position = 0;
                    stream.WriteByte(0x47);
                    stream.WriteByte(0x46);
                    stream.WriteByte(0x58);
                }
            }
        }

        private sealed record ToolRun(int ExitCode, string Error);

        private static ToolRun RunProcessChecked(string fileName, string arguments)
        {
            var stderr = new global::System.Text.StringBuilder();
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    },
                };
                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data)) Console.WriteLine($"   [Tool] {e.Data}");
                };
                process.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    Console.WriteLine($"   [Tool Error] {e.Data}");
                    if (stderr.Length < 400) stderr.AppendLine(e.Data.Trim());
                };

                process.Start();
                try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
                catch {}
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(120_000))
                {
                    Console.WriteLine("[MinimapBuilder] tool timed out after 120s - killing.");
                    try { process.Kill(true); } catch { }
                    return new ToolRun(-1, $"{Path.GetFileName(fileName)}: не ответил за 120 секунд");
                }

                var code = process.ExitCode;
                if (code == 0) return new ToolRun(0, string.Empty);
                var tail = stderr.ToString().Trim();
                return new ToolRun(code, $"{Path.GetFileName(fileName)}: код выхода {code}"
                                         + (tail.Length > 0 ? $" ({tail})" : string.Empty));
            }
            catch (Exception ex)
            {
                return new ToolRun(-1, $"{Path.GetFileName(fileName)}: не запустился ({ex.GetType().Name}: {ex.Message})");
            }
        }

        private static void RunProcess(string fileName, string arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Console.WriteLine($"   [Tool] {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Console.WriteLine($"   [Tool Error] {e.Data}");
            };

            process.Start();
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
            catch {}
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(120_000))
            {
                Console.WriteLine("[MinimapBuilder] tool timed out after 120s - killing.");
                try { process.Kill(true); } catch { }
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
                return;

            try
            {
                Directory.Delete(path, true);
            }
            catch
            {
            }
        }
    }
}
