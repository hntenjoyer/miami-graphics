using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.Services;
using MiamiGraphics.Shell.Services;
using MiamiGraphics.Core.Injector;
using ImageMagick;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Shell.Admin;

public sealed class AdminGunpackQueueService : IAdminGunpackQueueService
{
    private readonly IGunpackRepository _packs;
    private readonly IGunpackWhitelistRepository _whitelist;
    private readonly IRemoteStorage _storage;
    private readonly GltfpackRunner _gltfpack;
    private readonly string _filePath;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly SemaphoreSlim _runLock   = new(1, 1);
    private CancellationTokenSource? _runCts;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AdminGunpackQueueService(
        IGunpackRepository packs,
        IGunpackWhitelistRepository whitelist,
        IRemoteStorage storage,
        GltfpackRunner gltfpack)
    {
        _packs = packs;
        _whitelist = whitelist;
        _storage = storage;
        _gltfpack = gltfpack;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "MiamiGraphics", "admin");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "gunpack-queue.json");
    }

    public async Task<List<GunpackQueueItem>> ListAsync()
    {
        await _stateLock.WaitAsync();
        try { return await LoadAsync(); }
        finally { _stateLock.Release(); }
    }

    public async Task RemoveAsync(string tempId)
    {
        await _stateLock.WaitAsync();
        try
        {
            var all = await LoadAsync();
            all.RemoveAll(x => x.TempId == tempId);
            await SaveAsync(all);
        }
        finally { _stateLock.Release(); }
    }

    public void Cancel() => _runCts?.Cancel();

    public async Task<int> ReconcileOrphansAsync()
    {
        await _stateLock.WaitAsync();
        try
        {
            var all = await LoadAsync();
            int reset = 0;
            foreach (var item in all)
            {
                if (item.Status == "processing")
                {
                    item.Status       = "error";
                    item.ErrorMessage = "Прервано - приложение было закрыто во время обработки. Можешь удалить и попробовать снова.";
                    item.CurrentPhase = null;
                    reset++;
                }
            }
            if (reset > 0)
            {
                await SaveAsync(all);
                Debug.WriteLine($"[gunpack.queue.reconcile] reset {reset} orphan item(s) from 'processing' → 'error'");
            }
            return reset;
        }
        finally { _stateLock.Release(); }
    }

    public async Task<GunpackQueueItem> EnqueueAndStartAsync(
        GunpackQueueItem item,
        Action<GunpackQueueItem>? emit,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(item.TempId))    item.TempId = Guid.NewGuid().ToString("N")[..12];
        if (string.IsNullOrEmpty(item.Metadata.Id)) item.Metadata.Id = Guid.NewGuid().ToString();
        item.AddedAt = DateTime.UtcNow;
        item.Status = "pending";

        await _stateLock.WaitAsync();
        try
        {
            var all = await LoadAsync();

            all.RemoveAll(x => x.TempId == item.TempId);
            all.Add(item);
            await SaveAsync(all);
        }
        finally { _stateLock.Release(); }

        emit?.Invoke(Snapshot(item));

        _ = Task.Run(() => RunOneAsync(item, emit, ct), CancellationToken.None);

        return item;
    }

    private async Task RunOneAsync(GunpackQueueItem item, Action<GunpackQueueItem>? emit, CancellationToken outer)
    {

        await _runLock.WaitAsync();
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var ct = _runCts.Token;
        try
        {
            await ProcessAsync(item, emit, ct);
        }
        catch (OperationCanceledException)
        {
            await UpdateAsync(item.TempId, x => { x.Status = "error"; x.ErrorMessage = "Отменено"; x.CurrentPhase = null; }, emit);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gunpack.queue] '{item.Metadata.Name}' FAIL {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            await UpdateAsync(item.TempId, x => { x.Status = "error"; x.ErrorMessage = ex.Message; x.CurrentPhase = null; }, emit);
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task ProcessAsync(GunpackQueueItem item, Action<GunpackQueueItem>? emit, CancellationToken ct)
    {
        if (!File.Exists(item.SourceDlcRpfPath))
            throw new InvalidOperationException($"Источник DLC RPF не найден: {item.SourceDlcRpfPath}");

        var workRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MiamiGraphics", "workdir", "GunpacksAdmin", item.TempId);
        Directory.CreateDirectory(workRoot);
        await UpdateAsync(item.TempId, x => { x.TempWorkDir = workRoot; x.Status = "processing"; x.CurrentPhase = "analyzing"; x.Percent = 1; }, emit);

        await UpdateAsync(item.TempId, x => { x.CurrentPhase = "parsing"; x.Percent = 5; }, emit);

        var parser = new Core.Services.GunpackParserService();
        var parseResult = parser.Parse(new Core.Services.GunpackParserService.ParseRequest
        {
            SourceDlcRpfPath = item.SourceDlcRpfPath,
            ResultBaseDir    = workRoot,
            NameOverride     = item.Metadata.Name,
        });
        if (!parseResult.Success || parseResult.GunpackDir is null || parseResult.Info is null)
            throw new InvalidOperationException($"Парсинг провалился: {parseResult.Message}");

        var info        = parseResult.Info;
        var gunpackDir  = parseResult.GunpackDir;
        var weaponsRpfPath = Path.Combine(gunpackDir, info.WeaponsRpfName);
        if (!File.Exists(weaponsRpfPath))
            throw new InvalidOperationException($"weapons.rpf не извлечён парсером: {weaponsRpfPath}");

        await UpdateAsync(item.TempId, x => { x.CurrentPhase = "repacking"; x.Percent = 18; }, emit);

        var rebuiltPath = weaponsRpfPath + ".clean";
        try
        {
            RebuildWeaponsRpfClean(weaponsRpfPath, rebuiltPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Не удалось пересобрать weapons.rpf через clean-template: {ex.Message}", ex);
        }

        File.Delete(weaponsRpfPath);
        File.Move(rebuiltPath, weaponsRpfPath);
        Debug.WriteLine($"[gunpack.repack] {info.WeaponsRpfName}: " +
                        $"clean rebuild → {new FileInfo(weaponsRpfPath).Length / 1024} КБ");

        var weaponsRpfSha = ComputeFileSha256(weaponsRpfPath);
        var weaponsRpfSize = new FileInfo(weaponsRpfPath).Length;

        if (item.Variant is null)
        {
            var dup = await _packs.FindByWeaponsRpfSha256Async(weaponsRpfSha);
            if (dup is not null && !string.Equals(dup.Id, item.Metadata.Id, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Этот weapons.rpf уже залит в пак '{dup.Name}' (id {dup.Id}). " +
                    $"Удалите его сначала или используйте ту же запись для апдейта.");
        }

        await UpdateAsync(item.TempId, x => x.Percent = 22, emit);

        await UpdateAsync(item.TempId, x => { x.CurrentPhase = "filtering"; x.Percent = 25; }, emit);

        var whitelist = await _whitelist.ListAsync();
        if (whitelist.Count == 0)
            throw new InvalidOperationException("Whitelist пуст - заполните таблицу gunpack_whitelist.");
        var byInternalName = whitelist.ToDictionary(
            w => w.InternalName.ToLowerInvariant(),
            StringComparer.Ordinal);

        var matched = new List<MatchedGun>();

        foreach (var cat in info.Categories)
        {
            var key = (cat.WeaponPrefix + cat.BaseName).ToLowerInvariant();
            if (!byInternalName.TryGetValue(key, out var wle)) continue;
            matched.Add(new MatchedGun(
                baseName:      cat.BaseName,
                weaponPrefix:  cat.WeaponPrefix,
                whitelist:     wle,
                files:         cat.Files,
                perGunZipPath: Path.Combine(gunpackDir, "guns", cat.ZipFileName),
                smgRawYdrPath: null));
        }

        var smgOutDir = Path.Combine(workRoot, "smg");
        var smgArtefacts = WeaponsRpfSmgExtractor.Extract(
            weaponsRpfPath, whitelist, smgOutDir,
            logicalName: Services.TargetDlcEditor.MIAMI_WEAPON_RPF_NAME);
        foreach (var s in smgArtefacts)
        {
            if (!byInternalName.TryGetValue(s.InternalName.ToLowerInvariant(), out var wle)) continue;
            matched.Add(new MatchedGun(
                baseName:      s.BaseName,
                weaponPrefix:  s.WeaponPrefix,
                whitelist:     wle,
                files:         s.FileNames,
                perGunZipPath: s.PerGunZipPath,
                smgRawYdrPath: s.BaseYdrPath));
        }

        if (matched.Count == 0)
        {

            var foundInDlc = info.Categories
                .Select(c => (c.WeaponPrefix + c.BaseName).ToLowerInvariant())
                .Concat(smgArtefacts.Select(s => s.InternalName.ToLowerInvariant()))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            string foundList = foundInDlc.Count == 0
                ? "  (Core не распознал ни одной пушки в DLC)"
                : "  • " + string.Join("\n  • ", foundInDlc);

            var whitelistSample = whitelist
                .Select(w => w.InternalName.ToLowerInvariant())
                .OrderBy(s => s, StringComparer.Ordinal)
                .Take(8)
                .ToList();
            string whitelistHint = whitelist.Count == 0
                ? "  (whitelist пуст)"
                : $"  всего записей: {whitelist.Count}, первые: " + string.Join(", ", whitelistSample)
                  + (whitelist.Count > whitelistSample.Count ? ", ..." : "");

            throw new InvalidOperationException(
                "После whitelist в DLC не осталось ни одной разрешённой пушки.\n" +
                "\n" +
                "Найдено в DLC:\n" + foundList + "\n" +
                "\n" +
                "В таблице gunpack_whitelist:\n" + whitelistHint + "\n" +
                "\n" +
                "Добавьте недостающие internal_name в gunpack_whitelist и попробуйте ещё раз.");
        }

        Debug.WriteLine($"[gunpack.queue] matched {matched.Count} guns: {string.Join(", ", matched.Select(m => m.Whitelist.InternalName))}");
        await UpdateAsync(item.TempId, x => x.Percent = 30, emit);

        await UpdateAsync(item.TempId, x => { x.CurrentPhase = "converting"; }, emit);

        var glbDir = Path.Combine(workRoot, "glb");
        Directory.CreateDirectory(glbDir);

        var tmpYdrDir = Path.Combine(workRoot, "tmp-ydr");
        Directory.CreateDirectory(tmpYdrDir);

        for (int i = 0; i < matched.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var m = matched[i];
            var glbPath = Path.Combine(glbDir, $"{m.BaseName}.glb");

            string? sourceYdrPath = m.SmgRawYdrPath;
            if (sourceYdrPath is null)
            {

                sourceYdrPath = ExtractBaseYdrFromZip(m.PerGunZipPath, m.WeaponPrefix, m.BaseName, tmpYdrDir);
            }

            if (sourceYdrPath is not null)
            {

                var ytdPaths = ExtractYtdsFromZip(m.PerGunZipPath, m.BaseName, tmpYdrDir);
                try
                {
                    var report = new Core.Services.YdrToGltfConverter.ConvertReport();
                    var ok = await Core.Services.YdrToGltfConverter.ConvertAsync(sourceYdrPath, glbPath, ytdPaths, report);
                    m.GlbReady = ok && File.Exists(glbPath);
                    if (ok && ytdPaths.Count > 0)
                        Debug.WriteLine($"[gunpack.queue] YDR→GLB '{m.BaseName}' ok with {ytdPaths.Count} external YTD(s)");

                    if (report.Warnings.Count > 0)
                    {
                        Debug.WriteLine($"[gunpack.queue] '{m.BaseName}' texture warnings:\n  " +
                                        string.Join("\n  ", report.Warnings));
                        var head = report.Warnings.Take(3).ToList();
                        var tail = report.Warnings.Count > head.Count
                            ? $" (и ещё {report.Warnings.Count - head.Count})" : "";
                        await UpdateAsync(item.TempId, x =>
                        {
                            x.Warnings ??= new List<string>();
                            x.Warnings.Add(
                                $"«{m.BaseName}»: превью может не совпасть с игрой - " +
                                string.Join("; ", head) + tail + ". " +
                                $"Текстур в модели: {report.EmbeddedTextures} embedded" +
                                (report.ExternalAdded > 0 ? $" + {report.ExternalAdded} из .ytd" : "") + ".");
                        }, emit);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[gunpack.queue] YDR→GLB '{m.BaseName}' EXCEPTION: {ex.Message}");
                    m.GlbReady = false;
                }
            }

            var pct = 30 + (int)((i + 1) / (double)matched.Count * 20);
            await UpdateAsync(item.TempId, x => x.Percent = pct, emit);
        }

        await UpdateAsync(item.TempId, x => { x.CurrentPhase = "compressing"; x.Percent = 50; }, emit);

        var glbCompressedDir = Path.Combine(workRoot, "glb-compressed");
        Directory.CreateDirectory(glbCompressedDir);

        for (int i = 0; i < matched.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var m = matched[i];
            if (!m.GlbReady) continue;

            var src = Path.Combine(glbDir,            $"{m.BaseName}.glb");
            var dst = Path.Combine(glbCompressedDir,  $"{m.BaseName}.glb");
            await _gltfpack.TryCompressAsync(src, dst, ct);

            m.CompressedGlbPath = File.Exists(dst) ? dst : src;

            var pct = 50 + (int)((i + 1) / (double)matched.Count * 15);
            await UpdateAsync(item.TempId, x => x.Percent = pct, emit);
        }

        await UpdateAsync(item.TempId, x => { x.CurrentPhase = "rendering"; x.Percent = 65; }, emit);

        var thumbDir = Path.Combine(workRoot, "thumb");
        Directory.CreateDirectory(thumbDir);

        var probe = ProbeRendererState();
        var rendererAvailable = probe.IsUsable;
        Debug.WriteLine($"[gunpack.queue] renderer probe: {probe}");

        if (!rendererAvailable)
        {
            Debug.WriteLine("[gunpack.queue] GlbToPngRenderer unavailable - guns ship without thumbnails");
            await UpdateAsync(item.TempId, x =>
            {
                x.Warnings ??= new List<string>();
                x.Warnings.Add(BuildRendererUnavailableMessage(probe));
            }, emit);
        }

        int thumbAttempted = 0;
        int thumbRendered  = 0;
        int thumbFailed    = 0;
        if (!_gltfpack.IsAvailable)
        {

            await UpdateAsync(item.TempId, x =>
            {
                x.Warnings ??= new List<string>();
                x.Warnings.Add("GLB не сжаты в KTX2 - положи gltfpack.exe в additionals/tools/ для экономии трафика (3-4× меньший размер каждого GLB).");
            }, emit);
        }

        for (int i = 0; i < matched.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var m = matched[i];
            if (!m.GlbReady || string.IsNullOrEmpty(m.CompressedGlbPath)) continue;

            var webpPath = Path.Combine(thumbDir, $"{m.BaseName}.webp");

            if (rendererAvailable)
            {
                thumbAttempted++;
                try
                {
                    var rawGlbPath = Path.Combine(glbDir, $"{m.BaseName}.glb");
                    var glbForRender = File.Exists(rawGlbPath) ? rawGlbPath : m.CompressedGlbPath;
                    var rendered = await Core.Services.GlbToPngRenderer.RenderAsync(glbForRender, webpPath, 1024, 512);
                    if (rendered)
                    {
                        m.WebpPath = webpPath;
                        thumbRendered++;
                    }
                    else
                    {
                        thumbFailed++;
                        Debug.WriteLine($"[gunpack.queue] thumbnail '{m.BaseName}' RenderAsync returned false - see GlbToPngRenderer logs above");
                    }
                }
                catch (Exception ex)
                {
                    thumbFailed++;
                    Debug.WriteLine($"[gunpack.queue] thumbnail '{m.BaseName}' EXCEPTION: {ex.Message}");
                }
            }

            var pct = 65 + (int)((i + 1) / (double)matched.Count * 18);
            await UpdateAsync(item.TempId, x => x.Percent = pct, emit);
        }

        if (rendererAvailable && thumbAttempted > 0 && thumbRendered < thumbAttempted)
        {
            await UpdateAsync(item.TempId, x =>
            {
                x.Warnings ??= new List<string>();
                x.Warnings.Add(
                    $"Превью отрендерены частично: {thumbRendered}/{thumbAttempted} (упало {thumbFailed}). " +
                    $"Проба Renderer показала: {probe}. " +
                    "Обычные причины: antivirus блочит node.exe или Chromium, OOM при рендере, повреждённый GLB. " +
                    "Попробуй: 1) перезалить пак - часто проходит со второго раза; " +
                    "2) добавить %LOCALAPPDATA%\\MiamiGraphics\\Renderer в исключения Windows Defender; " +
                    "3) проверить свободную RAM (нужно ~500 MB на каждый параллельный рендер).");
            }, emit);
        }

        await UpdateAsync(item.TempId, x => { x.CurrentPhase = "packing"; x.Percent = 83; }, emit);

        var uploadDir = Path.Combine(workRoot, "_upload");
        if (Directory.Exists(uploadDir)) Directory.Delete(uploadDir, recursive: true);
        Directory.CreateDirectory(uploadDir);

        var uploadPackDir = Path.Combine(uploadDir, "pack");
        Directory.CreateDirectory(uploadPackDir);

        var miamiRpfPath = Path.Combine(uploadPackDir, "miami_weapon.rpf");
        File.Copy(weaponsRpfPath, miamiRpfPath, overwrite: true);

        var packZipPath = Path.Combine(uploadPackDir, "pack.zip");
        BuildPackZip(matched, packZipPath);
        var packZipSize  = new FileInfo(packZipPath).Length;
        var packZipSha   = ComputeFileSha256(packZipPath);

        var uploadGunsDir = Path.Combine(uploadDir, "guns");
        Directory.CreateDirectory(uploadGunsDir);
        foreach (var m in matched)
        {
            if (m.GlbReady && !string.IsNullOrEmpty(m.CompressedGlbPath) && File.Exists(m.CompressedGlbPath))
            {
                var dst = Path.Combine(uploadGunsDir, $"{m.BaseName}.glb");
                File.Copy(m.CompressedGlbPath, dst, overwrite: true);
                m.UploadGlbPath = dst;
            }
            if (!string.IsNullOrEmpty(m.WebpPath) && File.Exists(m.WebpPath))
            {
                var dst = Path.Combine(uploadGunsDir, $"{m.BaseName}.webp");
                File.Copy(m.WebpPath, dst, overwrite: true);
                m.UploadWebpPath = dst;
            }
        }

        var manifestPath = Path.Combine(uploadDir, "manifest.json");
        WriteManifest(manifestPath, info.WeaponsRpfName, weaponsRpfSize, weaponsRpfSha,
                      packZipSize, packZipSha, matched);

        await UpdateAsync(item.TempId, x => { x.CurrentPhase = "uploading"; x.Percent = 88; }, emit);

        var packId    = item.Variant?.PackId ?? item.Metadata.Id;
        var isVariant = item.Variant is not null;
        var r2Prefix  = isVariant
            ? $"gunpacks/{packId}/variants/{item.Variant!.VariantId:D}"
            : $"gunpacks/{packId}";

        string  weaponsRpfUrl = string.Empty;
        string? packZipUrl    = null;
        string? manifestUrl   = null;
        var glbUrls  = new Dictionary<string, string>();
        var webpUrls = new Dictionary<string, string>();

        if (item.UploadToR2)
        {

            try
            {
                await _storage.EnsureCorsAsync(ct);
            }
            catch (Exception ex)
            {
                var msg = ex.Message ?? string.Empty;
                var isAccessDenied = msg.IndexOf("Access Denied", StringComparison.OrdinalIgnoreCase) >= 0
                                  || msg.IndexOf("AccessDenied",  StringComparison.OrdinalIgnoreCase) >= 0;
                if (isAccessDenied)
                {
                    Debug.WriteLine($"[gunpack.queue] CORS apply skipped (token lacks s3:PutBucketCors - assuming bucket CORS is already set in Cloudflare dashboard).");
                }
                else
                {
                    Debug.WriteLine($"[gunpack.queue] WARN: CORS apply failed ({ex.Message}) - GLB fetch may fail until admin sets CORS manually.");
                    await UpdateAsync(item.TempId, x =>
                    {
                        x.Warnings ??= new List<string>();
                        x.Warnings.Add("CORS на R2 не применился автоматически - настрой через " +
                                       "Cloudflare → R2 → Settings → CORS (origin '*', methods GET/HEAD), " +
                                       "иначе 3D-просмотр в каталоге выдаст 'Failed to fetch'.");
                    }, emit);
                }
            }

            try
            {
                var purgePrefix = r2Prefix + "/";
                var purged = await _storage.DeletePrefixAsync(purgePrefix, ct);
                Debug.WriteLine($"[gunpack.queue] purged R2 prefix '{purgePrefix}' - {purged} object(s) removed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[gunpack.queue] WARN: R2 purge failed ({ex.Message}) - proceeding (orphans may remain).");
            }

            var plan = new List<(string Local, string Key, Action<string> Apply)>
            {
                (miamiRpfPath, $"{r2Prefix}/pack/miami_weapon.rpf", url => weaponsRpfUrl = url),
                (packZipPath,  $"{r2Prefix}/pack/pack.zip",         url => packZipUrl    = url),
                (manifestPath, $"{r2Prefix}/manifest.json",         url => manifestUrl   = url),
            };
            foreach (var m in matched)
            {
                if (!string.IsNullOrEmpty(m.UploadGlbPath))
                {
                    var nameCapt = m.BaseName;
                    plan.Add((m.UploadGlbPath, $"{r2Prefix}/guns/{nameCapt}.glb",
                             url => glbUrls[nameCapt] = url));
                }
                if (!string.IsNullOrEmpty(m.UploadWebpPath))
                {
                    var nameCapt = m.BaseName;
                    plan.Add((m.UploadWebpPath, $"{r2Prefix}/guns/{nameCapt}.webp",
                             url => webpUrls[nameCapt] = url));
                }
            }

            long totalBytes = 0;
            foreach (var (local, _, _) in plan)
                if (File.Exists(local)) totalBytes += new FileInfo(local).Length;

            long uploadedBytes = 0;
            var uploadLock = new object();
            using var gate = new SemaphoreSlim(6);
            var tasks = new List<Task>(plan.Count);
            foreach (var entry in plan)
            {
                var (local, key, apply) = entry;
                if (!File.Exists(local)) continue;
                var fileSize = new FileInfo(local).Length;

                tasks.Add(Task.Run(async () =>
                {
                    await gate.WaitAsync(ct);
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        var fileProgress = new Progress<int>(percent =>
                        {
                            var bytesNow = (long)((percent / 100.0) * fileSize);
                            long aggregate;
                            lock (uploadLock)
                            {
                                aggregate = totalBytes > 0
                                    ? 88 + ((uploadedBytes + bytesNow) * 9 / totalBytes)
                                    : 95;
                            }
                            var pct = (int)Math.Min(97, aggregate);
                            UpdateAsync(item.TempId, x => x.Percent = pct, emit).GetAwaiter().GetResult();
                        });

                        var url = await _storage.UploadAsync(local, key, fileProgress, ct);
                        lock (uploadLock) { uploadedBytes += fileSize; }
                        apply(url);
                    }
                    finally { gate.Release(); }
                }, ct));
            }
            await Task.WhenAll(tasks);
        }

        await UpdateAsync(item.TempId, x => { x.CurrentPhase = "registering"; x.Percent = 97; }, emit);

        if (isVariant)
        {
            var v = item.Variant!;

            var gunPreviews = new Dictionary<string, VariantGun>(StringComparer.Ordinal);
            foreach (var m in matched)
            {
                glbUrls.TryGetValue(m.BaseName, out var gu);
                webpUrls.TryGetValue(m.BaseName, out var pu);
                gunPreviews[m.BaseName] = new VariantGun
                {
                    Glb          = gu,
                    Webp         = pu,
                    DisplayName  = m.Whitelist.DisplayName,
                    Category     = m.Whitelist.Category,
                    WeaponPrefix = m.WeaponPrefix,
                    Files        = m.Files.ToList(),
                    SizeBytes    = ComputeFilesSizeFromZip(m.PerGunZipPath),
                    SortOrder    = m.Whitelist.SortOrder,
                };
            }

            var variant = new GunpackVariant
            {
                Id                = v.VariantId,
                GunpackId         = v.PackId,
                Name              = string.IsNullOrWhiteSpace(v.Name) ? "New variant" : v.Name.Trim(),
                WeaponsRpfUrl     = weaponsRpfUrl,
                WeaponsRpfSize    = weaponsRpfSize,
                WeaponsRpfSha256  = weaponsRpfSha,
                PackZipUrl        = packZipUrl,
                PackZipSize       = packZipSize,
                PackZipSha256     = packZipSha,
                ManifestUrl       = manifestUrl,
                CoverUrl          = null,
                IsDefault         = false,
                SortOrder         = 0,
                GunPreviews       = gunPreviews.Count == 0 ? null : gunPreviews,
            };
            await _packs.AddVariantAsync(variant, v.ServiceRoleKey);
            Debug.WriteLine($"[gunpack.queue] variant inserted id={v.VariantId} pack={v.PackId} previews={gunPreviews.Count}/{matched.Count}");
        }
        else
        {
            var serviceKey = item.ServiceRoleKey;
            if (string.IsNullOrWhiteSpace(serviceKey))
                throw new InvalidOperationException(
                    "Не задан Supabase Service Role Key в Admin → Настройки. " +
                    "Загрузка пака требует service-role-ключ (anon заблокирован RLS на gunpacks/gunpack_guns).");

            var pack = item.Metadata;
            pack.Id                = packId;
            pack.WeaponsRpfUrl     = weaponsRpfUrl;
            pack.WeaponsRpfSize    = weaponsRpfSize;
            pack.WeaponsRpfSha256  = weaponsRpfSha;
            pack.PackZipUrl        = packZipUrl;
            pack.PackZipSize       = packZipSize;
            pack.PackZipSha256     = packZipSha;
            pack.ManifestUrl       = manifestUrl;
            pack.UploadedAt        = DateTime.UtcNow;
            pack.UpdatedAt         = DateTime.UtcNow;
            if (string.IsNullOrEmpty(pack.Status)) pack.Status = "published";

            await _packs.AddWithServiceRoleAsync(pack, serviceKey);
            await _packs.DeleteAllGunsForPackWithServiceRoleAsync(pack.Id, serviceKey);

            var gunRows = matched.Select(m => new GunpackGun
            {

                GunpackId    = pack.Id,
                BaseName     = m.BaseName,
                WeaponPrefix = m.WeaponPrefix,
                Category     = m.Whitelist.Category,
                DisplayName  = m.Whitelist.DisplayName,
                GlbUrl       = glbUrls.TryGetValue(m.BaseName,  out var gu) ? gu : null,

                PreviewUrl   = webpUrls.TryGetValue(m.BaseName, out var pu)
                                ? pu
                                : (!string.IsNullOrWhiteSpace(m.Whitelist.PreviewUrl)
                                    ? m.Whitelist.PreviewUrl
                                    : null),
                Files        = m.Files.ToList(),
                SizeBytes    = ComputeFilesSizeFromZip(m.PerGunZipPath),
                IsHidden     = false,
                SortOrder    = m.Whitelist.SortOrder,
            }).ToList();
            await _packs.BulkUpsertGunsWithServiceRoleAsync(gunRows, serviceKey);
        }

        await UpdateAsync(item.TempId, x => { x.Status = "done"; x.CurrentPhase = null; x.Percent = 100; }, emit);

        try { if (Directory.Exists(uploadDir)) Directory.Delete(uploadDir, recursive: true); } catch { }
        try { if (Directory.Exists(tmpYdrDir)) Directory.Delete(tmpYdrDir, recursive: true); } catch { }
    }

    private static string? ExtractBaseYdrFromZip(string zipPath, string weaponPrefix, string baseName, string tmpDir)
    {
        if (!File.Exists(zipPath)) return null;

        try
        {
            using var zip = ZipFile.OpenRead(zipPath);

            var preferred = $"{weaponPrefix}{baseName}.ydr";
            var fallback  = $"{weaponPrefix}{baseName}_hi.ydr";
            var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                     ?? zip.Entries.FirstOrDefault(e => e.Name.Equals(fallback,  StringComparison.OrdinalIgnoreCase));
            if (entry is null) return null;

            var perGunDir = MiamiGraphics.Core.System.SafePath.ResolveLeafInside(tmpDir, baseName, "Имя ствола");
            Directory.CreateDirectory(perGunDir);
            var dst = MiamiGraphics.Core.System.SafePath.ResolveLeafInside(perGunDir, entry.Name, "Файл ствола из архива");
            entry.ExtractToFile(dst, overwrite: true);
            return dst;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gunpack.queue] couldn't extract ydr from {zipPath}: {ex.Message}");
            return null;
        }
    }

    private static List<string> ExtractYtdsFromZip(string zipPath, string baseName, string tmpDir)
    {
        var results = new List<string>();
        if (!File.Exists(zipPath)) return results;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var perGunDir = MiamiGraphics.Core.System.SafePath.ResolveLeafInside(tmpDir, baseName, "Имя ствола");
            Directory.CreateDirectory(perGunDir);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (!entry.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)) continue;
                var dst = MiamiGraphics.Core.System.SafePath.ResolveLeafInside(perGunDir, entry.Name, "Текстура ствола из архива");
                try
                {
                    entry.ExtractToFile(dst, overwrite: true);
                    results.Add(dst);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[gunpack.queue] couldn't extract ytd '{entry.Name}' from {zipPath}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gunpack.queue] couldn't open {zipPath} for ytd extraction: {ex.Message}");
        }
        return results;
    }

    private static void BuildPackZip(IReadOnlyList<MatchedGun> matched, string outputPath)
    {
        if (File.Exists(outputPath)) File.Delete(outputPath);
        using var fs = File.Create(outputPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        foreach (var m in matched)
        {
            if (!File.Exists(m.PerGunZipPath)) continue;
            using var src = ZipFile.OpenRead(m.PerGunZipPath);
            foreach (var srcEntry in src.Entries)
            {
                if (string.IsNullOrEmpty(srcEntry.Name)) continue;
                var dstName  = $"{m.BaseName}/{srcEntry.Name}";
                var dstEntry = zip.CreateEntry(dstName, CompressionLevel.Optimal);
                using var so = srcEntry.Open();
                using var sd = dstEntry.Open();
                so.CopyTo(sd);
            }
        }
    }

    private static void WriteManifest(
        string outPath,
        string weaponsRpfName,
        long   weaponsRpfSize,
        string weaponsRpfSha,
        long   packZipSize,
        string packZipSha,
        IReadOnlyList<MatchedGun> matched)
    {
        var doc = new
        {
            schemaVersion       = 1,
            generatedAt         = DateTime.UtcNow,
            weaponsRpfName      = weaponsRpfName,
            weaponsRpfHumanName = "miami_weapon.rpf",
            weaponsRpfSize      = weaponsRpfSize,
            weaponsRpfSha256    = weaponsRpfSha,
            packZipSize         = packZipSize,
            packZipSha256       = packZipSha,
            guns = matched.Select(m => new
            {
                baseName     = m.BaseName,
                weaponPrefix = m.WeaponPrefix,
                internalName = m.Whitelist.InternalName,
                category     = m.Whitelist.Category,
                displayName  = m.Whitelist.DisplayName,
                files        = m.Files,
                hasGlb       = m.UploadGlbPath  is not null,
                hasThumbnail = m.UploadWebpPath is not null,
            }).ToArray(),
        };
        File.WriteAllText(outPath, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static long ComputeFilesSizeFromZip(string zipPath)
    {
        if (!File.Exists(zipPath)) return 0;
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.Entries.Sum(e => e.Length);
        }
        catch { return 0; }
    }

    private static void ConvertPngToWebp(string pngPath, string webpPath, int quality)
    {

        using var img = new MagickImage(pngPath);
        img.Format = MagickFormat.WebP;
        img.Quality = (uint)Math.Clamp(quality, 1, 100);
        img.Write(webpPath);
    }

    private static string ComputeFileSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: false);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void RebuildWeaponsRpfClean(string srcPath, string dstPath)
    {
        var template = AdditionalsResolver.TemplatesPath("miami_empty.rpf");
        if (template is null || !File.Exists(template))
            throw new InvalidOperationException(
                "Шаблон templates/miami_empty.rpf не найден. " +
                "Проверь что папка templates лежит рядом с приложением (см. AdditionalsResolver).");

        if (File.Exists(dstPath)) File.Delete(dstPath);
        File.Copy(template, dstPath);

        using (var dst = RageArchiveWrapper7.Open(dstPath))
        {

            ClearArchiveDirectory(dst.Root);

            using (var srcStream = new FileStream(srcPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var src = RageArchiveWrapper7.Open(srcStream, Path.GetFileName(srcPath), true))
            {
                CopyArchiveTree(src.Root, dst.Root);
            }

            AddMissingOverlayMagStubs(dst.Root);

            dst.FileName = "miami_weapon.rpf";
            dst.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
            dst.Flush();
        }

        ArchiveFixer.FixOrThrow(dstPath, TargetDlcEditor.MIAMI_WEAPON_RPF_NAME);
    }

    private static void ClearArchiveDirectory(IArchiveDirectory dir)
    {
        foreach (var f in dir.GetFiles().ToList())     dir.DeleteFile(f);
        foreach (var d in dir.GetDirectories().ToList()) dir.DeleteDirectory(d);
    }

    private static void CopyArchiveTree(IArchiveDirectory srcDir, IArchiveDirectory dstDir)
    {

        foreach (var srcFile in srcDir.GetFiles())
        {
            byte[] bytes;
            using (var ms = new MemoryStream())
            {
                srcFile.Export(ms);
                bytes = ms.ToArray();
            }

            if (!RpfEntryGate.TryPrepare(srcFile.Name, bytes, out var ready, out var bad, out var note))
            {
                Debug.WriteLine($"[gunpack-queue] ОТСЕЯНО '{srcFile.Name}' ({bytes.Length:N0} б): {bad}");
                continue;
            }
            if (note != null) Debug.WriteLine($"[gunpack-queue] '{srcFile.Name}' {note}");
            bytes = ready;

            if (IsRsc7Resource(bytes))
            {
                var rf = dstDir.CreateResourceFile();
                rf.Name = srcFile.Name;
                using var ims = new MemoryStream(bytes);
                rf.Import(ims);
            }
            else
            {
                var bf = dstDir.CreateBinaryFile();
                bf.Name = srcFile.Name;
                bf.IsEncrypted = false;
                bf.IsCompressed = false;
                bf.UncompressedSize = bytes.LongLength;
                using var ims = new MemoryStream(bytes);
                bf.Import(ims);
            }
        }

        foreach (var srcSub in srcDir.GetDirectories())
        {
            var dstSub = dstDir.CreateDirectory();
            dstSub.Name = srcSub.Name;
            CopyArchiveTree(srcSub, dstSub);
        }
    }

    private static void AddMissingOverlayMagStubs(IArchiveDirectory root)
    {
        var overlayDir = AdditionalsResolver.TemplatesPath("overlaymags");
        if (overlayDir is null)
        {
            Debug.WriteLine("[gunpack.queue] templates/overlaymags не найдена - пересобираю без заглушек обвесов");
            return;
        }

        var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFileNames(root, have);

        int added = 0;
        foreach (var path in Directory.EnumerateFiles(overlayDir, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            if (have.Contains(name)) continue;
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) continue;

            if (!RpfEntryGate.TryPrepare(name, bytes, out var readyOverlay, out var badOverlay, out var noteOverlay))
            {
                Debug.WriteLine($"[gunpack-queue] ОТСЕЯН оверлей '{name}' ({bytes.Length:N0} б): {badOverlay}");
                continue;
            }
            if (noteOverlay != null) Debug.WriteLine($"[gunpack-queue] оверлей '{name}' {noteOverlay}");
            bytes = readyOverlay;

            if (IsRsc7Resource(bytes))
            {
                var rf = root.CreateResourceFile();
                rf.Name = name;
                using var ims = new MemoryStream(bytes);
                rf.Import(ims);
            }
            else
            {
                var bf = root.CreateBinaryFile();
                bf.Name = name;
                bf.IsEncrypted = false;
                bf.IsCompressed = false;
                bf.UncompressedSize = bytes.LongLength;
                using var ims = new MemoryStream(bytes);
                bf.Import(ims);
            }
            added++;
        }
        Debug.WriteLine($"[gunpack.queue] заглушки обвесов: докинуто {added} (у автора уже было {have.Count} файлов)");
    }

    private static void CollectFileNames(IArchiveDirectory dir, HashSet<string> names)
    {
        foreach (var f in dir.GetFiles()) names.Add(f.Name);
        foreach (var d in dir.GetDirectories()) CollectFileNames(d, names);
    }

    private static bool IsRsc7Resource(byte[] data) => RpfEntrySanity.IsRsc7(data);

    private async Task UpdateAsync(string tempId, Action<GunpackQueueItem> mutate, Action<GunpackQueueItem>? emit)
    {
        await _stateLock.WaitAsync();
        GunpackQueueItem? snap = null;
        try
        {
            var all = await LoadAsync();
            var existing = all.FirstOrDefault(x => x.TempId == tempId);
            if (existing is null) return;
            mutate(existing);
            snap = Snapshot(existing);
            await SaveAsync(all);
        }
        finally { _stateLock.Release(); }
        if (snap is not null) emit?.Invoke(snap);
    }

    private static GunpackQueueItem Snapshot(GunpackQueueItem item)
    {
        var json = JsonSerializer.Serialize(item, Json);
        return JsonSerializer.Deserialize<GunpackQueueItem>(json, Json) ?? item;
    }

    private async Task<List<GunpackQueueItem>> LoadAsync()
    {
        if (!File.Exists(_filePath)) return new List<GunpackQueueItem>();
        try
        {
            await using var fs = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<GunpackQueueItem>>(fs, Json) ?? new();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gunpack.queue] load error: {ex.Message} - starting empty");
            return new List<GunpackQueueItem>();
        }
    }

    private async Task SaveAsync(List<GunpackQueueItem> items)
    {
        await using var fs = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(fs, items, Json);
    }

    private sealed class MatchedGun
    {
        public string  BaseName       { get; }
        public string  WeaponPrefix   { get; }
        public GunpackWhitelistEntry Whitelist { get; }
        public List<string> Files     { get; }
        public string  PerGunZipPath  { get; }
        public string? SmgRawYdrPath  { get; }

        public bool    GlbReady          { get; set; }
        public string? CompressedGlbPath { get; set; }
        public string? WebpPath          { get; set; }
        public string? UploadGlbPath     { get; set; }
        public string? UploadWebpPath    { get; set; }

        public MatchedGun(
            string baseName,
            string weaponPrefix,
            GunpackWhitelistEntry whitelist,
            List<string> files,
            string perGunZipPath,
            string? smgRawYdrPath)
        {
            BaseName      = baseName;
            WeaponPrefix  = weaponPrefix;
            Whitelist     = whitelist;
            Files         = files;
            PerGunZipPath = perGunZipPath;
            SmgRawYdrPath = smgRawYdrPath;
        }
    }

    private readonly record struct RendererProbe(
        bool BaseDirExists,
        bool NodeExeExists,
        bool RenderJsExists,
        bool NodeModulesExists,
        long NodeModulesSizeMb,
        string? NodeVersion,
        string? NodeError,
        bool ChromiumInstalled,
        bool IsUsable)
    {
        public override string ToString() =>
            $"baseDir={BaseDirExists}, node.exe={NodeExeExists}, render.js={RenderJsExists}, " +
            $"node_modules={NodeModulesExists} ({NodeModulesSizeMb} MB), " +
            $"chromium={ChromiumInstalled}, " +
            $"node={NodeVersion ?? "FAIL: " + (NodeError ?? "?")}, usable={IsUsable}";
    }

    private static RendererProbe ProbeRendererState()
    {
        var p = MiamiGraphics.Shell.Services.RendererBootstrapper.Probe();
        return new RendererProbe(
            p.BaseDirExists, p.NodeExeExists, p.RenderJsExists,
            p.NodeModulesExists, p.NodeModulesSizeMb,
            p.NodeVersion, p.NodeError, p.ChromiumInstalled, p.IsUsable);
    }

    private static string BuildRendererUnavailableMessage(RendererProbe p)
    {
        if (!p.BaseDirExists)
            return "Renderer/ полностью отсутствует. Лаунчер должен был скачать его автоматически перед заливкой - " +
                   "если этот warning виден, значит auto-download упал. Перезалей пак: при следующей попытке скачивание повторится.";

        if (!p.NodeExeExists || !p.RenderJsExists)
            return $"Renderer/ повреждён: node.exe={p.NodeExeExists}, render.js={p.RenderJsExists}. " +
                   "Удали папку %LOCALAPPDATA%\\MiamiGraphics\\Renderer вручную и перезалей пак - скачается заново.";

        if (!p.NodeModulesExists || p.NodeModulesSizeMb < 50)
            return $"Renderer/node_modules не установлен или повреждён ({p.NodeModulesSizeMb} MB вместо ~150 MB). " +
                   "Antivirus мог удалить часть DLL Puppeteer/Chromium. Добавь Renderer/ в исключения Defender и перезалей.";

        if (p.NodeError is not null)
            return $"Renderer/node.exe не запускается: {p.NodeError}. " +
                   "Скорее всего antivirus блокирует. Добавь %LOCALAPPDATA%\\MiamiGraphics\\Renderer в исключения и перезапусти Miami Graphics.";

        if (!p.ChromiumInstalled)
            return "Chromium (chrome-headless-shell) не установлен. Открой Settings -> Renderer и нажми «Установить» - puppeteer скачает ~120 MB с googleapis.com.";

        return $"Renderer состояние: {p}. Превью пушек пропущены, ганы появятся в каталоге без PNG-превью " +
               "(3D-просмотр по GLB продолжит работать).";
    }
}
