using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using DbgWriter = System.Diagnostics.Debug;
using MiamiGraphics.Core.Services;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Parser
{

    public static class ArmorGlbExporter
    {

        public const string ArmorGlbFileName = "armor.glb";

        private static readonly Regex YddNameRegex = new Regex(
            @"^task_\d+_u\.ydd$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex YddNumberRegex = new Regex(
            @"^task_(\d+)_u\.ydd$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static void Log(string s) => DbgWriter.WriteLine("[ArmorGLB] " + s);

        public static bool TryExportArmorGlb(string workDir, IArchiveDirectory moddedRoot, ResolvedComponentMap componentMap)
        {
            Log($"start: workDir={workDir}");
            if (componentMap == null)
            {
                Log("componentMap==null - пропускаем.");
                return false;
            }
            if (!componentMap.Components.TryGetValue("armor", out var armorInfo) || armorInfo == null || !armorInfo.IsFound)
            {
                Log("armor компонент в этом редуксе не найден - пропускаем.");
                return false;
            }

            string armorComponentDir = Path.Combine(workDir, "components", "armor");
            if (!Directory.Exists(armorComponentDir))
            {

                Directory.CreateDirectory(armorComponentDir);
            }

            byte[] yddBytes = null;
            List<byte[]> ytdBytesList = null;
            string sourceRpf = null;
            string sourceYddPath = null;
            if (moddedRoot != null && armorInfo.InternalPaths != null)
            {
                foreach (string internalPath in armorInfo.InternalPaths)
                {
                    Log($"пробую внутри update.rpf: {internalPath}");
                    if (!TryOpenNestedArchive(moddedRoot, internalPath, out var nestedArc) || nestedArc == null)
                    {
                        Log($"  не удалось открыть {internalPath} - пропускаю.");
                        continue;
                    }
                    try
                    {
                        using (nestedArc)
                        {
                            var yddCandidates = new List<ResourceCandidate>();
                            var ytdCandidates = new List<ResourceCandidate>();
                            CollectResourceCandidates(nestedArc.Root, "", yddCandidates, ytdCandidates);
                            Log($"  найдено YDD-кандидатов: {yddCandidates.Count}, YTD-кандидатов: {ytdCandidates.Count}");
                            foreach (var c in yddCandidates.Take(20))
                                Log($"    YDD - {c.Path}");
                            if (yddCandidates.Count > 20)
                                Log($"    ... ещё {yddCandidates.Count - 20}");

                            var pick = yddCandidates.FirstOrDefault(c =>
                                    Path.GetFileName(c.Path).Equals("task_011_u.ydd", StringComparison.OrdinalIgnoreCase))
                                ?? yddCandidates.FirstOrDefault(c =>
                                    YddNameRegex.IsMatch(Path.GetFileName(c.Path)))
                                ?? yddCandidates.FirstOrDefault();

                            if (pick != null)
                            {
                                using var ms = new MemoryStream();
                                pick.File.Export(ms);
                                yddBytes = ms.ToArray();
                                sourceRpf = internalPath;
                                sourceYddPath = pick.Path;
                                Log($"  выбран {pick.Path} ({yddBytes.Length} bytes)");

                                ytdBytesList = ExtractMatchingYtdBytes(pick, ytdCandidates);
                                Log($"  собрано YTD-байтов для текстур: {ytdBytesList.Count}");

                                break;
                            }
                            Log($"  ни одного YDD внутри {internalPath} не найдено");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  ошибка обхода {internalPath}: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
            else
            {
                Log("moddedRoot==null или InternalPaths пуст - нечего извлекать.");
            }

            if (yddBytes == null)
            {
                Log("Ни в одном armor RPF YDD не найден - GLB не будет создан.");
                return false;
            }

            string outputGlb = Path.Combine(armorComponentDir, ArmorGlbFileName);
            Log($"Конвертирую {sourceYddPath} ({yddBytes.Length / 1024} KB из {sourceRpf}) + {ytdBytesList?.Count ?? 0} YTD → {outputGlb}");

            bool ok;
            try
            {

                ok = YddToGltfConverter.ConvertBytesAsync(yddBytes, ytdBytesList, outputGlb)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Log($"Конвертер бросил исключение: {ex.GetType().Name}: {ex.Message}");
                Log(ex.StackTrace ?? "(нет stack trace)");
                return false;
            }

            if (!ok)
            {
                Log("Конвертер вернул false - GLB не создан.");
                return false;
            }

            if (!File.Exists(outputGlb))
            {
                Log($"Конвертер вернул true, но файл {outputGlb} отсутствует - что-то странное.");
                return false;
            }

            Log($"OK: {new FileInfo(outputGlb).Length / 1024} KB");
            return true;
        }

        private static List<byte[]> ExtractMatchingYtdBytes(ResourceCandidate ydd, List<ResourceCandidate> allYtd)
        {
            var result = new List<byte[]>();
            if (allYtd == null || allYtd.Count == 0) return result;

            string yddName = Path.GetFileName(ydd.Path);
            string yddDir = GetDirPart(ydd.Path);

            string num = null;
            var m = YddNumberRegex.Match(yddName);
            if (m.Success) num = m.Groups[1].Value;

            Regex ytdRegex = num != null
                ? new Regex($@"^task_diff_{num}_[a-z0-9]+_uni\.ytd$", RegexOptions.IgnoreCase)
                : null;

            int strict = 0, fallback = 0;
            foreach (var c in allYtd)
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(GetDirPart(c.Path), yddDir))
                    continue;

                string ytdName = Path.GetFileName(c.Path);
                bool match = ytdRegex != null
                    ? ytdRegex.IsMatch(ytdName)
                    : ytdName.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase);

                if (!match) continue;

                try
                {
                    using var ms = new MemoryStream();
                    c.File.Export(ms);
                    result.Add(ms.ToArray());
                    if (ytdRegex != null) strict++;
                    else fallback++;
                    Log($"    YTD матч: {c.Path} ({ms.Length} bytes)");
                }
                catch (Exception ex)
                {
                    Log($"    YTD {c.Path}: ошибка экспорта - {ex.GetType().Name}: {ex.Message}");
                }
            }

            if (strict == 0 && ytdRegex != null && allYtd.Count > 0)
            {

                Log("    строгий pattern не дал YTD - fallback: все YTD из той же папки");
                foreach (var c in allYtd)
                {
                    if (!StringComparer.OrdinalIgnoreCase.Equals(GetDirPart(c.Path), yddDir)) continue;
                    try
                    {
                        using var ms = new MemoryStream();
                        c.File.Export(ms);
                        result.Add(ms.ToArray());
                        Log($"    YTD fallback: {c.Path} ({ms.Length} bytes)");
                    }
                    catch (Exception ex)
                    {
                        Log($"    YTD {c.Path}: ошибка экспорта - {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }

            return result;
        }

        private static string GetDirPart(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int slash = path.LastIndexOf('/');
            return slash < 0 ? "" : path.Substring(0, slash);
        }

        private static bool TryOpenNestedArchive(IArchiveDirectory root, string rpfInternalPath, out IArchive archive)
        {
            archive = null;
            if (string.IsNullOrWhiteSpace(rpfInternalPath))
                return false;

            var parts = rpfInternalPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return false;

            IArchiveDirectory current = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                current = current?.GetDirectories().FirstOrDefault(d =>
                    d.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (current == null) return false;
            }

            var rpfFile = current?.GetFiles().FirstOrDefault(f =>
                f.Name.Equals(parts[parts.Length - 1], StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile;
            if (rpfFile == null) return false;

            try
            {
                var stream = rpfFile.GetStream();
                archive = RageArchiveWrapper7.Open(stream, rpfFile.Name, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class ResourceCandidate
        {
            public string Path { get; set; }

            public IArchiveFile File { get; set; }
        }

        private static void CollectResourceCandidates(
            IArchiveDirectory dir, string prefix,
            List<ResourceCandidate> ydd, List<ResourceCandidate> ytd)
        {
            foreach (var f in dir.GetFiles())
            {
                string childPath = string.IsNullOrEmpty(prefix) ? f.Name : prefix + "/" + f.Name;

                if (f.Name.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase))
                {
                    ydd.Add(new ResourceCandidate { Path = childPath, File = f });
                }
                else if (f.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
                {
                    ytd.Add(new ResourceCandidate { Path = childPath, File = f });
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
    }
}
