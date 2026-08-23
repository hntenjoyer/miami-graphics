using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.System;
using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public sealed class AdminQueueService : IAdminQueueService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly IReduxCatalogRepository _catalog;
    private readonly IReduxVersionsRepository _versions;
    private readonly Services.IRemoteStorage _storage;
    private CancellationTokenSource? _runCts;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public AdminQueueService(
        IReduxCatalogRepository catalog,
        Services.IRemoteStorage storage,
        IReduxVersionsRepository versions)
    {
        _catalog = catalog;
        _storage = storage;
        _versions = versions;
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "MiamiGraphics", "admin");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "queue.json");
    }

    public async Task<List<QueueItem>> ListAsync()
    {
        await _lock.WaitAsync();
        try { return await LoadAsync(); }
        finally { _lock.Release(); }
    }

    public async Task<QueueItem> AddAsync(QueueItem item)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await LoadAsync();
            if (string.IsNullOrEmpty(item.TempId)) item.TempId = Guid.NewGuid().ToString("N")[..12];
            item.AddedAt = DateTime.UtcNow;
            item.Status = "pending";
            all.Add(item);
            await SaveAsync(all);
            return item;
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveAsync(string tempId)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await LoadAsync();
            all.RemoveAll(x => x.TempId == tempId);
            await SaveAsync(all);
        }
        finally { _lock.Release(); }
    }

    public void Cancel() => _runCts?.Cancel();

    public async Task<int> ReconcileOrphansAsync()
    {
        await _lock.WaitAsync();
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
                Debug.WriteLine($"[admin.queue.reconcile] reset {reset} orphan item(s) from 'processing' → 'error'");
            }
            return reset;
        }
        finally { _lock.Release(); }
    }

    public async Task RunAsync(IProgress<QueueItem>? progress, CancellationToken outer)
    {
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var ct = _runCts.Token;

        var sw = Stopwatch.StartNew();
        long lastEmitMs = -1;
        string? lastPhase = null;
        void EmitThrottled(QueueItem snap)
        {
            var nowMs = sw.ElapsedMilliseconds;
            var phaseChanged = snap.CurrentPhase != lastPhase;
            var terminal = snap.Status is "done" or "error";
            if (!phaseChanged && !terminal && nowMs - lastEmitMs < 250) return;
            lastEmitMs = nowMs;
            lastPhase = snap.CurrentPhase;
            progress?.Report(snap);
        }

        var pending = (await ListAsync()).Where(x => x.Status == "pending").ToList();
        foreach (var item in pending)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                await BuildAndPublishAsync(item, EmitThrottled, ct);
            }
            catch (OperationCanceledException)
            {
                await UpdateAsync(item, x => { x.Status = "error"; x.ErrorMessage = "Cancelled"; }, EmitThrottled);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[admin.queue.run] item '{item.Metadata.Id}' FAIL: {ex.GetType().Name}: {ex.Message}");
                await UpdateAsync(item, x => { x.Status = "error"; x.ErrorMessage = ex.Message; }, EmitThrottled);
            }
        }
    }

    private async Task BuildAndPublishAsync(QueueItem item, Action<QueueItem> emit, CancellationToken ct)
    {

        if (item.Versions is { Count: > 0 })
        {
            await BuildAndPublishMultiVersionAsync(item, emit, ct);
            return;
        }

        await UpdateAsync(item, x => { x.Status = "processing"; x.CurrentPhase = "building"; x.Percent = 0; }, emit);

        var workDir = item.TempWorkDir;
        if (string.IsNullOrWhiteSpace(workDir) || !Directory.Exists(workDir))
            throw new InvalidOperationException($"workDir missing: {workDir}");

        var buildOutput = Path.Combine(workDir, "_upload");
        if (Directory.Exists(buildOutput)) Directory.Delete(buildOutput, recursive: true);
        Directory.CreateDirectory(buildOutput);

        var patchSourceDir = Path.Combine(workDir, "patch_files");
        var patchZipPath = Path.Combine(buildOutput, "patch.zip");
        if (Directory.Exists(patchSourceDir))
        {
            ZipFile.CreateFromDirectory(patchSourceDir, patchZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            Debug.WriteLine($"[admin.build] patch.zip -> {new FileInfo(patchZipPath).Length / 1024 / 1024} MB");
        }
        await UpdateAsync(item, x => x.Percent = 20, emit);

        var componentsRoot = Path.Combine(workDir, "components");
        var componentsOutputDir = Path.Combine(buildOutput, "components");
        Directory.CreateDirectory(componentsOutputDir);

        var componentZips = new List<(string Name, string ZipPath)>();
        if (Directory.Exists(componentsRoot))
        {
            foreach (var componentDir in Directory.GetDirectories(componentsRoot))
            {
                var name = Path.GetFileName(componentDir);
                var zip = Path.Combine(componentsOutputDir, $"{name}.zip");
                ZipFile.CreateFromDirectory(componentDir, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
                componentZips.Add((name, zip));
                Debug.WriteLine($"[admin.build] {name}.zip -> {new FileInfo(zip).Length / 1024} KB");
            }
        }
        await UpdateAsync(item, x => x.Percent = 40, emit);

        string[] jsonNames = { "manifest.json", "content_info.json", "component_map.json" };
        foreach (var jsonName in jsonNames)
        {
            var src = Path.Combine(workDir, jsonName);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(buildOutput, jsonName), overwrite: true);
        }
        await UpdateAsync(item, x => x.Percent = 50, emit);

        var r2Urls = new R2UrlsLocal();

        if (item.UploadToR2)
        {
            await UpdateAsync(item, x => { x.CurrentPhase = "uploading"; x.Percent = 50; }, emit);

            var baseKey = $"redux/{item.Metadata.Id}";

            try
            {
                var purged = await _storage.DeletePrefixAsync($"{baseKey}/", ct);
                Debug.WriteLine($"[admin.upload] purged R2 prefix '{baseKey}/' - {purged} object(s) removed");
            }
            catch (Exception ex)
            {

                Debug.WriteLine($"[admin.upload] WARN: prefix purge failed ({ex.Message}). Proceeding with PUT-overwrite only - orphans may remain.");
            }

            var plan = new List<(string Local, string RemoteKey, Action<string> Apply)>();

            if (File.Exists(patchZipPath))
                plan.Add((patchZipPath, $"{baseKey}/patch.zip", url => r2Urls.Patch = url));

            foreach (var (n, zipPath) in componentZips)
            {
                var nameCapt = n;
                plan.Add((zipPath, $"{baseKey}/components/{nameCapt}.zip", url => r2Urls.Components[nameCapt] = url));
            }

            var armorGlbLocal = Path.Combine(workDir, "components", "armor",
                MiamiGraphics.Core.Parser.ArmorGlbExporter.ArmorGlbFileName);
            if (File.Exists(armorGlbLocal))
            {
                Debug.WriteLine($"[admin.build] armor.glb найден ({new FileInfo(armorGlbLocal).Length / 1024} KB) - добавляю в план");
                plan.Add((armorGlbLocal, $"{baseKey}/components/armor.glb",
                    url => r2Urls.Components["armor_glb"] = url));
            }
            else
            {
                Debug.WriteLine($"[admin.build] armor.glb НЕ найден на диске: {armorGlbLocal} - на R2 не уйдёт");
            }

            foreach (var jsonName in jsonNames)
            {
                var jsonPath = Path.Combine(buildOutput, jsonName);
                if (!File.Exists(jsonPath)) continue;
                var nameCapt = jsonName;
                plan.Add((jsonPath, $"{baseKey}/{nameCapt}", url =>
                {
                    if (nameCapt == "manifest.json") r2Urls.Manifest = url;
                    else if (nameCapt == "component_map.json") r2Urls.ComponentMap = url;
                    else if (nameCapt == "content_info.json") r2Urls.ContentInfo = url;
                }));
            }

            long totalBytes = plan.Sum(p => new FileInfo(p.Local).Length);
            long uploadedSoFar = 0;

            foreach (var (local, key, apply) in plan)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

                var fileSize = new FileInfo(local).Length;
                var fileStart = uploadedSoFar;
                Debug.WriteLine($"[admin.upload] {key} ({fileSize / 1024} KB)");

                var fileProgress = new Progress<int>(percent =>
                {
                    var fileBytes = (long)((percent / 100.0) * fileSize);
                    var total = fileStart + fileBytes;
                    var aggregate = totalBytes > 0
                        ? (int)(50 + (total * 40 / totalBytes))
                        : 90;
                    UpdateAsync(item, x => x.Percent = Math.Min(90, aggregate), emit).GetAwaiter().GetResult();
                });

                var url = await _storage.UploadAsync(local, key, fileProgress, ct);
                apply(url);
                uploadedSoFar += fileSize;
            }
        }

        await UpdateAsync(item, x => x.Percent = 90, emit);

        await UpdateAsync(item, x => { x.CurrentPhase = "registering"; x.Percent = 95; }, emit);

        var snapshot = item.Metadata;

        snapshot.R2Urls = item.UploadToR2 ? r2Urls : item.Metadata.R2Urls;

        long patchZipBytes = 0;
        if (File.Exists(patchZipPath))
        {
            patchZipBytes = new FileInfo(patchZipPath).Length;
            snapshot.PatchSha256 = ComputeFileSha256(patchZipPath);
            Debug.WriteLine($"[admin.build] patch.zip sha256={snapshot.PatchSha256}");
        }
        snapshot.PatchSizeBytes = patchZipBytes;
        Debug.WriteLine($"[admin.build] patch.zip size = {patchZipBytes / 1024 / 1024} MB ({patchZipBytes:N0} bytes)");
        snapshot.UploadedAt = DateTime.UtcNow;
        snapshot.Status = "published";
        await _catalog.AddAsync(snapshot);

        await UpdateAsync(item, x => { x.Status = "done"; x.CurrentPhase = null; x.Percent = 100; }, emit);

        try
        {
            if (Directory.Exists(buildOutput)) Directory.Delete(buildOutput, recursive: true);
        }
        catch {  }
    }

    private async Task BuildAndPublishMultiVersionAsync(QueueItem item, Action<QueueItem> emit, CancellationToken ct)
    {
        await UpdateAsync(item, x => { x.Status = "processing"; x.CurrentPhase = "building"; x.Percent = 0; }, emit);

        var versions = item.Versions!;
        var reduxId = item.Metadata.Id;
        if (string.IsNullOrWhiteSpace(reduxId))
            throw new InvalidOperationException("Metadata.Id is required for multi-version upload");

        var appendMode = !string.IsNullOrWhiteSpace(item.AppendToReduxId);
        if (appendMode && !string.Equals(item.AppendToReduxId, reduxId, StringComparison.Ordinal))
            throw new InvalidOperationException($"AppendToReduxId '{item.AppendToReduxId}' must match Metadata.Id '{reduxId}'");

        var slotArtifacts = new List<(VersionSpec Spec, string PatchZip, List<(string Name, string Zip)> Components, string PatchSha)>();

        if (item.UploadToR2 && !appendMode)
        {
            try
            {
                var preserve = new[]
                {
                    $"redux/{reduxId}/screenshots/",
                    $"redux/{reduxId}/mirror/",
                };
                var purged = await _storage.DeletePrefixExceptAsync($"redux/{reduxId}/", preserve, ct);
                Debug.WriteLine($"[admin.build.mv] purged R2 prefix 'redux/{reduxId}/' (keep screenshots+mirror) - {purged} object(s) removed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[admin.build.mv] WARN: prefix purge failed ({ex.Message}). Proceeding anyway - orphans may remain.");
            }
        }
        else if (item.UploadToR2 && appendMode)
        {
            foreach (var spec in versions)
            {
                try
                {
                    var slotPrefix = $"redux/{reduxId}/v{spec.Slot}/";
                    var purged = await _storage.DeletePrefixAsync(slotPrefix, ct);
                    Debug.WriteLine($"[admin.build.mv.append] purged R2 slot prefix '{slotPrefix}' - {purged} object(s) removed");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[admin.build.mv.append] WARN: slot prefix purge failed ({ex.Message}). Proceeding anyway.");
                }
            }
        }

        for (int i = 0; i < versions.Count; i++)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            var spec = versions[i];
            if (string.IsNullOrWhiteSpace(spec.TempWorkDir) || !Directory.Exists(spec.TempWorkDir))
                throw new InvalidOperationException($"version {spec.Slot} '{spec.Label}': workDir missing: {spec.TempWorkDir}");

            var buildOutput = Path.Combine(spec.TempWorkDir, "_upload");
            if (Directory.Exists(buildOutput)) Directory.Delete(buildOutput, recursive: true);
            Directory.CreateDirectory(buildOutput);

            var patchSourceDir = Path.Combine(spec.TempWorkDir, "patch_files");
            var patchZipPath = Path.Combine(buildOutput, "patch.zip");
            if (Directory.Exists(patchSourceDir))
            {
                ZipFile.CreateFromDirectory(patchSourceDir, patchZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
                Debug.WriteLine($"[admin.build.mv] slot {spec.Slot} patch.zip -> {new FileInfo(patchZipPath).Length / 1024 / 1024} MB");
            }

            var componentsRoot = Path.Combine(spec.TempWorkDir, "components");
            var componentsOutputDir = Path.Combine(buildOutput, "components");
            Directory.CreateDirectory(componentsOutputDir);
            var componentZips = new List<(string Name, string Zip)>();
            if (Directory.Exists(componentsRoot))
            {
                foreach (var componentDir in Directory.GetDirectories(componentsRoot))
                {
                    var name = Path.GetFileName(componentDir);
                    var zip = Path.Combine(componentsOutputDir, $"{name}.zip");
                    ZipFile.CreateFromDirectory(componentDir, zip, CompressionLevel.Optimal, includeBaseDirectory: false);
                    componentZips.Add((name, zip));
                }
            }

            string[] jsonNames = { "manifest.json", "content_info.json", "component_map.json" };
            foreach (var jsonName in jsonNames)
            {
                var src = Path.Combine(spec.TempWorkDir, jsonName);
                if (File.Exists(src))
                    File.Copy(src, Path.Combine(buildOutput, jsonName), overwrite: true);
            }

            var patchSha = File.Exists(patchZipPath) ? ComputeFileSha256(patchZipPath) : string.Empty;
            slotArtifacts.Add((spec, patchZipPath, componentZips, patchSha));

            var pctBuild = (int)(((i + 1) / (double)versions.Count) * 40);
            await UpdateAsync(item, x => x.Percent = pctBuild, emit);
        }

        var slotUrls = new Dictionary<int, R2UrlsLocal>();
        if (item.UploadToR2)
        {
            await UpdateAsync(item, x => { x.CurrentPhase = "uploading"; x.Percent = 40; }, emit);

            long totalBytes = 0;
            foreach (var (_, patchZip, comps, _) in slotArtifacts)
            {
                if (File.Exists(patchZip)) totalBytes += new FileInfo(patchZip).Length;
                foreach (var (_, z) in comps) if (File.Exists(z)) totalBytes += new FileInfo(z).Length;
            }

            long uploadedSoFar = 0;

            foreach (var (spec, patchZip, comps, _) in slotArtifacts)
            {
                if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);

                var r2 = new R2UrlsLocal();
                var baseKey = $"redux/{reduxId}/v{spec.Slot}";

                var plan = new List<(string Local, string RemoteKey, Action<string> Apply)>();
                if (File.Exists(patchZip))
                    plan.Add((patchZip, $"{baseKey}/patch.zip", url => r2.Patch = url));
                foreach (var (n, zipPath) in comps)
                {
                    var nCapt = n;
                    plan.Add((zipPath, $"{baseKey}/components/{nCapt}.zip", url => r2.Components[nCapt] = url));
                }

                var armorGlbLocalMv = Path.Combine(spec.TempWorkDir, "components", "armor",
                    MiamiGraphics.Core.Parser.ArmorGlbExporter.ArmorGlbFileName);
                if (File.Exists(armorGlbLocalMv))
                {
                    Debug.WriteLine($"[admin.build.mv] armor.glb найден ({new FileInfo(armorGlbLocalMv).Length / 1024} KB) - добавляю в план");
                    plan.Add((armorGlbLocalMv, $"{baseKey}/components/armor.glb",
                        url => r2.Components["armor_glb"] = url));
                }
                else
                {
                    Debug.WriteLine($"[admin.build.mv] armor.glb НЕ найден на диске: {armorGlbLocalMv} - на R2 не уйдёт");
                }

                string[] jsonNames = { "manifest.json", "content_info.json", "component_map.json" };
                foreach (var jsonName in jsonNames)
                {
                    var jsonPath = Path.Combine(spec.TempWorkDir, "_upload", jsonName);
                    if (!File.Exists(jsonPath)) continue;
                    var nameCapt = jsonName;
                    plan.Add((jsonPath, $"{baseKey}/{nameCapt}", url =>
                    {
                        if      (nameCapt == "manifest.json")        r2.Manifest     = url;
                        else if (nameCapt == "component_map.json")   r2.ComponentMap = url;
                        else if (nameCapt == "content_info.json")    r2.ContentInfo  = url;
                    }));
                }

                foreach (var (local, key, apply) in plan)
                {
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    var fileSize = new FileInfo(local).Length;
                    var fileStart = uploadedSoFar;
                    var fileProgress = new Progress<int>(percent =>
                    {
                        var fileBytes = (long)((percent / 100.0) * fileSize);
                        var total = fileStart + fileBytes;
                        var aggregate = totalBytes > 0
                            ? (int)(40 + (total * 50 / totalBytes))
                            : 90;
                        UpdateAsync(item, x => x.Percent = Math.Min(90, aggregate), emit).GetAwaiter().GetResult();
                    });
                    var url = await _storage.UploadAsync(local, key, fileProgress, ct);
                    apply(url);
                    uploadedSoFar += fileSize;
                }

                slotUrls[spec.Slot] = r2;
            }
        }

        await UpdateAsync(item, x => x.Percent = 90, emit);

        await UpdateAsync(item, x => { x.CurrentPhase = "registering"; x.Percent = 95; }, emit);

        var snapshot = item.Metadata;

        var slot1ShortcutUrls = new R2UrlsLocal();
        if (slotUrls.TryGetValue(1, out var s1) && s1 is not null)
        {

            foreach (var kv in s1.Components)
            {
                if (kv.Key is "armor_glb")
                {
                    slot1ShortcutUrls.Components[kv.Key] = kv.Value;
                }
            }
        }
        snapshot.R2Urls           = slot1ShortcutUrls;

        var firstSlot = slotArtifacts.OrderBy(x => x.Spec.Slot).FirstOrDefault();
        var firstPatchBytes = !string.IsNullOrWhiteSpace(firstSlot.PatchZip) && File.Exists(firstSlot.PatchZip)
            ? new FileInfo(firstSlot.PatchZip).Length
            : 0;
        snapshot.PatchSizeBytes   = firstPatchBytes;
        snapshot.PatchSha256      = string.Empty;
        snapshot.TargetGtaVersion = string.Empty;
        snapshot.Components       = AggregateComponents(item.Versions);
        snapshot.UploadedAt       = DateTime.UtcNow;
        snapshot.Status           = "published";
        Debug.WriteLine($"[admin.build.mv] parent display size = slot {firstSlot.Spec?.Slot ?? 0} patch.zip ({firstPatchBytes / 1024 / 1024} MB)");
        if (appendMode)
        {
            List<ReduxVersion> existingVersions;
            try { existingVersions = await _versions.ListByReduxAsync(reduxId); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[admin.build.mv.append] WARN: versions list FAIL for {reduxId}: {ex.Message}. Aggregating only the new version.");
                existingVersions = new();
            }
            var unionStubs = existingVersions
                .Select(v => new VersionSpec { Components = v.Components ?? new() })
                .Concat(item.Versions!)
                .ToList();
            var mergedComponents = AggregateComponents(unionStubs);
            await _catalog.UpdateAsync(reduxId, r => r.Components = mergedComponents);
            Debug.WriteLine($"[admin.build.mv.append] catalog row '{reduxId}' components merged ({mergedComponents.Count} keys)");
        }
        else
        {
            await _catalog.AddAsync(snapshot);
        }

        foreach (var (spec, patchZip, components, patchSha) in slotArtifacts)
        {

            var sizeOnDisk = File.Exists(patchZip) ? new FileInfo(patchZip).Length : spec.SizeBytes;
            var urls = slotUrls.TryGetValue(spec.Slot, out var r2u) ? r2u : new R2UrlsLocal();
            var version = new ReduxVersion
            {
                Id               = Guid.Empty,
                ReduxId          = reduxId,
                Slot             = spec.Slot,
                Label            = spec.Label,
                PatchUrl         = urls.Patch,
                PatchSizeBytes   = sizeOnDisk,
                PatchSha256      = string.IsNullOrEmpty(patchSha) ? null : patchSha,
                SourceSha256     = string.IsNullOrEmpty(spec.SourceSha256) ? null : spec.SourceSha256,
                TargetGtaVersion = spec.TargetGtaVersion,
                Components       = spec.Components,
                ComponentUrls    = new Dictionary<string, string>(urls.Components),
                ManifestUrl      = urls.Manifest,
                ComponentMapUrl  = urls.ComponentMap,
                ContentInfoUrl   = urls.ContentInfo,
            };
            await _versions.UpsertAsync(version);
            Debug.WriteLine($"[admin.build.mv] registered slot {spec.Slot} '{spec.Label}' sha={patchSha}");
        }

        await UpdateAsync(item, x => { x.Status = "done"; x.CurrentPhase = null; x.Percent = 100; }, emit);

        foreach (var (spec, _, _, _) in slotArtifacts)
        {
            try
            {
                var bo = Path.Combine(spec.TempWorkDir, "_upload");
                if (Directory.Exists(bo)) Directory.Delete(bo, recursive: true);
            }
            catch {  }
        }
    }

    public async Task<int> RebuildComponentsIndexAsync()
    {
        var all = await _catalog.ListAsync(null);
        int updated = 0;
        foreach (var redux in all)
        {
            List<ReduxVersion> versions;
            try { versions = await _versions.ListByReduxAsync(redux.Id); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[admin.components.backfill] versions list FAIL for {redux.Id}: {ex.Message}");
                continue;
            }

            var stubs = versions.Select(v => new VersionSpec { Components = v.Components ?? new() }).ToList();
            var aggregated = AggregateComponents(stubs);

            if (aggregated.Count == 0 && (redux.Components is null || redux.Components.Count == 0))
                continue;
            if (ComponentsEqual(redux.Components, aggregated))
                continue;

            try
            {
                await _catalog.UpdateAsync(redux.Id, r => r.Components = aggregated);
                updated++;
                Debug.WriteLine($"[admin.components.backfill] {redux.Id}: components rebuilt ({aggregated.Count} keys)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[admin.components.backfill] update FAIL for {redux.Id}: {ex.Message}");
            }
        }
        Debug.WriteLine($"[admin.components.backfill] DONE - {updated}/{all.Count} rows updated");
        return updated;
    }

    public async Task<int> RecalculatePatchSizesAsync(CancellationToken ct)
    {
        using var http = HttpClientFactory.CreateFragmenting();
        var all = await _catalog.ListAsync(null);
        var updated = 0;

        foreach (var redux in all)
        {
            ct.ThrowIfCancellationRequested();

            List<ReduxVersion> versions;
            try { versions = await _versions.ListByReduxAsync(redux.Id); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[admin.patch-size.recalc] versions list FAIL for {redux.Id}: {ex.Message}");
                continue;
            }

            if (versions.Count > 0)
            {
                long? slotOneSize = null;
                foreach (var version in versions.OrderBy(v => v.Slot))
                {
                    var info = await TryFetchPatchShaAndSizeAsync(http, version.PatchUrl, ct);
                    if (info is null)
                    {
                        Debug.WriteLine($"[admin.patch-recalc] skip {redux.Id}/v{version.Slot}: no patch for '{version.PatchUrl}'");
                        continue;
                    }
                    var (sha, size) = info.Value;

                    if (version.Slot == 1)
                        slotOneSize = size;

                    var sizeChanged = version.PatchSizeBytes != size;
                    var shaChanged  = !string.Equals(version.PatchSha256 ?? string.Empty, sha, StringComparison.OrdinalIgnoreCase);
                    if (sizeChanged || shaChanged)
                    {
                        version.PatchSizeBytes = size;
                        version.PatchSha256    = sha;
                        await _versions.UpsertAsync(version);
                        updated++;
                        Debug.WriteLine($"[admin.patch-recalc] version {redux.Id}/v{version.Slot}: size={size:N0} sizeChanged={sizeChanged} shaChanged={shaChanged}");
                    }
                }

                if (slotOneSize is not null && redux.PatchSizeBytes != slotOneSize.Value)
                {
                    await _catalog.UpdateAsync(redux.Id, r => r.PatchSizeBytes = slotOneSize.Value);
                    updated++;
                    Debug.WriteLine($"[admin.patch-size.recalc] parent {redux.Id}: {slotOneSize.Value:N0} bytes from slot 1");
                }

                continue;
            }

            var patchUrl = redux.R2Urls?.Patch;
            var singleSize = await TryGetContentLengthAsync(http, patchUrl, ct);
            if (singleSize is null)
            {
                Debug.WriteLine($"[admin.patch-size.recalc] skip {redux.Id}: no patch size for '{patchUrl}'");
                continue;
            }

            if (redux.PatchSizeBytes != singleSize.Value)
            {
                await _catalog.UpdateAsync(redux.Id, r => r.PatchSizeBytes = singleSize.Value);
                updated++;
                Debug.WriteLine($"[admin.patch-size.recalc] parent {redux.Id}: {singleSize.Value:N0} bytes");
            }
        }

        Debug.WriteLine($"[admin.patch-size.recalc] DONE - {updated} row(s) updated");
        return updated;
    }

    private static async Task<long?> TryGetContentLengthAsync(HttpClient http, string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var effectiveUrl = await MirrorSelector.RewriteUrlAsync(url, ct);
            using var req = new HttpRequestMessage(HttpMethod.Head, effectiveUrl);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[admin.patch-size.recalc] HEAD {effectiveUrl} -> {(int)resp.StatusCode}");
                return null;
            }
            return resp.Content.Headers.ContentLength;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"[admin.patch-size.recalc] HEAD FAIL {url}: {ex.Message}");
            return null;
        }
    }

    private static async Task<(string Sha, long Size)?> TryFetchPatchShaAndSizeAsync(HttpClient http, string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var effectiveUrl = await MirrorSelector.RewriteUrlAsync(url, ct);
            using var resp = await http.GetAsync(effectiveUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[admin.patch-recalc] GET {effectiveUrl} -> {(int)resp.StatusCode}");
                return null;
            }
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var ih = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buf = new byte[1 << 16];
            long total = 0; int read;
            while ((read = await stream.ReadAsync(buf, ct)) > 0)
            {
                ih.AppendData(buf, 0, read);
                total += read;
            }
            var sha = Convert.ToHexString(ih.GetHashAndReset()).ToLowerInvariant();
            return (sha, total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Debug.WriteLine($"[admin.patch-recalc] sha/size FAIL {url}: {ex.Message}");
            return null;
        }
    }

    private static bool ComponentsEqual(
        Dictionary<string, ReduxComponentInfo>? a,
        Dictionary<string, ReduxComponentInfo>  b)
    {
        if (a is null || a.Count != b.Count) return false;
        foreach (var (k, vb) in b)
        {
            if (!a.TryGetValue(k, out var va)) return false;
            if (va.IsFound != vb.IsFound) return false;
            if (!new HashSet<string>(va.Flags ?? new(), StringComparer.OrdinalIgnoreCase)
                  .SetEquals(vb.Flags ?? new())) return false;
        }
        return true;
    }

    private static Dictionary<string, ReduxComponentInfo> AggregateComponents(List<VersionSpec>? versions)
    {
        var agg = new Dictionary<string, ReduxComponentInfo>(StringComparer.OrdinalIgnoreCase);
        if (versions is null) return agg;
        foreach (var v in versions)
        {
            if (v.Components is null) continue;
            foreach (var (name, info) in v.Components)
            {
                if (info is null) continue;
                if (!agg.TryGetValue(name, out var rolled))
                {
                    rolled = new ReduxComponentInfo
                    {
                        IsFound       = info.IsFound,
                        SourceRpf     = info.SourceRpf ?? string.Empty,
                        InternalPaths = new List<string>(info.InternalPaths ?? Enumerable.Empty<string>()),
                        Flags         = new List<string>(info.Flags ?? Enumerable.Empty<string>()),
                    };
                    agg[name] = rolled;
                    continue;
                }
                rolled.IsFound = rolled.IsFound || info.IsFound;
                if (string.IsNullOrEmpty(rolled.SourceRpf) && !string.IsNullOrEmpty(info.SourceRpf))
                    rolled.SourceRpf = info.SourceRpf;
                foreach (var f in info.Flags ?? Enumerable.Empty<string>())
                    if (!rolled.Flags.Contains(f, StringComparer.OrdinalIgnoreCase))
                        rolled.Flags.Add(f);
                foreach (var p in info.InternalPaths ?? Enumerable.Empty<string>())
                    if (!rolled.InternalPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                        rolled.InternalPaths.Add(p);
            }
        }
        return agg;
    }

    private async Task UpdateAsync(QueueItem item, Action<QueueItem> mutate, Action<QueueItem>? emit)
    {
        await _lock.WaitAsync();
        try
        {
            var all = await LoadAsync();
            var existing = all.FirstOrDefault(x => x.TempId == item.TempId);
            if (existing is null) return;
            mutate(existing);
            mutate(item);
            await SaveAsync(all);
            emit?.Invoke(existing);
        }
        finally { _lock.Release(); }
    }

    private static string ComputeFileSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: false);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<List<QueueItem>> LoadAsync()
    {
        if (!File.Exists(_filePath)) return new List<QueueItem>();
        try
        {
            await using var fs = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<List<QueueItem>>(fs, Json) ?? new();
        }
        catch
        {
            return new List<QueueItem>();
        }
    }

    private async Task SaveAsync(List<QueueItem> items)
    {
        await using var fs = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(fs, items, Json);
    }
}
