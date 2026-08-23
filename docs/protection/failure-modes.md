# Что будет если не пройти

Логически - каждый [гард](guards.md) может упасть. Что **физически** случится с системой юзера в каждом случае.

## Mutex timeout

10 минут ждём `_mutex.WaitAsync`. Не получили - throw `TimeoutException`.

**Что физически с диском:** ничего. Никаких файлов не изменено. Просто install pipeline не стартовал.

**Recovery:** другой install (тот что держит mutex) рано или поздно закончится / упадёт. Mutex отпустится. Следующий клик «Установить» сработает.

В **крайнем** случае - юзер может **закрыть** Miami Graphics (kill процесс). Mutex живёт в памяти процесса, при exit'е освобождается. На запуске re-acquire будет свободен.

## GTA path не найдена

Settings указывает на `C:\Program Files\GTA V\` но папки нет.

**Что физически:** ничего. Install отменён до начала.

**Recovery:** Settings → исправить путь. UI делает folder picker.

## Файл заблокирован (`IOException: Sharing violation`)

`update.rpf` держит GTA / Rockstar Launcher / antivirus.

**Что физически:** ничего не изменено.

**Recovery (3 уровня):**

1. **Soft retry**: автоматически 3 попытки с backoff (500 ms / 1000 ms / 2000 ms). Часто помогает - антивирус scan заканчивается, файл освобождается.
2. **User action**: показываем modal со списком процессов через FileLockDetector. Юзер кликает «Закрыть программы» → мы `Process.Kill` их → retry.
3. **Manual**: если процессы не убиваются (system service) или юзер отказался - install отменён. Юзер закрывает руками что не пускает.

## DIRTY файл

SHA-256 не совпадает ни с CLEAN ни с LAST. Юзер играл до нас через другой launcher.

**Что физически:** ничего ещё не изменено.

**Recovery:** UI показывает 3-way modal:

- **Сохранить свои изменения** - preserve flow, попытка merge'а. Сложный путь, успех ~80% если изменения не пересекаются.
- **Перетереть** - force=true, restore-clean → инжект. Юзер потеряет всё custom.
- **Отмена** - закрыть modal, ничего не делаем.

## Diskspace

Меньше 5 ГБ свободно на диске где GTA.

**Что физически:** ничего ещё. Install не начался.

**Recovery:** UI говорит сколько нужно. Юзер чистит диск, повторяет.

В **редком** случае мы это **пропускаем** через диагностический пайплайн чтобы попробовать. Может пройти если у нас был мелкий запас. Но на серьёзный мод (1 ГБ patch.zip) скорее всего упадёт где-то посередине.

## Smart Rebuild упал посередине

Это **в** install pipeline, не до. Гарды все прошли, мы уже в Smart Rebuild.

Возможные причины:

- Disk write error (наполнился диск во время rebuild);
- RageLib bug на специфическом mod'е (rare, но было);
- Antivirus убил `update.rpf.hnt_temp` (думал что зловред);
- System OOM (мало RAM);
- BSOD / power loss.

**Что физически:**

- Если RageLib **прошёл** через catch блок - `.hnt_temp` удаляется, оригинальный `update.rpf` цел.
- Если **не прошёл** через catch (SIGKILL / power loss) - `.hnt_temp` остаётся, `.bak` может остаться, оригинальный `update.rpf` **возможно** уже переименован в `.bak`.

**Recovery (несколько слоёв):**

1. `TryRollbackAsync` в нашем `ReduxInstallInternalAsync` восстанавливает из `.preinstall`. Это **первая** линия защиты - `.preinstall` это snapshot до начала Smart Rebuild.
2. Если `TryRollbackAsync` сам упал - на следующем запуске лаунчера [OrphanBackupRecovery](../inject/rollback.md) ищет висячие `.bak` файлы в `<gta>/update/`.
3. Если и это не помогло (или OrphanBackupRecovery не запускался) - юзер видит «GTA files corrupted» при запуске игры. В Miami есть кнопка «Восстановить чистый update.rpf» которая делает `RestoreFromCleanAsync` из локального backup'а.
4. Финальный fallback - переустановка GTA через Rockstar Launcher.

## ArchiveFix упал

Smart Rebuild прошёл, новый `update.rpf` на месте, но ArchiveFix вернул error / timeout.

**Что физически:** новый `update.rpf` на диске. Но GTA его отвергнет - checksum'ы не пересчитаны.

**Recovery:** `TryRollbackAsync` восстанавливает `.preinstall`. Юзер остаётся с тем что было до install'а.

В логах debug error от ArchiveFix - обычно «не смог открыть файл», «exit code 1». Юзер видит ошибку в UI.

## Backup сам упал

Юзер пытается **сделать** backup, BackupService падает (нет clean.rpf на R2, или download failed).

**Что физически:** наш `MiamiGraphics/backup/` папка может быть **полупустой** (manifest есть но clean.rpf не дочкан). Manifest пишется **в конце** через atomic write, поэтому либо целый manifest есть и all done, либо ничего нет.

**Recovery:** на следующем запуске backup pipeline видит что `clean.rpf` отсутствует или SHA не совпадает с manifest'ом → начинает заново. Идемпотентно.

## update.rpf физически отсутствует

Юзер удалил его руками или Rockstar Launcher verify integrity'нул, но что-то пошло не так и файла нет.

**Что физически:** дыра в GTA. Игра не запустится вообще.

**Recovery:** мы **не** пытаемся восстановить ванильный `update.rpf` (мы не Rockstar, у нас нет authoritative source). Юзер должен либо:

1. Через Rockstar Launcher: «Verify Integrity» → переcкачает 2 ГБ.
2. Установить мод поверх нашего clean copy через `RestoreFromCleanAsync` → у юзера в `<gta>/update/` появится чистый `update.rpf`.

Второй путь нативно UI поддерживает - Settings → «Восстановить update.rpf из бэкапа».

## Худший случай (которого мы не видели)

**Disk error во время writing finals**: пишем новый `update.rpf` через atomic move, диск возвращает success но **физически** не записал. Bit-rot.

В теории NTFS atomic move это гарантия. Bit-rot диска - это **отказ** диска уровень. Не наша ответственность - мы доверяем что когда `File.Move` возвращает success, файл реально записан.

Если диск умирает - `RestoreFromCleanAsync` тоже упадёт (наша backup-копия тоже на этом диске). Юзер с убитым диском в любом случае выбывает из строя - это не наша зона.

## Не покрыты намеренно

- **Кто-то третий пишет в `<gta>/update/`** параллельно с нашим install'ом - например другой mod launcher одновременно с Miami. Никак не защититься. **Допущение** - юзер не запускает два launcher'а одновременно.
- **Anti-malware system Defender удалил `Miami Graphics.exe` посреди install'а** - Defender может decide that мы вирус (false positive). Тут поможем добавлением подписи к exe (см. [Inno history](../update/inno-supabase.md#vt)).

## Telemetry - что мы видим

Каждый install pipeline пишет debug-log в `%LocalAppData%\MiamiGraphics\log\`:

```
[2026-05-19 14:23:12.345] [redux.install] preflight: current=f3a8b2c1 matchesLast=true matchesClean=false
[2026-05-19 14:23:13.512] [backup.restore.clean] starting...
[2026-05-19 14:23:18.732] [backup.restore.clean] done (5.2s)
[2026-05-19 14:23:18.733] [redux.install] starting download
[2026-05-19 14:23:48.123] [redux.install] download complete (29.4s, 234 MB)
[2026-05-19 14:23:48.124] [redux.install] snapshot to .preinstall...
[2026-05-19 14:23:52.456] [redux.install] starting Smart Rebuild
[Injector] >>> Запуск Smart Rebuild для: update/update.rpf
[Injector] Пересборка файлового дерева...
[Injector] Сохранение нового архива...
[2026-05-19 14:23:58.789] [redux.install] Smart Rebuild done (6.3s)
[ArchiveFix] Запуск исправления хешей: update.rpf
[ArchiveFix] Успешно.
[2026-05-19 14:24:02.234] [redux.install] ArchiveFix done (3.4s)
[2026-05-19 14:24:02.235] [redux.install] writing install_state.json
[2026-05-19 14:24:02.245] [redux.install] DONE
```

Если у юзера проблемы - он может прислать этот лог в support, мы видим **на каком** шаге всё ломалось.

