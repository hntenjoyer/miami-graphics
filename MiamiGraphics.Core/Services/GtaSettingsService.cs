#nullable disable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using MiamiGraphics.Core.System;

namespace MiamiGraphics.Core.Services
{

    public static class GtaSettingsService
    {

        private static readonly string[] GameProcessNames = { "GTA5", "GTA5_Enhanced", "PlayGTAV" };

        public static string GetSettingsPath()
        {
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(docs, "Rockstar Games", "GTA V", "settings.xml");
        }

        public static bool IsGameRunning()
        {
            foreach (var name in GameProcessNames)
            {
                try
                {
                    if (Process.GetProcessesByName(name).Length > 0) return true;
                }
                catch {  }
            }
            return false;
        }

        public static Task<bool> ApplyLowSettingsAsync(string sourceXmlPath)
            => Task.Run(() => ApplyCore(sourceXmlPath));

        private static bool ApplyCore(string sourceXmlPath)
        {
            try
            {
                Log("Источник: " + sourceXmlPath);
                if (!File.Exists(sourceXmlPath))
                {
                    Log("ERROR: source XML не найден");
                    return false;
                }

                string realGpu;
                try
                {
                    realGpu = new HardwareLocator().FindGpuName();
                }
                catch (Exception ex)
                {
                    Log("ERROR при определении GPU: " + ex.Message);
                    return false;
                }
                Log("Реальная видеокарта: " + realGpu);
                if (string.IsNullOrWhiteSpace(realGpu) || realGpu == "Unknown GPU")
                {
                    Log("ВНИМАНИЕ: видеокарта не определена. Применяем XML без подмены VideoCardDescription.");
                }

                XDocument doc;
                try
                {
                    doc = XDocument.Load(sourceXmlPath);
                }
                catch (Exception ex)
                {
                    Log("ERROR парсинга XML: " + ex.Message);
                    return false;
                }

                if (doc.Root == null)
                {
                    Log("ERROR: пустой XML");
                    return false;
                }

                int patched = 0;
                if (!string.IsNullOrWhiteSpace(realGpu) && realGpu != "Unknown GPU")
                {
                    foreach (var elem in doc.Descendants("VideoCardDescription"))
                    {
                        Log("VideoCardDescription было: '" + elem.Value + "'");
                        elem.Value = realGpu;
                        patched++;
                    }
                    Log("VideoCardDescription заменено на: '" + realGpu + "' (тегов: " + patched + ")");
                    if (patched == 0)
                        Log("ВНИМАНИЕ: тег VideoCardDescription не найден в XML - настройки могут не примениться.");
                }

                string targetPath = GetSettingsPath();
                Log("Целевой путь: " + targetPath);

                string targetDir = Path.GetDirectoryName(targetPath);
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    Log("Создана папка: " + targetDir);
                }

                if (IsGameRunning())
                {
                    Log("ВНИМАНИЕ: GTA V запущена, settings.xml может не примениться. Рекомендую закрыть игру.");
                }

                if (File.Exists(targetPath))
                {
                    string backupPath = MakeBackup(targetPath);
                    Log("Бэкап: " + backupPath);
                }

                if (File.Exists(targetPath))
                {
                    var attrs = File.GetAttributes(targetPath);
                    if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                    {
                        File.SetAttributes(targetPath, attrs & ~FileAttributes.ReadOnly);
                        Log("Снят атрибут ReadOnly");
                    }
                }

                doc.Save(targetPath);
                Log("✓ settings.xml применён.");
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Settings] EXCEPTION: " + ex);
                Console.ResetColor();
                return false;
            }
        }

        private static string MakeBackup(string filePath)
        {
            string ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dir = Path.GetDirectoryName(filePath);
            string name = Path.GetFileName(filePath);
            string backup = Path.Combine(dir, name + ".backup-" + ts);
            File.Copy(filePath, backup, overwrite: false);
            return backup;
        }

        private static void Log(string msg) => Console.WriteLine("[Settings] " + msg);
    }
}
