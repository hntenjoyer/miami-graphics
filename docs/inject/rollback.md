# Откат и `.bak`

Есть несколько слоёв защиты от испорченного `update.rpf`.

## Слой 1: `.preinstall` snapshot

В самом начале install pipeline, **до** Smart Rebuild, делаем копию текущего `update.rpf`:

```cs title="MiamiGraphics.Shell/Bridge/AppBridge.cs:4079"
await Task.Run(() => File.Copy(updateRpf, rollbackPath, overwrite: true));
// rollbackPath = "<gta>/update/update.rpf.preinstall"
```

Это **точный** snapshot того что было до. Если на следующих шагах что-то крашит (Smart Rebuild упал на корруптном donor file, ArchiveFix отвалился, диск кончился) - у нас есть точная копия.

`TryRollbackAsync`:

```cs
private async Task TryRollbackAsync(string updateRpf, string rollbackPath)
{
    try
    {
        if (File.Exists(rollbackPath))
        {
            await Task.Run(() => File.Copy(rollbackPath, updateRpf, overwrite: true));
            Debug.WriteLine("[redux.install] rolled back to preinstall snapshot");
        }
    }
    finally
    {
        try { if (File.Exists(rollbackPath)) File.Delete(rollbackPath); } catch { }
    }
}
```

После успешного install rollback файл **удаляется**. Если процесс убит между snapshot и delete - `.preinstall` остаётся, его нужно вычистить вручную или при следующем install.

## Слой 2: `.bak` внутри Smart Rebuild

В самом `InjectSmartRebuild` есть **второй** уровень защиты - atomic swap через три File.Move:

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:151"
if (File.Exists(backupPath)) File.Delete(backupPath);
File.Move(sourceArchivePath, backupPath);     // update.rpf → update.rpf.bak
File.Move(tempPath, sourceArchivePath);       // update.rpf.hnt_temp → update.rpf
File.Delete(backupPath);                       // удаляем .bak
```

Между шагом 1 и 2 есть **окно** в несколько миллисекунд где `update.rpf` отсутствует физически - он переименован в `.bak`. Если процесс убит именно в этот момент (SIGKILL, BSOD, отключение света) - у юзера в `<gta>/update/` лежит **только** `update.rpf.bak`.

GTA при запуске откажется грузить (нет файла) - но это **легко исправимо** если мы найдём этот висячий `.bak`.

Catch-блок в `InjectSmartRebuild` пытается откатить:

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:158"
catch (Exception ex)
{
    SetError($"Ошибка Rebuild: {ex.GetType().Name}: {ex.Message}");

    try
    {
        if (File.Exists(tempPath)) File.Delete(tempPath);
        if (File.Exists(backupPath) && !File.Exists(sourceArchivePath))
            File.Move(backupPath, sourceArchivePath);
    }
    catch (Exception cleanupEx) { /* ... */ }

    return false;
}
```

Но **это работает только если процесс прошёл через catch**. Если убили SIGKILL - catch не вызывается.

## Слой 3: OrphanBackupRecovery на старте лаунчера

Чтобы покрыть SIGKILL-сценарий, на каждый запуск Miami Graphics сканируем GTA-папки на висячие `.bak`:

