# Шифрование AES + NG

Rockstar использует два разных алгоритма для шифрования RPF - `AES` и `NG`. Каждый файл внутри архива может быть зашифрован любым из них, либо лежать в чистом виде.

## AES

Это стандартный AES-128 в режиме ECB, ключ - фиксированные 32 байта взятые из памяти GTA5.exe. Лежит у нас как `gtav_aes_key.dat` в `additionals/Keys/`. Этот ключ публичный - он давно вытащен community, никакой обфускации, никакой динамической генерации.

Используется для:

- TOC и name table в шапке RPF (если `encryption == 0x0FFFFFF9`);
- Конфигурационных файлов внутри архива (`.meta`, `.ymf`, `.ymt`, `.dat`);
- ScaleForm файлов (`.gfx`).

ECB здесь не косяк безопасности - это контейнер защищён от случайной модификации, не от криптоанализа. По блочной структуре ECB удобен потому что позволяет расшифровать отдельные блоки без последовательного чтения всего файла, чего CBC/CTR не дадут.

## NG

Это самописный Rockstar шифр. Расшифровывался обратной разработкой EXE-файла GTA V. Структурно похож на блочный шифр с 17 раундами, в каждом раунде идут 4 операции:

1. `XOR` с раунд-ключом;
2. Подстановка через S-box (256-byte permutation table);
3. Перестановка байт (P-box);
4. Сложение по mod 2^8.

Ключи - это **массив из 101 ключа** по 272 байта каждый. Какой именно ключ применять - определяется хешем имени файла. То есть для каждого файла внутри RPF используется **детерминированный** ключ из массива, и его номер вычисляется из имени.

Логика выбора (упрощённо):

```
int keyIndex = (FileNameHash(fileName) ^ FileLength) % 0x65;  // 101 keys
byte[] roundKey = PC_NG_KEYS[keyIndex];
```

`FileNameHash` - Rockstar joaat hash имени без расширения, lowercase.

NG используется для **ресурсов** - `.ydr`, `.ydd`, `.yft`, `.ytd`, `.ypt`, `.ynv`, `.yld`, `.yed`. Это файлы с RSC7 шапкой, у них внутри ещё свой слой структуры (virtual/physical page maps), и Rockstar хотели один общий механизм защиты для всех таких файлов.

## Откуда у нас ключи

`additionals/Keys/` содержит:

```
gtav_aes_key.dat        32 байт    AES-128 ключ
gtav_ng_key.dat         272 * 101  массив всех NG round-keys
gtav_ng_decrypt_tables.dat        S-box и P-box таблицы
gtav_ng_encrypt_tables.dat        обратные таблицы для шифрования
gtav_hash_lut.dat       65536 бит  Lookup table для joaat hash
```

Это **embedded в наш .exe** (см. `MiamiGraphics.Shell.csproj` ItemGroup `None Include="...\Keys\*.dat"`). RageLib читает их через `GTA5Constants.cs` при первом обращении к шифрованию.

```cs title="MiamiGraphics.Shell.csproj"
<ItemGroup>
    <None Include="C:\Users\neverexisted\Desktop\Keys\*.dat">
        <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
        <Link>Keys%(Filename)%(Extension)</Link>
    </None>
</ItemGroup>
```

Path в csproj указывает на локальную папку разработчика - у конечного юзера эти файлы уже лежат рядом с `Miami Graphics.exe` (в single-file сборке они extract'ятся в temp при первом запуске). Это даёт нам полный набор ключей без необходимости пользователю вытаскивать их самому из своей GTA.

## Что происходит если ключи не загрузились

Это был [инцидент в первой версии RageLib v1](../history/ragelib-v1.md). RageLib искал ключи в фиксированной директории относительно `AppDomain.CurrentDomain.BaseDirectory`, но из-за single-file публикации они оказывались в другом месте после extraction. Пайплайн запускался, отлавливал `0` ключей, и **молча** работал дальше - пытался открыть RPF, расшифровка возвращала мусор, парсер видел «всё совпадает с чистой версией», админ заливал «пустой» мод в каталог.

Сейчас явно логируем при старте парсера:

```cs title="MiamiGraphics.Core/Parser/ReduxParserPipeline.cs:36"
Console.WriteLine($"[Pipeline] NG keys loaded: {loadedKeys}/{totalKeys}");
if (loadedKeys == 0)
{
    Console.WriteLine("[Pipeline] WARNING: NO NG keys loaded. Decryption will be skipped.");
    Console.WriteLine($"[Pipeline] Working dir: {AppDomain.CurrentDomain.BaseDirectory}");
}
```

И в `RpfDiffEngine` отдельная защита от ложного diff'а если ключи отсутствуют - выкидываем ошибку вместо тихого возврата `[]`.

## Шифрование файлов

После того как мы создали новый файл в архиве через `RpfInjectEngine.AddModdedFile`, мы **никогда** не шифруем его:

```cs title="MiamiGraphics.Core/Injector/RpfInjectEngine.cs"
newF.IsEncrypted = false;
newF.Import(new MemoryStream(rawData));
```

Это допустимо - RPF8 ест нешифрованные файлы рядом с зашифрованными в том же архиве. Игра проверяет по `IsEncrypted` флагу в TOC, не пытается расшифровать файл с этим флагом = false.

Что **обязательно** надо сохранить - encryption всего RPF целиком (для TOC шапки):

```cs
outArc.archive_.Encryption = inArc.archive_.Encryption;
```

Если новый архив создан с дефолтным AES, а старый был NG - игра увидит TOC закодированный AES, попытается расшифровать NG, получит мусор, отбросит весь архив. `files corrupted`.

[Дальше: RageLib.GTA5 - что используем →](ragelib.md)
