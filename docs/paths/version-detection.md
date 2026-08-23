# Версия GTA

После того как [нашли путь к GTA](gta-detection.md), нам нужно понять **какая версия игры** установлена. От этого зависит:

- Какой reference SHA-256 ожидать для `update.rpf` (см. таблица `gta_versions` в Supabase);
- Совместим ли мод с этой версией (некоторые моды собраны на старой и не работают на новой);
- Какой `clean_update.rpf` качать с R2 если бэкап повреждён.

## Источник правды - GTA5.exe FileVersion

Windows API `GetFileVersionInfo`:

```cs title="MiamiGraphics.Core/System/BackupService.cs"
var exePath = Path.Combine(gtaPath, "GTA5.exe");
if (!File.Exists(exePath))
    return Fail("EXE_NOT_FOUND", "GTA5.exe not found");
var exeVersion = FileVersionInfo.GetVersionInfo(exePath).FileVersion ?? "unknown";
```

`exeVersion` это строка типа `1.0.3788.0`. Это **то** что Rockstar пишет в свой .exe при сборке патча. Мы используем её как primary key в таблице `gta_versions`.

## Таблица gta_versions

```sql
create table public.gta_versions (
    exe_version text primary key,           -- '1.0.3788.0'
    update_rpf_sha256 text not null,
    update_rpf_size bigint not null,
    dlc_rpf_sha256 text not null,
    dlc_rpf_size bigint not null,
    is_supported boolean default true,
    notes text,
    created_at timestamptz default now()
);
```

Каждая поддерживаемая нами версия GTA имеет row. Админ заполняет её через Admin → GTA Versions когда Rockstar выкатывает новый патч:

1. Админ обновляет свою GTA до новой версии.
2. Запускает Miami Graphics → лаунчер видит `exe_version = '1.0.3789.0'` (новая), её нет в таблице.
3. Заходит в Admin → GTA Versions → кнопка «Generate from current install».
4. C# код считает SHA-256 чистого `update.rpf` и `dlc.rpf` его установки.
5. Вставляет новую row в таблицу с `is_supported = true`.
6. Залить `clean_update.rpf` на R2 (для юзеров у которых файл оказался не чистым).

## Реакция на unsupported версию

Если юзер запустил Miami Graphics а его GTA-версия ещё **не** в таблице - backup pipeline помечает `versionUnsupported = true`:

```cs title="MiamiGraphics.Core/System/BackupService.cs"
var reference = _registry.GetReferenceForVersion(exeVersion);
bool versionUnsupported = reference is null;
bool isDirty;
if (reference is null)
{
    isDirty = true;   // считаем как DIRTY (не доверяем что у юзера clean)
}
else
{
    isDirty = !(userSize == reference.UpdateRpfSize &&
                string.Equals(userHash, reference.UpdateRpfSha256, StringComparison.OrdinalIgnoreCase));
}
```

UI получает `versionUnsupported = true` в BackupResult, показывает baner: «Твоя версия GTA (X) пока не поддерживается. Можешь устанавливать моды, но не гарантируем что они подойдут под твою версию».

## Почему мы не «авто-определяем» SHA

Кажется логичным - раз у нас есть чистый файл юзера, мы бы могли посчитать его SHA и **запомнить** как reference для этой версии. Без админ-конфирмации.

Не делаем - потому что **не знаем** чистый ли у юзера сейчас файл. Если он играл с модами до прихода к нам, его `update.rpf` уже грязный. Запомнить его SHA как reference = «отравить» базу, **все** будущие юзеры с той же версией будут думать что грязный = чистый.

Поэтому новая версия требует **админскую установку чистой GTA**, скан, и явный upload в Supabase.

## Migration на новую версию GTA

Когда Rockstar выкатывает патч, у юзера автоматом обновится GTA через Rockstar Launcher. Его `update.rpf` поменяется. Что произойдёт у нас:

1. Юзер запускает Miami Graphics после патча.
2. Лаунчер видит новую `exeVersion`.
3. Если эта версия **уже в таблице** (мы успели её обработать) - backup pipeline сравнивает SHA, видит совпадение → всё ок.
4. Если **нет** в таблице - `versionUnsupported = true`, UI показывает баннер «версия пока не поддерживается».
5. Параллельно у юзера в backup-папке **старая** `clean/update_1.0.3788.0.rpf` - она больше не подходит. Manifest помечает старую как `obsolete` и **скачивает заново** clean'ную для новой версии.

Это **не блокирует** работу - юзер может устанавливать моды и на unsupported версии, просто без гарантии что reference SHA правильный (мы делаем install через DIRTY-путь с подтверждением).

## Каталог редуксов и `targetGtaVersion`

Каждый redux в БД имеет поле `target_gta_version`:

```sql
create table public.redux_versions (
    id uuid primary key,
    redux_id uuid references redux_items,
    slot smallint,
    target_gta_version text,   -- '1.0.3788.0' или 'all'
    ...
);
```

Это **намёк** для UI: «этот мод тестировался на конкретной версии GTA». Если у юзера версия отличается - UI показывает warning на карточке («может не работать на твоей версии GTA») но не блокирует.

`target_gta_version = 'all'` значит мод **универсальный** (например простая замена минимапы - формат `.gfx` стабилен между патчами).

## Edge case: пиратская GTA

У пираток `GTA5.exe.FileVersion` может быть **враньё** - кряк часто меняет version info на 1.0.0.0 или какое-то custom-значение. В этом случае:

- `exeVersion = "1.0.0.0"` или другое;
- В таблице такой версии нет;
- `versionUnsupported = true`;
- Backup идёт через DIRTY-путь с подтверждением.

Это работает - но юзер видит баннер «версия не поддерживается». Это сознательно - мы не тестируем на пиратках, и не хотим брать на себя гарантии. Если ему важна совместимость, пусть обновится до официальной.

[Дальше: AppData layout →](appdata-layout.md)
