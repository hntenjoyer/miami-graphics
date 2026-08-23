# `installed_version.txt`

Маленький текстовый файл который **критически** важен для auto-update'ов.

## Содержимое

```
%LocalAppData%\MiamiGraphics\config\installed_version.txt
```

Один файл, одна строка - номер версии лаунчера:

```
1.0.2
```

## Зачем нужен

Версия лаунчера определяется через C# reflection:

```cs
var version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
// или
var info = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
return info.FileVersion;
```

Это работает в **multi-file** publish - DLL'и лежат на диске с правильным `FileVersionInfo`.

В **single-file** publish (то что у нас сейчас) reflection возвращает `0.0.0.0` или **пустую** строку. Single-file extract все DLL'и в temp при первом старте, и `Assembly.Location` указывает на temp. `FileVersionInfo` не подгружается.

Без marker файла наш `AppUpdateCheck` сравнивал бы `current = "0.0.0.0"` с `latest = "1.0.2"` → разные → **постоянный** prompt «обновись».

## Pipeline записи

`AppUpdateInstallAsync` пишет marker **ДО** запуска installer'а:

```cs title="AppBridge.cs"
// Запишем installed-version marker ДО запуска installer'а.
// Если apдейт завершится успешно - приложение перезапустится
// и при первом AppUpdateCheck увидит current=row.Version, latest=row.Version → no update.
// Если installer упадёт - юзер запустит приложение, увидит маркер, и AppUpdateCheck
// покажет «всё актуально» (что в худшем случае - false positive, но не infinite update loop).
WriteInstalledVersionMarker(row.Version);

var helperPath = await WriteUpdateHelperAsync(updateDir, installerPath, safeVersion);
Process.Start(...);   // PowerShell helper
Application.Current?.Shutdown();
```

## Pipeline чтения

```cs title="AppBridge.cs"
private static string GetCurrentAppVersion()
{
    // 1. Marker файл первым
    try {
        var path = Path.Combine(LocalAppData, "MiamiGraphics", "config", "installed_version.txt");
        if (File.Exists(path)) {
            var s = File.ReadAllText(path).Trim();
            if (!string.IsNullOrWhiteSpace(s)) return s;
        }
    } catch { }

    // 2. Fallback: csproj reflection (для dev-сборок, multi-file публикаций)
    try {
        var info = FileVersionInfo.GetVersionInfo(Assembly.GetEntryAssembly().Location);
        var v = info.ProductVersion ?? info.FileVersion;
        if (!string.IsNullOrWhiteSpace(v)) return v.Split('+')[0].Split('-')[0];
    } catch { }

    return "unknown";
}
```

`installed_version.txt` имеет приоритет. Reflection - fallback.

## Что было до marker

В первых релизах single-file publish reflection возвращал что-то странное (зависит от .NET runtime версии). Юзеры жаловались что каждый старт лаунчера показывает «доступно обновление» даже после установки latest версии.

Расследование показало:

1. AppUpdateCheck читает version через reflection.
2. Single-file extract возвращает `null` или empty string.
3. AppUpdateInfo: `current = ""`, `latest = "1.0.0"`.
4. `hasUpdate = "" != "1.0.0"` → **true**.
5. UI показывает prompt.
6. Юзер кликает «обновить» → скачивается installer → ставится та же версия → перезапуск → опять reflection пуст → опять prompt.

**Бесконечный update loop.**

После добавления marker:

1. `AppUpdateInstall` пишет marker = "1.0.0".
2. После перезапуска reflection пуст, но marker = "1.0.0" → current = "1.0.0".
3. `latest = "1.0.0"` → `hasUpdate = false` → нет prompt'а.

## Atomic write

Marker пишется через temp + rename как и [другие критичные файлы](../backup/atomic-writes.md):

```cs
private static void WriteInstalledVersionMarker(string version)
{
    var dir = Path.Combine(LocalAppData, "MiamiGraphics", "config");
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "installed_version.txt");
    var tmp = path + ".tmp";
    File.WriteAllText(tmp, version);
    File.Move(tmp, path, overwrite: true);
}
```

Защита от 0-byte marker при крэше во время записи.

## Что если marker удалён

Юзер может вручную почистить `%LocalAppData%\MiamiGraphics\` (Reset cache в Settings или просто рукой). Marker исчезнет.

При следующем `AppUpdateCheck`:

1. Marker not found → fallback к reflection.
2. Single-file reflection → пусто.
3. `current = "unknown"`, `latest = "1.0.2"`.
4. `hasUpdate = "unknown" != "1.0.2"` → true.

Юзер увидит prompt «обновись до 1.0.2». Кликнет → лаунчер скачает installer 1.0.2, **запишет marker** перед запуском, перезапустится, marker даст правильный version.

То есть **один раз** false positive prompt, но дальше всё стабильно. Это **acceptable** trade-off - не worth defending против ручной чистки кэша.

## Connection с Backup [`writing_working_update`](../backup/pipeline.md)

В одной из ранних версий мы хотели запихнуть проверку install_state.json (с PostInstallSha256) в backup pipeline (как stage `writing_working_update`). Идея - backup pipeline сам бы детектил «после нашего install'а нужно обновить marker».

Получилось плохо - backup и install это две **разные** операции, смешивать их state - путаница. Отозвали в commit `225bcf8`. С тех пор install_state и installed_version.txt живут отдельно от backup-manifest.

[Дальше: update loop incident →](loop-incident.md)
