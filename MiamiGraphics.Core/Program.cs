using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.Parser;
using MiamiGraphics.Core.Services;
using MiamiGraphics.Core.System;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core
{
    internal class Program
    {

        private static readonly string TracerModelsRoot = ResolveTracerModelsRoot();

        private static string ResolveTracerModelsRoot()
        {
            var direct = Path.Combine(AppContext.BaseDirectory, "additionals", "tracers");
            if (Directory.Exists(direct)) return direct;

            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                var p = Path.Combine(dir, "additionals", "tracers");
                if (Directory.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir);
            }

            var local = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiamiGraphics", "additionals", "tracers");
            if (Directory.Exists(local)) return local;

            return string.Empty;
        }

        private static readonly string ResultBaseDir = CrossInjectBuilder.ResultBaseDir;
        private static readonly string[] CrossInjectComponentNames =
        {
            "minimap",
            "map",
            "crosshair",
            "tracers",
            "bloodfx",
            "timecycle"
        };

        private static readonly JsonSerializerOptions ManifestJsonOptions = CreateManifestJsonOptions();

        private static string gtaPath = "";
        private static string lastResultFolder = "";

        private static async Task Main(string[] args)
        {
            Console.Title = "HUNTER GRAPHICS | Core Engine Pipeline";
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            try
            {
                LoadKeys();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[FATAL] Программа не может продолжить работу без ключей GTA 5.");
                Console.WriteLine(ex.Message);
                Console.ResetColor();
                Console.ReadKey();
                return;
            }

            if (args.Length >= 3 && args[0] == "render-whitelist")
            {
                await WhitelistRenderTool.RunAsync(args[1], args[2]);
                return;
            }

            if (args.Length >= 3 && args[0] == "render-explicit")
            {
                await WhitelistRenderTool.RunExplicitAsync(args[1], args[2]);
                return;
            }

            if (args.Length >= 3 && args[0] == "render-glb")
            {
                var okk = await GlbToPngRenderer.RenderAsync(args[1], args[2]);
                Console.WriteLine(okk ? "OK " + args[2] : "FAIL");
                return;
            }

            if (args.Length >= 4 && args[0] == "convert-ydr")
            {
                var ytds = global::System.IO.Directory.EnumerateFiles(args[3], "*.ytd")
                    .OrderBy(p => p.Contains("+hi", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ToList();
                Console.WriteLine($"convert-ydr: {ytds.Count} ytd");
                var okk = await YdrToGltfConverter.ConvertAsync(args[1], args[2], ytds);
                Console.WriteLine(okk ? "OK " + args[2] : "FAIL");
                return;
            }

            while (true)
            {
                Console.Clear();
                DrawHeader();
                DrawSessionStatus();

                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine(" 1. [INIT]   Найти GTA 5 и видеокарту");
                Console.WriteLine(" 2. [BACKUP] Проверить хеши и создать бекапы");
                Console.WriteLine(" 3. [PARSER] Спарсить Redux (создать патч-дифф)");
                Console.WriteLine(" 4. [INJECT] Заинжектить патч в игру");
                Console.WriteLine(" 5. [CLEAN]  Очистить папку с результатами");
                Console.WriteLine(" 6. [LIST]   Список доступных редуксов");
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine(" 7. [MODS]   Установленные моды (список)");
                Console.WriteLine(" 8. [REBUILD] Пересобрать update.rpf из активных модов");
                Console.WriteLine(" 9. [GUNPACK] Ганпаки (парсинг и установка)");
                Console.WriteLine("10. YDR → GLB (weapon)");
                Console.WriteLine("11. YDR Inspector (диагностика)");
                Console.WriteLine("12. GLB → PNG (preview)");
                Console.WriteLine("13. Применить low-настройки GTA V");
                Console.WriteLine("--------------------------------------------------");
                Console.WriteLine(" 0. Выход");
                Console.Write("\n Выберите действие: ");

                string input = Console.ReadLine()?.Trim() ?? "";
                Console.WriteLine();

                switch (input)
                {
                    case "1":
                        RunInitialization();
                        break;
                    case "2":
                        RunBackupService();
                        break;
                    case "3":
                        RunParserPipeline();
                        break;
                    case "4":
                        RunInjector();
                        break;
                    case "5":
                        ClearResultFolders();
                        break;
                    case "6":
                        PrintAvailableReduxes();
                        break;
                    case "7":
                        RunShowInstalledMods();
                        break;
                    case "8":
                        RunRebuildUpdateRpf();
                        break;
                    case "9":
                        RunGunpackMenu();
                        break;
                    case "10":
                    {
                        Console.Write("Путь к YDR файлу: ");
                        var ydrPath = Console.ReadLine()?.Trim().Trim('"');
                        if (string.IsNullOrWhiteSpace(ydrPath) || !File.Exists(ydrPath))
                        {
                            Console.WriteLine("Файл не найден.");
                            break;
                        }
                        var outPath = Path.ChangeExtension(ydrPath, ".glb");
                        Console.WriteLine($"Конвертация: {ydrPath}");
                        Console.WriteLine($"Вывод:       {outPath}");
                        var ok = await MiamiGraphics.Core.Services.YdrToGltfConverter.ConvertAsync(ydrPath, outPath);
                        Console.WriteLine(ok ? "✓ Готово" : "✗ Ошибка (см. лог выше)");
                        break;
                    }
                    case "11":
                    {
                        Console.Write("Путь к YDR файлу для инспекции: ");
                        var ydrPath = Console.ReadLine()?.Trim().Trim('"');
                        if (string.IsNullOrWhiteSpace(ydrPath) || !File.Exists(ydrPath))
                        {
                            Console.WriteLine("Файл не найден.");
                            break;
                        }
                        var report = await MiamiGraphics.Core.Services.YdrInspector.InspectAndSaveAsync(ydrPath);
                        Console.WriteLine("Отчёт: " + report);
                        break;
                    }
                    case "12":
                    {
                        Console.Write("Путь к GLB файлу: ");
                        var glbPath = Console.ReadLine()?.Trim().Trim('"');
                        if (string.IsNullOrWhiteSpace(glbPath) || !File.Exists(glbPath))
                        {
                            Console.WriteLine("Файл не найден.");
                            break;
                        }
                        var pngPath = Path.ChangeExtension(glbPath, ".png");
                        Console.WriteLine($"Рендер: {glbPath}");
                        Console.WriteLine($"Вывод:  {pngPath}");
                        var ok = await MiamiGraphics.Core.Services.GlbToPngRenderer.RenderAsync(glbPath, pngPath);
                        Console.WriteLine(ok ? "✓ PNG готов" : "✗ Ошибка рендера");
                        break;
                    }
                    case "13":
                    {
                        Console.WriteLine("Целевой settings.xml: " + MiamiGraphics.Core.Services.GtaSettingsService.GetSettingsPath());
                        Console.Write("Путь к source low-XML: ");
                        var sourceXml = Console.ReadLine()?.Trim().Trim('"');
                        if (string.IsNullOrWhiteSpace(sourceXml) || !File.Exists(sourceXml))
                        {
                            Console.WriteLine("Файл не найден.");
                            break;
                        }
                        var ok = await MiamiGraphics.Core.Services.GtaSettingsService.ApplyLowSettingsAsync(sourceXml);
                        Console.WriteLine(ok ? "✓ Применено" : "✗ Ошибка");
                        break;
                    }
                    case "0":
                        return;
                }

                Console.WriteLine("\nНажмите любую клавишу для возврата в меню...");
                Console.ReadKey();
            }
        }

        private static void LoadKeys()
        {
            Console.WriteLine("[INIT] Загрузка GTA5 ключей...");

            var candidates = new List<string>
            {
                Path.Combine(AppContext.BaseDirectory, "additionals", "keys"),
                Path.Combine(AppContext.BaseDirectory, "additionals"),
                Path.Combine(AppContext.BaseDirectory, "keys"),
            };
            var dir = AppContext.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                candidates.Add(Path.Combine(dir, "additionals", "keys"));
                candidates.Add(Path.Combine(dir, "additionals"));
                dir = Path.GetDirectoryName(dir);
            }
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiamiGraphics", "additionals", "keys"));

            foreach (var path in candidates)
            {
                try
                {
                    GTA5Constants.LoadFromPath(path);
                    Console.WriteLine($"[INIT] Ключи загружены из {path}");
                    return;
                }
                catch
                {
                    Console.WriteLine($"[WARN] Не удалось загрузить ключи из {path}");
                }
            }

            throw new Exception(
                "Не удалось найти ключи ни по одному из путей: " + string.Join(", ", candidates));

            if (GTA5Constants.PC_NG_KEYS == null || GTA5Constants.PC_NG_KEYS.Length == 0)
                throw new Exception("PC_NG_KEYS пустой после LoadFromPath");

            int loaded = GTA5Constants.PC_NG_KEYS.Count(key => key != null && key.Length > 0);
            Console.WriteLine($"[INIT] NG keys loaded: {loaded}/{GTA5Constants.PC_NG_KEYS.Length}");
            Console.WriteLine("Нажмите любую клавишу для продолжения...");
            Console.ReadKey();
        }

        private static void DrawHeader()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"  _    _ _    _ _   _ _______ ______ _____  ");
            Console.WriteLine(@" | |  | | |  | | \ | |__   __|  ____|  __ \ ");
            Console.WriteLine(@" | |__| | |  | |  \| |  | |  | |__  | |__) |");
            Console.WriteLine(@" |  __  | |  | | . ` |  | |  |  __| |  _  / ");
            Console.WriteLine(@" | |  | | |__| | |\  |  | |  | |____| | \ \ ");
            Console.WriteLine(@" |_|  |_|\____/|_| \_|  |_|  |______|_|  \_\");
            Console.ResetColor();
            Console.WriteLine("\n          PROJECT: HUNTER GRAPHICS");
            Console.WriteLine("==================================================");
        }

        private static void DrawSessionStatus()
        {
            if (string.IsNullOrWhiteSpace(gtaPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(" [!] Путь к GTA V: НЕ ОПРЕДЕЛЕН (выполните пункт 1)");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($" [OK] GTA V: {gtaPath}");
            }

            Console.ResetColor();

            if (!string.IsNullOrWhiteSpace(lastResultFolder))
                Console.WriteLine($" [OK] Последний результат: {lastResultFolder}");
        }

        private static void RunInitialization()
        {
            Console.WriteLine("[INIT] Поиск компонентов системы...");
            var locator = new HardwareLocator();
            gtaPath = locator.FindGtaPath();
            string gpu = locator.FindGpuName();

            if (!string.IsNullOrWhiteSpace(gtaPath))
                Console.WriteLine($"[SUCCESS] Игра найдена: {gtaPath}");
            else
                Console.WriteLine("[ERROR] Не удалось найти GTA 5 автоматически.");

            Console.WriteLine($"[INFO] Видеокарта: {gpu}");
        }

        private static void RunBackupService()
        {

            Console.WriteLine("[BACKUP] CLI backup is no longer supported here. Run the Shell UI.");
        }

        private static void RunParserPipeline()
        {
            if (string.IsNullOrWhiteSpace(gtaPath))
            {
                Console.WriteLine("Ошибка: сначала укажите путь к GTA.");
                return;
            }

            Console.Write("Введите название мода (например, Redux_TestMod): ");
            string? modName = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(modName))
                modName = "MyReduxMod";

            string cleanRpfPath = Path.Combine(gtaPath, @"update\update.rpf");

            Console.Write("Введите путь к модифицированному update.rpf: ");
            string? moddedRpfPath = Console.ReadLine();

            if (!File.Exists(cleanRpfPath) || string.IsNullOrWhiteSpace(moddedRpfPath) || !File.Exists(moddedRpfPath))
            {
                Console.WriteLine("[ERROR] Файлы архивов не найдены. Проверьте пути.");
                return;
            }

            Console.WriteLine("[PARSER] Запуск процесса сравнения и анализа...");
            var pipeline = new ReduxParserPipeline();
            lastResultFolder = pipeline.ParseRedux(cleanRpfPath, moddedRpfPath, modName);
        }

        private static void RunInjector()
        {
            if (string.IsNullOrWhiteSpace(gtaPath))
            {
                Console.WriteLine("Ошибка: сначала укажите путь к GTA.");
                return;
            }

            try
            {
                var tamperSvc = new InstalledModsService();
                string updateRpfPathForCheck = Path.Combine(gtaPath, "update", "update.rpf");
                string? warn = tamperSvc.CheckManualTamper(updateRpfPathForCheck);
                if (!string.IsNullOrWhiteSpace(warn))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n[WARN] " + warn);
                    Console.ResetColor();
                }
            }
            catch { }

            var installedCustomizations = new ModCustomizations();

            var builder = new CrossInjectBuilder();
            string? basePatchPath = SelectBaseRedux(builder);
            if (string.IsNullOrWhiteSpace(basePatchPath))
                return;

            string patchPathForInject = basePatchPath;
            bool hasWorkingCopy = false;
            DiffManifest? manifest = null;

            var donorSelections = AskCrossInjectSelections(builder, basePatchPath);
            if (donorSelections.Count > 0)
            {
                patchPathForInject = builder.BuildCrossInjectedPatch(basePatchPath, donorSelections);
                hasWorkingCopy = true;
                manifest = LoadManifest(patchPathForInject);
                Console.WriteLine($"[CrossInject] Собран объединенный патч: {patchPathForInject}");

                foreach (var kv in donorSelections)
                {
                    installedCustomizations.CrossInjectDonors.Add(new CrossInjectDonorRef
                    {
                        Component = kv.Key,
                        DonorBasePatchPath = kv.Value
                    });
                }
            }

            try
            {
                if (AskYesNo("Хотите кастомизировать миникарту? (y/n): "))
                {
                    EnsureWorkingPatchCopy(builder, basePatchPath, ref patchPathForInject, ref manifest, ref hasWorkingCopy);

                    (byte healthR, byte healthG, byte healthB) = ReadRgb("Введите RGB для Health (например, 255,0,0): ");
                    (byte armourR, byte armourG, byte armourB) = ReadRgb("Введите RGB для Armour: ");

                    Console.Write("Введите путь к PNG/GIF хитмаркеру (Enter = пропустить): ");
                    string? hitmarkerPath = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(hitmarkerPath))
                        hitmarkerPath = null;

                    if (!string.IsNullOrWhiteSpace(hitmarkerPath) && !File.Exists(hitmarkerPath))
                    {
                        Console.WriteLine("[MinimapBuilder] Файл хитмаркера не найден. Кастомный хитмаркер будет пропущен.");
                        hitmarkerPath = null;
                    }

                    var minimapBuilder = new MinimapBuilderService();
                    minimapBuilder.Customize(
                        patchPathForInject,
                        manifest!,
                        gtaPath,
                        healthR,
                        healthG,
                        healthB,
                        armourR,
                        armourG,
                        armourB,
                        hitmarkerPath);

                    SaveManifest(patchPathForInject, manifest!);

                    installedCustomizations.Minimap = new MinimapCustomization
                    {
                        HealthR = healthR, HealthG = healthG, HealthB = healthB,
                        ArmourR = armourR, ArmourG = armourG, ArmourB = armourB,
                        HitmarkerPath = hitmarkerPath
                    };
                }

                if (AskYesNo("Хотите кастомизировать трейсеры? (y/n): "))
                {
                    string? modelDirectory = SelectTracerModelDirectory();
                    if (!string.IsNullOrWhiteSpace(modelDirectory))
                    {
                        EnsureWorkingPatchCopy(builder, basePatchPath, ref patchPathForInject, ref manifest, ref hasWorkingCopy);

                        (byte tracerR, byte tracerG, byte tracerB) = ReadRgb("Введите RGB для трейсера: ");

                        var tracerBuilder = new TracerBuilderService();

                        tracerBuilder.Customize(new TracerCustomizationRequest
                        {
                            Scenario           = TracerScenario.MasterTexture,
                            PatchRootDirectory = patchPathForInject,
                            Manifest           = manifest!,
                            GtaRootPath        = gtaPath,
                            Red                = tracerR,
                            Green              = tracerG,
                            Blue               = tracerB,
                            ModelDirectory     = modelDirectory
                        });

                        SaveManifest(patchPathForInject, manifest!);

                        installedCustomizations.Tracers = new TracerCustomization
                        {
                            Scenario = "MasterTexture",
                            ModelDirectory = modelDirectory,
                            Red = tracerR, Green = tracerG, Blue = tracerB
                        };

                        YptHashDiagnostic.Separator("AFTER TracerBuilder.Customize");
                        {
                            string physicalCoreYpt = Path.Combine(
                                patchPathForInject,
                                "patch_files",
                                "x64", "patch", "data", "effects", "ptfx.rpf", "core.ypt");
                            YptHashDiagnostic.DumpPhysicalFile(
                                "patch_files/.../core.ypt AFTER TracerBuilder",
                                physicalCoreYpt);
                        }
                    }
                }

                if (AskYesNo("Хотите перенести арену из другого редукса? (y/n): "))
                {
                    EnsureWorkingPatchCopy(builder, basePatchPath, ref patchPathForInject, ref manifest, ref hasWorkingCopy);

                    string? donorPath = SelectArenaDonor(builder, basePatchPath);
                    if (!string.IsNullOrWhiteSpace(donorPath))
                    {
                        var arenaService = new ArenaTransferService();
                        var arenaResult = arenaService.Transfer(new ArenaTransferRequest
                        {
                            RecipientPatchRoot = patchPathForInject,
                            RecipientManifest  = manifest!,
                            DonorPatchRoot     = donorPath
                        });

                        if (arenaResult.Success)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[ArenaTransfer] {arenaResult.Message}");
                            foreach (string f in arenaResult.TransferredFiles)
                                Console.WriteLine($"[ArenaTransfer] + {f}");
                            Console.ResetColor();
                            SaveManifest(patchPathForInject, manifest!);

                            installedCustomizations.Arena = new ArenaCustomization
                            {
                                DonorBasePatchPath = donorPath
                            };
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[ArenaTransfer] {arenaResult.Message}");
                            Console.ResetColor();
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[CUSTOMIZE] Ошибка кастомизации: {ex.Message}");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("!!! ВНИМАНИЕ: Это действие изменит корневые архивы игры. Убедитесь, что сделан бекап. !!!");
            Console.ResetColor();

            if (!AskYesNo("Начать инжект мода? (y/n): "))
                return;

            YptHashDiagnostic.Separator("BEFORE INJECT");
            {
                string physicalCoreYpt = Path.Combine(
                    patchPathForInject,
                    "patch_files",
                    "x64", "patch", "data", "effects", "ptfx.rpf", "core.ypt");
                YptHashDiagnostic.DumpPhysicalFile(
                    "patch_files/.../core.ypt BEFORE inject",
                    physicalCoreYpt);
            }

            Console.WriteLine("\n[INJECT] Вскрытие архивов и применение патча...");
            var injector = new RpfInjectEngine(gtaPath);

            Stopwatch stopwatch = Stopwatch.StartNew();
            bool success = injector.InjectPatch(patchPathForInject);
            stopwatch.Stop();

            if (success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[SUCCESS] Мод успешно установлен за {stopwatch.ElapsedMilliseconds} мс. Хеши исправлены.");

                YptHashDiagnostic.Separator("AFTER INJECT (inside update.rpf)");
                {
                    string updateRpfPath = Path.Combine(gtaPath, "update", "update.rpf");
                    YptHashDiagnostic.DumpInsideUpdateRpf(
                        "update.rpf/.../core.ypt AFTER inject",
                        updateRpfPath,
                        "x64/patch/data/effects/ptfx.rpf/core.ypt");
                }

                {
                    string updateRpfPathForHc = Path.Combine(gtaPath, "update", "update.rpf");
                    UpdateRpfHealthCheck.Run(updateRpfPathForHc);
                }

                try
                {
                    string updateRpfPath = Path.Combine(gtaPath, "update", "update.rpf");
                    long updateRpfSize = File.Exists(updateRpfPath)
                        ? new FileInfo(updateRpfPath).Length : 0;

                    string reduxName = Path.GetFileName(basePatchPath) ?? "unknown";
                    var record = new InstalledModRecord
                    {
                        ReduxName = reduxName,
                        InstalledAt = DateTime.Now,
                        BasePatchPath = basePatchPath,
                        TotalPatchSizeBytes = manifest?.TotalPatchSize ?? 0,
                        ActionsCount = manifest?.Actions.Count ?? 0,
                        Customizations = installedCustomizations
                    };

                    new InstalledModsService().RecordInstall(record, updateRpfSize);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[InstalledMods] WARN: не удалось записать журнал: {ex.Message}");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[FAILED] Ошибка при инжекте.");
            }

            Console.ResetColor();
        }

        private static string? SelectBaseRedux(CrossInjectBuilder builder)
        {
            IReadOnlyList<ReduxPatchInfo> reduxes = builder.GetAvailableReduxes();

            if (reduxes.Count == 0)
            {
                Console.Write("Путь к папке патча (где лежит manifest.json): ");
                return ValidatePatchFolder(Console.ReadLine());
            }

            Console.WriteLine("[INJECT] Доступные редуксы:");
            for (int i = 0; i < reduxes.Count; i++)
            {
                Console.WriteLine($" {i + 1}. {reduxes[i].ReduxName} | {reduxes[i].FolderName} | {reduxes[i].ParsedAt:yyyy-MM-dd HH:mm:ss}");
            }

            Console.Write("Выберите базовый редукс по номеру или введите путь вручную: ");
            string? input = Console.ReadLine();

            if (int.TryParse(input, out int selectedIndex) && selectedIndex >= 1 && selectedIndex <= reduxes.Count)
                return reduxes[selectedIndex - 1].FolderPath;

            return ValidatePatchFolder(input);
        }

        private static Dictionary<string, string> AskCrossInjectSelections(CrossInjectBuilder builder, string basePatchPath)
        {
            var selections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!AskYesNo("Хотите импортировать компоненты из другого редукса? (y/n): "))
                return selections;

            foreach (string componentName in CrossInjectComponentNames)
            {
                IReadOnlyList<ReduxPatchInfo> candidates = builder.GetDonorCandidates(basePatchPath, componentName);
                if (candidates.Count == 0)
                {
                    Console.WriteLine($"[CrossInject] Для компонента {componentName} доноры не найдены.");
                    continue;
                }

                Console.WriteLine($"\n[CrossInject] Доноры для {componentName}:");
                for (int i = 0; i < candidates.Count; i++)
                {
                    Console.WriteLine($" {i + 1}. {candidates[i].ReduxName} | {candidates[i].FolderName} | {candidates[i].ParsedAt:yyyy-MM-dd HH:mm:ss}");
                }
                Console.WriteLine(" 0. Пропустить");
                Console.Write($"Выберите донор для {componentName}: ");

                string? input = Console.ReadLine();
                if (!int.TryParse(input, out int selectedIndex) || selectedIndex < 0 || selectedIndex > candidates.Count)
                {
                    Console.WriteLine($"[CrossInject] Компонент {componentName} пропущен: некорректный выбор.");
                    continue;
                }

                if (selectedIndex == 0)
                    continue;

                selections[componentName] = candidates[selectedIndex - 1].FolderPath;
            }

            return selections;
        }

        private static void EnsureWorkingPatchCopy(
            CrossInjectBuilder builder,
            string basePatchPath,
            ref string patchPathForInject,
            ref DiffManifest? manifest,
            ref bool hasWorkingCopy)
        {
            if (hasWorkingCopy && manifest != null)
                return;

            patchPathForInject = builder.CreateWorkingPatchCopy(basePatchPath);
            manifest = LoadManifest(patchPathForInject);
            hasWorkingCopy = true;

            Console.WriteLine($"[PATCH] Создана рабочая копия патча: {patchPathForInject}");
        }

        private static string? SelectArenaDonor(CrossInjectBuilder builder, string selfPatchRoot)
        {
            IReadOnlyList<ReduxPatchInfo> candidates = builder.GetDonorCandidates(selfPatchRoot, "arena");
            if (candidates.Count == 0)
            {
                Console.WriteLine("[ArenaTransfer] Доноры с компонентом 'arena' не найдены.");
                return null;
            }

            Console.WriteLine("[ArenaTransfer] Доступные редуксы-доноры с ареной:");
            for (int i = 0; i < candidates.Count; i++)
                Console.WriteLine($" {i + 1}. {candidates[i].ReduxName} | {candidates[i].FolderName} | {candidates[i].ParsedAt:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine(" 0. Отмена");
            Console.Write("Выберите донор: ");

            string? input = Console.ReadLine();
            if (!int.TryParse(input, out int idx) || idx < 0 || idx > candidates.Count)
            {
                Console.WriteLine("[ArenaTransfer] Некорректный выбор.");
                return null;
            }

            if (idx == 0) return null;
            return candidates[idx - 1].FolderPath;
        }

        private static string? SelectTracerModelDirectory()
        {
            if (!Directory.Exists(TracerModelsRoot))
            {
                Console.WriteLine($"[TracerBuilder] Папка с моделями не найдена: {TracerModelsRoot}");
                return null;
            }

            string[] directories = Directory.GetDirectories(TracerModelsRoot)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (directories.Length == 0)
            {
                Console.WriteLine($"[TracerBuilder] В {TracerModelsRoot} нет папок с моделями.");
                return null;
            }

            Console.WriteLine("[TracerBuilder] Доступные модели:");
            for (int i = 0; i < directories.Length; i++)
            {
                Console.WriteLine($" {i + 1}. {Path.GetFileName(directories[i])}");
            }

            while (true)
            {
                Console.Write("Выберите модель по номеру (0 = отмена): ");
                string? input = Console.ReadLine();

                if (int.TryParse(input, out int selectedIndex))
                {
                    if (selectedIndex == 0)
                        return null;

                    if (selectedIndex >= 1 && selectedIndex <= directories.Length)
                        return directories[selectedIndex - 1];
                }

                Console.WriteLine("Некорректный выбор. Попробуйте еще раз.");
            }
        }

        private static (byte R, byte G, byte B) ReadRgb(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                string? input = Console.ReadLine();

                if (TryParseRgb(input, out byte red, out byte green, out byte blue))
                    return (red, green, blue);

                Console.WriteLine("Некорректный RGB. Используйте формат R,G,B.");
            }
        }

        private static bool TryParseRgb(string? input, out byte red, out byte green, out byte blue)
        {
            red = 0;
            green = 0;
            blue = 0;

            if (string.IsNullOrWhiteSpace(input))
                return false;

            string[] parts = input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3)
                return false;

            return byte.TryParse(parts[0], out red) &&
                   byte.TryParse(parts[1], out green) &&
                   byte.TryParse(parts[2], out blue);
        }

        private static bool AskYesNo(string prompt)
        {
            Console.Write(prompt);
            ConsoleKey key = Console.ReadKey().Key;
            Console.WriteLine();
            return key == ConsoleKey.Y;
        }

        private static void ClearResultFolders()
        {
            if (!Directory.Exists(ResultBaseDir))
            {
                Console.WriteLine($"Папка результатов не найдена: {ResultBaseDir}");
                return;
            }

            List<string> removableDirectories = Directory.GetDirectories(ResultBaseDir)
                .Where(path => !Path.GetFileName(path).StartsWith("_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (removableDirectories.Count == 0)
            {
                Console.WriteLine("Удалять нечего. Служебные папки сохранены.");
                return;
            }

            Console.WriteLine($"Будут удалены {removableDirectories.Count} папок из {ResultBaseDir}.");
            if (!AskYesNo("Продолжить? (y/n): "))
                return;

            foreach (string directory in removableDirectories)
            {
                Directory.Delete(directory, true);
                Console.WriteLine($"[CLEAN] Удалено: {directory}");
            }
        }

        private static void PrintAvailableReduxes()
        {
            var builder = new CrossInjectBuilder();
            IReadOnlyList<ReduxPatchInfo> reduxes = builder.GetAvailableReduxes();

            if (reduxes.Count == 0)
            {
                Console.WriteLine($"В {ResultBaseDir} пока нет спарсенных редуксов.");
                return;
            }

            Console.WriteLine($"Найдено редуксов: {reduxes.Count}");

            foreach (ReduxPatchInfo redux in reduxes)
            {
                string foundComponents = redux.ComponentMap?.Components?
                    .Where(kv => kv.Value != null && kv.Value.IsFound)
                    .Select(kv => kv.Key)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .DefaultIfEmpty("нет компонентов")
                    .Aggregate((left, right) => $"{left}, {right}") ?? "нет компонентов";

                Console.WriteLine($"- {redux.ReduxName} | {redux.FolderName} | {redux.ParsedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Путь: {redux.FolderPath}");
                Console.WriteLine($"  Компоненты: {foundComponents}");
            }
        }

        private static string? ValidatePatchFolder(string? patchPath)
        {
            if (string.IsNullOrWhiteSpace(patchPath) || !Directory.Exists(patchPath))
            {
                Console.WriteLine("Ошибка: папка патча не найдена.");
                return null;
            }

            string manifestFile = Path.Combine(patchPath, "manifest.json");
            if (!File.Exists(manifestFile))
            {
                Console.WriteLine("Ошибка: в папке патча не найден manifest.json.");
                return null;
            }

            return patchPath;
        }

        private static DiffManifest LoadManifest(string patchRootDirectory)
        {
            string manifestPath = Path.Combine(patchRootDirectory, "manifest.json");
            return JsonSerializer.Deserialize<DiffManifest>(File.ReadAllText(manifestPath), ManifestJsonOptions)
                ?? throw new InvalidOperationException($"Не удалось прочитать manifest.json: {manifestPath}");
        }

        private static void SaveManifest(string patchRootDirectory, DiffManifest manifest)
        {
            PatchCustomizationSupport.RecalculateTotalPatchSize(manifest);
            string manifestPath = Path.Combine(patchRootDirectory, "manifest.json");
            string json = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
            File.WriteAllText(manifestPath, json);
        }

        private static JsonSerializerOptions CreateManifestJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private static void RunShowInstalledMods()
        {
            Console.Clear();
            DrawHeader();
            Console.WriteLine("--- [MODS] Установленные моды ---\n");

            var svc = new InstalledModsService();
            var j = svc.Load();

            Console.WriteLine($"Файл журнала: {svc.JournalPath}");
            Console.WriteLine($"Чистый update.rpf: {j.CleanUpdateRpfPath ?? "НЕ ЗАДАН"}");
            Console.WriteLine($"Последний rebuild: {j.LastRebuiltAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-"}");
            Console.WriteLine();

            if (j.ActiveGunpack != null)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"Активный ганпак: {j.ActiveGunpack.Name} ({j.ActiveGunpack.Id}) " +
                                  $"[{j.ActiveGunpack.Mode}] установлен {j.ActiveGunpack.InstalledAt:yyyy-MM-dd HH:mm}");
                Console.ResetColor();
                Console.WriteLine();
            }

            if (j.ActiveMods.Count == 0)
            {
                Console.WriteLine("Активных модов нет.");
            }
            else
            {
                Console.WriteLine($"АКТИВНЫЕ МОДЫ ({j.ActiveMods.Count}):");
                for (int i = 0; i < j.ActiveMods.Count; i++)
                {
                    var r = j.ActiveMods[i];
                    Console.WriteLine($" [{i + 1}] {r.ReduxName}  (id: {r.Id})");
                    Console.WriteLine($"     Установлен: {r.InstalledAt:yyyy-MM-dd HH:mm}");
                    Console.WriteLine($"     Вес: {r.TotalPatchSizeBytes / 1024 / 1024} МБ, Actions: {r.ActionsCount}");
                    Console.WriteLine($"     Path: {r.BasePatchPath}");

                    var c = r.Customizations;
                    if (c.Minimap != null)
                    {
                        Console.WriteLine($"     + Minimap:");
                        Console.WriteLine($"         Health:    RGB({c.Minimap.HealthR}, {c.Minimap.HealthG}, {c.Minimap.HealthB})");
                        Console.WriteLine($"         Armour:    RGB({c.Minimap.ArmourR}, {c.Minimap.ArmourG}, {c.Minimap.ArmourB})");
                        if (!string.IsNullOrWhiteSpace(c.Minimap.HitmarkerPath))
                            Console.WriteLine($"         Hitmarker: {c.Minimap.HitmarkerPath}");
                    }
                    if (c.Tracers != null)
                    {
                        Console.WriteLine($"     + Tracers:");
                        Console.WriteLine($"         Scenario:  {c.Tracers.Scenario}");
                        Console.WriteLine($"         Color:     RGB({c.Tracers.Red}, {c.Tracers.Green}, {c.Tracers.Blue})");
                        Console.WriteLine($"         Model:     {c.Tracers.ModelDirectory ?? "-"}");
                    }
                    if (c.Arena != null)
                    {
                        Console.WriteLine($"     + Arena:");
                        Console.WriteLine($"         Donor:     {c.Arena.DonorBasePatchPath}");
                    }
                    if (c.CrossInjectDonors.Count > 0)
                    {
                        Console.WriteLine($"     + CrossInject ({c.CrossInjectDonors.Count}):");
                        foreach (var d in c.CrossInjectDonors)
                            Console.WriteLine($"         {d.Component,-12} ← {d.DonorBasePatchPath}");
                    }
                    Console.WriteLine();
                }
            }

            if (j.InactiveHistory.Count > 0)
            {
                Console.WriteLine($"\nИСТОРИЯ ДЕАКТИВИРОВАННЫХ (последние 5 из {j.InactiveHistory.Count}):");
                foreach (var r in j.InactiveHistory.TakeLast(5))
                    Console.WriteLine($"  {r.InstalledAt:yyyy-MM-dd HH:mm}  {r.ReduxName}  (id: {r.Id})");
            }

            Console.WriteLine("\nДействия:");
            Console.WriteLine(" [D] Деактивировать запись (по номеру или Id)");
            Console.WriteLine(" [P] Задать путь к чистому update.rpf");
            Console.WriteLine(" [Enter] Назад");
            Console.Write("\n> ");
            string? cmd = Console.ReadLine()?.Trim().ToUpperInvariant();

            if (cmd == "D")
            {
                if (j.ActiveMods.Count == 0)
                {
                    Console.WriteLine("Нет активных записей.");
                    return;
                }

                Console.Write("Номер из списка (1, 2, ...) ИЛИ 8-значный Id: ");
                string? input = Console.ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(input))
                {
                    Console.WriteLine("Ввод пуст, отмена.");
                    return;
                }

                string? targetId = null;

                if (int.TryParse(input, out int num) && num >= 1 && num <= j.ActiveMods.Count)
                {
                    targetId = j.ActiveMods[num - 1].Id;
                    Console.WriteLine($"Выбрано: [{num}] {j.ActiveMods[num - 1].ReduxName}");
                }
                else
                {
                    var byId = j.ActiveMods.FirstOrDefault(r =>
                        string.Equals(r.Id, input, StringComparison.OrdinalIgnoreCase));
                    if (byId != null)
                        targetId = byId.Id;
                }

                if (string.IsNullOrWhiteSpace(targetId))
                {
                    Console.WriteLine("Запись не найдена (ни как номер, ни как Id).");
                    return;
                }

                bool ok = svc.Deactivate(targetId);
                Console.WriteLine(ok
                    ? "✓ Деактивирована."
                    : "⚠ Запись не найдена (возможно, журнал обновился параллельно).");

                var jAfter = svc.Load();
                Console.WriteLine($"Осталось активных: {jAfter.ActiveMods.Count}, в истории: {jAfter.InactiveHistory.Count}");

                if (string.IsNullOrWhiteSpace(gtaPath))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine();
                    Console.WriteLine("⚠ gtaPath не задан (пункт 1 не выполнялся).");
                    Console.WriteLine("  Физически мод всё ещё в update.rpf. Когда будете готовы - сделайте");
                    Console.WriteLine("  пункт 1 (INIT), затем пункт 8 (REBUILD) для полного удаления.");
                    Console.ResetColor();
                    return;
                }

                if (string.IsNullOrWhiteSpace(jAfter.CleanUpdateRpfPath) ||
                    !File.Exists(jAfter.CleanUpdateRpfPath))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine();
                    Console.WriteLine("⚠ Чистый update.rpf не задан. Сначала задайте его через [P].");
                    Console.WriteLine("  После этого сделайте пункт 8 (REBUILD) - мод физически уйдёт из игры.");
                    Console.ResetColor();
                    return;
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("▶ Запускаю пересборку update.rpf...");
                Console.WriteLine("  Восстановится чистый update.rpf, затем применятся оставшиеся активные моды.");
                Console.ResetColor();
                Console.WriteLine();

                try
                {
                    var rebuilder = new RebuildUpdateRpfService(svc);
                    bool rebuildOk = rebuilder.Rebuild(gtaPath);

                    if (rebuildOk)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine();
                        Console.WriteLine("✓ Мод физически удалён из update.rpf.");
                        Console.ResetColor();
                    }
                    else if (jAfter.ActiveMods.Count == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine();
                        Console.WriteLine("✓ Последний мод удалён. update.rpf возвращён к чистому состоянию.");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine();
                        Console.WriteLine("⚠ Пересборка вернула false. Посмотрите логи выше.");
                        Console.ResetColor();
                    }
                }
                catch (Exception rex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine();
                    Console.WriteLine($"✗ Ошибка при пересборке: {rex.Message}");
                    Console.WriteLine("  Запись в журнале уже деактивирована, но update.rpf может быть");
                    Console.WriteLine("  в промежуточном состоянии. Попробуйте пункт 8 вручную.");
                    Console.ResetColor();
                }
            }
            else if (cmd == "P")
            {
                Console.Write("Полный путь к чистому update.rpf: ");
                string? p = Console.ReadLine()?.Trim('"', ' ');
                if (!string.IsNullOrWhiteSpace(p) && File.Exists(p))
                {
                    var jFresh = svc.Load();
                    jFresh.CleanUpdateRpfPath = p;
                    svc.Save(jFresh);
                    Console.WriteLine("✓ Сохранено.");
                }
                else
                {
                    Console.WriteLine("Файл не найден.");
                }
            }
        }

        private static void RunRebuildUpdateRpf()
        {
            Console.Clear();
            DrawHeader();
            Console.WriteLine("--- [REBUILD] Пересборка update.rpf ---\n");

            if (string.IsNullOrWhiteSpace(gtaPath))
            {
                Console.WriteLine("Сначала укажите путь к GTA (пункт 1).");
                return;
            }

            var svc = new InstalledModsService();
            var j = svc.Load();

            if (string.IsNullOrWhiteSpace(j.CleanUpdateRpfPath) || !File.Exists(j.CleanUpdateRpfPath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Чистый update.rpf не задан. Задайте его через пункт 7 → [P]");
                Console.ResetColor();
                return;
            }

            Console.WriteLine($"Чистый update.rpf: {j.CleanUpdateRpfPath}");
            Console.WriteLine($"Активных модов: {j.ActiveMods.Count}");
            foreach (var r in j.ActiveMods.OrderBy(x => x.InstalledAt))
                Console.WriteLine($"  • {r.ReduxName} ({r.InstalledAt:yyyy-MM-dd HH:mm})");

            if (!AskYesNo("\nНачать пересборку? (y/n): "))
                return;

            var rebuilder = new RebuildUpdateRpfService(svc);
            rebuilder.Rebuild(gtaPath);
        }

        private static void RunGunpackMenu()
        {
            while (true)
            {
                Console.Clear();
                DrawHeader();
                Console.WriteLine("--- [GUNPACK] Ганпаки ---\n");

                var svc = new InstalledModsService();
                var journal = svc.Load();

                Console.WriteLine($"Активный ганпак: {(journal.ActiveGunpack != null ? $"{journal.ActiveGunpack.Name} ({journal.ActiveGunpack.Id})" : "нет")}");
                Console.WriteLine();
                Console.WriteLine(" 1. Спарсить новый ганпак (input: путь к dlc.rpf)");
                Console.WriteLine(" 2. Список спарсенных ганпаков");
                Console.WriteLine(" 3. Установить ганпак целиком (FullPack)");
                Console.WriteLine(" 4. Деактивировать активный ганпак");
                Console.WriteLine(" 5. Вернуть чистый dlc.rpf (force rollback - снести всё)");
                Console.WriteLine(" 6. Выбрать отдельные ганы");
                Console.WriteLine(" 7. Мой список выбранных ганов");
                Console.WriteLine(" 0. Назад");
                Console.Write("\nВыбор: ");

                var key = Console.ReadKey().Key;
                Console.WriteLine("\n");

                if (key == ConsoleKey.D0) return;
                if (key == ConsoleKey.D1) RunGunpackParse();
                else if (key == ConsoleKey.D2) RunGunpackList();
                else if (key == ConsoleKey.D3) RunGunpackInstallFull();
                else if (key == ConsoleKey.D4) RunGunpackDeactivate();
                else if (key == ConsoleKey.D5) RunGunpackForceRollback();
                else if (key == ConsoleKey.D6) RunGunpackSelectGuns();
                else if (key == ConsoleKey.D7) RunGunpackMyGunsList();

                Console.WriteLine("\nНажмите любую клавишу...");
                Console.ReadKey();
            }
        }

        private static void RunGunpackParse()
        {
            Console.WriteLine("Введите путь к dlc.rpf ганпака (drag&drop работает):");
            Console.Write("> ");
            string? raw = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(raw)) return;
            string path = raw.Trim().Trim('"').Trim();
            if (!File.Exists(path))
            {
                Console.WriteLine("Файл не найден.");
                return;
            }

            var parser = new GunpackParserService();
            var res = parser.Parse(new GunpackParserService.ParseRequest
            {
                SourceDlcRpfPath = path
            });

            if (!res.Success)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Ошибка: {res.Message}");
                Console.ResetColor();
            }
        }

        private static void RunGunpackList()
        {
            var packs = GunpackParserService.ListAvailableGunpacks();
            if (packs.Count == 0)
            {
                Console.WriteLine("Спарсенных ганпаков нет.");
                return;
            }

            for (int i = 0; i < packs.Count; i++)
            {
                var p = packs[i];
                Console.WriteLine($"[{i + 1}] {p.Name}");
                Console.WriteLine($"     Parsed: {p.ParsedAt:yyyy-MM-dd HH:mm}");
                Console.WriteLine($"     Категорий ганов: {p.Categories.Count}, weapons: {p.WeaponsRpfSize / 1024 / 1024} МБ");
                Console.WriteLine($"     Source: {p.SourceDlcRpfPath}");
                if (p.UnrecognizedFiles.Count > 0)
                    Console.WriteLine($"     ⚠ Нераспознанных: {p.UnrecognizedFiles.Count}");
                Console.WriteLine();
            }
        }

        private static void RunGunpackInstallFull()
        {
            if (string.IsNullOrWhiteSpace(gtaPath))
            {
                Console.WriteLine("Сначала пункт 1 (INIT).");
                return;
            }

            var packs = GunpackParserService.ListAvailableGunpacks();
            if (packs.Count == 0)
            {
                Console.WriteLine("Нет спарсенных ганпаков. Сначала пункт 1 (Parse).");
                return;
            }

            for (int i = 0; i < packs.Count; i++)
                Console.WriteLine($"[{i + 1}] {packs[i].Name}  ({packs[i].ParsedAt:yyyy-MM-dd HH:mm})");

            Console.Write("\nНомер ганпака для установки (0 = отмена): ");
            string? inp = Console.ReadLine()?.Trim();
            if (!int.TryParse(inp, out int num) || num < 1 || num > packs.Count) return;

            var selected = packs[num - 1];
            string gunpacksRoot = Path.Combine(MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir, "Gunpacks");
            string? gunpackDir = Directory.GetDirectories(gunpacksRoot)
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "gunpack_info.json")) &&
                                     TryMatchGunpackDir(d, selected));
            if (gunpackDir == null)
            {
                Console.WriteLine("Не удалось найти папку выбранного ганпака.");
                return;
            }

            var journalSvc = new InstalledModsService();
            var j = journalSvc.Load();
            if (j.ActiveGunpack != null)
                j.GunpackHistory.Add(j.ActiveGunpack);

            j.ActiveGunpack = new GunpackInstallRecord
            {
                Name = selected.Name,
                InstalledAt = DateTime.Now,
                Mode = GunpackMode.FullPack,
                GunpackDir = gunpackDir
            };
            journalSvc.Save(j);

            var svc = new GunpackBuilderService();
            if (svc.RebuildTargetDlc(gtaPath, j, journalSvc))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n✓ Ганпак {selected.Name} установлен.");
                if (j.Hntgraph.SelectedGuns.Count > 0)
                    Console.WriteLine($"  + ваши ганы ({j.Hntgraph.SelectedGuns.Count}) поверх");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n✗ Rebuild target dlc.rpf не удался.");
                Console.ResetColor();
            }
        }

        private static void RunGunpackDeactivate()
        {
            var svc = new InstalledModsService();
            var j = svc.Load();
            if (j.ActiveGunpack == null)
            {
                Console.WriteLine("Активного ганпака нет.");
                return;
            }

            Console.WriteLine($"Активный ганпак: {j.ActiveGunpack.Name} ({j.ActiveGunpack.Id})");
            if (j.Hntgraph.SelectedGuns.Count > 0)
                Console.WriteLine($"  Выбранные ганы ({j.Hntgraph.SelectedGuns.Count}) СОХРАНЯТСЯ.");
            Console.Write("Деактивировать ганпак? (y/n): ");
            if (!AskYesNo(""))
                return;

            if (string.IsNullOrWhiteSpace(gtaPath))
            {
                Console.WriteLine("gtaPath пустой - пункт 1 (INIT) не выполнен. Обновляю только журнал.");
                j.GunpackHistory.Add(j.ActiveGunpack);
                j.ActiveGunpack = null;
                svc.Save(j);
                return;
            }

            j.GunpackHistory.Add(j.ActiveGunpack);
            j.ActiveGunpack = null;
            svc.Save(j);

            var gunpackSvc = new GunpackBuilderService();
            if (gunpackSvc.RebuildTargetDlc(gtaPath, j, svc))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Ганпак деактивирован. Выбранные ганы остались (если были).");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Rebuild target dlc.rpf не удался. Проверьте эталон.");
                Console.ResetColor();
            }
        }

        private static void RunGunpackForceRollback()
        {
            if (string.IsNullOrWhiteSpace(gtaPath))
            {
                Console.WriteLine("Сначала пункт 1 (INIT).");
                return;
            }

            var svc = new InstalledModsService();
            var j = svc.Load();

            Console.WriteLine("Полный сброс: target dlc.rpf будет возвращён к чистому эталону.");
            if (j.ActiveGunpack != null)
                Console.WriteLine($"  Активный ганпак: {j.ActiveGunpack.Name} → в историю");
            if (j.Hntgraph.SelectedGuns.Count > 0)
                Console.WriteLine($"  Выбранные ганы: {j.Hntgraph.SelectedGuns.Count} → ОЧИЩАЮТСЯ");
            Console.Write("\nПродолжить? (y/n): ");
            if (!AskYesNo(""))
                return;

            if (j.ActiveGunpack != null)
            {
                j.GunpackHistory.Add(j.ActiveGunpack);
                j.ActiveGunpack = null;
            }
            j.Hntgraph.SelectedGuns.Clear();
            j.Hntgraph.LastBuiltAt = null;
            j.Hntgraph.LastBuiltSize = 0;
            j.Hntgraph.LastBuiltSha256 = null;
            svc.Save(j);

            var gunpackSvc = new GunpackBuilderService();
            if (gunpackSvc.RollbackToClean(gtaPath, out string err))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Полный сброс выполнен.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Ошибка: {err}");
                Console.ResetColor();
            }
        }

        private static bool TryMatchGunpackDir(string dir, GunpackInfo info)
        {
            try
            {
                string infoPath = Path.Combine(dir, "gunpack_info.json");
                if (!File.Exists(infoPath)) return false;
                var actual = JsonSerializer.Deserialize<GunpackInfo>(File.ReadAllText(infoPath));
                return actual != null
                    && actual.Name == info.Name
                    && actual.ParsedAt == info.ParsedAt;
            }
            catch { return false; }
        }

        private static void RunGunpackSelectGuns()
        {
            if (string.IsNullOrWhiteSpace(gtaPath))
            {
                Console.WriteLine("Сначала пункт 1 (INIT).");
                return;
            }

            var packs = GunpackParserService.ListAvailableGunpacks();
            if (packs.Count == 0)
            {
                Console.WriteLine("Нет спарсенных ганпаков. Сначала пункт 1 (Parse).");
                return;
            }

            Console.WriteLine("Выберите ганпак-источник:");
            for (int i = 0; i < packs.Count; i++)
                Console.WriteLine($"[{i + 1}] {packs[i].Name}  ({packs[i].Categories.Count} категорий)");
            Console.Write("\nНомер (0 = отмена): ");
            if (!int.TryParse(Console.ReadLine(), out int pIdx) || pIdx < 1 || pIdx > packs.Count)
                return;

            var pack = packs[pIdx - 1];

            string gunpacksRoot = Path.Combine(MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir, "Gunpacks");
            string? gunpackDir = Directory.GetDirectories(gunpacksRoot)
                .FirstOrDefault(d =>
                {
                    string ip = Path.Combine(d, "gunpack_info.json");
                    if (!File.Exists(ip)) return false;
                    try
                    {
                        var actual = JsonSerializer.Deserialize<GunpackInfo>(File.ReadAllText(ip));
                        return actual != null && actual.Name == pack.Name && actual.ParsedAt == pack.ParsedAt;
                    }
                    catch { return false; }
                });
            if (gunpackDir == null)
            {
                Console.WriteLine("Не удалось найти папку ганпака.");
                return;
            }

            Console.Clear();
            Console.WriteLine($"Категории в ганпаке {pack.Name}:\n");
            for (int i = 0; i < pack.Categories.Count; i++)
            {
                var c = pack.Categories[i];
                Console.WriteLine($"[{i + 1,3}] {c.DisplayName}  ({c.Files.Count} файлов)");
            }
            Console.WriteLine("\nВведите номера через запятую (например: 1,3,5) или 'all'. 0 = отмена.");
            Console.Write("> ");
            string? raw = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(raw) || raw == "0") return;

            List<GunCategory> chosen = new();
            if (raw.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                chosen.AddRange(pack.Categories);
            }
            else
            {
                foreach (var piece in raw.Split(','))
                {
                    if (int.TryParse(piece.Trim(), out int n) && n >= 1 && n <= pack.Categories.Count)
                        chosen.Add(pack.Categories[n - 1]);
                }
            }

            if (chosen.Count == 0)
            {
                Console.WriteLine("Ничего не выбрано.");
                return;
            }

            var jsvc = new InstalledModsService();
            var journal = jsvc.Load();

            foreach (var cat in chosen)
            {
                journal.Hntgraph.SelectedGuns.RemoveAll(g =>
                    g.WeaponPrefix.Equals(cat.WeaponPrefix, StringComparison.OrdinalIgnoreCase) &&
                    g.BaseName.Equals(cat.BaseName, StringComparison.OrdinalIgnoreCase));

                journal.Hntgraph.SelectedGuns.Add(new SelectedGunRecord
                {
                    GunpackDir = gunpackDir,
                    GunpackName = pack.Name,
                    BaseName = cat.BaseName,
                    WeaponPrefix = cat.WeaponPrefix,
                    DisplayName = cat.DisplayName,
                    Files = new List<string>(cat.Files),
                    AddedAt = DateTime.Now
                });
            }

            jsvc.Save(journal);

            Console.WriteLine($"\nДобавлено/заменено: {chosen.Count}. Пересобираю target...");

            var gunpackSvc = new GunpackBuilderService();
            if (gunpackSvc.RebuildTargetDlc(gtaPath, journal, jsvc))
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ Готово.");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ Rebuild не удался.");
                Console.ResetColor();
            }
        }

        private static void RunGunpackMyGunsList()
        {
            var jsvc = new InstalledModsService();
            var journal = jsvc.Load();

            if (journal.Hntgraph.SelectedGuns.Count == 0)
            {
                Console.WriteLine("Список пуст.");
                return;
            }

            Console.WriteLine($"Ваши выбранные ганы ({journal.Hntgraph.SelectedGuns.Count}):\n");
            for (int i = 0; i < journal.Hntgraph.SelectedGuns.Count; i++)
            {
                var g = journal.Hntgraph.SelectedGuns[i];
                Console.WriteLine($"[{i + 1,3}] {g.DisplayName}");
                Console.WriteLine($"       из ганпака: {g.GunpackName}");
                Console.WriteLine($"       файлов: {g.Files.Count}, добавлен: {g.AddedAt:yyyy-MM-dd HH:mm}");
            }

            Console.WriteLine("\nДействия:");
            Console.WriteLine(" [номер]  удалить ган по номеру");
            Console.WriteLine(" [ALL]    удалить все (спросим подтверждение)");
            Console.WriteLine(" [Enter]  назад");
            Console.Write("\n> ");
            string? cmd = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(cmd)) return;

            if (cmd.Equals("ALL", StringComparison.OrdinalIgnoreCase))
            {
                Console.Write($"Точно удалить все {journal.Hntgraph.SelectedGuns.Count} ганов? (y/n): ");
                if (!AskYesNo("")) return;

                journal.Hntgraph.SelectedGuns.Clear();
                jsvc.Save(journal);
                Console.WriteLine("✓ Очищено.");
            }
            else if (int.TryParse(cmd, out int n) && n >= 1 && n <= journal.Hntgraph.SelectedGuns.Count)
            {
                var removed = journal.Hntgraph.SelectedGuns[n - 1];
                journal.Hntgraph.SelectedGuns.RemoveAt(n - 1);
                jsvc.Save(journal);
                Console.WriteLine($"✓ Удалён: {removed.DisplayName}");
            }
            else
            {
                Console.WriteLine("Не понял команду.");
                return;
            }

            if (!string.IsNullOrWhiteSpace(gtaPath))
            {
                var gunpackSvc = new GunpackBuilderService();
                if (gunpackSvc.RebuildTargetDlc(gtaPath, journal, jsvc))
                    Console.WriteLine("✓ target dlc.rpf пересобран.");
                else
                    Console.WriteLine("✗ Rebuild не удался.");
            }
            else
            {
                Console.WriteLine("gtaPath не задан - только журнал обновлён.");
            }
        }
    }
}
