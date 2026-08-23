# Конфликты ганов

Оружейные паки (gunpacks) - это **модели** оружия с custom текстурами. Юзер может загрузить пак с 16 stylized стволами, заменить **отдельные** стволы из разных паков (карабин из Pack A, ar15 из Pack B, snipper из Pack C).

С этой системой было больше всего конфликтов потому что оружие в GTA имеет **много** связей между файлами.

## Что внутри одного gunpack

```
miami_weapon/dlc.rpf (наш custom DLC)
├── x64/levels/gta5/weapons.rpf/
│   ├── w_ar_carbinerifle.ydr            ← модель карабина
│   ├── w_ar_carbinerifle.ytd            ← текстуры карабина
│   ├── w_sb_assaultsmg.ydr              ← модель SMG
│   └── ...
├── x64/audio/sfx/WEAPONS_PLAYER.rpf/
│   ├── carbinerifle_fire.awc            ← звук стрельбы карабина
│   └── ...
├── data/weapons.meta                    ← config: урон, точность, дальность
├── data/weaponanimations.meta           ← анимации перезарядки
├── data/weaponcomponents.meta           ← глушители, прицелы, magazines
└── content.xml
```

Когда мод хочет **только** заменить модель карабина:

- Обязательно даёт `w_ar_carbinerifle.ydr` (модель);
- Обязательно даёт `w_ar_carbinerifle.ytd` (текстуры - иначе модель будет purple/black по дефолтным missing texture'ам);
- Часто даёт `weapons.meta` с поправками урона (если автор хотел свой баланс);
- Иногда даёт `weaponcomponents.meta` (если у мода свои глушители - например прозрачные).

Это значит **один** ган имеет **2-5 файлов**, которые могут пересекаться с другими ганами.

## Конфликт 1: weapons.meta - один файл на всё оружие

`weapons.meta` это **один** XML файл с настройками **всех** weapons GTA - пистолетов, AR, SMG, sniper, melee, granadа, всё в одном. Если Pack A правит `carbinerifle damage = 50`, и Pack B правит `assaultsmg damage = 80` - они **оба** должны попасть в финальный `weapons.meta`.

Если мы возьмём `weapons.meta` от Pack A - у нас будут только его правки, правки Pack B пропадут. И наоборот.

**Решение** - **merge** на уровне XML. Когда юзер ставит Pack B поверх Pack A, мы:

1. Читаем `weapons.meta` от Pack A (он сейчас в `update.rpf`).
2. Читаем `weapons.meta` от Pack B (из его patch.zip).
3. **Сравниваем** - для каждого `<WeaponData>` элемента:
   - Если есть только в A → берём из A;
   - Если есть только в B → берём из B;
   - Если есть в обоих → берём из **более свежего** (текущая операция установки Pack B → берём B).
4. Сохраняем merged XML в новый `weapons.meta` → инжектим.

Реальный код это `WeaponsMetaMergeService` (в `MiamiGraphics.Core/Services/`), 200 строк С# с явной обработкой каждого weapon type.

## Конфликт 2: глушители и аксессуары

`weaponcomponents.meta` определяет какие **аксессуары** (suppressor / scope / extended mag) подходят к какому **оружию**. Если автор Pack A сделал прозрачный suppressor для `carbinerifle`, а автор Pack B сделал свой для того же оружия - конфликт.

Здесь merge **сложнее**, потому что:

- У каждого **аксессуара** есть свой `<ComponentInfo>` с модель/текстуры;
- Линк `weapon → component` лежит в `<WeaponComponents>` массиве;
- Аксессуары могут **зависеть** от конкретных моделей (modular weapon assembly).

Не пытаемся merge'ить `weaponcomponents.meta`. Вместо этого - **последний ставит выигрывает**. Pack B при install заменяет `weaponcomponents.meta` целиком, accessories от A теряются.

Это **известное ограничение**. UI явно говорит юзеру в tooltip'е:

> При установке этого пака аксессуары (глушители, прицелы) из других паков могут перестать работать.

В будущем планировали merge'ить и `weaponcomponents.meta`, но это сложно и не приоритет.

## Конфликт 3: 3D-модель оружия

Модель `w_ar_carbinerifle.ydr` - **один файл на каждое оружие**. Если Pack A и Pack B оба заменили `carbinerifle.ydr` - мы можем взять **только один**.

Это решается через **selected guns**:

```
miami_weapon/dlc.rpf
└── hunter_guns_selected.rpf/
    ├── w_ar_carbinerifle.ydr   ← из Pack B (юзер выбрал)
    ├── w_ar_carbinerifle.ytd
    ├── w_sb_assaultsmg.ydr     ← из Pack A
    └── ...
```

`hunter_guns_selected.rpf` это **наш** вложенный rpf внутри `miami_weapon/dlc.rpf`. Содержит **выбранные** ганы из разных pack'ов. Юзер в UI выбирает «карабин из Pack B, smg из Pack A» - мы **собираем** этот rpf на лету при install.

Логика - `SelectedGunsInstaller`. Юзер может:

- **Установить весь пак** (`gunpackInstall`) - заменяет `hunter_guns_selected.rpf` всем содержимым пака;
- **Установить отдельный ган** (`selectedGunsInstall`) - добавляет/заменяет конкретный ган в существующий `hunter_guns_selected.rpf`;
- **Снять ган** (`selectedGunsRemove`) - удаляет конкретный ган.

`hunter_guns_selected.rpf` пересобирается **на каждое** изменение через RpfInjectEngine.

## Конфликт 4: 3D-превью в каталоге

Никак не связано с install'ом, но связано с **разметкой UI**. Юзер видит грид ганов в каталоге. У каждого гана **превью** - PNG-картинка (для маленьких карточек) или GLB (для 3D viewer'а в детали).

