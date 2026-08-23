# Smart Rebuild

Сердце инжектора. Функция `RpfInjectEngine.InjectSmartRebuild` берёт чистый `update.rpf` и список actions, выдаёт новый `update.rpf` с применёнными изменениями.

## Почему «Smart» и почему «Rebuild»

«Rebuild» - потому что мы **не редактируем** существующий архив. Мы создаём новый файл с нуля, переливая в него содержимое чистого + правки. Это [фундаментальное ограничение](../rpf/ragelib.md) RPF8 формата - TOC шапки лежит в начале, file offsets там абсолютные, удалить или изменить размер файла in-place нельзя без полной перестройки.

«Smart» - потому что мы **не выгружаем** содержимое на диск между операциями. Всё работает потоково через RageLib stream'ы. На 2 ГБ файле это даёт нам 6 секунд против 4 минут «тупой» реализации в [первой версии](../history/ragelib-v2.md).

## Полный код функции

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:107"
public bool InjectSmartRebuild(
    string sourceArchivePath,    // чистый update.rpf (после preflight restore)
    List<PatchAction> actions,
    string patchDirectory)        // распакованный patch.zip
{
    RemoveReadOnly(sourceArchivePath);

    string tempPath = sourceArchivePath + ".hnt_temp";
    string backupPath = sourceArchivePath + ".bak";

    var actionMap = BuildActionMap(actions);

    try
    {
        using (var cleanArchive = RageArchiveWrapper7.Open(sourceArchivePath))
        {
            var destArchive = RageArchiveWrapper7.Create(tempPath);

            Console.WriteLine($"[Injector] Пересборка файлового дерева...");
            RebuildDirectory(cleanArchive.Root, destArchive.Root, "", actionMap, patchDirectory);

            destArchive.FileName = Path.GetFileName(tempPath);
            destArchive.archive_.Encryption = cleanArchive.archive_.Encryption;

            Console.WriteLine($"[Injector] Сохранение нового архива...");
            destArchive.Flush();
            destArchive.Dispose();
        }

        if (File.Exists(backupPath)) File.Delete(backupPath);
        File.Move(sourceArchivePath, backupPath);
        File.Move(tempPath, sourceArchivePath);
        File.Delete(backupPath);

        return true;
    }
    catch (Exception ex)
    {
        SetError($"Ошибка Rebuild: {ex.GetType().Name}: {ex.Message}");

        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(backupPath) && !File.Exists(sourceArchivePath))
                File.Move(backupPath, sourceArchivePath);
        }
        catch (Exception cleanupEx) { /* ... */ }

        return false;
    }
}
```

## RebuildDirectory - рекурсивный обход

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:159"
private void RebuildDirectory(
    IArchiveDirectory sourceDir,
    IArchiveDirectory destDir,
    string currentPath,
    Dictionary<string, PatchAction> actionMap,
    string patchDirectory)
{
    // === 1. Собираем список подпапок которые надо обработать ===
    List<string> dirsToProcess = new();
    if (sourceDir != null)
        dirsToProcess.AddRange(sourceDir.GetDirectories().Select(d => d.Name));

    // Новые папки которые мод хочет добавить (Import actions)
    var importDirs = actionMap.Where(x =>
            x.Value.Type == ActionType.Import &&
            x.Key.StartsWith(currentPath, StringComparison.OrdinalIgnoreCase) &&
            x.Key.Length > currentPath.Length)
        .Select(x => GetNextFolder(x.Key, currentPath))
        .Where(d => !string.IsNullOrEmpty(d));

    foreach (var d in importDirs)
    {
        if (d.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) continue;
        if (!dirsToProcess.Contains(d, StringComparer.OrdinalIgnoreCase))
            dirsToProcess.Add(d);
    }

    // === 2. Рекурсивный обход каждой подпапки ===
    foreach (var dirName in dirsToProcess.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var cleanSub = sourceDir?.GetDirectories().FirstOrDefault(d =>
            d.Name.Equals(dirName, StringComparison.OrdinalIgnoreCase));
        var destSub = destDir.CreateDirectory();
        destSub.Name = dirName;
        RebuildDirectory(cleanSub, destSub, currentPath + dirName.ToLower() + "/",
            actionMap, patchDirectory);
    }

    // === 3. Файлы из чистого архива ===
    List<string> processedFiles = new();
    if (sourceDir != null)
    {
        foreach (var file in sourceDir.GetFiles())
        {
            string path = currentPath + file.Name.ToLower();
            processedFiles.Add(file.Name.ToLower());

            if (actionMap.TryGetValue(path, out var action))
            {
                if (action.Type == ActionType.Delete) continue;       // не копируем
                if (action.Type == ActionType.Replace || action.Type == ActionType.Import)
                {
                    AddModdedFile(destDir, file.Name, action, patchDirectory, file as IArchiveBinaryFile);
                    continue;
                }
            }

            // Action не найден - копируем "как есть" (включая рекурсию во вложенные .rpf)
            CopyFile(file, destDir, path, actionMap, patchDirectory);
        }
    }

    // === 4. Import actions для файлов которых нет в чистом ===
    var exactImports = actionMap.Where(x =>
        x.Value.Type == ActionType.Import &&
        GetParentPath(x.Key).Equals(currentPath, StringComparison.OrdinalIgnoreCase));

    foreach (var kvp in exactImports)
    {
        string fileName = GetFileName(kvp.Key);
        if (!processedFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            AddModdedFile(destDir, fileName, kvp.Value, patchDirectory, null);
    }
}
```

