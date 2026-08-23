using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.Parser;

namespace MiamiGraphics.Core.Services
{
    public class RebuildUpdateRpfService
    {
        private readonly InstalledModsService _journalService;
        private readonly CrossInjectBuilder _crossInjectBuilder;

        private static readonly JsonSerializerOptions ManifestJsonOptions = CreateManifestJsonOptions();

        public RebuildUpdateRpfService(InstalledModsService journalService)
        {
            _journalService = journalService;
            _crossInjectBuilder = new CrossInjectBuilder();
        }

        public bool Rebuild(string gtaPath)
        {
            var journal = _journalService.Load();

            if (string.IsNullOrWhiteSpace(journal.CleanUpdateRpfPath) ||
                !File.Exists(journal.CleanUpdateRpfPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Rebuild] Чистый update.rpf не задан или не найден: {journal.CleanUpdateRpfPath}");
                Console.WriteLine("[Rebuild] Задайте его в меню (пункт 7 → [P]).");
                Console.ResetColor();
                return false;
            }

            string updateRpfPath = Path.Combine(gtaPath, "update", "update.rpf");
            if (!TryRestoreCleanUpdateRpf(journal.CleanUpdateRpfPath, updateRpfPath))
                return false;

            if (File.Exists(updateRpfPath))
            {
                journal.LastUpdateRpfSize = new FileInfo(updateRpfPath).Length;
                journal.LastRebuiltAt = DateTime.Now;
                _journalService.Save(journal);
            }

            if (journal.ActiveMods.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Rebuild] Активных модов нет - update.rpf возвращён в чистое состояние.");
                Console.ResetColor();
                return true;
            }

            var injector = new RpfInjectEngine(gtaPath);
            int applied = 0;
            foreach (var record in journal.ActiveMods.OrderBy(r => r.InstalledAt))
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n[Rebuild] ━━━ Применяю: {record.ReduxName} ({record.Id}) ━━━");
                Console.ResetColor();

