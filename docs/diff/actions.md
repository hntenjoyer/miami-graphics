# Типы Action

Три типа в нашем `manifest.json`. Каждый обрабатывается отдельной веткой в [RpfInjectEngine.RebuildDirectory](../inject/smart-rebuild.md).

## Import

> Этого файла нет в чистой GTA, но он есть у мода. Добавь.

Применяется когда мод **добавляет** новый файл. Не «заменяет существующий», а именно добавляет в дерево.

### Когда генерируется

`RpfDiffEngine` обходит дерево mod RPF, для каждого файла проверяет наличие в clean RPF. Не нашёл - Import.

```cs
foreach (var modFile in moddedDir.GetFiles())
{
    var cleanFile = cleanDir.GetFiles().FirstOrDefault(f =>
        f.Name.Equals(modFile.Name, StringComparison.OrdinalIgnoreCase));

    if (cleanFile == null)
    {
        actions.Add(new PatchAction {
            Type = ActionType.Import,
            TargetPath = currentPath + modFile.Name,
            SourcePath = ExtractToPatchDir(modFile),
            Size = modFile.Size,
            Sha256 = ComputeSha256(modFile)
        });
    }
}
```

### Как применяется

В Smart Rebuild когда инжектор обходит чистый rpf - он **не встречает** файл с таким именем, потому что в чистом его нет. Поэтому есть отдельный проход в конце функции `RebuildDirectory`:

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:212"
// После обхода clean files, обрабатываем "exact imports" -
// файлы которых не было в clean но должны появиться в результирующем
var exactImports = actionMap.Where(x =>
    x.Value.Type == ActionType.Import &&
    GetParentPath(x.Key).Equals(currentPath, StringComparison.OrdinalIgnoreCase));

foreach (var kvp in exactImports)
{
    string fileName = GetFileName(kvp.Key);
    if (!processedFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
    {
        AddModdedFile(destDir, fileName, kvp.Value, patchDirectory, null);
    }
}
```

## Replace

> Файл есть и в чистой, и у мода. Они разные. Замени на модный.

Самый частый action. Обработка простая - встретили файл в clean, видим что для него есть Replace action, **не копируем** clean-версию а зовём `AddModdedFile` с моддерским контентом.

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:201"
if (action.Type == ActionType.Replace || action.Type == ActionType.Import)
{
    AddModdedFile(destDir, file.Name, action, patchDirectory, file as IArchiveBinaryFile);
    continue;
}
```

Replace **наследует** compression-флаги от оригинала через template:

```cs title="AddModdedFile параметр oldBinTemplate"
bool shouldCompress = !name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);
if (oldBinTemplate != null && !oldBinTemplate.IsCompressed) shouldCompress = false;
```

То есть если ванильный файл лежал **uncompressed** - наша замена тоже будет uncompressed. Это критично для ресурсов внутри которых уже RSC7 (повторное DEFLATE даст хуже + лишний CPU).

Для Import нет шаблона (`oldBinTemplate == null`), поэтому **всегда DEFLATE** кроме случая когда сам файл это .rpf.

## Delete

> Этот файл есть в чистой, но его НЕТ у мода. Удали.

Самый редкий. Применяется только когда мы знаем что данный путь это «маркер отключения».

### Обработка

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:194"
if (action.Type == ActionType.Delete)
{
    Console.ForegroundColor = ConsoleColor.Magenta;
    Console.WriteLine($"[Injector] Удален: {path}");
    Console.ResetColor();
    continue;  // не копируем файл в результирующий архив
}
```

То есть **просто не копируем**. В новом архиве этого файла не будет.

### Почему нельзя «удалить всё лишнее»

Логически казалось бы - если у мода нет файла а у чистой есть, надо удалить. На практике это **сломает игру**.

Пример: мод трогает только минимапу. Парсер обходит `update.rpf`, генерирует Replace для `minimap.gfx`. И **триста** Delete actions для всех остальных файлов которых нет в моде (потому что мод не таскает с собой каждый ванильный текстуру или конфиг).

Если эти Delete применить - у юзера полупустой `update.rpf`, игра не запустится.

Поэтому мод **должен включать всё что должно остаться**. Так как авторы модов всегда делают - они кидают `dlc.rpf` со всем содержимым которое было в чистой плюс свои правки. Diff-engine на стороне админа видит: 9999 файлов идентичны → skip, 1 файл отличается → Replace. Никаких Delete.

Delete используется **только в нашем custom-DLC** для оружейных модов:

```
miami_weapon/dlc.rpf
└── weapons.meta            ← мы заменяем
└── tracer_white.ypt        ← мы хотим удалить (если юзер ставит coloured tracers pack)
```

Тут админ при заливке гана-пака **явно** говорит «эти файлы нужно убирать», и парсер генерирует Delete actions для них.

## Группировка по root RPF

Перед обработкой actions, инжектор группирует их по **корневому** rpf (тот что лежит на диске, не вложенные):

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:341"
private Dictionary<string, List<PatchAction>> GroupActionsByRootRpf(List<PatchAction> actions)
{
    var result = new Dictionary<string, List<PatchAction>>(StringComparer.OrdinalIgnoreCase);
    foreach (var action in actions)
    {
        string rootRpf = ResolveRootRpf(action.TargetPath);
        // ResolveRootRpf отсекает всё после первого ":" и оставляет:
        //   "update/update.rpf:/x64/..." → "update/update.rpf"
        //   "x64/dlcpacks/miami_weapon/dlc.rpf:/..." → "x64/dlcpacks/miami_weapon/dlc.rpf"
        if (rootRpf == "SKIP") continue;
        if (!result.ContainsKey(rootRpf)) result[rootRpf] = new List<PatchAction>();
        result[rootRpf].Add(action);
    }
    return result;
}
```

Зачем - каждый root rpf обрабатывается **отдельным** rebuild proceedurally:

```cs
foreach (var kvp in actionsByRoot)
{
    string rootRpfName = kvp.Key;
    string absoluteRootPath = Path.Combine(_gtaRootPath, rootRpfName);
    bool success = InjectSmartRebuild(absoluteRootPath, kvp.Value, patchDirectory);
    if (success)
    {
        FixArchive(absoluteRootPath);   // ArchiveFix только если успешно
    }
}
```

Это позволяет нам атомарно ставить мод который трогает **и** `update.rpf` **и** `x64/dlcpacks/miami_weapon/dlc.rpf` - два разных корневых архива, каждый получает свой Smart Rebuild + свой ArchiveFix.

[Дальше: pipeline инжекта →](../inject/pipeline.md)
