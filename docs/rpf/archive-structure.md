# Структура архива RPF8

Открываем `update.rpf` в hex-редакторе - первые 16 байт это шапка.

```
offset 0x00:  52 50 46 37   'R' 'P' 'F' '7'        magic
offset 0x04:  XX XX XX XX                          entryCount (LE int32)
offset 0x08:  XX XX XX XX                          namesLength
offset 0x0C:  F9 FF FF 0F                          encryption=AES (0x0FFFFFF9)
                  или
              FF FF EF 0F                          encryption=NG (0x0FEFFFFF)
                  или
              4F 50 45 4E                          encryption=OPEN (0x4E45504F)
                  или
              00 00 00 00                          encryption=NONE
```

`update.rpf` всегда AES или NG. OPEN бывает у моддерских архивов, NONE - у каких-то custom-DLC от любителей. Игра ест все четыре варианта.

## TOC (Table of Contents)

Сразу после шапки идёт **зашифрованный** блок TOC. Это массив из `entryCount` записей по 16 байт каждая.

```
struct RpfEntryBase {
  uint32 nameOffset;   // offset в name table (после TOC)
  // дальше 12 байт зависит от типа записи
}
```

Три типа записи различаются по первому биту `flags`:

| Тип | Что означает |
|---|---|
| `Directory` | Подкаталог внутри архива |
| `BinaryFile` | Обычный файл (или Resource внутри которого RSC7) |
| `ResourceFile` | Resource (legacy формат, в новых RPF8 не используется) |

```
struct RpfDirectoryEntry {
  uint32 nameOffset;
  uint32 reserved;
  uint32 firstChildIndex;   // первый ребёнок в TOC
  uint32 childCount;
}

struct RpfBinaryFileEntry {
  uint32 nameOffset;
  uint32 fileSize;
  uint32 fileOffset;        // в блоках по 512 байт от начала RPF
  uint32 systemFlags;       // bits: isEncrypted, isCompressed, isResource, uncompressedSize
}
```

После TOC идёт **зашифрованный name table** (имена `\0`-разделены). И уже после него - payload файлов.

## Расшифровка TOC

TOC и name table зашифрованы тем же ключом что указан в шапке. AES - стандартный AES-128-ECB с конкретным ключом (`gtav_aes_key.dat` в нашем `additionals/Keys/`). NG - табличный шифр Rockstar'а, [подробнее в Encryption](encryption.md).

Если ключи не загружены, RageLib не сможет ни прочитать TOC, ни понять структуру. Открытие архива упадёт сразу. Поэтому при старте `ReduxParserPipeline` явно проверяет:

```cs title="MiamiGraphics.Core/Parser/ReduxParserPipeline.cs"
if (RageLib.GTA5.Cryptography.GTA5Constants.PC_NG_KEYS != null)
{
    totalKeys = RageLib.GTA5.Cryptography.GTA5Constants.PC_NG_KEYS.Length;
    for (int i = 0; i < totalKeys; i++)
    {
        var k = RageLib.GTA5.Cryptography.GTA5Constants.PC_NG_KEYS[i];
        if (k != null && k.Length > 0) loadedKeys++;
    }
}
Console.WriteLine($"[Pipeline] NG keys loaded: {loadedKeys}/{totalKeys}");
if (loadedKeys == 0)
{
    Console.WriteLine("[Pipeline] WARNING: NO NG keys loaded.");
}
```

Если `loadedKeys=0` - парсер не сможет распаковать ничего, выдаст пустой diff и админка покажет «у мода 0 файлов отличий, что-то не так». Это надёжная диагностика - у нас был [инцидент](../history/ragelib-v1.md), когда ключи лежали не там где RageLib их искал, и весь парсер тихо работал «как будто всё совпадает».

## Вложенные RPF

Внутри `update.rpf` лежат подархивы - `dlc.rpf`, `scaleform_minimap.rpf`, `ptfx.rpf`. С точки зрения шапки они обычные `BinaryFile` entries: имя `dlc.rpf`, размер, offset. Снаружи они - простые файлы.

Но **внутри** этих файлов снова шапка `'RPF7'` со своим TOC, своими entries, своим encryption flag. И эти вложенные могут содержать ещё более вложенные.

RageLib даёт нам API для work-in-place: `RageArchiveWrapper7.Open(parentBinFile.GetStream(), name)` - открываем дочерний архив прямо поверх stream'а родителя, без сохранения на диск. Это критично потому что иначе пересборка `update.rpf` со вложенными правками потребовала бы экстрактнуть на диск всё, изменить, и упаковать обратно. На 2 ГБ архиве это были бы те самые «4 минуты» из истории, которых мы избежали.

В нашем `RpfInjectEngine.RebuildDirectory` это работает так:

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs"
if (isRpf && needsRebuild && file is IArchiveBinaryFile bin)
{
    var newF = destDir.CreateBinaryFile();
    newF.Name = file.Name;

    // открываем нестед-rpf прямо в stream'е родителя
    var inArc = RageArchiveWrapper7.Open(bin.GetStream(), file.Name);
    var outArc = RageArchiveWrapper7.Create(newF.GetStream(), file.Name);

    // рекурсивно ходим по дереву вложенного rpf
    RebuildDirectory(inArc.Root, outArc.Root, path + "/", actionMap, patchDirectory);

    outArc.FileName = file.Name;
    outArc.archive_.Encryption = inArc.archive_.Encryption;  // КРИТИЧНО
    outArc.Flush();
    outArc.Dispose();
    inArc.Dispose();
}
```

Строка `outArc.archive_.Encryption = inArc.archive_.Encryption` - самая важная. Это копия encryption flag из исходного архива. Если её забыть, новый вложенный `.rpf` получит дефолтный NONE - и игра отбросит файл. Это [баг встречался](../history/ragelib-v1.md) в первой версии инжектора.

## Что внутри файла

`BinaryFile` запись хранит:

- `fileOffset` - где в RPF лежит блоб (адрес в блоках по 512 байт от начала родительского архива);
- `fileSize` - сжатый размер на диске;
- `uncompressedSize` - оригинальный размер до DEFLATE;
- `systemFlags.IsEncrypted` - зашифрован файл или нет;
- `systemFlags.IsCompressed` - DEFLATE-сжат или нет.

При чтении RageLib сначала читает блок с диска, если `IsEncrypted` - расшифровывает (тем же ключом архива), потом если `IsCompressed` - пропускает через `DeflateStream`. На выходе получаем оригинальные байты файла.

Шифрование файлов внутри RPF **независимо** от шифрования TOC шапки. Файл может быть AES, NG или вообще нешифрованным даже в архиве с AES TOC. Это используется для оптимизации: ресурсы (`.ytd`, `.ydr`) уже сжаты внутренне в RSC7, повторно их жать DEFLATE'ом бессмысленно, поэтому они хранятся как `IsCompressed=false`.

[Дальше: шифрование AES + NG →](encryption.md)
