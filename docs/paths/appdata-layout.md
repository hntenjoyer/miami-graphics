# AppData layout

Всё что наш лаунчер пишет на диск юзера хранится в **двух** местах:

1. `<Program Files>\Miami Graphics\` - сама программа, поставленная Inno installer'ом. Read-only для юзера, требует admin для записи.
2. `%LocalAppData%\MiamiGraphics\` - все данные юзера (бэкапы, кэш, конфиг, временные файлы). Per-user, без UAC.

## Структура

```
%LocalAppData%\MiamiGraphics\
├── config\
│   ├── installed_version.txt         ← версия лаунчера (для AppUpdateCheck)
│   ├── install_state.json            ← SHA после последнего успешного install
│   ├── app_settings.json             ← UI настройки (theme, accent, GTA path, region)
│   └── region.json                   ← EU/RU выбор для Supabase proxy
├── backup\
│   ├── manifest.json                 ← каким файлам какая версия GTA
│   ├── clean\
│   │   ├── update_1.0.3788.0.rpf     ← чистая копия от Rockstar
│   │   └── dlc_1.0.3788.0.rpf
│   └── snapshot\
│       └── update_1.0.3788.0_20260519_142345.rpf  ← оригинал юзера до того как мы влезли
├── cache\
│   └── assets\                       ← AssetCache (превью, PNG, GLB)
│       ├── <hash>.bin
│       └── <hash>.bin.meta
├── tmp\
│   ├── redux_install_<id>\           ← рабочая директория install pipeline
│   ├── customize_<reduxId>\          ← для apply кастомизации
│   ├── Renderer.tmp.zip              ← во время скачивания Renderer бутстрапером
│   └── Jre.tmp.zip
├── additionals\                      ← опционально, если не на месте основной
│   ├── ArchiveFix.exe
│   ├── Keys\
│   │   ├── gtav_aes_key.dat
│   │   └── gtav_ng_key.dat
│   ├── jre\                          ← qual-downloaded через JreBootstrapper
│   └── minimap\
│       ├── ffdec.jar
│       └── swfmill.exe
└── log\
    └── 2026-05-19.log                ← debug-output из Miami Graphics.exe
```

## Зачем в LocalAppData а не в Program Files

`Program Files` требует UAC для записи. Если лаунчер пишет туда юзерские данные:

- Каждое сохранение настроек = UAC prompt;
- Нет per-user изоляции (если несколько юзеров на PC - пересекаются настройки);
- Антивирусы подозрительно смотрят на запись приложения в свою же install-папку.

`LocalAppData` (`C:\Users\<user>\AppData\Local\MiamiGraphics`) per-user, без UAC, никаких prompt'ов. Это **стандартное** место для приложений Windows.

## Файлы которые мы НЕ трогаем

### `Documents\Rockstar Games\GTA V\`

Здесь Rockstar Launcher хранит:

- `settings.xml` - графические настройки игры
- save-файлы
- профили

Мы **читаем** `settings.xml` чтобы импортировать настройки от PRO-игроков, но **не пишем** в эту папку. Если юзер кликнул «применить настройки PRO-игрока», мы записываем в `settings.xml` через `bridge.gtaSettingsWrite()` - но это user-explicit action, не неявная запись.

### `Documents\Rockstar Games\Social Club\`

Профили Social Club, тут вообще не лезем. Это privacy-sensitive - там сохранены аккаунты, токены.

### Реестр

Минимум - только записываем uninstall info через Inno installer (для отображения в «Программы и компоненты» в Windows). Никаких настроек в реестре, всё в файлах.

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\
└── {B5C65F97-79B5-41F2-8F3C-9891146D0632}_is1\   ← Inno генерирует
    ├── DisplayName: Miami Graphics
    ├── UninstallString: "C:\Program Files\Miami Graphics\unins000.exe"
    └── ...
```

## Cleanup при uninstall

Inno installer при uninstall:

1. Удаляет `<Program Files>\Miami Graphics\` (все файлы программы).
2. **Удаляет** `%LocalAppData%\MiamiGraphics\` целиком.
3. Удаляет Start Menu shortcuts и Desktop shortcut.
4. Удаляет registry uninstall info.

Не удаляем:

- **`<gta>/update/update.rpf`** - там стоит наш мод. Юзер должен сам выбрать что с ним делать (можно сделать «Restore clean» в Miami **до** uninstall'а, либо принять что мод останется до следующего GTA update'а).
- **`<gta>/update/update.rpf.preinstall`** - если случайно осталась. Не наша забота.
- **`<gta>/update/*.bak`** - то же самое (см. [rollback](../inject/rollback.md)).

Можно было бы при uninstall'е делать restore-clean автоматом. Не делаем - это **дорогая** операция (2-4 минуты на копирование 2 ГБ), и юзер часто uninstall'ит без необходимости откатить (например для переустановки лаунчера или из-за временного бага).

## Factory reset (с Miami Graphics)

В Settings есть кнопка «Сбросить» которая делает **полную** очистку:

```cs title="MiamiGraphics.Shell/Bridge/AppBridge.cs (FactoryResetAndRestartAsync)"
public async Task FactoryResetAndRestartAsync()
{
    // 1. Снести %LocalAppData%\MiamiGraphics\
    // 2. Снести %TEMP%\MiamiGraphics\
    // 3. Удалить DeviceId из реестра HKCU
    // 4. Снести WebView2 user-data (через external helper script
    //    потому что наш процесс держит lock на эту папку)
    // 5. Перезапустить лаунчер
}
```

WebView2 user-data (где кэш React приложения) занят пока процесс жив. Удалять её надо **после exit'а** нашего процесса. Поэтому пишем helper-script который:

1. Ждёт пока pid X (наш) умрёт.
2. Удаляет WebView2 папку.
3. Запускает Miami Graphics.exe заново.

Запускаем helper через `Process.Start`, потом сами exit'имся. UI ждёт async promise который никогда не resolve'нится (мы exit'имся раньше).

## Размеры

Типичный юзер за полгода использования:

| Папка | Размер |
|---|---|
| `<Program Files>\Miami Graphics\` | 132 МБ (single-file exe + additionals) |
| `config\` | < 1 МБ |
| `backup\clean\` | 2.5 ГБ (update.rpf + dlc.rpf) |
| `backup\snapshot\` | 2 ГБ (или 0 если у юзера была чистая GTA) |
| `cache\assets\` | 100-500 МБ (превью + GLB модели) |
| `tmp\` | 0-300 МБ (зависит когда чистился) |
| `additionals\jre\` | 100 МБ (если ставил минимапу) |
| `additionals\Renderer\` | 200 МБ (если админит) |
| **Total** | 5-7 ГБ |

5-7 ГБ это много, но это **в основном** копии 2 ГБ файлов GTA для отката. Без `backup\clean\` бы было ~500 МБ. Мы решили что отказоустойчивость стоит места на диске.

## AssetCache cleanup

`cache\assets\` имеет лимит ~500 МБ (настраивается в коде). При превышении - LRU-eviction:

```cs title="MiamiGraphics.Shell/Services/AssetCache.cs"
public void EvictIfOversize()
{
    var files = new DirectoryInfo(_root).EnumerateFiles()
        .Where(f => !f.Name.EndsWith(".meta", StringComparison.Ordinal))
        .Select(f => new { File = f, LastAccess = f.LastAccessTimeUtc })
        .OrderBy(x => x.LastAccess);

    long total = files.Sum(x => x.File.Length);
    foreach (var item in files)
    {
        if (total <= MaxCacheBytes) break;
        try {
            item.File.Delete();
            File.Delete(item.File.FullName + ".meta");
            total -= item.File.Length;
        } catch { /* ignore */ }
    }
}
```

Вызывается раз в час (StartupTimer в Shell). Без явного лимита cache рос бы безгранично - особенно для админов которые скроллят грид из 200+ модов и видят все превью.

[Дальше: customize data model →](../customize/data-model.md)