                if (!Directory.Exists(record.BasePatchPath))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Rebuild] SKIP: BasePatchPath не существует: {record.BasePatchPath}");
                    Console.ResetColor();
                    continue;
                }

                string manifestPath = Path.Combine(record.BasePatchPath, "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[Rebuild] SKIP: manifest.json не найден в {record.BasePatchPath}");
                    Console.ResetColor();
                    continue;
                }

                try
                {
                    ApplyOneMod(injector, gtaPath, record);
                    applied++;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[Rebuild] ОШИБКА на моде {record.ReduxName}: {ex.Message}");
                    Console.WriteLine(ex.StackTrace);
                    Console.ResetColor();
                }
            }

            if (journal.ActiveGunpack != null || journal.Hntgraph.SelectedGuns.Count > 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n[Rebuild] ━━━ Gunpack + Hntgraph ━━━");
                Console.ResetColor();

                var gunpackSvc = new GunpackBuilderService();
                if (!gunpackSvc.RebuildTargetDlc(gtaPath, journal, _journalService))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[Rebuild] WARN: RebuildTargetDlc не удался.");
                    Console.ResetColor();
                }
            }

            if (File.Exists(updateRpfPath))
            {
                journal.LastUpdateRpfSize = new FileInfo(updateRpfPath).Length;
                journal.LastRebuiltAt = DateTime.Now;
                _journalService.Save(journal);
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n[Rebuild] Готово. Применено: {applied}/{journal.ActiveMods.Count}");
            Console.ResetColor();
            return applied > 0;
        }

        private bool TryRestoreCleanUpdateRpf(string cleanPath, string targetUpdateRpfPath)
        {
            try
            {
                Console.WriteLine($"[Rebuild] Восстанавливаю чистый update.rpf из: {cleanPath}");

                if (File.Exists(targetUpdateRpfPath))
                {
                    string backupDir = Path.Combine(Path.GetDirectoryName(targetUpdateRpfPath)!, "_backup_before_rebuild");
                    Directory.CreateDirectory(backupDir);
                    string backupPath = Path.Combine(backupDir,
                        $"{DateTime.Now:yyyyMMdd_HHmmss}_update.rpf");
                    File.Copy(targetUpdateRpfPath, backupPath, overwrite: true);
                    Console.WriteLine($"[Rebuild] Backup старого: {backupPath}");
                }

                File.Copy(cleanPath, targetUpdateRpfPath, overwrite: true);
                Console.WriteLine($"[Rebuild] Чистый update.rpf восстановлен ({new FileInfo(targetUpdateRpfPath).Length / 1024 / 1024} МБ).");
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Rebuild] ОШИБКА при восстановлении: {ex.Message}");
                Console.ResetColor();
                return false;
            }
        }

        private void ApplyOneMod(RpfInjectEngine injector, string gtaPath, InstalledModRecord record)
        {
            string basePatchPath = record.BasePatchPath;
            string patchPathForInject = basePatchPath;
            DiffManifest? manifest = null;
            bool hasWorkingCopy = false;

            if (record.Customizations.CrossInjectDonors.Count > 0)
            {
                var donorSelections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var donor in record.Customizations.CrossInjectDonors)
                {
                    if (Directory.Exists(donor.DonorBasePatchPath))
                        donorSelections[donor.Component] = donor.DonorBasePatchPath;
                    else
                        Console.WriteLine($"[Rebuild] WARN: donor недоступен ({donor.Component}): {donor.DonorBasePatchPath}");
                }

                if (donorSelections.Count > 0)
                {
                    patchPathForInject = _crossInjectBuilder.BuildCrossInjectedPatch(basePatchPath, donorSelections);
                    hasWorkingCopy = true;
                    manifest = LoadManifest(patchPathForInject);
                }
            }

            if (record.Customizations.Minimap != null)
            {
                EnsureWorkingCopy(basePatchPath, ref patchPathForInject, ref manifest, ref hasWorkingCopy);

                var m = record.Customizations.Minimap;
                string? hitmarker = !string.IsNullOrWhiteSpace(m.HitmarkerPath) && File.Exists(m.HitmarkerPath)
                    ? m.HitmarkerPath : null;

                var minimap = new MinimapBuilderService();
                minimap.Customize(patchPathForInject, manifest!, gtaPath,
                    m.HealthR, m.HealthG, m.HealthB,
                    m.ArmourR, m.ArmourG, m.ArmourB,
                    hitmarker);
                SaveManifest(patchPathForInject, manifest!);
            }

            if (record.Customizations.Tracers != null)
            {
                var t = record.Customizations.Tracers;
                if (string.IsNullOrWhiteSpace(t.ModelDirectory) || !Directory.Exists(t.ModelDirectory))
                {
                    Console.WriteLine($"[Rebuild] WARN: Tracers ModelDirectory недоступен: {t.ModelDirectory}. Skip tracers.");
                }
                else
                {
                    EnsureWorkingCopy(basePatchPath, ref patchPathForInject, ref manifest, ref hasWorkingCopy);

                    var tracer = new TracerBuilderService();
                    tracer.Customize(new TracerCustomizationRequest
                    {
                        Scenario = TracerScenario.MasterTexture,
                        PatchRootDirectory = patchPathForInject,
                        Manifest = manifest!,
                        GtaRootPath = gtaPath,
                        Red = t.Red,
                        Green = t.Green,
                        Blue = t.Blue,
                        ModelDirectory = t.ModelDirectory
                    });
                    SaveManifest(patchPathForInject, manifest!);
                }
            }

            if (record.Customizations.Arena != null &&
                Directory.Exists(record.Customizations.Arena.DonorBasePatchPath))
            {
                EnsureWorkingCopy(basePatchPath, ref patchPathForInject, ref manifest, ref hasWorkingCopy);

                var arenaSvc = new ArenaTransferService();
                var res = arenaSvc.Transfer(new ArenaTransferRequest
                {
                    RecipientPatchRoot = patchPathForInject,
                    RecipientManifest = manifest!,
                    DonorPatchRoot = record.Customizations.Arena.DonorBasePatchPath
                });

                if (res.Success)
                    SaveManifest(patchPathForInject, manifest!);
            }

            Console.WriteLine($"[Rebuild] Inject: {patchPathForInject}");
            bool ok = injector.InjectPatch(patchPathForInject);
            if (!ok)
            {
                var detail = string.IsNullOrWhiteSpace(injector.LastError)
                    ? $"InjectPatch вернул false для {record.ReduxName}"
                    : injector.LastError;
                throw new Exception(detail);
            }
        }

        private void EnsureWorkingCopy(
            string basePatchPath,
            ref string patchPathForInject,
            ref DiffManifest? manifest,
            ref bool hasWorkingCopy)
        {
            if (hasWorkingCopy && manifest != null)
                return;

            patchPathForInject = _crossInjectBuilder.CreateWorkingPatchCopy(basePatchPath);
            manifest = LoadManifest(patchPathForInject);
            hasWorkingCopy = true;

            Console.WriteLine($"[Rebuild] Создана рабочая копия патча: {patchPathForInject}");
        }

        private static DiffManifest LoadManifest(string patchRootDirectory)
        {
            string manifestPath = Path.Combine(patchRootDirectory, "manifest.json");
            return JsonSerializer.Deserialize<DiffManifest>(
                File.ReadAllText(manifestPath), ManifestJsonOptions)
                ?? throw new InvalidOperationException($"manifest.json: {manifestPath}");
        }

        private static void SaveManifest(string patchRootDirectory, DiffManifest manifest)
        {
            PatchCustomizationSupport.RecalculateTotalPatchSize(manifest);
            string manifestPath = Path.Combine(patchRootDirectory, "manifest.json");
            File.WriteAllText(manifestPath,
                JsonSerializer.Serialize(manifest, ManifestJsonOptions));
        }

        private static JsonSerializerOptions CreateManifestJsonOptions()
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            opts.Converters.Add(new JsonStringEnumConverter());
            return opts;
        }
    }
}
