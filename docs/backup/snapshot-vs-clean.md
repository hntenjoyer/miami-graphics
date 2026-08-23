# Snapshot vs Clean

Два разных типа бэкапа в Miami Graphics, которые часто путают.

## Clean baseline

**Что**: snapshot **чистого** `update.rpf` из ванильной GTA. Один на установку лаунчера, делается единожды.

**Когда**: при первом запуске лаунчера, если SHA текущего `update.rpf` совпадает с известным «чистым» SHA для этой версии GTA. Если не совпадает - лаунчер просит юзера сначала verify integrity через Rockstar/Steam launcher (чтобы заполучить чистый файл).

**Где**: `%LocalAppData%\MiamiGraphics\cache\clean\update.rpf` + `backup-manifest.json` с `kind: "clean"`.

**Зачем**: 
- Для **restore-clean** - кнопка «Откатить до vanilla GTA» (см. admin injector).
- Для [preflight](../inject/preflight.md) - сравнение SHA текущего файла с clean'ым, чтобы понять «юзер на vanilla?» (`matchesClean = true`).
- Для **smart-rebuild** - diff против чистого, чтобы определить что мы изменили vs что было.

**Долговечность**: вечный. Не перезаписывается. Если юзер обновил GTA до новой версии и SHA чистого update.rpf изменился - лаунчер делает **новый** clean baseline для новой версии (старый удаляется).

## Snapshot

**Что**: snapshot **текущего** state'а GTA в произвольный момент. Юзер мог иметь установленный мод, мог быть в процессе кастомизации - снимок фотографирует **всё как есть**.

**Когда**: 
- **Автоматически** - перед каждой инсталляцией мода. `.preinstall` snapshot - см. [rollback](../inject/rollback.md).
- **Вручную** - кнопка «Сохранить текущий setup» в Settings.

**Где**: `%LocalAppData%\MiamiGraphics\cache\snapshots\<timestamp>\update.rpf` + manifest с `kind: "snapshot"`.

**Зачем**:
- **Rollback** - если только что установленный мод не понравился, restore из последнего `.preinstall`.
- **Save points** - юзер может вручную сохранить «вот моя любимая сборка», потом эксперементировать, потом вернуться.

**Долговечность**: LRU eviction. Держим **последние 5** snapshot'ов автоматических + до 10 вручную сохранённых. Старше = удаляется при следующем backup'е.

## Сравнительная таблица

| | Clean | Snapshot |
|---|---|---|
| Сколько | 1 на установку | Множество с timestamp'ами |
| Когда создаётся | При первой инсталляции лаунчера | Перед каждым install'ом + вручную |
| Удаление | Никогда (только при апгрейде GTA) | LRU, 5+10 last |
| `kind` в manifest'е | `"clean"` | `"snapshot"` |
| Use case | «откатить до vanilla» | «отменить последнюю установку», «вернуться к save point'у» |

## Иерархия восстановления

При сбое в инсталляции мы пробуем по порядку:

1. **`.bak`-файлы из SmartRebuild** - если в `update.rpf` остались `.bak` рядом, значит rebuild незавершился, восстановим по ним.
2. **Snapshot `.preinstall`** - если есть свежий snapshot перед текущей операцией, restore оттуда.
3. **Clean baseline** - если ничего не помогло, vanilla GTA.

См. [rollback](../inject/rollback.md) для деталей.

## UI

В Settings → Backups юзер видит **обе** секции:

```
Чистый бэкап
─────────────
✅ Есть, версия GTA 1.0.3411.0, сделан 2026-04-01

   [Откатить до vanilla]   [Пересоздать чистый]


Снимки (snapshots)
─────────────────
2026-05-12  Auto (.preinstall перед Hunter Reborn 4.0)
2026-05-11  Auto (.preinstall перед FiveM Realism)
2026-05-10  Manual: "Мой favorit setup"
2026-05-08  Auto (.preinstall перед Niko Hunter build)
2026-05-07  Manual: "Перед экспериментом"

   [Сохранить текущий setup как snapshot]
```

Юзер может кликнуть на любой snapshot → modal «Restore from this snapshot?» с превью данных (какие моды стояли по информации из manifest'а).

## Что не бэкапится

- `dlcpacks/*` - отдельные DLC паки не входят. Это **намеренно**: они стоят отдельно от `update.rpf`, при restore их не трогаем. Снять/поставить DLC - отдельный flow в UI (через `dlclist.xml`).
- `settings.xml` - графические настройки. Бэкапится отдельно при apply GTA preset'а (`settings.xml.backup.xml` рядом).
- Save-games. **Никогда** не трогаем юзерские save'ы.

[Дальше: Atomic writes →](atomic-writes.md)