При upload'е гана-пака админ сам **рендерит** PNG (через [headless Chrome](../3d/headless-render.md)) и **конвертирует** `.ydr` → `.glb` (через `CodeWalker.Core` + наш glTF exporter).

Все PNG и GLB лежат в R2 под `gunpacks/<id>/guns/<name>.png` / `<name>.glb`. При установке гана у юзера эти ассеты он **не качает** - только во время просмотра каталога.

## Конфликт 5: имена `internal_name` оружия

GTA использует **internal name** для weapon - например `w_ar_carbinerifle` (`w_` = weapon, `ar_` = assault rifle, `carbinerifle` = base name). Если автор пака `CarbineRifle Reskin` обзовёт свой файл `w_ar_carbinerifle.ydr` - он автоматически **заменяет** ваниль'ный carbinerifle в игре.

Чтобы не **подменять** случайно, у нас есть **whitelist** - `gunpack_whitelist` таблица в БД:

```sql
create table public.gunpack_whitelist (
    internal_name text primary key,
    display_name text,
    category text,                 -- 'assault_rifle' / 'pistol' / 'sniper' / ...
    sort_order int
);
```

Только ганы с `internal_name` из whitelist могут быть установлены. Это **35 записей** - все vanilla weapons GTA Online (без cut weapons).

Когда автор пака даёт ган с `w_ar_new_custom_rifle` (не в whitelist) - мы **отвергаем** этот ган при заливке. Парсер пишет warning «unknown internal_name», админ видит и решает либо обновить whitelist (если это новый weapon из update GTA), либо отказать.

Это защита от:

- Случайных typo в имени;
- Авторов которые хотят добавить **новое** оружие (это требует более глубоких изменений, не только модели).

## История

Все эти конфликты накопились по мере роста каталога:

- Изначально мы поддерживали **только** "полная замена пака" - юзер ставит pack целиком, ничего не миксует. Это просто, без конфликтов.
- Через месяц юзеры хотели «карабин из A но снайперка из B». Добавили selected guns.
- Через два месяца юзеры жаловались что damage values меняются после установки нескольких pack'ов (Pack B unfortunately наводнил pack A damage). Добавили `WeaponsMetaMergeService`.
- Через полгода вошли в стабильный режим - все конфликты которые могли сломать UX, имеют известное решение.

Главный урок - **merge модов** это сложная задача. Полная семантика merge (например MergeKit для llm) не нужна, но базовый XML-merge для известных схем (`weapons.meta` нашей структуры) - нужен. И UI должен **честно** объяснять что юзер получит, а что не получит.

[Дальше: история 3D рендера →](3d-render.md)
