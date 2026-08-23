# Inno + Supabase app_versions

Auto-update Miami Graphics - это два **простых** компонента:

1. Inno Setup installer (тот самый что юзер скачивает с сайта первый раз);
2. Supabase row в таблице `app_versions` - single source of truth для «какая версия сейчас актуальная».

## Таблица app_versions

```sql title="supabase migrations"
create table public.app_versions (
    id uuid primary key default gen_random_uuid(),
    version text not null,           -- '1.0.2'
    installer_url text not null,     -- https://miamigraphicsstorage.uk/releases/MiamiGraphics_Setup_1.0.2.exe
    sha256 text not null,
    size_bytes bigint not null,
    release_notes text,
    is_required boolean default false,
    is_active boolean default false,
    published_at timestamptz default now()
);

-- Только одна row может быть is_active=true:
create unique index app_versions_only_one_active on public.app_versions(is_active)
where is_active = true;
```

`is_active = true` помечает текущую актуальную версию. Все клиенты при `AppUpdateCheck` тянут эту row.

## AppUpdateCheck

При запуске лаунчера фоновый процесс делает:

```cs title="MiamiGraphics.Shell/Bridge/AppBridge.cs (упрощённо)"
public async Task<AppUpdateInfoDto> AppUpdateCheckAsync()
{
    var current = GetCurrentAppVersion();   // '1.0.0' например

    // Берём latest active row из Supabase
    var rows = await _supabase.SelectAsync<AppVersionRow>(
        "app_versions",
        "select=version,installer_url,sha256,size_bytes,release_notes,is_required,published_at" +
        "&is_active=eq.true&order=published_at.desc&limit=1");

    var latest = rows.FirstOrDefault();
    if (latest is null) return NoUpdate(current);

    // С 1.0.0+ - любая отличная активная версия = update, не только semver-большая.
    // Это позволяет нам делать "откаты" (rollback на патч), и юзер увидит prompt.
    var hasUpdate = !string.Equals(latest.Version, current, StringComparison.OrdinalIgnoreCase);

    return new AppUpdateInfoDto(hasUpdate, latest.IsRequired, current,
        latest.Version, latest.InstallerUrl, latest.ReleaseNotes,
        latest.SizeBytes, latest.Sha256, latest.PublishedAt);
}
```

`hasUpdate = latest != current` (а не `>`) сознательно. Это позволяет нам:

- Откатить релиз (опубликовать `1.0.1` снова после релиза `1.0.2` с критичным багом);
- Юзеры на `1.0.2` увидят prompt «обновись до 1.0.1».

При `>` логике откатывать нельзя - клиенты не увидят что версия ниже их.

## AppUpdateInstall

Юзер кликает «Обновить» в prompt. Лаунчер:

1. Скачивает installer.exe из `installer_url`.
2. Проверяет SHA-256.
3. Записывает marker `installed_version.txt = latest_version` (см. [следующий раздел](installed-version-marker.md)).
4. Запускает helper-скрипт через PowerShell.
5. Helper ждёт пока наш PID умрёт, потом запускает `installer.exe`.
6. Лаунчер сам себя exit'ит.
7. Installer ставит новую версию поверх (Inno UninstallPrevious удалит старую).

```cs title="AppBridge.cs (упрощённо)"
public async Task<AppUpdateInstallResultDto> AppUpdateInstallAsync(string version)
{
    var row = await SelectVersionRow(version);
    var installerPath = await DownloadInstallerAsync(row.InstallerUrl, version);

    if (!string.Equals(await Sha256FileAsync(installerPath), row.Sha256, StringComparison.OrdinalIgnoreCase))
    {
        File.Delete(installerPath);
        return new AppUpdateInstallResultDto(false, "SHA-256 не совпал", null);
    }

    // ВАЖНО: пишем marker ДО запуска installer'а
    WriteInstalledVersionMarker(row.Version);

    var helperPath = await WriteUpdateHelperAsync(updateDir, installerPath, safeVersion);
    Process.Start(new ProcessStartInfo {
        FileName = "powershell.exe",
        Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{helperPath}\"",
        UseShellExecute = false,
        CreateNoWindow = true,
    });

    // Сами exit'имся через 700 ms (UI успевает показать "перезапуск")
    _ = Task.Run(async () => {
        await Task.Delay(700);
        Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());
    });

    return new AppUpdateInstallResultDto(true, null, installerPath);
}
```

