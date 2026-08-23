using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiamiGraphics.Core.Services
{
    public class InstalledModsService
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public string JournalPath { get; }

        public InstalledModsService(string? customPath = null)
        {
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                JournalPath = customPath!;
            }
            else
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                                ?? AppContext.BaseDirectory;
                JournalPath = Path.Combine(exeDir, "installed_mods.json");
            }
        }

        public InstalledModsJournal Load()
        {
            try
            {
                if (!File.Exists(JournalPath))
                    return new InstalledModsJournal();

                string json = File.ReadAllText(JournalPath);
                if (string.IsNullOrWhiteSpace(json))
                    return new InstalledModsJournal();

                var j = JsonSerializer.Deserialize<InstalledModsJournal>(json, JsonOpts);
                return j ?? new InstalledModsJournal();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[InstalledMods] Не удалось прочитать {JournalPath}: {ex.Message}");
                Console.WriteLine("[InstalledMods] Создаю пустой журнал.");
                Console.ResetColor();
                return new InstalledModsJournal();
            }
        }

        public void Save(InstalledModsJournal journal)
        {
            try
            {
                string? dir = Path.GetDirectoryName(JournalPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(journal, JsonOpts);
                File.WriteAllText(JournalPath, json);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[InstalledMods] ОШИБКА сохранения {JournalPath}: {ex.Message}");
                Console.ResetColor();
            }
        }

        public void RecordInstall(InstalledModRecord record, long updateRpfSizeAfter)
        {
            var j = Load();
            j.ActiveMods.Add(record);
            j.LastRebuiltAt = DateTime.Now;
            j.LastUpdateRpfSize = updateRpfSizeAfter;
            Save(j);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[InstalledMods] + Записано: {record.ReduxName} ({record.Id})");
            Console.ResetColor();
        }

        public bool Deactivate(string id)
        {
            var j = Load();
            var rec = j.ActiveMods.FirstOrDefault(r =>
                string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
            if (rec == null) return false;

            j.ActiveMods.Remove(rec);
            j.InactiveHistory.Add(rec);
            Save(j);
            return true;
        }

        public string? CheckManualTamper(string currentUpdateRpfPath, long toleranceBytes = 1_048_576)
        {
            if (!File.Exists(currentUpdateRpfPath)) return null;

            var j = Load();
            if (j.LastUpdateRpfSize <= 0) return null;

            long actual = new FileInfo(currentUpdateRpfPath).Length;
            long diff = Math.Abs(actual - j.LastUpdateRpfSize);
            if (diff <= toleranceBytes) return null;

            return $"Размер update.rpf изменился с момента последней установки.\n" +
                   $"  Было:   {j.LastUpdateRpfSize:N0} байт ({j.LastUpdateRpfSize / 1024.0 / 1024.0:F1} МБ)\n" +
                   $"  Сейчас: {actual:N0} байт ({actual / 1024.0 / 1024.0:F1} МБ)\n" +
                   $"  Разница: {diff:N0} байт. Возможно, были ручные изменения.\n" +
                   $"  При пересборке они будут затёрты.";
        }
    }
}
