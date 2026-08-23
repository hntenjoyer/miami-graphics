using System;
using System.IO;
using System.Linq;
using System.Text.Json;

using DbgWriter = System.Diagnostics.Debug;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Parser
{
    public class ReduxParserPipeline
    {

        private readonly string _resultBaseDir = MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir;

        public string ParseRedux(string cleanRpfPath, string moddedRpfPath, string reduxName)
        {
            Console.WriteLine();
            Console.WriteLine($"[Pipeline] Начинаем разбор редукса: {reduxName}");

            int loadedKeys = 0;
            int totalKeys = 0;
            try
            {
                if (RageLib.GTA5.Cryptography.GTA5Constants.PC_NG_KEYS != null)
                {
                    totalKeys = RageLib.GTA5.Cryptography.GTA5Constants.PC_NG_KEYS.Length;
                    for (int i = 0; i < totalKeys; i++)
                    {
                        var k = RageLib.GTA5.Cryptography.GTA5Constants.PC_NG_KEYS[i];
                        if (k != null && k.Length > 0) loadedKeys++;
                    }
                }
            }
            catch {  }
            Console.WriteLine($"[Pipeline] NG keys loaded: {loadedKeys}/{totalKeys}");
            if (loadedKeys == 0)
            {
                Console.WriteLine("[Pipeline] WARNING: NO NG keys loaded. Decryption will be skipped (RpfDiffEngine guard).");
                Console.WriteLine($"[Pipeline] Working dir: {AppDomain.CurrentDomain.BaseDirectory}");
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string workDir = Path.Combine(_resultBaseDir, $"Redux_{reduxName}_{timestamp}");
            string patchDir = Path.Combine(workDir, "patch_files");

            Directory.CreateDirectory(workDir);
            Directory.CreateDirectory(patchDir);

            var diffManifest = new DiffManifest
            {
                ReduxName = reduxName,
                ParsedAt = DateTime.Now
            };

            using var cleanArchive = RageArchiveWrapper7.Open(cleanRpfPath);
            using var moddedArchive = RageArchiveWrapper7.Open(moddedRpfPath);

            var contentAnalyzer = new ContentXmlAnalyzer();
            ContentXmlInfo contentInfo = contentAnalyzer.AnalyzeFromArchive(moddedArchive.Root);
            if (!string.IsNullOrEmpty(contentInfo.ParseError))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Pipeline] ВНИМАНИЕ: {contentInfo.ParseError}. " +
                    "Компоненты редукса не будут обнаружены из content.xml - проверь редукс!");
                Console.ResetColor();
            }

            var diffEngine = new RpfDiffEngine();
            Console.WriteLine("[Pipeline] Запуск движка сравнения...");
            diffEngine.CompareDirectories(cleanArchive.Root, moddedArchive.Root, "", patchDir, diffManifest.Actions);

            var scanner = new ComponentScanner();
            Console.WriteLine("[Pipeline] Сканирование компонентов...");
            ResolvedComponentMap componentMap = scanner.BuildResolvedComponentMap(moddedArchive.Root, contentInfo);

            var arenaInfo = ArenaComponentDetector.Detect(moddedArchive.Root, contentInfo);
            if (arenaInfo.IsFound)
            {
                componentMap.Components["arena"] = arenaInfo;
                Console.WriteLine($"[Pipeline] Компонент 'arena' зарегистрирован: {string.Join(", ", arenaInfo.InternalPaths)}");
            }

            var armorInfo = ArmorComponentDetector.Detect(moddedArchive.Root, contentInfo);
            if (armorInfo.IsFound)
            {
                componentMap.Components["armor"] = armorInfo;
                Console.WriteLine($"[Pipeline] Компонент 'armor' зарегистрирован: {string.Join(", ", armorInfo.InternalPaths)}");
            }

            var linkedFonts = MinimapFontLinker.AttachCustomFonts(componentMap, diffManifest.Actions);
            if (linkedFonts.Count > 0)
                Console.WriteLine($"[Pipeline] К minimap привязаны кастомные шрифты ({linkedFonts.Count}): {string.Join(", ", linkedFonts)}");

            diffManifest.TotalPatchSize = diffManifest.Actions
                .Where(a => a.Type != ActionType.Delete)
                .Sum(a => a.Size);

            SaveJson(Path.Combine(workDir, "manifest.json"), diffManifest);
            SaveJson(Path.Combine(workDir, "content_info.json"), contentInfo);
            SaveJson(Path.Combine(workDir, "component_map.json"), componentMap);

            Console.WriteLine("[Pipeline] Экспорт компонентов...");
            var exporter = new ComponentExportService();
            exporter.ExportResolvedComponents(workDir, patchDir, moddedArchive.Root, componentMap);

            try
            {
                MiamiGraphics.Core.Services.TracerComponentTextures.DumpForComponent(workDir, componentMap);
            }
            catch (Exception ex)
            {
                DbgWriter.WriteLine($"[Pipeline] WARN: дамп трейсер-DDS провалился: {ex.GetType().Name}: {ex.Message}");
            }

            DbgWriter.WriteLine($"[Pipeline] Запускаю ArmorGlbExporter для workDir={workDir}");
            try
            {

                ArmorGlbExporter.TryExportArmorGlb(workDir, moddedArchive.Root, componentMap);
            }
            catch (Exception ex)
            {
                DbgWriter.WriteLine($"[Pipeline] WARN: armor GLB export провалился: {ex.GetType().Name}: {ex.Message}");
                DbgWriter.WriteLine(ex.StackTrace ?? "(нет stack trace)");
            }

            Console.WriteLine($"[Pipeline] Успех! Результат: {workDir}");
            Console.WriteLine($"[Pipeline] Изменений: {diffManifest.Actions.Count}, Вес: {diffManifest.TotalPatchSize / 1024 / 1024} MB");
            return workDir;
        }

        private void SaveJson(string path, object data)
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
        }
    }
}