## Helper-скрипт

```powershell
# update_helper.ps1
$ourPid = ...    # передаётся параметром
$installer = "C:\Users\...\MiamiGraphics_Setup_1.0.2.exe"

# Ждём пока процесс Miami Graphics закроется
do {
    Start-Sleep -Milliseconds 200
    $alive = Get-Process -Id $ourPid -ErrorAction SilentlyContinue
} while ($alive)

# Запускаем installer
Start-Process -FilePath $installer
```

Так мы избегаем «installer не может перезаписать запущенный exe». PowerShell-helper это маленький способ обойти ограничение.

## Inno UninstallPrevious

Inno Setup при installation проверяет registry на наличие старой версии:

```ini title="installer-user.iss"
[Setup]
AppId={{B5C65F97-79B5-41F2-8F3C-9891146D0632}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Miami Graphics
UsePreviousAppDir=no
```

`AppId` (GUID) - стабильный между версиями. Если в реестре уже есть установка с этим AppId - Inno **удаляет** старую перед установкой новой. Это даёт чистый upgrade.

`UsePreviousAppDir=no` - не сохраняем dir юзера. Всегда ставим в default location.

## Что внутри installer.exe

```ini title="installer-user.iss [Files]"
Source: "publish\*"; DestDir: "{app}\app"; ...; Excludes: "*.xml,*.pdb,launchSettings.json,..."
Source: "redist\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "redist\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{app}\app\redist"; Flags: ignoreversion
```

Внутри installer:

- `publish/*` - single-file `Miami Graphics.exe` (132 МБ) + `additionals/` + `ui/`;
- `MicrosoftEdgeWebView2Setup.exe` - online-stub (1.7 МБ) для установки WebView2 Runtime если он отсутствует на машине юзера.

LZMA2 compression - финальный installer **132 МБ**. Это была [долгая оптимизация](../history/ragelib-v2.md) - раньше installer был 503 МБ (multi-file publish + offline WebView2 bundle).

## Online WebView2

```ini
[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe";
  Parameters: "/silent /install";
  Check: not BundledWebView2RuntimeInstalled and not WebView2RuntimeInstalled;
  Flags: waituntilterminated
```

`Check:` функция (на Pascal Script) - проверяет:

1. Лежит ли offline-bundle WebView2 рядом (для оффлайн-сборки);
2. Установлен ли уже WebView2 Runtime в системе (через registry check).

Если ни то ни то - запускает Microsoft online stub, который качает и ставит WebView2. На Win10/11 он почти всегда уже стоит (через Edge), поэтому этот шаг **обычно** skip'ается.

## Что юзер видит при upgrade

1. UI показывает баннер «Доступно обновление 1.0.2».
2. Юзер кликает «Обновить» → диалог «Скачиваем 132 МБ...».
3. Скачка через [FragmentingHttpHandler](../network/fragmenting-http-handler.md) - 30-60 секунд.
4. SHA проверка - 1 секунда.
5. Лаунчер закрывается.
6. Inno installer окно - «Удаляю старую версию...» → «Устанавливаю...» → готово.
7. Auto-launch новой версии через `Flags: postinstall runascurrentuser` в `[Run]`.

Полный upgrade - **2 минуты** в среднем. UX похож на VS Code или Discord (silent download + один UAC при installer'е).

[Дальше: installed_version.txt marker →](installed-version-marker.md)
