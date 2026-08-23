# Update loop incident - что было сломано

Несколько релизов подряд у юзеров случался **бесконечный update prompt**. Кликнул «обновить», скачался installer, поставился, перезапустился - снова prompt. И так каждый раз.

Не у всех - только у тех кто на single-file publish. И только когда мы накладывали update **поверх** previous single-file install.

## Цепочка проблем

### Шаг 1: single-file reflection

[Уже описано](installed-version-marker.md). Reflection в single-file publish возвращает `null` или пустую строку. AppUpdateCheck думает что версия `unknown`, что разное с `1.0.0` → есть update → prompt.

Это **корневая** причина. Лечится marker файлом.

### Шаг 2: маркер удалялся installer'ом

После добавления marker всё должно было работать. Не работало. Почему - потому что **наш installer удалял** `%LocalAppData%\MiamiGraphics\` целиком при uninstall старой версии.

```iss title="installer-user.iss [UninstallDelete]"
Type: filesandordirs; Name: "{localappdata}\MiamiGraphics"
Type: filesandordirs; Name: "{userappdata}\MiamiGraphics"
```

При upgrade Inno делает `UninstallPrevious` (uninstall старой версии перед установкой новой). `UninstallDelete` срабатывает - `%LocalAppData%\MiamiGraphics\` удаляется. **С marker'ом внутри**.

После install новой версии:

1. Marker отсутствует.
2. AppUpdateCheck → reflection → пусто → `current = "unknown"`.
3. Latest в Supabase = "1.0.1" (та что мы только что установили).
4. `hasUpdate = "unknown" != "1.0.1"` → true.
5. Prompt появляется опять.

### Шаг 3: пытались записать marker в installer

Очевидное решение - пусть **installer** пишет marker сразу после install. Через Inno `[Code]` Pascal Script:

```pas
procedure CurStepChanged(CurStep: TSetupStep);
begin
    if CurStep = ssPostInstall then begin
        SaveStringToFile(
            ExpandConstant('{localappdata}\MiamiGraphics\config\installed_version.txt'),
            '{#MyAppVersion}',
            False);
    end;
end;
```

**Не сработало** из-за порядка:

1. Inno делает `UninstallPrevious` → удаляет `%LocalAppData%\MiamiGraphics\`.
2. Inno копирует файлы программы в `Program Files`.
3. Inno вызывает `ssPostInstall` → пишет marker.
4. Шаг 3 происходит, но **только если** PostInstall достиг этого callback'а.

Если юзер кликнул «Cancel» во время install (или у installer'а упал error на копировании) - `ssPostInstall` **не вызывается**. Marker остаётся отсутствующим. При первом запуске старой/новой версии - опять loop.

### Шаг 4: финальное решение - пишем marker ДО installer'а

Решение которое работает: **наш собственный код** пишет marker **до** того как мы запустим installer:

```cs title="AppBridge.cs"
// 1. Скачали installer, проверили SHA
// 2. Пишем marker = "1.0.1" в локальный config
WriteInstalledVersionMarker(row.Version);
// 3. Запускаем installer через helper
Process.Start(...);
// 4. Сами exit'имся
```

Логика - мы **знаем** какую версию будет ставить юзер (это та строка из `app_versions` которая привела к prompt'у). Записываем её **до** запуска installer'а, в `%LocalAppData%`. 

Дальше installer:

1. UninstallPrevious удаляет старый install.
2. **`UninstallDelete` удаляет `%LocalAppData%\MiamiGraphics\` включая marker**. ← опять проблема!

### Шаг 5: убрать MiamiGraphics из UninstallDelete

Чтобы marker выживал uninstall старой версии - нужно **не** удалять `%LocalAppData%\MiamiGraphics\` в `[UninstallDelete]`:

```iss title="installer-user.iss [UninstallDelete] - финальный"
; Удаляем только OBSOLETE legacy папки, не текущую MiamiGraphics
Type: filesandordirs; Name: "{localappdata}\Miami Graphics"   ← с пробелом, legacy
Type: filesandordirs; Name: "{localappdata}\MiamiGraphics"   ← старое название
; БЫЛО: Type: filesandordirs; Name: "{localappdata}\MiamiGraphics"   ← без пробела, current - НЕ удаляем
```

Текущую `MiamiGraphics\` не трогаем. Marker и backup переживают upgrade.

При **полном** uninstall (юзер кликает «Удалить программу» в «Программы и компоненты») - `UninstallDelete` **тогда** удаляет `MiamiGraphics\`. Это правильно - юзер явно удаляет всё.

Но как Inno различить «upgrade» vs «full uninstall»? **Никак** - это технически одно и то же. Решение - **не** удалять при любом uninstall, оставлять `MiamiGraphics\` всегда. Юзер если захочет - почистит через Settings → Reset (`factoryReset`) или вручную.

Это правильнее семантически - `%LocalAppData%\<app>\` это **юзерские данные**, не часть install'а.

## Итог

Marker файл живёт через upgrade. Reflection остался fallback'ом. `hasUpdate = latest != current` сравнение работает корректно.

Если у юзера всё-таки есть update loop (марker как-то потёрся) - есть recovery path: Settings → Reset cache → factoryReset снесёт всё и перезапустит лаунчер. После перезапуска первый AppUpdateCheck покажет prompt, install запишет marker, дальше работает.

## Что мы сделали по итогу

1. **Marker файл** `installed_version.txt` - основной источник версии для single-file builds.
2. **Marker пишется ДО installer'а** (в `AppUpdateInstallAsync`).
3. **`%LocalAppData%\MiamiGraphics\` НЕ удаляется** при upgrade.
4. **`hasUpdate = latest != current`** (не >) - для rollback-релизов.
5. **`AppId` стабильный** в Inno - гарантирует UninstallPrevious.
6. **PowerShell helper** для запуска installer'а после нашего exit'а - без него `installer.exe` не может перезаписать запущенный `Miami Graphics.exe`.

Все эти шесть деталей надо иметь одновременно. Любая дыра возвращает loop.

[Дальше: история провалов через CodeWalker →](../history/codewalker.md)
