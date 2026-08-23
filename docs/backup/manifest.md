# Backup manifest

Каждый backup пишет рядом с файлами `backup-manifest.json` - описание что именно сохранилось, когда, для какой версии GTA. Это **источник истины** для restore-операций.

## Формат

```json title="cache/clean/backup-manifest.json"
{
  "schemaVersion": 1,
  "kind": "clean",
  "createdAt": "2026-05-08T14:23:11Z",
  "gtaVersion": "1.0.3411.0",
  "gtaPath": "C:\\Program Files (x86)\\Rockstar Games\\GTA V",
  "files": [
    {
      "relativePath": "update/update.rpf",
      "sha256": "a1b2c3...",
      "sizeBytes": 4567890123
    },
    {
      "relativePath": "x64a.rpf",
      "sha256": "...",
      "sizeBytes": 123456789
    }
  ],
  "totalSizeBytes": 5000000000
}
```

## Поля

| Поле | Назначение |
|---|---|
| `schemaVersion` | Версия формата manifest'а. Для будущих breaking changes |
| `kind` | `"clean"` (чистый baseline) или `"snapshot"` (юзерский snapshot) |
| `createdAt` | ISO 8601 timestamp |
| `gtaVersion` | Version GTA, для которой делался backup. При restore-валидации сверяем |
| `gtaPath` | Полный путь к GTA на момент backup'а |
| `files[]` | Список **всех** забэкапленных файлов с relative path и SHA |
| `totalSizeBytes` | Сумма sizes - для UI display |

## Зачем sha256

При restore мы могли бы просто скопировать файлы из backup-папки в GTA. Зачем SHA?

- **Проверка целостности.** Если backup-папка была повреждена (диск bad sector, ransomware), SHA не совпадёт - мы откажем в restore «бэкап повреждён».
- **Skip-on-match optimization.** Если в backup `sha = X` и **текущий** файл в GTA уже `sha = X` - копировать не нужно. Это ускоряет частичный restore.
- **Логирование.** При написании issue в саппорт юзер прикладывает manifest, и мы видим какие именно файлы у него были и в каком состоянии.

## Где manifest хранится

| Тип | Путь |
|---|---|
| Clean baseline | `%LocalAppData%\MiamiGraphics\cache\clean\backup-manifest.json` |
| Snapshot | `%LocalAppData%\MiamiGraphics\cache\snapshots\<timestamp>\backup-manifest.json` |

`<timestamp>` - формат `2026-05-12_142311` (YYYY-MM-DD_HHmmss). Sorting по lexical order = chronological.

## Снимок текущего state'а перед backup'ом

Перед тем как **писать** manifest, мы должны **прочитать** SHA каждого файла. Это медленно для больших файлов (5 GB update.rpf - ~10 секунд SHA-256 на NVMe, ~30 секунд на SATA).

Поэтому мы вычисляем SHA параллельно с copy'ем (single read, два sink'а):

```cs title="MiamiGraphics.Shell/Services/BackupService.cs (упрощено)"
private async Task<(string sha, long size)> CopyAndHashAsync(string src, string dest)
{
    using var sha = SHA256.Create();
    using var srcFs  = File.OpenRead(src);
    using var destFs = File.Create(dest);

    var buffer = new byte[1024 * 1024];   // 1 МБ chunks
    long total = 0;
    int read;
    while ((read = await srcFs.ReadAsync(buffer)) > 0)
    {
        sha.TransformBlock(buffer, 0, read, null, 0);
        await destFs.WriteAsync(buffer.AsMemory(0, read));
        total += read;
    }
    sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

    return (Convert.ToHexString(sha.Hash!).ToLowerInvariant(), total);
}
```

Один read, два writer'а (HashAlgorithm и FileStream). Тратим O(N) IO вместо O(2N).

## Read manifest

```cs
public async Task<BackupManifest> ReadManifestAsync(string manifestPath)
{
    if (!File.Exists(manifestPath))
        throw new FileNotFoundException("backup-manifest.json не найден", manifestPath);

    var json = await File.ReadAllTextAsync(manifestPath);
    var manifest = JsonSerializer.Deserialize<BackupManifest>(json)
        ?? throw new InvalidOperationException("Manifest пустой или невалидный JSON");

    if (manifest.SchemaVersion != 1)
        throw new InvalidOperationException(
            $"Unsupported manifest schema {manifest.SchemaVersion}. Обновите лаунчер.");

    return manifest;
}
```

## Migration

Если manifest старого формата (schema 0, без `kind`) - апгрейдим on-read:

```cs
if (manifest.SchemaVersion == 0)
{
    // legacy migration: dir name = kind
    var dirName = Path.GetFileName(Path.GetDirectoryName(manifestPath));
    manifest = manifest with {
        SchemaVersion = 1,
        Kind = dirName == "clean" ? "clean" : "snapshot",
    };
    // Re-save в новом формате
    await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));
}
```

Сейчас все backup'ы schema 1 - у юзеров с очень старыми билдами могут быть schema 0. Migration безопасный.

[Дальше: Snapshot vs Clean →](snapshot-vs-clean.md)