## CopyFile с обработкой вложенных RPF

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs:223"
private void CopyFile(IArchiveFile file, IArchiveDirectory destDir, string path,
    Dictionary<string, PatchAction> actionMap, string patchDirectory)
{
    bool isRpf = file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);
    bool needsRebuild = false;

    if (isRpf)
    {
        string rpfPrefix = path + "/";
        needsRebuild = actionMap.Keys.Any(k =>
            k.StartsWith(rpfPrefix, StringComparison.OrdinalIgnoreCase));
    }

    if (isRpf && needsRebuild && file is IArchiveBinaryFile bin)
    {
        // Внутри этого вложенного rpf есть actions → рекурсивная пересборка
        var newF = destDir.CreateBinaryFile();
        newF.Name = file.Name;

        var inArc = RageArchiveWrapper7.Open(bin.GetStream(), file.Name);
        var outArc = RageArchiveWrapper7.Create(newF.GetStream(), file.Name);

        RebuildDirectory(inArc.Root, outArc.Root, path + "/", actionMap, patchDirectory);

        outArc.FileName = file.Name;
        outArc.archive_.Encryption = inArc.archive_.Encryption;
        outArc.Flush();
        outArc.Dispose();
        inArc.Dispose();
    }
    else if (file is IArchiveBinaryFile bFile)
    {
        // Обычный bin-файл, копируем как есть с сохранением флагов
        var newF = destDir.CreateBinaryFile();
        newF.Name = file.Name;
        newF.Import(bFile.GetStream());
        newF.IsCompressed = bFile.IsCompressed;
        newF.IsEncrypted = bFile.IsEncrypted;
        newF.UncompressedSize = bFile.UncompressedSize;
    }
    else if (file is IArchiveResourceFile rFile)
    {
        // Resource-файл (RSC7) - переливаем через MemoryStream
        var newF = destDir.CreateResourceFile();
        newF.Name = file.Name;
        using (var ms = new MemoryStream()) { rFile.Export(ms); ms.Position = 0; newF.Import(ms); }
    }
}
```

## Как мы оптимизировали с 4 минут до 6 секунд

См. подробно [историю RageLib v2](../history/ragelib-v2.md).

Ключевые точки:

### 1. Stream-based вместо disk-based

В v1 каждый файл при копировании сначала **выгружался** на диск как temp-файл, потом читался обратно для записи в новый rpf. На 50000+ файлах внутри `update.rpf` это давало 50000 пар read/write на NVMe = тонна I/O.

В v2 переписали через `IArchiveFile.GetStream()` который возвращает stream **прямо** из source-rpf, без промежуточного диска. Write идёт сразу в dest-stream.

### 2. Inheritance compression flags

В v1 любой копируемый файл проходил через DEFLATE compression - даже те что уже были сжаты в источнике. Это **двойное сжатие** = тратили CPU, увеличивали размер.

Сейчас:

```cs
newF.IsCompressed = bFile.IsCompressed;      // наследуем
newF.UncompressedSize = bFile.UncompressedSize;
```

Мы **переливаем уже сжатый поток** без decompression+recompression. На неизменяемых файлах (99% от total) - pure copy.

### 3. Ленивая обработка вложенных RPF

В v1 каждый вложенный `.rpf` пересобирался **всегда** - даже если у мода не было ничего в нём. Просто на всякий случай.

В v2 проверяем actionMap перед рекурсией:

```cs
bool needsRebuild = actionMap.Keys.Any(k =>
    k.StartsWith(rpfPrefix, StringComparison.OrdinalIgnoreCase));

if (isRpf && needsRebuild && file is IArchiveBinaryFile bin)
{
    // открываем и пересобираем
}
else if (file is IArchiveBinaryFile bFile)
{
    // просто копируем как обычный bin (без распаковки rpf)
    newF.Import(bFile.GetStream());
}
```

Если у мода в minimap правка - мы пересобираем только `scaleform_minimap.rpf`. Остальные 200+ вложенных rpf копируются за миллисекунды как чёрный ящик.

### 4. Sequential write через buffered FileStream

RageLib `Flush()` в v1 использовал дефолтный буфер 4096 байт. На NVMe это значит миллионы syscalls. В v2 мы заставили его использовать 1 МБ буфер (через настройку в RageLib коде нашего форка).

### Результат

| Версия | Время на 2 ГБ update.rpf на NVMe |
|---|---|
| RageLib v1 (disk-based) | **4 минуты 12 секунд** |
| RageLib v2 (наш форк) | **6 секунд** |

Все эти изменения сидят в `RageLib.GTA5` нашем форке. Апстрим Neodymium не апдейтили.

## Edge cases

### Вложенный rpf в моде которого нет в чистой

Юзер мод добавил полностью новый custom DLC: `x64/dlcpacks/my_redux/dlc.rpf`. У чистой GTA такого пути нет. В нашем actionMap есть Import action на сам `dlc.rpf` целиком (с флагом `IsWholeReplaceNestedRpf=true`).

Обрабатывается через `AddModdedFile` - забираем файл из `patch_files/`, кладём в архив как обычный бинарь (Compressed=false для .rpf). Внутрь не лезем.

### Пустые каталоги

Если мод **удалил** все файлы из подпапки (Delete actions на каждый), подпапка остаётся пустой. RageLib позволяет создавать пустые директории - нормально. Игра не падает.

### Имена с unicode

GTA RPF поддерживает только ASCII в именах файлов (это `byte[]` в name table). Если мод по ошибке положит файл с кириллицей - RageLib upcast'нёт его в UTF-8 байты, игра не распознает. На этапе парсинга админка такие моды **отвергает** через валидацию.

[Дальше: rollback и .bak →](rollback.md)