```cs title="MiamiGraphics.Shell/Services/OrphanBackupRecovery.cs"
public static class OrphanBackupRecovery
{
    public static void Run()
    {
        var locator = new MiamiGraphics.Core.System.HardwareLocator();
        var gtaPath = locator.FindGtaPath();
        if (string.IsNullOrWhiteSpace(gtaPath) || !Directory.Exists(gtaPath))
            return;

        var probeDirs = new[]
        {
            Path.Combine(gtaPath, "update"),
            Path.Combine(gtaPath, "update", "x64", "dlcpacks"),
            Path.Combine(gtaPath, "x64", "audio", "sfx"),
            Path.Combine(gtaPath, "mods", "update"),
            Path.Combine(gtaPath, "mods", "update", "x64", "dlcpacks"),
            Path.Combine(gtaPath, "mods", "x64", "audio", "sfx"),
        };

        int restored = 0, removed = 0;
        foreach (var dir in probeDirs.Where(Directory.Exists))
        {
            foreach (var bakPath in Directory.EnumerateFiles(dir, "*.bak", SearchOption.TopDirectoryOnly))
            {
                var origPath = bakPath.Substring(0, bakPath.Length - 4); // отрезаем ".bak"
                if (!File.Exists(origPath))
                {
                    // Только .bak, .rpf нет → восстанавливаем
                    File.Move(bakPath, origPath);
                    restored++;
                }
                else
                {
                    // .rpf И .bak оба → .bak это residual после crash на шаге 3
                    File.Delete(bakPath);
                    removed++;
                }
            }
            // Также чистим .hnt_temp файлы - это temporary rebuild output
            foreach (var tmpPath in Directory.EnumerateFiles(dir, "*.hnt_temp", SearchOption.TopDirectoryOnly))
                File.Delete(tmpPath);
        }
    }
}
```

Вызывается из `App.OnStartup`:

```cs title="MiamiGraphics.Shell/App.xaml.cs"
protected override void OnStartup(StartupEventArgs e)
{
    try { MiamiGraphics.Shell.Services.MiamiPathMigration.EnsureMiamiLayout(); } catch {}
    try { MiamiGraphics.Shell.Services.OrphanBackupRecovery.Run(); } catch {}
    // ... остальной startup ...
}
```

Это **идемпотентно**: запускается каждый startup, best-effort, log-only. Не валит лаунчер если что-то странное в GTA-папке.

## Сценарии что покрыто

| Сценарий | Слой 1 (preinstall) | Слой 2 (catch) | Слой 3 (orphan-bak) |
|---|---|---|---|
| Smart Rebuild упал с exception | ✅ восстанавливает | ✅ откатывает `.bak` | - |
| ArchiveFix вернул error | ✅ восстанавливает | - | - |
| Процесс убит между File.Move шагами 1 и 2 | - | - | ✅ восстанавливает `.bak` на старте |
| Процесс убит между File.Move 2 и 3 | - | - | ✅ удаляет residual `.bak` |
| BSOD во время Smart Rebuild | - | - | ✅ удаляет `.hnt_temp` |
| Power-loss до atomic swap | - | - | ✅ удаляет `.hnt_temp` (update.rpf не тронут) |
| Power-loss после `.preinstall` создан но `update.rpf` целый | manual cleanup | - | - |

Слой 1 пишется один раз перед началом, слой 2 только при обычном exception flow, слой 3 спасает катастрофические сценарии.

Если все три слоя не сработали (диск умер, гта-папка переименована) - финальный fallback это [BackupService.RestoreFromCleanAsync](../backup/pipeline.md). Юзер кликнет «Восстановить чистый» в UI, мы возьмём reference из локального кеша или CDN.

## Что было до этих слоёв

В первых релизах был **только** слой 2 (`.bak` внутри SmartRebuild). Юзеры с нестабильным компом (особенно ноутбуки в энергосберегающем режиме) изредка ловили "GTA files corrupted" после установки мода. Потому что Windows может усыпить процесс или kill'нуть его за высокое потребление CPU.

Слой 3 (OrphanBackupRecovery) добавили после [одного публичного инцидента](../update/loop-incident.md) - у RP-сервера случился наплыв юзеров, и сразу 8 человек написали что после установки redux'а GTA не запускается. Полчаса разбирались, оказалось у каждого `.bak` без `.rpf` в `<gta>/update/`. Восстановили вручную, на следующий день написали слой 3.

Слой 1 (`.preinstall` snapshot) - добавили ещё раньше, как только начали поддерживать customize (где сам install pipeline может упасть на этапе apply кастомного компонента, не на Smart Rebuild).

[Дальше: UpdateRpfMutex →](mutex.md)
