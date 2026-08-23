# Inno Setup installer

Распространяем Miami Graphics не как голый `.exe` от `dotnet publish`, а **обёрнутым** в Inno Setup 6 installer. Юзер скачивает `MiamiGraphicsSetup_1.0.2.exe`, кликает, проходит мастер «Next-Next-Finish», получает ярлык на десктопе и Start Menu entry.

## Почему не голый .exe

- **Юзер ожидает installer.** Это норма Windows. Голый `.exe` без installer'а воспринимается как «подозрительный».
- **Uninstall entry.** В Control Panel → Programs Inno автоматом регистрирует entry «Удалить Miami Graphics».
- **Path management.** Можем выбрать install dir (default `C:\Program Files\Miami Graphics`).
- **`installed_version.txt`.** Inno пишет этот файл в install dir после копирования .exe - критично для [auto-update](../update/installed-version-marker.md).
- **WebView2 runtime check.** Если у юзера нет WebView2 runtime (бывает на старых Win10) - Inno автоматически качает и ставит.

## Скрипт Inno

```pascal title="installer/installer.iss"
[Setup]
AppName=Miami Graphics
AppVersion={#AppVersion}
AppPublisher=Miami Graphics
AppPublisherURL=https://miamigraphics.app
DefaultDirName={pf}\Miami Graphics
DefaultGroupName=Miami Graphics
OutputDir=output
OutputBaseFilename=MiamiGraphicsSetup_{#AppVersion}
SetupIconFile=icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\MiamiGraphics_{#AppVersion}.exe"; \
    DestDir: "{app}"; \
    DestName: "MiamiGraphics.exe"; \
    Flags: ignoreversion

[Icons]
Name: "{group}\Miami Graphics"; Filename: "{app}\MiamiGraphics.exe"
Name: "{userdesktop}\Miami Graphics"; Filename: "{app}\MiamiGraphics.exe"; \
    Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; \
    GroupDescription: "Дополнительные значки:"; Flags: checkedonce

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  VersionFile: string;
begin
  if CurStep = ssPostInstall then
  begin
    // Записываем installed_version.txt - критично для auto-update
    VersionFile := ExpandConstant('{app}\installed_version.txt');
    SaveStringToFile(VersionFile, '{#AppVersion}', False);
  end;
end;

function InitializeSetup(): Boolean;
begin
  // Проверка WebView2
  if not IsWebView2Installed() then
    if MsgBox('Требуется Microsoft WebView2 runtime. Установить?',
              mbConfirmation, MB_YESNO) = IDYES then
      DownloadAndInstallWebView2();

  Result := True;
end;
```

## Powershell helper

Билд installer'а автоматизирован:

```powershell title="scripts/build_installer.ps1"
param([string]$Version = "1.0.0")

# Ensure publish exists
$exePath = "publish/MiamiGraphics_$Version.exe"
if (-not (Test-Path $exePath)) {
    throw "Сначала запусти scripts/publish.ps1"
}

# Run Inno compiler
$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
& $iscc /DAppVersion=$Version installer/installer.iss

# Output: installer/output/MiamiGraphicsSetup_<Version>.exe
$outputExe = "installer/output/MiamiGraphicsSetup_$Version.exe"
$sha = (Get-FileHash $outputExe -Algorithm SHA256).Hash
$size = (Get-Item $outputExe).Length

Write-Host "Installer ready: $outputExe"
Write-Host "  SHA-256: $sha"
Write-Host "  Size: $([math]::Round($size/1MB, 2)) MB"
```

## WebView2 detection

Inno умеет читать registry для проверки наличия WebView2:

```pascal
function IsWebView2Installed(): Boolean;
var
  Version: string;
begin
  Result := RegQueryStringValue(
    HKEY_LOCAL_MACHINE,
    'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'pv', Version);
  Result := Result and (Version <> '') and (Version <> '0.0.0.0');
end;
```

`{F3017226-...}` это product ID WebView2 runtime'а. На Win11 и свежем Win10 он preinstalled. На старых Win10 - нужно качать с Microsoft (Evergreen Bootstrapper, ~2 МБ stub который сам качает full runtime).

## Uninstall

При деинсталляции Inno:

1. Удаляет `MiamiGraphics.exe`.
2. Удаляет `installed_version.txt`.
3. Удаляет ярлыки.
4. **Не** удаляет `%LocalAppData%\MiamiGraphics\` - это юзерские данные (кеш, install-state, backups). Если юзер хочет clean - отдельная опция в installer-мастере «Также удалить пользовательские данные» (по default off).

## Размер installer'а

`MiamiGraphics_1.0.2.exe` (publish): 80 МБ
`MiamiGraphicsSetup_1.0.2.exe`: 82 МБ (Inno overhead ~2 МБ)

Inno использует LZMA2 ultra64 - лучшее сжатие, медленный distarchive (15 секунд на медленной машине). Это **одноразовая** медлительность (только на install).

## Альтернатива MSIX

MSIX - современный Windows install format, подаёт в Microsoft Store. Не выбрали потому что:

- Store ограничивает: нельзя править файлы вне sandbox (а нам надо в `Program Files (x86)\Rockstar Games\GTA V\`).
- Code signing требует MS Developer cert ($99/год).
- Юзеры всё равно ставят через `.exe`-download (Store reach в РФ нулевой).

Inno даёт нам гибкость без bureaucracy. На roadmap'е - [custom WPF installer](https://github.com/...) который заменит Inno на собственную обёрку в стиле лаунчера, но это отложено.

[Дальше: Релиз pipeline →](release-pipeline.md)
