# dotnet publish single-file

Финальный артефакт - **один** `.exe` файл, ~250 МБ. Включает в себя .NET runtime, WPF, WebView2 loader, нативные DLL (RageLib native bits), весь React UI после `npm run build`.

## Команда сборки

```powershell title="scripts/publish.ps1"
cd MiamiGraphics.Shell

# Step 1: build React UI
Push-Location ui
npm install
npm run build
Pop-Location

# Step 2: dotnet publish
dotnet publish MiamiGraphics.Shell.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o ../publish
```

Результат - `publish/MiamiGraphics.Shell.exe`. Это **standalone** бинарь, не требует .NET установленного у юзера.

## Ключевые свойства MSBuild

| Property | Что делает |
|---|---|
| `PublishSingleFile=true` | Упаковать всё в один .exe |
| `IncludeNativeLibrariesForSelfExtract=true` | Включить нативные DLL (без этого они рядом останутся) |
| `EnableCompressionInSingleFile=true` | LZ4 сжатие (250 МБ → 80 МБ) |
| `DebugType=None`, `DebugSymbols=false` | Не включать PDB |
| `SelfContained=true` | Включить .NET runtime |

Это даёт `.exe` ~80 МБ после сжатия. Без сжатия был бы ~250 МБ. Запуск чуть медленнее (приходится распаковывать в `%TEMP%`), но размер критичнее.

## Распаковка single-file в runtime

При первом запуске .NET runtime распаковывает содержимое в `%TEMP%\.net\<HostId>\<version>\`. Это ~250 МБ файлов на диске. Запуск занимает ~5 секунд **только в первый раз**. Последующие запуски используют extracted cache (если timestamp не изменился) - миллисекунды.

Это **проблема** для нашего auto-update'а: после update у нового .exe новый timestamp, кеш distrust'ится, юзер опять ждёт 5 секунд. Это причина почему мы [скачиваем заранее](../update/inno-supabase.md) - пока юзер в лаунчере, мы экстрагируем новый билд, чтобы при перезапуске был быстрый старт.

## React UI как embedded resource

`ui/dist/` после `npm run build` - это `index.html` + JS-чанки + CSS. Чтобы они оказались внутри .exe, в `.csproj` есть:

```xml title="MiamiGraphics.Shell.csproj"
<ItemGroup>
  <Content Include="ui\dist\**\*" CopyToOutputDirectory="PreserveNewest">
    <Link>ui\%(RecursiveDir)%(FileName)%(Extension)</Link>
  </Content>
</ItemGroup>
```

При publish все файлы из `ui/dist/` попадают в `publish/ui/`. WebView2 потом грузит `file:///<path-to-self-extract>/ui/index.html`.

## installed_version.txt

Critical файл для auto-update - см. [installed-version-marker](../update/installed-version-marker.md). Эта файла **нет** в publish output по дефолту. Inno installer его пишет рядом с .exe после установки. Лаунчер при старте читает.

Это решение, потому что прямое чтение из MSBuild `<Version>` через `Assembly.GetExecutingAssembly().GetName().Version` **не работает** в single-file bundle (reflection возвращает 1.0.0.0 всегда).

## Размер vs Performance trade-offs

Альтернативы которые отвергнуты:

- **`PublishTrimmed=true`** - обрезает unused code. Экономит ~30 МБ. Но **ломает** reflection в JSON serialization, breaks Supabase client, breaks `XDocument.Load`. Слишком хрупко для нашего размера codebase.
- **ReadyToRun** (R2R) - pre-compile в native code. Ускорит startup, но добавит ~50 МБ.
- **NativeAOT** - полностью native compile. Не поддерживается WPF.

Выбран trade-off: single-file + compression, без trimming/R2R. Acceptable startup, простая поддержка.

## Build script полностью

```powershell title="scripts/publish.ps1 (full)"
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

# Bump version in csproj
$csproj = "MiamiGraphics.Shell/MiamiGraphics.Shell.csproj"
(Get-Content $csproj) -replace '<Version>.*</Version>', "<Version>$Version</Version>" |
    Set-Content $csproj

# Build UI
Push-Location MiamiGraphics.Shell/ui
npm install --silent
npm run build
Pop-Location

# Publish .NET
dotnet publish MiamiGraphics.Shell/MiamiGraphics.Shell.csproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o publish

# Rename output
Move-Item publish/MiamiGraphics.Shell.exe "publish/MiamiGraphics_$Version.exe" -Force

Write-Host "Published: publish/MiamiGraphics_$Version.exe"
```

[Дальше: Inno installer →](inno.md)
