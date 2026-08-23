using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using MiamiGraphics.Bridge;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.System;
using MiamiGraphics.Shell.Admin;

namespace MiamiGraphics.Shell.Services;

public sealed class GunpackInstaller
{
    public const string PROGRESS_EVENT = "gunpack:installProgress";

    private const string TARGET_DLC_REL_PATH = @"update\x64\dlcpacks\patchday18ng\dlc.rpf";

    private const string MIAMI_WEAPON_RPF_NAME = "miami_weapon.rpf";

    private readonly IGunpackRepository _packs;
    private readonly IRemoteStorage _storage;
    private readonly IAdminConfigService _adminConfig;
    private readonly Services.SupabaseClient _supabase;
    private readonly ISelectedGunsInstaller _selectedGuns;

    private readonly string _stateFilePath;
    private readonly SemaphoreSlim _stateLock = new(1, 1);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public GunpackInstaller(
        IGunpackRepository packs,
        IRemoteStorage storage,
        IAdminConfigService adminConfig,
        Services.SupabaseClient supabase,
        ISelectedGunsInstaller selectedGuns)
    {
        _packs = packs;
        _storage = storage;
        _adminConfig = adminConfig;
        _supabase = supabase;
        _selectedGuns = selectedGuns;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "MiamiGraphics", "admin");
        Directory.CreateDirectory(dir);
        _stateFilePath = Path.Combine(dir, "installed_gunpack.json");
    }

    public delegate void EmitProgress(string phase, int percent, string? errorMessage, string? detailMessage);

    private static ISelectedGunsInstaller.EmitProgress BandedRebuildEmit(
        EmitProgress outer, int from, int to)
        => (phase, percent, errorMessage, detailMessage) =>
        {
            if (string.Equals(phase, "error", StringComparison.Ordinal)) return;
            if (string.Equals(phase, "done", StringComparison.Ordinal))
            {
                outer("installing", to, null, detailMessage);
                return;
            }
            var mapped = percent < 0
                ? percent
                : from + (int)Math.Round((to - from) * Math.Clamp(percent, 0, 100) / 100.0);
            outer(phase, mapped, errorMessage, detailMessage);
        };

    public async Task<InstalledGunpackState> GetInstalledStateAsync()
    {
        await _stateLock.WaitAsync();
        try { return await ReadStateAsync(); }
        finally { _stateLock.Release(); }
    }

    private static async Task<JsonElement?> ReadSelectedGunsStateRawAsync()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(appData, "MiamiGraphics", "admin", "selected_guns.json");
        if (!File.Exists(path)) return null;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gunpack.reconcile] selected_guns.json read FAIL: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> IsInstalledAsync(string gunpackId)
    {
        if (string.IsNullOrWhiteSpace(gunpackId)) return false;
        var s = await GetInstalledStateAsync();
        return string.Equals(s.ActiveGunpackId, gunpackId, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> ReconcileStateAsync(string gtaPath)
    {
        var state = await GetInstalledStateAsync();
        var target = Path.Combine(gtaPath, TARGET_DLC_REL_PATH);
        var template = Path.Combine(
            MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir, "backup", "dlc.rpf");

        bool stateClaimsInstalled = !string.IsNullOrEmpty(state.ActiveGunpackId);
        bool dlcExists            = File.Exists(target);

        bool? weaponInsideTri = dlcExists
            ? TargetDlcEditor.TryRpfExistsInsideTarget(target, TargetDlcEditor.MIAMI_WEAPON_RPF_NAME)
            : false;
        if (stateClaimsInstalled && dlcExists && weaponInsideTri is null)
        {
            Debug.WriteLine("[gunpack.reconcile] target DLC unreadable (locked by running game?) - leaving state intact");
            return false;
        }
        bool weaponInside = weaponInsideTri == true;

        var selectedStateRaw = await ReadSelectedGunsStateRawAsync();
        bool hasSelections = selectedStateRaw is not null && selectedStateRaw.Value
            .TryGetProperty("guns", out var gunsEl) && gunsEl.ValueKind == JsonValueKind.Array && gunsEl.GetArrayLength() > 0;

        bool drift = false;
        string driftReason = string.Empty;

        if (stateClaimsInstalled && !dlcExists)
        {
            drift = true;
            driftReason = "state claims installed but target DLC missing on disk";
        }
        else if (stateClaimsInstalled && !weaponInside)
        {
            drift = true;
            driftReason = "state claims installed but miami_weapon.rpf missing inside DLC";
        }
        else if (stateClaimsInstalled && !hasSelections && !string.IsNullOrEmpty(state.WeaponsRpfSha256))
        {
            var actualSha = TargetDlcEditor.ComputeEmbeddedRpfSha256(target, TargetDlcEditor.MIAMI_WEAPON_RPF_NAME);
            if (!string.Equals(actualSha, state.WeaponsRpfSha256, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[gunpack.reconcile] embedded weapon.rpf SHA differs from stored pack SHA " +
                    $"(state={state.WeaponsRpfSha256?[..Math.Min(8, state.WeaponsRpfSha256.Length)]} " +
                    $"actual={actualSha?[..Math.Min(8, actualSha?.Length ?? 1)]}) - re-packed rpf, treating as installed (NOT drift).");
            }
        }
        else if (stateClaimsInstalled && hasSelections)
        {
            Debug.WriteLine($"[gunpack.reconcile] selections active - skipping weapon.rpf SHA check (merged rpf, expected to differ from original).");
        }

        if (!drift)
        {
            Debug.WriteLine($"[gunpack.reconcile] state matches disk (installed={stateClaimsInstalled})");
            return false;
        }

        Debug.WriteLine($"[gunpack.reconcile] DRIFT detected - {driftReason}. Resetting state and restoring clean DLC.");
        await SaveStateAsync(new InstalledGunpackState());

        if (File.Exists(template))
        {
            try
            {
                TargetDlcEditor.RebuildFromTemplate(template, target, null, null);
                Debug.WriteLine($"[gunpack.reconcile] target DLC restored to clean template");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[gunpack.reconcile] restore-from-template FAIL: {ex.Message}");
            }
        }
        else if (dlcExists)
        {
            try
            {
                TargetDlcEditor.DeleteTargetDlc(target);
                Debug.WriteLine($"[gunpack.reconcile] no template cached → deleted target DLC");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[gunpack.reconcile] delete failed: {ex.Message}");
            }
        }
        return true;
    }

    public async Task<GunpackVerifyReport> VerifyInstalledAsync(string gtaPath)
    {
        var state = await GetInstalledStateAsync();
        var target = Path.Combine(gtaPath, TARGET_DLC_REL_PATH);

        if (string.IsNullOrEmpty(state.ActiveGunpackId))
            return new GunpackVerifyReport(true, false, false, null, null, Loc.T("verify.nothingInstalled"));

        if (!File.Exists(target))
            return new GunpackVerifyReport(false, true, false, state.WeaponsRpfSha256, null,
                Loc.T("verify.packInStateButTargetDlcMissing"));

        var present = TargetDlcEditor.RpfExistsInsideTarget(target, MIAMI_WEAPON_RPF_NAME);
        if (!present)
            return new GunpackVerifyReport(false, true, false, state.WeaponsRpfSha256, null,
                Loc.T("verify.weaponRpfMissingInDlc"));

        var actual = TargetDlcEditor.ComputeEmbeddedRpfSha256(target, MIAMI_WEAPON_RPF_NAME);
        var ok = string.Equals(actual, state.WeaponsRpfSha256, StringComparison.OrdinalIgnoreCase);
        return new GunpackVerifyReport(
            ok, true, true, state.WeaponsRpfSha256, actual,
            ok ? Loc.T("verify.installedPackIntact")
               : Loc.T("verify.packShaMismatch"));
    }

    public async Task<InjectResultDto> InstallFullAsync(
        string  gunpackId,
        string  gtaPath,
        string  exeVersion,
        EmitProgress emit,
        CancellationToken ct,
        Guid? variantId = null)
    {
        if (string.IsNullOrWhiteSpace(gunpackId))
            return Fail(Loc.T("error.gunpackIdMissing"), emit);
        if (string.IsNullOrWhiteSpace(gtaPath) || !Directory.Exists(gtaPath))
            return Fail(Loc.T("error.gtaNotFoundAdminSettingsPaths"), emit);

        emit("starting", 0, null, null);

        var pack = await _packs.GetByIdAsync(gunpackId);
        if (pack is null) return Fail(Loc.T("error.packNotInCatalog", ("id", gunpackId)), emit);

        string  weaponsUrl    = pack.WeaponsRpfUrl;
        long    weaponsSize   = pack.WeaponsRpfSize;
        string  weaponsSha256 = pack.WeaponsRpfSha256;
        string  variantLabel  = string.Empty;
        var packVariants = await _packs.ListVariantsAsync(gunpackId);
        if (packVariants.Count > 0)
        {
            Admin.GunpackVariant? chosen = null;
            if (variantId is Guid vid && vid != Guid.Empty)
                chosen = packVariants.FirstOrDefault(v => v.Id == vid);
            chosen ??= packVariants.FirstOrDefault(v => v.IsDefault) ?? packVariants[0];

            weaponsUrl    = chosen.WeaponsRpfUrl;
            weaponsSize   = chosen.WeaponsRpfSize;
            weaponsSha256 = chosen.WeaponsRpfSha256;
            variantLabel  = chosen.Name;
            Debug.WriteLine($"[gunpack.install] variant chosen: id={chosen.Id} name='{chosen.Name}' isDefault={chosen.IsDefault}");
        }

        if (string.IsNullOrWhiteSpace(weaponsUrl))
            return Fail(Loc.T("error.packVariantNoWeaponRpfUrl"), emit);
        if (string.IsNullOrWhiteSpace(weaponsSha256))
            return Fail(Loc.T("error.packVariantNoWeaponRpfSha"), emit);

        var displayName = string.IsNullOrEmpty(pack.Name) ? gunpackId : pack.Name;
        if (!string.IsNullOrEmpty(variantLabel)) displayName = $"{displayName} ({variantLabel})";

        emit("resolving_version", 5, null, Loc.T("install.gtaVersion", ("version", exeVersion)));
        var (templateUrlFromDb, templateSha, templateSize, templateMetaOk) = await TryGetTemplateMetaAsync();
        if (!templateMetaOk)
            return Fail(Loc.T("error.catalogMetaUnavailableForTemplate"), emit);
        if (string.IsNullOrEmpty(templateSha))
            return Fail(Loc.T("error.templateShaMissingInCatalog"), emit);

        var templatePath = Path.Combine(
            MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir, "backup", "dlc.rpf");

        var cfg = await _adminConfig.GetAsync();
        var r2Public = (cfg.R2PublicUrl ?? string.Empty).TrimEnd('/');

        const string CANONICAL_VERSION = "1.0.3751.0";
        var templateUrl = !string.IsNullOrWhiteSpace(templateUrlFromDb)
            ? templateUrlFromDb!
            : (!string.IsNullOrEmpty(r2Public)
                ? $"{r2Public}/gta_versions/{CANONICAL_VERSION}/guns.rpf"
                : null);
        if (string.IsNullOrEmpty(templateUrl))
            return Fail(Loc.T("error.cleanDlcTemplateNotConfigured"), emit);

        Debug.WriteLine($"[gunpack.install] template URL = {templateUrl} sha={(string.IsNullOrEmpty(templateSha) ? "-" : templateSha[..8])} size={templateSize}");

        try
        {
            await EnsureCachedAsync(
                url: templateUrl, destPath: templatePath,
                expectedSha: templateSha, expectedSize: templateSize,
                phase: "downloading_template", phasePctStart: 8, phasePctEnd: 28,
                fileLabel: Loc.T("misc.cleanPatchday18ngDlcRpf"),
                emit: emit, ct: ct);
        }
        catch (Exception ex)
        {
            return Fail(Loc.T("error.cleanDlcTemplateDownloadFailed", ("url", templateUrl), ("reason", ex.Message)), emit);
        }

        var packDir = Path.Combine(
            MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir,
            "Gunpacks", $"install-{SanitiseId(gunpackId)}");
        Directory.CreateDirectory(packDir);
        PurgeStaleInstallDirs(packDir);
        var miamiRpfPath = Path.Combine(packDir, MIAMI_WEAPON_RPF_NAME);

        try
        {
            await EnsureCachedAsync(
                url: weaponsUrl, destPath: miamiRpfPath,
                expectedSha: weaponsSha256, expectedSize: weaponsSize,
                phase: "downloading_pack", phasePctStart: 28, phasePctEnd: 60,
                fileLabel: Loc.T("misc.weaponRpfOfPack", ("pack", displayName)),
                emit: emit, ct: ct);
        }
        catch (Exception ex)
        {
            return Fail(Loc.T("error.weaponRpfDownloadFailed", ("reason", ex.Message)), emit);
        }

        emit("installing", 62, null, Loc.T("install.preparingDlcContents"));
        Debug.WriteLine($"[gunpack.install] fresh weapon.rpf cached at {miamiRpfPath} ({new FileInfo(miamiRpfPath).Length:N0} bytes)");

        emit("registering", 70, null, null);
        var oldState = await ReadStateAsync();
        await SaveStateAsync(new InstalledGunpackState
        {
            ActiveGunpackId   = pack.Id,
            ActiveGunpackName = pack.Name,
            WeaponsRpfSha256  = weaponsSha256,
            InstalledAt       = DateTime.UtcNow,
        });

        var rebuildEmit = BandedRebuildEmit(emit, 72, 97);
        var targetDlc = Path.Combine(gtaPath, TARGET_DLC_REL_PATH);
        InjectResultDto rebuildResult;
        try
        {
            rebuildResult = await _selectedGuns.RebuildAsync(rebuildEmit, ct);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gunpack.install] delegated rebuild CRASH: {ex}");
            await TryRestoreStateAsync(oldState, "rebuild-crash");
            return Fail(Loc.T("error.targetDlcRebuildFailed", ("reason", ex.Message)), emit);
        }
        if (!rebuildResult.Success)
        {
            await TryRestoreStateAsync(oldState, "rebuild-fail");
            return Fail(rebuildResult.ErrorMessage ?? Loc.T("error.dlcBuildFailed"), emit);
        }

        try { await _packs.IncrementDownloadsAsync(gunpackId); }
        catch (Exception ex) { Debug.WriteLine($"[gunpack.install] download_count bump FAIL: {ex.Message}"); }

        emit("done", 100, null, Loc.T("install.gunpackInstalled", ("pack", displayName)));
        return new InjectResultDto(true, Loc.T("install.gunpackInstalledDot", ("pack", displayName)), targetDlc);
    }

    public async Task<bool> UninstallAsync(string gtaPath, EmitProgress emit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(gtaPath) || !Directory.Exists(gtaPath))
        {
            emit("error", 0, Loc.T("error.gtaNotFoundShort"), null);
            return false;
        }
        emit("starting", 0, null, null);

        var targetDlc = Path.Combine(gtaPath, TARGET_DLC_REL_PATH);

        var templatePath = Path.Combine(
            MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir, "backup", "dlc.rpf");
        if (!File.Exists(templatePath))
        {
            Debug.WriteLine("[gunpack.uninstall] no cached template - falling back to plain DLC delete");
            try
            {
                if (File.Exists(targetDlc)) TargetDlcEditor.DeleteTargetDlc(targetDlc);
            }
            catch (Exception ex)
            {
                emit("error", 0, Loc.T("error.targetDlcDeleteFailed", ("reason", ex.Message)), null);
                return false;
            }
            await SaveStateAsync(new InstalledGunpackState());
            emit("done", 100, null, Loc.T("install.targetDlcDeletedNoTemplate"));
            return true;
        }

        emit("restoring", 30, null, Loc.T("install.preparingDlcWithoutGunpack"));

        await SaveStateAsync(new InstalledGunpackState());

        var rebuildEmit = BandedRebuildEmit(emit, 32, 97);
        InjectResultDto rebuildResult;
        try
        {
            rebuildResult = await _selectedGuns.RebuildAsync(rebuildEmit, ct);
        }
        catch (Exception ex)
        {
            emit("error", 0, Loc.T("error.dlcRebuildFailed", ("reason", ex.Message)), null);
            return false;
        }
        if (!rebuildResult.Success)
        {
            emit("error", 0, rebuildResult.ErrorMessage ?? Loc.T("error.dlcBuildFailed"), null);
            return false;
        }

        emit("done", 100, null, Loc.T("install.gunpackRemovedDlcRebuilt"));
        return true;
    }

    private async Task SaveStateAsync(InstalledGunpackState state)
    {

        await _stateLock.WaitAsync();
        try
        {
            var tmp = _stateFilePath + ".tmp";
            await using (var fs = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(fs, state, Json);
                await fs.FlushAsync();
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, _stateFilePath, overwrite: true);
        }
        finally { _stateLock.Release(); }
    }

    private async Task TryRestoreStateAsync(InstalledGunpackState old, string reason)
    {
        try
        {
            await SaveStateAsync(old);
            Debug.WriteLine($"[gunpack.state] rolled back to old state (reason={reason}, activeId={old.ActiveGunpackId})");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gunpack.state] rollback FAIL: {ex.Message}");
        }
    }

    private async Task<InstalledGunpackState> ReadStateAsync()
    {
        if (!File.Exists(_stateFilePath)) return new InstalledGunpackState();
        try
        {
            await using var fs = File.OpenRead(_stateFilePath);
            return await JsonSerializer.DeserializeAsync<InstalledGunpackState>(fs, Json)
                   ?? new InstalledGunpackState();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gunpack.state] read FAIL: {ex.Message} - starting empty");
            return new InstalledGunpackState();
        }
    }

    private static void PurgeStaleInstallDirs(string keepDir)
    {
        try
        {
            var root = Path.GetDirectoryName(keepDir);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

            foreach (var dir in Directory.EnumerateDirectories(root, "install-*"))
            {
                if (string.Equals(Path.GetFullPath(dir), Path.GetFullPath(keepDir),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    Directory.Delete(dir, recursive: true);
                    Debug.WriteLine($"[gunpack.install] убрал брошенную папку установки {Path.GetFileName(dir)}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[gunpack.install] не смог убрать {Path.GetFileName(dir)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gunpack.install] обход папок установок не удался: {ex.Message}");
        }
    }

    private static void EmitMirrorSwitch(EmitProgress emit, string phase, int phasePctStart,
                                         string fileLabel, string candidate, int index, int count)
    {
        var host = Uri.TryCreate(candidate, UriKind.Absolute, out var u) ? u.Host : candidate;
        emit(phase, phasePctStart, null, Loc.T("install.fileMirrorSwitch",
            ("file", fileLabel), ("host", host), ("next", Math.Min(index + 2, count)), ("total", count)));
    }

    private static async Task EnsureCachedAsync(
        string url, string destPath, string expectedSha, long expectedSize,
        string phase, int phasePctStart, int phasePctEnd, string fileLabel,
        EmitProgress emit, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        VerifiedDownload.RequireReference(expectedSha, fileLabel);

        if (File.Exists(destPath))
        {
            try
            {
                if (expectedSize > 0 && new FileInfo(destPath).Length != expectedSize)
                {
                    Debug.WriteLine($"[gunpack.cache] {fileLabel}: size mismatch - refetching");
                }
                else
                {
                    var actualSha = await Task.Run(() => VerifiedDownload.ComputeSha256(destPath), ct);
                    if (VerifiedDownload.Matches(expectedSha, actualSha))
                    {
                        emit(phase, phasePctEnd, null, Loc.T("install.fileCacheOk", ("file", fileLabel)));
                        return;
                    }
                    Debug.WriteLine($"[gunpack.cache] {fileLabel}: SHA mismatch (have {actualSha[..8]}, want {expectedSha[..8]}) - refetching");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[gunpack.cache] {fileLabel}: probe failed ({ex.Message}) - refetching");
            }
        }

        emit(phase, phasePctStart, null, Loc.T("install.fileDownloading", ("file", fileLabel)));
        var partPath = destPath + ".part";
        if (File.Exists(partPath)) { try { File.Delete(partPath); } catch { } }

        var candidates = Bridge.AppBridge.BuildDownloadCandidates(url);
        try
        {
            var probed = await MirrorSelector.RewriteUrlAsync(url, ct);
            if (!string.IsNullOrWhiteSpace(probed))
            {
                candidates.Remove(probed);
                candidates.Insert(0, probed);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[gunpack.cache] mirror probe failed: {ex.Message} - keeping static order"); }
        using var http = HttpClientFactory.CreateFragmenting(TimeSpan.FromMinutes(20));
        Exception? lastErr = null;

        var hardCapMinutes = expectedSize > 0
            ? Math.Clamp(expectedSize / (30.0 * 1024 * 60), 25, 120)
            : 25;
        for (int pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
                emit(phase, phasePctStart, null, Loc.T("install.fileAllMirrorsSlow", ("file", fileLabel)));

            for (int ci = 0; ci < candidates.Count; ci++)
            {
                var candidate = candidates[ci];
                using var bodyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                bodyCts.CancelAfter(TimeSpan.FromMinutes(hardCapMinutes));
                long receivedSoFar = 0;
                bool slowStartTripped = false;
                if (pass == 1 && ci < candidates.Count - 1)
                {
                    var wd = bodyCts.Token;
                    _ = Task.Run(async () =>
                    {
                        try { await Task.Delay(TimeSpan.FromMinutes(2), wd); } catch { return; }
                        if (Volatile.Read(ref receivedSoFar) >= (1L << 20)) return;
                        Volatile.Write(ref slowStartTripped, true);
                        try { bodyCts.Cancel(); } catch { }
                    });
                }
                try
                {
                    if (File.Exists(partPath)) { try { File.Delete(partPath); } catch { } }
                    using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    headerCts.CancelAfter(TimeSpan.FromSeconds(90));
                    using var resp = await http.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, headerCts.Token);
                    resp.EnsureSuccessStatusCode();
                    var total = resp.Content.Headers.ContentLength ?? expectedSize;
                    await using (var src = await resp.Content.ReadAsStreamAsync(ct))
                    await using (var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
                    {
                        long lastEmit = 0;
                        await GuardedDownload.CopyAsync(
                            src, dst,
                            onBytes: received =>
                            {
                                Volatile.Write(ref receivedSoFar, received);
                                if (received - lastEmit >= (1 << 19) && total > 0)
                                {
                                    lastEmit = received;
                                    var span = phasePctEnd - phasePctStart;
                                    var pct = phasePctStart + (int)(received * span / Math.Max(1, total));
                                    if (pct >= phasePctEnd) pct = phasePctEnd - 1;
                                    emit(phase, pct, null, Loc.T("install.fileDownloadMb", ("file", fileLabel), ("done", received / (1024 * 1024)), ("total", (total > 0 ? total : received) / (1024 * 1024))));
                                }
                            },
                            ct: bodyCts.Token,
                            throttleGuard: pass == 0,
                            expectedTotal: total);
                    }

                    if (expectedSize > 0 && new FileInfo(partPath).Length != expectedSize)
                        throw new IOException($"size {new FileInfo(partPath).Length} != expected {expectedSize}");
                    var dlSha = await Task.Run(() => VerifiedDownload.ComputeSha256(partPath), ct);
                    if (!VerifiedDownload.Matches(expectedSha, dlSha))
                        throw new InvalidOperationException(Loc.T("error.sha256Mismatch", ("got", dlSha[..16])));

                    if (File.Exists(destPath)) { try { File.Delete(destPath); } catch { } }
                    File.Move(partPath, destPath);
                    emit(phase, phasePctEnd, null, Loc.T("install.fileDone", ("file", fileLabel)));
                    return;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (OperationCanceledException) when (bodyCts.IsCancellationRequested)
                {
                    var tripped = Volatile.Read(ref slowStartTripped);
                    lastErr = tripped
                        ? new IOException(Loc.T("error.mirrorNoProgress2Min"))
                        : new IOException(Loc.T("error.downloadExceededMinutes", ("minutes", hardCapMinutes.ToString("F0"))));
                    Debug.WriteLine($"[gunpack.cache] {fileLabel}: candidate {candidate} {(tripped ? "no progress in 2 min" : $"hit {hardCapMinutes:F0}-min hard cap")} (pass {pass})");
                    try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }
                    EmitMirrorSwitch(emit, phase, phasePctStart, fileLabel, candidate, ci, candidates.Count);
                }
                catch (OperationCanceledException)
                {
                    lastErr = new IOException(Loc.T("error.mirrorNoResponse90Sec"));
                    Debug.WriteLine($"[gunpack.cache] {fileLabel}: candidate {candidate} header timeout (pass {pass})");
                    try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }
                    EmitMirrorSwitch(emit, phase, phasePctStart, fileLabel, candidate, ci, candidates.Count);
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                    Debug.WriteLine($"[gunpack.cache] {fileLabel}: candidate {candidate} failed (pass {pass}): {ex.GetType().Name}: {ex.Message}");
                    try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }
                    EmitMirrorSwitch(emit, phase, phasePctStart, fileLabel, candidate, ci, candidates.Count);
                }
            }
        }

        throw new InvalidOperationException(
            Loc.T("error.fileDownloadAllMirrorsFailed", ("file", fileLabel), ("reason", lastErr?.Message)));
    }

    private async Task<(string? url, string sha, long size, bool lookupOk)> TryGetTemplateMetaAsync()
    {
        try
        {
            var rows = await _supabase.SelectAsync<TemplateRow>(
                "gta_versions",
                "select=guns_rpf_url,guns_rpf_sha256,guns_rpf_size&guns_rpf_url=not.is.null&limit=1");
            var row = rows.FirstOrDefault();
            if (row is null) return (null, string.Empty, 0, true);
            return (
                string.IsNullOrWhiteSpace(row.GunsRpfUrl) ? null : row.GunsRpfUrl,
                row.GunsRpfSha256 ?? string.Empty,
                row.GunsRpfSize ?? 0,
                true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gunpack.install] gta_versions lookup failed: {ex.Message}");
            return (null, string.Empty, 0, false);
        }
    }

    private static string SanitiseId(string id)
    {
        var sb = new System.Text.StringBuilder(id.Length);
        foreach (var c in id)
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        return sb.ToString();
    }

    private static InjectResultDto Fail(string message, EmitProgress emit)
    {
        emit("error", 0, message, null);
        return new InjectResultDto(false, message, null);
    }

    private sealed class TemplateRow
    {
        public string? GunsRpfUrl    { get; set; }
        public string? GunsRpfSha256 { get; set; }
        public long?   GunsRpfSize   { get; set; }
    }
}

public sealed record GunpackVerifyReport(
    bool Ok,
    bool TargetDlcExists,
    bool RpfPresentInDlc,
    string? StateSha,
    string? ActualSha,
    string Summary);
