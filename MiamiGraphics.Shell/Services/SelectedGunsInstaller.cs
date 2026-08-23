using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using MiamiGraphics.Bridge;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.System;
using MiamiGraphics.Shell.Admin;

namespace MiamiGraphics.Shell.Services;

public sealed class PackZipUnavailableException : Exception
{
    public string GunDisplayName { get; }
    public PackZipUnavailableException(string gunDisplayName, Exception inner)
        : base(Loc.T("error.packZipUnavailableFor", ("gun", gunDisplayName), ("reason", inner.Message)), inner)
        => GunDisplayName = gunDisplayName;
}

public sealed class SelectedGunsInstaller : ISelectedGunsInstaller
{
    public const string PROGRESS_EVENT = "selectedguns:installProgress";
    private const string TARGET_DLC_REL_PATH    = @"update\x64\dlcpacks\patchday18ng\dlc.rpf";

    private const string SELECTED_RPF_NAME      = "miami_guns_selected.rpf";

    private const string CACHE_WEAPON_RPF_NAME  = "miami_weapon.rpf";

    private readonly Admin.IGunpackRepository _packs;
    private readonly Admin.IGunpackWhitelistRepository _whitelist;
    private readonly IAdminConfigService _adminConfig;
    private readonly PackZipCache _packZipCache;

    private readonly Func<Task<string>> _resolveGtaPath;

    private readonly SupabaseClient? _supabase;

    private readonly string _stateFilePath;
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly SemaphoreSlim _customsLock = new(1, 1);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SelectedGunsInstaller(
        Admin.IGunpackRepository packs,
        Admin.IGunpackWhitelistRepository whitelist,
        IAdminConfigService adminConfig,
        PackZipCache packZipCache,
        Func<Task<string>> resolveGtaPath,
        SupabaseClient? supabase = null)
    {
        _packs = packs;
        _whitelist = whitelist;
        _adminConfig = adminConfig;
        _packZipCache = packZipCache;
        _resolveGtaPath = resolveGtaPath;
        _supabase = supabase;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(appData, "MiamiGraphics", "admin");
        Directory.CreateDirectory(dir);
        _stateFilePath = Path.Combine(dir, "selected_guns.json");
    }

    public async Task<List<SelectedGun>> ListInstalledAsync()
        => new List<SelectedGun>((await LoadStateAsync()).Guns);

    public async Task<bool> ReconcileStateAsync()
    {
        var state = await LoadStateAsync();
        if (state.Guns.Count == 0)
        {
            Debug.WriteLine("[selguns.reconcile] state empty - nothing to reconcile");
            return false;
        }

        var gunpackState = await ReadInstalledGunpackStateAsync();
        if (!string.IsNullOrWhiteSpace(gunpackState?.ActiveGunpackId))
        {
            Debug.WriteLine($"[selguns.reconcile] gunpack active ({gunpackState!.ActiveGunpackId}) - selected merged into weapon.rpf, no separate-rpf reconcile.");
            return false;
        }

        var gtaPath = await _resolveGtaPath();
        if (string.IsNullOrWhiteSpace(gtaPath) || !Directory.Exists(gtaPath))
        {
            Debug.WriteLine($"[selguns.reconcile] gtaPath empty/missing - leaving state intact");
            return false;
        }
        var targetDlc = Path.Combine(gtaPath, TARGET_DLC_REL_PATH);
        bool dlcExists = File.Exists(targetDlc);

        bool? weaponInside   = dlcExists ? TargetDlcEditor.TryRpfExistsInsideTarget(targetDlc, TargetDlcEditor.MIAMI_WEAPON_RPF_NAME) : false;
        bool? selectedInside = dlcExists ? TargetDlcEditor.TryRpfExistsInsideTarget(targetDlc, SELECTED_RPF_NAME) : false;
        if (dlcExists && (weaponInside is null || selectedInside is null))
        {
            Debug.WriteLine("[selguns.reconcile] target DLC unreadable (locked by running game?) - leaving state intact");
            return false;
        }
        bool anyInside = weaponInside == true || selectedInside == true;

        bool drift = false;
        string driftReason = string.Empty;

        if (!dlcExists)
        {
            drift = true; driftReason = "target DLC missing";
        }
        else if (!anyInside)
        {
            drift = true; driftReason = $"{SELECTED_RPF_NAME} missing inside DLC";
        }
        else if (!string.IsNullOrEmpty(state.LastInjectedSha256))
        {
            var actualSha =
                TargetDlcEditor.ComputeEmbeddedRpfSha256(targetDlc, TargetDlcEditor.MIAMI_WEAPON_RPF_NAME) ??
                TargetDlcEditor.ComputeEmbeddedRpfSha256(targetDlc, SELECTED_RPF_NAME);
            if (!string.Equals(actualSha, state.LastInjectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                Debug.WriteLine($"[selguns.reconcile] embedded rpf SHA differs from stored inject SHA " +
                    $"(state={state.LastInjectedSha256[..Math.Min(8, state.LastInjectedSha256.Length)]} " +
                    $"actual={actualSha?[..Math.Min(8, actualSha?.Length ?? 1)]}) - re-packed rpf, treating as installed (NOT drift).");
            }
        }

        if (!drift)
        {
            Debug.WriteLine($"[selguns.reconcile] {state.Guns.Count} guns in state, all consistent with disk");
            return false;
        }

        Debug.WriteLine($"[selguns.reconcile] DRIFT - {driftReason}. Wiping state ({state.Guns.Count} guns).");
        await MutateStateAsync(s =>
        {
            s.Guns.Clear();
            s.LastBuiltAt = DateTime.UtcNow;
            s.LastBuiltSize = 0;
            s.LastBuiltSha256 = null;
            s.LastInjectedSha256 = null;
        });
        return true;
    }

    public async Task<bool> IsInstalledAsync(string internalName)
    {
        if (string.IsNullOrWhiteSpace(internalName)) return false;
        var s = await LoadStateAsync();
        return s.Guns.Any(g => string.Equals(
            g.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<bool> HasAnySelectedAsync()
        => (await LoadStateAsync()).Guns.Count > 0;

    public async Task<InjectResultDto> InstallGunAsync(
        string gunpackId,
        string internalName,
        ISelectedGunsInstaller.EmitProgress emit,
        CancellationToken ct)
    {
        Debug.WriteLine($"[selguns.install] START: pack={gunpackId} internal={internalName}");
        if (string.IsNullOrWhiteSpace(gunpackId) || string.IsNullOrWhiteSpace(internalName))
            return Fail("gunpackId / internalName required", emit);

        emit("starting", 0, null, null);

        var pack = await _packs.GetByIdAsync(gunpackId);
        if (pack is null)
        {
            Debug.WriteLine($"[selguns.install] FAIL: pack {gunpackId} not in repo");
            return Fail(Loc.T("error.packNotFound"), emit);
        }
        var packGuns = await _packs.ListGunsAsync(gunpackId);
        Debug.WriteLine($"[selguns.install] pack «{pack.Name}» has {packGuns.Count} guns; looking for '{internalName}'");
        var gun = packGuns.FirstOrDefault(g =>
            string.Equals(g.WeaponPrefix + g.BaseName, internalName, StringComparison.OrdinalIgnoreCase));
        if (gun is null)
        {

            var combos = string.Join(", ", packGuns.Select(g => $"'{g.WeaponPrefix}{g.BaseName}'").Take(20));
            Debug.WriteLine($"[selguns.install] FAIL: '{internalName}' not in pack. Have: {combos}");
            return Fail(Loc.T("error.gunNotInPack", ("pack", pack.Name), ("gun", internalName)), emit);
        }
        Debug.WriteLine($"[selguns.install] matched gun id={gun.Id} files={gun.Files.Count}");
        if (string.IsNullOrWhiteSpace(pack.PackZipUrl))
        {
            Debug.WriteLine($"[selguns.install] FAIL: pack «{pack.Name}» has no PackZipUrl");
            return Fail(Loc.T("error.packNoPackZip"), emit);
        }
        if (string.IsNullOrWhiteSpace(pack.PackZipSha256))
        {
            Debug.WriteLine($"[selguns.install] FAIL: pack «{pack.Name}» has no PackZipSha256");
            return Fail(Loc.T("error.packNoPackZipSha"), emit);
        }

        var gunpackState = await ReadInstalledGunpackStateAsync();
        string? extractedFrom = null;
        if (!string.IsNullOrWhiteSpace(gunpackState?.ActiveGunpackId))
        {
            var activeId = gunpackState!.ActiveGunpackId!;
            try
            {
                var activeGuns = await _packs.ListGunsAsync(activeId);
                bool activeHasSameInternal = activeGuns.Any(g =>
                    string.Equals(g.WeaponPrefix + g.BaseName, internalName, StringComparison.OrdinalIgnoreCase));
                if (activeHasSameInternal)
                {
                    extractedFrom = activeId;
                    Debug.WriteLine($"[selguns.install] split: '{internalName}' also lives in active pack {activeId} - marking for extraction.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[selguns.install] split-check FAIL ({ex.Message}) - proceeding without marker.");
            }
        }

        var record = new SelectedGun
        {
            GunpackId                 = pack.Id,
            GunpackName               = pack.Name,
            GunId                     = gun.Id.ToString(),
            InternalName              = internalName,
            DisplayName               = gun.DisplayName ?? gun.BaseName,
            BaseName                  = gun.BaseName,
            WeaponPrefix              = gun.WeaponPrefix,
            Files                     = new List<string>(gun.Files ?? Enumerable.Empty<string>()),
            PackZipUrl                = pack.PackZipUrl ?? string.Empty,
            PackZipSha256             = pack.PackZipSha256 ?? string.Empty,
            SelectedAt                = DateTime.UtcNow,
            ExtractedFromActivePackId = extractedFrom,
        };

        emit("updating_state", 1, null, Loc.T("install.addingGunToSelection", ("gun", record.DisplayName)));
        await MutateStateAsync(state =>
        {
            state.Guns.RemoveAll(g => string.Equals(
                g.InternalName, record.InternalName, StringComparison.OrdinalIgnoreCase));
            state.Guns.Add(record);
        });

        return await RebuildAsync(emit, ct);
    }

    public async Task<InjectResultDto> RemoveGunAsync(
        string internalName,
        ISelectedGunsInstaller.EmitProgress emit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(internalName))
            return Fail("internalName required", emit);

        emit("starting", 0, null, null);

        var removed = false;
        await MutateStateAsync(state =>
        {
            var before = state.Guns.Count;
            state.Guns.RemoveAll(g => string.Equals(
                g.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
            removed = state.Guns.Count != before;
        });

        if (!removed)
        {
            emit("done", 100, null, Loc.T("install.gunWasNotInstalledSkip"));
            return new InjectResultDto(true, Loc.T("install.wasNotInSelection"), null);
        }

        return await RebuildAsync(emit, ct);
    }

    public async Task SetVanillaSlotsAsync(string? packId, IReadOnlyCollection<string>? internalNames)
    {
        var list = internalNames?
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        await MutateStateAsync(s =>
        {
            s.VanillaSlotsPackId       = list.Count > 0 ? packId : null;
            s.VanillaSlotInternalNames = list;
        });
        Debug.WriteLine($"[selguns.vanillaSlots] pack={packId ?? "<none>"} count={list.Count}: {string.Join(", ", list)}");
    }

    public async Task<InjectResultDto> RebuildAsync(
        ISelectedGunsInstaller.EmitProgress emit,
        CancellationToken ct)
    {
        var state = await LoadStateAsync();
        Debug.WriteLine($"[selguns.rebuild] START: {state.Guns.Count} guns in state");
        SessionLog.Info("selguns.rebuild",
            $"START: {state.Guns.Count} guns [{string.Join(", ", state.Guns.Select(g => g.InternalName))}]");
        var gtaPath = await _resolveGtaPath();
        if (string.IsNullOrWhiteSpace(gtaPath) || !Directory.Exists(gtaPath))
        {
            Debug.WriteLine($"[selguns.rebuild] FAIL: gtaPath empty or missing ('{gtaPath}')");
            return Fail(Loc.T("error.gtaNotFoundAdminSettings"), emit);
        }
        var targetDlc    = Path.Combine(gtaPath, TARGET_DLC_REL_PATH);
        var dlcTemplate  = Path.Combine(
            MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir, "backup", "dlc.rpf");
        Debug.WriteLine($"[selguns.rebuild] targetDlc={targetDlc} exists={File.Exists(targetDlc)} dlcTemplate={dlcTemplate} exists={File.Exists(dlcTemplate)}");

        var gunpackState = await ReadInstalledGunpackStateAsync();
        var hasActiveGunpack = !string.IsNullOrWhiteSpace(gunpackState?.ActiveGunpackId);

        var customEntries = (await LoadCustomsAsync()).Customs;
        var customFiles   = LoadCustomFilesFromCache(customEntries);
        var hasCustoms    = customFiles.Count > 0;
        Debug.WriteLine($"[selguns.rebuild] customs: {customEntries.Count} entries, {customFiles.Count} overlay files");

        if (hasActiveGunpack)
        {
            try
            {
                var activeGuns = await _packs.ListGunsAsync(gunpackState!.ActiveGunpackId!);
                var activeNames = new HashSet<string>(
                    activeGuns.Select(g => g.WeaponPrefix + g.BaseName),
                    StringComparer.OrdinalIgnoreCase);
                await MutateStateAsync(s =>
                {
                    foreach (var g in s.Guns)
                    {
                        g.ExtractedFromActivePackId = activeNames.Contains(g.InternalName)
                            ? gunpackState.ActiveGunpackId
                            : null;
                    }
                });
                state = await LoadStateAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[selguns.rebuild] marker recompute FAIL: {ex.Message} - proceeding with persisted markers");
            }
        }
        else
        {

            await MutateStateAsync(s =>
            {
                foreach (var g in s.Guns) g.ExtractedFromActivePackId = null;
            });
            state = await LoadStateAsync();
        }

        byte[]? selectedBytes = null;
        byte[]? weaponBytes = null;
        HunterGunsSelectedRpfBuilder.BuildResult? buildResult = null;

        if (hasActiveGunpack)
        {

            bool hasAnyConflict = state.Guns.Any(g =>
                string.Equals(g.ExtractedFromActivePackId, gunpackState!.ActiveGunpackId,
                    StringComparison.OrdinalIgnoreCase));
            Debug.WriteLine($"[selguns.rebuild] conflicts={hasAnyConflict} (selected guns sharing internal_name with active pack)");

            if (hasAnyConflict)
            {

                try
                {
                    var (mergedBytes, droppedInternalNames) = await BuildMergedWeaponBytesAsync(
                        gunpackState!.ActiveGunpackId!, state, emit, ct);
                    if (droppedInternalNames.Count > 0)
                    {
                        Debug.WriteLine($"[selguns.rebuild] silently dropping {droppedInternalNames.Count} guns whose pack.zip unreachable");
                        await MutateStateAsync(s =>
                            s.Guns.RemoveAll(g => droppedInternalNames.Contains(
                                g.InternalName, StringComparer.OrdinalIgnoreCase)));
                        state = await LoadStateAsync();
                    }
                    weaponBytes = mergedBytes;
                }
                catch (PackZipUnavailableException ex)
                {
                    Debug.WriteLine($"[selguns.rebuild] merge aborted (pack.zip unreachable): {ex.Message}");
                    return Fail(
                        Loc.T("error.gunFilesDownloadFailed", ("gun", ex.GunDisplayName)),
                        emit);
                }
                catch (Exception ex)
                {

                    Debug.WriteLine($"[selguns.rebuild] merge failed: {ex.Message}");
                    return Fail(
                        Loc.T("error.gunpackNonStandardWeaponRpf", ("reason", ex.Message)),
                        emit);
                }

                if (weaponBytes is null || weaponBytes.Length == 0)
                    return Fail(Loc.T("error.weaponRpfBuildNoCache"), emit);
            }
            else
            {

                var packDir = Path.Combine(
                    MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir,
                    "Gunpacks", $"install-{SanitiseId(gunpackState!.ActiveGunpackId!)}");
                var freshPath = Path.Combine(packDir, CACHE_WEAPON_RPF_NAME);
                if (!File.Exists(freshPath))
                    return Fail(Loc.T("error.cachedWeaponRpfMissing"), emit);
                try
                {
                    weaponBytes = await File.ReadAllBytesAsync(freshPath, ct);
                    Debug.WriteLine($"[selguns.rebuild] dual-rpf: weapon.rpf as-is = {weaponBytes.Length:N0} bytes");
                }
                catch (Exception ex)
                {
                    return Fail(Loc.T("error.cachedWeaponRpfReadFailed", ("reason", ex.Message)), emit);
                }

                if (state.Guns.Count > 0)
                {

                    try
                    {
                        (buildResult, selectedBytes) = await BuildSelectedBytesInternalAsync(state, emit, ct);
                    }
                    catch (Exception ex)
                    {
                        return Fail(Loc.T("error.selectedRpfBuildCrashed", ("reason", ex.Message)), emit);
                    }
                    if (buildResult is null || !buildResult.Success || selectedBytes is null || selectedBytes.Length == 0)
                        return Fail(Loc.T("error.selectedRpfBuildFailed", ("reason", buildResult?.Message ?? "?")), emit);
                }
                try
                {
                    var (mergedBytes, droppedInternalNames) = await BuildMergedWeaponBytesAsync(
                        gunpackState!.ActiveGunpackId!, state, emit, ct);
                    if (droppedInternalNames.Count > 0)
                    {
                        await MutateStateAsync(s =>
                            s.Guns.RemoveAll(g => droppedInternalNames.Contains(
                                g.InternalName, StringComparer.OrdinalIgnoreCase)));
                        state = await LoadStateAsync();
                    }
                    weaponBytes = mergedBytes;
                    selectedBytes = null;
                }
                catch (PackZipUnavailableException ex)
                {
                    Debug.WriteLine($"[selguns.rebuild] merge-additions aborted (pack.zip unreachable): {ex.Message}");
                    return Fail(
                        Loc.T("error.gunFilesDownloadFailed", ("gun", ex.GunDisplayName)),
                        emit);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[selguns.rebuild] merge-additions failed: {ex.Message}");
                    return Fail(
                        Loc.T("error.mergeSelectedIntoWeaponRpfFailed", ("reason", ex.Message)),
                        emit);
                }

                if (weaponBytes is null || weaponBytes.Length == 0)
                    return Fail(Loc.T("error.weaponRpfFromCacheFailed"), emit);
            }
        }
        else if (state.Guns.Count > 0)
        {

            SessionLog.Info("selguns.rebuild", $"BuildSelectedBytes BEGIN - {state.Guns.Count} guns (no active gunpack)");
            try
            {
                (buildResult, selectedBytes) = await BuildSelectedBytesInternalAsync(state, emit, ct, customFiles);
            }
            catch (Exception ex)
            {
                SessionLog.Error("selguns.rebuild", "BuildSelectedBytes FAILED", ex);
                return Fail(Loc.T("error.rpfBuildCrashed", ("reason", ex.Message)), emit);
            }
            if (buildResult is null || !buildResult.Success || selectedBytes is null || selectedBytes.Length == 0)
                return Fail(Loc.T("error.rpfBuildFailed", ("reason", buildResult?.Message ?? "?")), emit);
        }

        if (hasCustoms)
        {
            if (weaponBytes is { Length: > 0 })
            {
                emit("merging_guns", 72, null, Loc.T("install.applyingCustomSkinsToWeaponRpf"));
                try
                {
                    weaponBytes = await Task.Run(
                        () => RpfFileMutator.Apply(weaponBytes, Array.Empty<string>(), customFiles), ct);
                }
                catch (Exception ex)
                {
                    return Fail(Loc.T("error.applyCustomSkinsFailed", ("reason", ex.Message)), emit);
                }
            }
            else if (selectedBytes is null)
            {
                var custTemplate = await EnsureEmptyTemplateCachedAsync(emit, ct);
                var custOverlay  = WalkUpForDirectory(@"templates\overlaymags")
                                ?? WalkUpForDirectory(@"MiamiGraphics.Core\templates\overlaymags");
                if (custTemplate is null || custOverlay is null)
                    return Fail(Loc.T("error.emptyTemplateOrOverlaymagsMissing"), emit);
                emit("building_rpf", 60, null, Loc.T("install.buildingRpfCustomSkinsOnly", ("file", SELECTED_RPF_NAME)));
                try
                {
                    (buildResult, selectedBytes) = await Task.Run(
                        () => HunterGunsSelectedRpfBuilder.BuildToBytes(customFiles, custOverlay, custTemplate), ct);
                }
                catch (Exception ex) { return Fail(Loc.T("error.customSkinsBuildFailed", ("reason", ex.Message)), emit); }
                if (buildResult is null || !buildResult.Success || selectedBytes is null || selectedBytes.Length == 0)
                    return Fail(Loc.T("error.customSkinsBuildFailed", ("reason", buildResult?.Message ?? "?")), emit);
            }
        }

        if (selectedBytes is null && weaponBytes is null)
        {

            if (File.Exists(dlcTemplate))
            {
                emit("removing_from_dlc", 50, null, Loc.T("install.resettingDlcToCleanTemplate"));
                try
                {
                    await Task.Run(() => TargetDlcEditor.RebuildFromTemplate(
                        templatePath:           dlcTemplate,
                        targetDlcPath:          targetDlc,
                        hunterWeaponBytes:      null,
                        hunterGunsSelectedBytes: null), ct);
                }
                catch (Exception ex) { return Fail(Loc.T("error.dlcRebuildFailed", ("reason", ex.Message)), emit); }
            }
            else if (File.Exists(targetDlc))
            {
                try { TargetDlcEditor.DeleteTargetDlc(targetDlc); }
                catch (Exception ex) { return Fail(Loc.T("error.dlcDeleteFailed", ("reason", ex.Message)), emit); }
            }
            await MutateStateAsync(s =>
            {
                s.LastBuiltAt = DateTime.UtcNow;
                s.LastBuiltSize = 0;
                s.LastBuiltSha256 = null;
                s.LastInjectedSha256 = null;
            });
            emit("done", 100, null, Loc.T("install.nothingToPutInDlc"));
            return new InjectResultDto(true, Loc.T("install.listEmptyDlcReset"), targetDlc);
        }

        if (!File.Exists(dlcTemplate))
        {
            emit("preparing", 65, null, Loc.T("install.downloadingCleanDlcTemplate"));
            var seeded = await TrySeedTemplateFromR2Async(dlcTemplate, ct);
            if (!seeded || !File.Exists(dlcTemplate))
            {
                return Fail(Loc.T("error.cleanDlcTemplateUnavailable"), emit);
            }
        }

        emit("injecting", 80, null, weaponBytes is { Length: > 0 } && selectedBytes is { Length: > 0 }
            ? Loc.T("install.rebuildingDlcGunpackAndSelected")
            : selectedBytes is { Length: > 0 }
                ? Loc.T("install.rebuildingDlcSelectedOnly")
                : Loc.T("install.rebuildingDlcGunpackOnly"));
        SessionLog.Info("selguns.rebuild",
            $"native RebuildFromTemplate BEGIN - weaponBytes={weaponBytes?.Length ?? 0}, selectedBytes={selectedBytes?.Length ?? 0}, guns={state.Guns.Count}, target={targetDlc}");
        try
        {
            await Task.Run(() => TargetDlcEditor.RebuildFromTemplate(
                templatePath:           dlcTemplate,
                targetDlcPath:          targetDlc,
                hunterWeaponBytes:      weaponBytes,
                hunterGunsSelectedBytes: selectedBytes), ct);
        }
        catch (Exception ex)
        {
            SessionLog.Error("selguns.rebuild", "native RebuildFromTemplate FAILED", ex);
            return Fail(Loc.T("error.targetDlcRebuildFailed", ("reason", ex.Message)), emit);
        }
        SessionLog.Info("selguns.rebuild", "native RebuildFromTemplate OK");

        string? animWarning = null;
        try { await ReapplyAnimLooseFilesAsync(targetDlc, ct); }
        catch (Exception ex)
        {
            SessionLog.Error("selguns.rebuild", "anim re-register failed", ex);
            emit("injecting", 85, null, Loc.T("install.animDetailsFailedRebuildingWithout"));
            try
            {
                await Task.Run(() => TargetDlcEditor.RebuildFromTemplate(
                    templatePath:           dlcTemplate,
                    targetDlcPath:          targetDlc,
                    hunterWeaponBytes:      weaponBytes,
                    hunterGunsSelectedBytes: selectedBytes), ct);
                animWarning = Loc.T("install.animDetailsNotReinstalled", ("reason", ex.Message));
            }
            catch (Exception ex2)
            {
                SessionLog.Error("selguns.rebuild", "anim rollback rebuild FAILED", ex2);
                return Fail(Loc.T("error.animDetailsRollbackFailed", ("reason", ex.Message)), emit);
            }
        }

        var injectedSha = buildResult is not null
            ? TargetDlcEditor.ComputeEmbeddedRpfSha256(targetDlc, TargetDlcEditor.MIAMI_WEAPON_RPF_NAME)
              ?? TargetDlcEditor.ComputeEmbeddedRpfSha256(targetDlc, SELECTED_RPF_NAME)
              ?? buildResult.Sha256
            : null;

        await MutateStateAsync(s =>
        {
            s.LastBuiltAt        = DateTime.UtcNow;
            s.LastBuiltSize      = buildResult?.FinalSize ?? 0;
            s.LastBuiltSha256    = buildResult?.Sha256;
            s.LastInjectedSha256 = injectedSha;
        });

        var doneDetail = Loc.T("install.selectedGunsDone",
            ("count", state.Guns.Count),
            ("kb", (buildResult?.FinalSize ?? 0) / 1024),
            ("sha", (buildResult?.Sha256 ?? "-")[..Math.Min(8, buildResult?.Sha256?.Length ?? 1)]));
        if (animWarning is not null) doneDetail += Loc.T("install.doneWarningSuffix", ("warning", animWarning));
        emit("done", 100, null, doneDetail);
        return new InjectResultDto(
            true,
            animWarning is null
                ? Loc.T("install.gunsSelectedDlcRebuilt", ("count", state.Guns.Count))
                : Loc.T("install.gunsSelectedDlcRebuiltBut", ("count", state.Guns.Count), ("warning", animWarning)),
            targetDlc);
    }

    public async Task<byte[]?> BuildBytesFromStateAsync(CancellationToken ct)
    {
        var state = await LoadStateAsync();
        if (state.Guns.Count == 0) return null;

        ISelectedGunsInstaller.EmitProgress noop = (_, _, _, _) => { };
        var (result, bytes) = await BuildSelectedBytesInternalAsync(state, noop, ct);
        if (!result.Success || bytes.Length == 0)
            throw new InvalidOperationException(
                $"BuildBytesFromState failed: {result.Message}");
        return bytes;
    }

    private async Task<(HunterGunsSelectedRpfBuilder.BuildResult result, byte[] bytes)>
        BuildSelectedBytesInternalAsync(
            SelectedGunsState state,
            ISelectedGunsInstaller.EmitProgress emit,
            CancellationToken ct,
            IReadOnlyDictionary<string, byte[]>? extraOverlayFiles = null)
    {

        var templatePath = await EnsureEmptyTemplateCachedAsync(emit, ct);
        var overlayDir   = WalkUpForDirectory(@"templates\overlaymags")
                       ?? WalkUpForDirectory(@"MiamiGraphics.Core\templates\overlaymags");
        Debug.WriteLine($"[selguns.build] templates: rpf='{templatePath ?? "<null>"}', overlaymags='{overlayDir ?? "<null>"}'");
        if (templatePath is null || overlayDir is null)
            throw new FileNotFoundException(Loc.T("error.emptyRpfOrOverlaymagsUnavailable"));

        var packs = state.Guns
            .GroupBy(g => g.GunpackId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        emit("downloading_packs", 5, null, Loc.T("install.sourcesPacksGuns", ("packs", packs.Count), ("guns", state.Guns.Count)));

        var packZipPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int packIdx = 0;
        foreach (var grp in packs)
        {
            ct.ThrowIfCancellationRequested();
            var first = grp.First();
            packIdx++;
            int pctStart = 5 + (packIdx - 1) * 30 / Math.Max(1, packs.Count);
            int pctEnd   = 5 + packIdx * 30 / Math.Max(1, packs.Count);
            emit("downloading_packs", pctStart, null, Loc.T("install.packZipOfPack", ("pack", first.GunpackName), ("index", packIdx), ("total", packs.Count)));

            var dst = await _packZipCache.EnsurePackZipAsync(
                url:            first.PackZipUrl,
                expectedSha256: first.PackZipSha256,
                expectedSize:   null,
                bytesProgress:  new Progress<(long received, long total)>(p =>
                {
                    if (p.total <= 0) return;
                    var pct = pctStart + (int)((pctEnd - pctStart) * p.received / Math.Max(1, p.total));
                    emit("downloading_packs", pct, null,
                        Loc.T("install.packDownloadMb", ("pack", first.GunpackName), ("done", p.received / (1024 * 1024)), ("total", p.total / (1024 * 1024))));
                }),
                ct: ct);
            packZipPaths[first.GunpackId] = dst;
        }

        emit("extracting", 40, null, Loc.T("install.extractingSelectedGunFiles"));
        var allFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var grp in packs)
        {
            var packZip = packZipPaths[grp.Key];
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in grp) foreach (var f in g.Files) wanted.Add(f);
            if (wanted.Count == 0) continue;
            var extracted = PackZipCache.ExtractFiles(packZip, wanted);
            Debug.WriteLine($"[selguns.build] pack «{grp.First().GunpackName}»: extracted {extracted.Count} of {wanted.Count} wanted from {packZip}");
            if (extracted.Count == 0)
                Debug.WriteLine($"[selguns.build]   wanted: {string.Join(", ", wanted)}");
            foreach (var (name, bytes) in extracted)
                allFiles[name] = bytes;
        }

        if (extraOverlayFiles is { Count: > 0 })
            foreach (var (name, bytes) in extraOverlayFiles)
                if (!string.IsNullOrEmpty(name) && bytes is { Length: > 0 })
                    allFiles[Path.GetFileName(name)] = bytes;

        if (allFiles.Count == 0)
            throw new InvalidOperationException(Loc.T("error.noGunFilesInPackZips"));

        emit("building_rpf", 60, null, Loc.T("install.buildingRpf", ("file", SELECTED_RPF_NAME)));
        return await Task.Run(() =>
            HunterGunsSelectedRpfBuilder.BuildToBytes(allFiles, overlayDir, templatePath), ct);
    }

    private async Task<InstalledGunpackState?> ReadInstalledGunpackStateAsync()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(appData, "MiamiGraphics", "admin", "installed_gunpack.json");
        if (!File.Exists(path)) return null;
        try
        {
            await using var fs = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<InstalledGunpackState>(fs, Json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[selguns.rebuild] ReadInstalledGunpackState FAIL: {ex.Message}");
            return null;
        }
    }

    private async Task<(byte[]? bytes, List<string> droppedInternalNames)> BuildMergedWeaponBytesAsync(
        string activePackId,
        SelectedGunsState state,
        ISelectedGunsInstaller.EmitProgress emit,
        CancellationToken ct)
    {

        var packDir = Path.Combine(
            MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir,
            "Gunpacks", $"install-{SanitiseId(activePackId)}");
        var freshPath = Path.Combine(packDir, CACHE_WEAPON_RPF_NAME);
        if (!File.Exists(freshPath))
        {
            Debug.WriteLine($"[selguns.merge] cached weapon.rpf MISSING at {freshPath}");
            return (null, new List<string>());
        }

        var weaponBytes = await File.ReadAllBytesAsync(freshPath, ct);
        Debug.WriteLine($"[selguns.merge] base weaponBytes={weaponBytes.Length:N0} (from cache)");

        IReadOnlyList<MiamiGraphics.Shell.Admin.GunpackGun> activeGuns;
        try
        {
            activeGuns = await _packs.ListGunsAsync(activePackId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[selguns.merge] ListGunsAsync FAIL: {ex.Message} - proceeding with empty active gun list (no conflict-cuts will fire)");
            activeGuns = Array.Empty<MiamiGraphics.Shell.Admin.GunpackGun>();
        }
        var activeByInternal = new Dictionary<string, MiamiGraphics.Shell.Admin.GunpackGun>(StringComparer.OrdinalIgnoreCase);
        foreach (var ag in activeGuns)
            activeByInternal[ag.WeaponPrefix + ag.BaseName] = ag;

        var removeNames    = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addByName      = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var droppedInternalNames = new List<string>();

        int idx = 0;
        foreach (var s in state.Guns)
        {
            idx++;
            int progressPct = 60 + (state.Guns.Count > 0 ? (idx * 8) / state.Guns.Count : 0);
            emit("merging_guns", progressPct, null, Loc.T("install.preparingGun", ("gun", s.DisplayName)));

            Dictionary<string, byte[]>? selectedFiles = null;
            try
            {
                if (string.IsNullOrWhiteSpace(s.PackZipUrl) || string.IsNullOrWhiteSpace(s.PackZipSha256))
                {
                    Debug.WriteLine($"[selguns.merge] «{s.DisplayName}» has no PackZipUrl/Sha - dropping from state");
                    droppedInternalNames.Add(s.InternalName);
                    continue;
                }
                var zipPath = await _packZipCache.EnsurePackZipAsync(
                    url: s.PackZipUrl,
                    expectedSha256: s.PackZipSha256,
                    expectedSize: null,
                    bytesProgress: null,
                    ct: ct);
                selectedFiles = PackZipCache.ExtractFiles(zipPath, s.Files);
                if (selectedFiles.Count == 0)
                {
                    Debug.WriteLine($"[selguns.merge] no files extracted for «{s.DisplayName}» - dropping from state");
                    droppedInternalNames.Add(s.InternalName);
                    continue;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[selguns.merge] pack.zip fetch/extract FAIL for «{s.DisplayName}»: {ex.Message} - aborting rebuild (selection preserved)");
                throw new PackZipUnavailableException(s.DisplayName, ex);
            }

            if (s.ExtractedFromActivePackId == activePackId
                && activeByInternal.TryGetValue(s.InternalName, out var activeGun)
                && activeGun.Files is { Count: > 0 })
            {
                foreach (var f in activeGun.Files) removeNames.Add(f);
                Debug.WriteLine($"[selguns.merge] queued {activeGun.Files.Count} removals for «{s.InternalName}»");
            }

            foreach (var (name, bytes) in selectedFiles) addByName[name] = bytes;
        }

        if (!string.IsNullOrWhiteSpace(state.VanillaSlotsPackId)
            && string.Equals(state.VanillaSlotsPackId, activePackId, StringComparison.OrdinalIgnoreCase)
            && state.VanillaSlotInternalNames is { Count: > 0 })
        {
            var selectedInternal = new HashSet<string>(
                state.Guns.Select(g => g.InternalName), StringComparer.OrdinalIgnoreCase);
            int cut = 0;
            foreach (var vanillaName in state.VanillaSlotInternalNames)
            {
                if (selectedInternal.Contains(vanillaName)) continue;
                if (!activeByInternal.TryGetValue(vanillaName, out var vg)) continue;
                if (vg.Files is not { Count: > 0 }) continue;
                foreach (var f in vg.Files) removeNames.Add(f);
                cut++;
            }
            Debug.WriteLine($"[selguns.merge] vanilla slots: cut {cut}/{state.VanillaSlotInternalNames.Count} guns out of the pack");
        }

        var overlayStubs = LoadOverlayMagStubs();

        if (removeNames.Count > 0 || addByName.Count > 0)
        {
            emit("merging_guns", 70, null, Loc.T("install.rebuildingWeaponRpf"));
            try
            {
                weaponBytes = RpfFileMutator.Apply(weaponBytes, removeNames, addByName, overlayStubs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[selguns.merge] Apply FAIL: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }
        else
        {
            emit("merging_guns", 70, null, Loc.T("install.rebuildingWeaponRpf"));
            weaponBytes = await Task.Run(() => RpfFileMutator.NormalizeAndFix(weaponBytes, overlayStubs), ct);
            Debug.WriteLine($"[selguns.merge] nothing to merge - normalized base (OPEN->NG) = {weaponBytes.Length:N0}");
        }

        Debug.WriteLine($"[selguns.merge] DONE: final weaponBytes={weaponBytes.Length:N0}, removed={removeNames.Count} added={addByName.Count} dropped={droppedInternalNames.Count}");
        return (weaponBytes, droppedInternalNames);
    }

    private static Dictionary<string, byte[]> LoadOverlayMagStubs()
    {
        var stubs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var dir = WalkUpForDirectory(@"templates\overlaymags")
                   ?? WalkUpForDirectory(@"MiamiGraphics.Core\templates\overlaymags");
            if (dir is null)
            {
                Debug.WriteLine("[selguns.merge] overlaymags не найдены - пересобираю без заглушек обвесов");
                return stubs;
            }
            foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length > 0) stubs[Path.GetFileName(path)] = bytes;
            }
            Debug.WriteLine($"[selguns.merge] overlaymags: {stubs.Count} заглушек из {dir}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[selguns.merge] чтение overlaymags упало: {ex.Message} - еду без заглушек");
        }
        return stubs;
    }

    private static string SanitiseId(string id)
    {
        var sb = new System.Text.StringBuilder(id.Length);
        foreach (var c in id)
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        return sb.ToString();
    }

    public async Task<InjectResultDto> ApplyStandaloneCustomAsync(
        string internalName,
        string displayName,
        string packId,
        IReadOnlyDictionary<string, byte[]> gunFilesByName,
        ISelectedGunsInstaller.EmitProgress emit,
        CancellationToken ct)
    {
        if (gunFilesByName is null || gunFilesByName.Count == 0)
            return Fail(Loc.T("error.noFilesForSkinInstall"), emit);
        if (string.IsNullOrWhiteSpace(internalName))
            return Fail(Loc.T("error.customSkinInternalNameRequired"), emit);

        using var _mtx = await UpdateRpfMutex.AcquireAsync("gunskin-apply", ct);

        emit("starting", 0, null, Loc.T("install.preparingCustomSkin"));

        await AddOrReplaceCustomAsync(internalName, displayName, packId, gunFilesByName);

        return await RebuildAsync(emit, ct);
    }

    public async Task<InjectResultDto> ApplyStandaloneAnimAsync(
        string internalName, string displayName, string packId,
        IReadOnlyDictionary<string, byte[]> gunFilesByName,
        IReadOnlyList<TargetDlcEditor.AnimLooseFile> looseFiles,
        (string DlcName, string RelPath, byte[] Bytes)? updateRpfPatch,
        ISelectedGunsInstaller.EmitProgress emit,
        CancellationToken ct)
    {
        if (gunFilesByName is null || gunFilesByName.Count == 0)
            return Fail(Loc.T("error.noFilesForAnimDetailInstall"), emit);
        if (string.IsNullOrWhiteSpace(internalName))
            return Fail(Loc.T("error.animDetailGunUnknown"), emit);

        using var _mtx = await UpdateRpfMutex.AcquireAsync("anim-apply", ct);
        emit("starting", 0, null, Loc.T("install.preparingAnimDetail"));

        var gtaPath = await _resolveGtaPath();
        if (string.IsNullOrWhiteSpace(gtaPath) || !Directory.Exists(gtaPath))
            return Fail(Loc.T("error.gtaNotFoundSetPathInSettings"), emit);
        var targetDlc = Path.Combine(gtaPath, TARGET_DLC_REL_PATH);

        await AddOrReplaceCustomAsync(internalName, displayName, packId, gunFilesByName);
        await AddOrReplaceAnimDetailAsync(internalName, displayName, packId, looseFiles, updateRpfPatch);

        emit("injecting", 40, null, Loc.T("install.rebuildingDlcWithAnimDetail"));
        var rebuild = await RebuildAsync(emit, ct);
        if (!rebuild.Success)
            return rebuild;

        if (updateRpfPatch is { } p && p.Bytes is { Length: > 0 })
        {
            emit("weaponmeta", 92, null, Loc.T("install.writingWeaponMetaToUpdateRpf"));
            try
            {
                var updateRpf = Path.Combine(gtaPath, "update", "update.rpf");
                await Task.Run(() => TargetDlcEditor.PatchUpdateRpfDlcPatch(
                    updateRpf, p.DlcName, p.RelPath, p.Bytes), ct);
            }
            catch (Exception ex) { return Fail(Loc.T("error.weaponMetaPatchFailed", ("reason", ex.Message)), emit); }
        }

        emit("done", 100, null, Loc.T("install.animDetailInstalledCheckInGame"));
        return new InjectResultDto(true, Loc.T("install.animDetailInstalledCoexists"), targetDlc);
    }

    private static string AnimCacheDir(string internalName)
        => Path.Combine(CustomsCacheRoot, "anim", SanitiseId(internalName));

    private async Task AddOrReplaceAnimDetailAsync(
        string internalName, string displayName, string packId,
        IReadOnlyList<TargetDlcEditor.AnimLooseFile> looseFiles,
        (string DlcName, string RelPath, byte[] Bytes)? updateRpfPatch)
    {
        var dir = AnimCacheDir(internalName);
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);

        var descs = new List<AnimLooseDesc>();
        int i = 0;
        foreach (var f in looseFiles ?? Array.Empty<TargetDlcEditor.AnimLooseFile>())
        {
            if (f is null || string.IsNullOrWhiteSpace(f.RelPath)) continue;
            string? bytesFile = null;
            if (f.Bytes is { Length: > 0 })
            {
                bytesFile = $"loose_{i++}_{SanitiseId(Path.GetFileName(f.RelPath))}";
                await File.WriteAllBytesAsync(Path.Combine(dir, bytesFile), f.Bytes);
            }
            descs.Add(new AnimLooseDesc(f.RelPath, f.ContentXmlFileType, f.Contents, bytesFile));
        }

        string? updDlc = null, updRel = null, updFile = null;
        if (updateRpfPatch is { } p && p.Bytes is { Length: > 0 })
        {
            updDlc = p.DlcName; updRel = p.RelPath; updFile = "update_meta.bin";
            await File.WriteAllBytesAsync(Path.Combine(dir, updFile), p.Bytes);
        }

        await MutateCustomsAsync(s =>
        {
            s.AnimDetails.RemoveAll(a => string.Equals(a.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
            s.AnimDetails.Add(new AnimDetailEntry(
                internalName,
                string.IsNullOrWhiteSpace(displayName) ? internalName : displayName,
                packId ?? string.Empty,
                descs, updDlc, updRel, updFile));
        });
    }

    private async Task ReapplyAnimLooseFilesAsync(string targetDlc, CancellationToken ct)
    {
        var anims = (await LoadCustomsAsync()).AnimDetails;
        if (anims is null || anims.Count == 0) return;

        var loose = new List<TargetDlcEditor.AnimLooseFile>();
        foreach (var a in anims)
        {
            var dir = AnimCacheDir(a.InternalName);
            foreach (var d in a.Loose ?? new List<AnimLooseDesc>())
            {
                byte[]? bytes = null;
                if (!string.IsNullOrEmpty(d.BytesFile))
                {
                    var pth = Path.Combine(dir, d.BytesFile);
                    if (File.Exists(pth)) { try { bytes = await File.ReadAllBytesAsync(pth, ct); } catch { } }
                }
                loose.Add(new TargetDlcEditor.AnimLooseFile(d.RelPath, bytes, d.ContentXmlFileType, d.Contents));
            }
        }
        if (loose.Count == 0) return;
        await Task.Run(() => TargetDlcEditor.InstallAnimLooseFiles(targetDlc, loose, Path.GetFileName(targetDlc)), ct);
    }

    private string CustomSkinsFilePath => Path.Combine(
        Path.GetDirectoryName(_stateFilePath)!, "custom_skins.json");

    private string LegacyCustomSkinFilePath => Path.Combine(
        Path.GetDirectoryName(_stateFilePath)!, "custom_skin.json");

    private static string CustomsCacheRoot
        => MiamiGraphics.Core.System.AppDataRoot.Dir("gunsmith", "customs");

    private static string CustomCacheDir(string internalName)
        => Path.Combine(CustomsCacheRoot, SanitiseId(internalName));

    private static Dictionary<string, byte[]> LoadCustomFilesFromCache(IEnumerable<CustomSkinEntry> entries)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var dir = CustomCacheDir(e.InternalName);
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.EnumerateFiles(dir))
            {
                try { map[Path.GetFileName(f)] = File.ReadAllBytes(f); }
                catch (Exception ex) { Debug.WriteLine($"[selguns.customs] cache read FAIL {f}: {ex.Message}"); }
            }
        }
        return map;
    }

    private async Task<CustomSkinsState> ReadCustomsUnlocked()
    {
        if (File.Exists(CustomSkinsFilePath))
        {
            try
            {
                await using var fs = File.OpenRead(CustomSkinsFilePath);
                return await JsonSerializer.DeserializeAsync<CustomSkinsState>(fs, Json) ?? new CustomSkinsState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[selguns.customs] read FAIL: {ex.Message} - empty");
                return new CustomSkinsState();
            }
        }
        if (File.Exists(LegacyCustomSkinFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(LegacyCustomSkinFilePath);
                var dto = JsonSerializer.Deserialize<CustomSkinAppliedDto>(json);
                var st = new CustomSkinsState();
                if (dto is not null && !string.IsNullOrWhiteSpace(dto.InternalName))
                    st.Customs.Add(new CustomSkinEntry(dto.InternalName, dto.DisplayName, dto.PackId));
                Debug.WriteLine($"[selguns.customs] migrated legacy custom_skin.json ({st.Customs.Count} entry)");
                return st;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[selguns.customs] legacy migrate FAIL: {ex.Message}");
            }
        }
        return new CustomSkinsState();
    }

    private async Task<CustomSkinsState> LoadCustomsAsync()
    {
        await _customsLock.WaitAsync();
        try { return await ReadCustomsUnlocked(); }
        finally { _customsLock.Release(); }
    }

    private async Task MutateCustomsAsync(Action<CustomSkinsState> mutate)
    {
        await _customsLock.WaitAsync();
        try
        {
            var s = await ReadCustomsUnlocked();
            mutate(s);
            await using var fs = File.Create(CustomSkinsFilePath);
            await JsonSerializer.SerializeAsync(fs, s, Json);
        }
        finally { _customsLock.Release(); }
    }

    public async Task<List<CustomSkinEntry>> GetCustomsAsync()
        => new List<CustomSkinEntry>((await LoadCustomsAsync()).Customs);

    public async Task AddOrReplaceCustomAsync(
        string internalName, string displayName, string packId,
        IReadOnlyDictionary<string, byte[]> files)
    {
        if (string.IsNullOrWhiteSpace(internalName))
            throw new ArgumentException("internalName required", nameof(internalName));

        var dir = CustomCacheDir(internalName);
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        Directory.CreateDirectory(dir);
        if (files is not null)
            foreach (var (name, bytes) in files)
            {
                if (string.IsNullOrWhiteSpace(name) || bytes is null || bytes.Length == 0) continue;
                var safeFile = Path.GetFileName(name);
                if (string.IsNullOrEmpty(safeFile)) continue;
                await File.WriteAllBytesAsync(Path.Combine(dir, safeFile), bytes);
            }

        await MutateCustomsAsync(s =>
        {
            s.Customs.RemoveAll(c => string.Equals(c.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
            s.Customs.Add(new CustomSkinEntry(
                internalName,
                string.IsNullOrWhiteSpace(displayName) ? internalName : displayName,
                packId ?? string.Empty));
        });
    }

    public async Task<InjectResultDto> RemoveCustomAsync(
        string internalName, ISelectedGunsInstaller.EmitProgress emit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(internalName))
            return Fail("internalName required", emit);

        using var _mtx = await UpdateRpfMutex.AcquireAsync("gunskin-remove", ct);
        emit("starting", 0, null, null);

        bool removed = false;
        await MutateCustomsAsync(s =>
        {
            var before = s.Customs.Count + s.AnimDetails.Count;
            s.Customs.RemoveAll(c => string.Equals(c.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
            s.AnimDetails.RemoveAll(a => string.Equals(a.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
            removed = (s.Customs.Count + s.AnimDetails.Count) != before;
        });
        try { var dir = CustomCacheDir(internalName); if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
        try { var dir = AnimCacheDir(internalName);   if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }

        if (!removed)
        {
            emit("done", 100, null, Loc.T("install.skinWasNotInListSkip"));
            return new InjectResultDto(true, Loc.T("install.wasNotInList"), null);
        }

        return await RebuildAsync(emit, ct);
    }

    public async Task ForgetCustomAsync(string internalName)
    {
        if (string.IsNullOrWhiteSpace(internalName)) return;
        await MutateCustomsAsync(s =>
        {
            s.Customs.RemoveAll(c => string.Equals(c.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
            s.AnimDetails.RemoveAll(a => string.Equals(a.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
        });
        try { var d = CustomCacheDir(internalName); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        try { var d = AnimCacheDir(internalName);   if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
    }

    public async Task<CustomSkinAppliedDto?> GetCustomSkinAsync()
    {
        var e = (await LoadCustomsAsync()).Customs.FirstOrDefault();
        return e is null ? null : new CustomSkinAppliedDto(e.InternalName, e.DisplayName, e.PackId);
    }

    public async Task SetCustomSkinAsync(string internalName, string displayName, string packId)
    {
        if (string.IsNullOrWhiteSpace(internalName)) return;
        await MutateCustomsAsync(s =>
        {
            s.Customs.RemoveAll(c => string.Equals(c.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
            s.Customs.Add(new CustomSkinEntry(
                internalName,
                string.IsNullOrWhiteSpace(displayName) ? internalName : displayName,
                packId ?? string.Empty));
        });
    }

    public async Task<InjectResultDto> RemoveCustomSkinAsync(
        ISelectedGunsInstaller.EmitProgress emit, CancellationToken ct)
    {
        using var _mtx = await UpdateRpfMutex.AcquireAsync("gunskin-remove-all", ct);
        List<CustomSkinEntry> entries;
        await _customsLock.WaitAsync(ct);
        try { entries = new List<CustomSkinEntry>((await ReadCustomsUnlocked()).Customs); }
        finally { _customsLock.Release(); }

        await MutateCustomsAsync(s => s.Customs.Clear());
        foreach (var e in entries)
            try { var d = CustomCacheDir(e.InternalName); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }

        return await RebuildAsync(emit, ct);
    }

    public async Task<VerifyReport> VerifyAsync()
    {
        var state = await LoadStateAsync();
        var gtaPath = await _resolveGtaPath();
        var targetDlc = string.IsNullOrEmpty(gtaPath) ? null : Path.Combine(gtaPath, TARGET_DLC_REL_PATH);

        if (state.Guns.Count == 0)
            return new VerifyReport(true, 0, targetDlc != null && File.Exists(targetDlc), false, null, null,
                Loc.T("verify.noSelectedGuns"));

        if (targetDlc is null || !File.Exists(targetDlc))
            return new VerifyReport(false, state.Guns.Count, false, false, state.LastInjectedSha256, null,
                Loc.T("verify.targetDlcMissingButGunsSelected"));

        var gunpackState = await ReadInstalledGunpackStateAsync();
        if (!string.IsNullOrWhiteSpace(gunpackState?.ActiveGunpackId))
        {
            return new VerifyReport(true, state.Guns.Count, true, false, null, null,
                Loc.T("verify.activePackGunsMerged", ("pack", gunpackState!.ActiveGunpackName ?? gunpackState.ActiveGunpackId)));
        }

        var present =
            TargetDlcEditor.RpfExistsInsideTarget(targetDlc, TargetDlcEditor.MIAMI_WEAPON_RPF_NAME) ||
            TargetDlcEditor.RpfExistsInsideTarget(targetDlc, SELECTED_RPF_NAME);
        if (!present)
            return new VerifyReport(false, state.Guns.Count, true, false, state.LastInjectedSha256, null,
                Loc.T("verify.selectedRpfMissingInsideDlc"));

        var actual =
            TargetDlcEditor.ComputeEmbeddedRpfSha256(targetDlc, TargetDlcEditor.MIAMI_WEAPON_RPF_NAME) ??
            TargetDlcEditor.ComputeEmbeddedRpfSha256(targetDlc, SELECTED_RPF_NAME);
        var ok = string.Equals(actual, state.LastInjectedSha256, StringComparison.OrdinalIgnoreCase);
        return new VerifyReport(ok, state.Guns.Count, true, true, state.LastInjectedSha256, actual,
            ok ? Loc.T("verify.integrityOk", ("count", state.Guns.Count), ("sha", actual?[..8] ?? "-"))
               : Loc.T("verify.shaMismatch"));
    }

    public async Task<InjectResultDto> UninstallAllAsync(
        ISelectedGunsInstaller.EmitProgress emit,
        CancellationToken ct)
    {
        emit("starting", 0, null, null);
        await MutateStateAsync(state => state.Guns.Clear());

        return await RebuildAsync(emit, ct);
    }

    private async Task MutateStateAsync(Action<SelectedGunsState> mutate)
    {
        await _stateLock.WaitAsync();
        try
        {
            var state = await ReadAsync();
            mutate(state);
            await WriteAsync(state);
        }
        finally { _stateLock.Release(); }
    }

    private async Task<SelectedGunsState> LoadStateAsync()
    {
        await _stateLock.WaitAsync();
        try { return await ReadAsync(); }
        finally { _stateLock.Release(); }
    }

    private async Task<SelectedGunsState> ReadAsync()
    {
        if (!File.Exists(_stateFilePath)) return new SelectedGunsState();
        try
        {
            await using var fs = File.OpenRead(_stateFilePath);
            return await JsonSerializer.DeserializeAsync<SelectedGunsState>(fs, Json)
                   ?? new SelectedGunsState();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[selguns.state] read FAIL: {ex.Message} - empty");
            return new SelectedGunsState();
        }
    }

    private async Task WriteAsync(SelectedGunsState state)
    {
        await using var fs = File.Create(_stateFilePath);
        await JsonSerializer.SerializeAsync(fs, state, Json);
    }

    private static InjectResultDto Fail(
        string message, ISelectedGunsInstaller.EmitProgress emit)
    {
        emit("error", 0, message, null);
        return new InjectResultDto(false, message, null);
    }

    private async Task<string?> EnsureEmptyTemplateCachedAsync(
        ISelectedGunsInstaller.EmitProgress emit,
        CancellationToken ct)
    {
        var cacheDir = Path.Combine(
            MiamiGraphics.Core.System.WorkDirDefaults.ResultBaseDir, "backup");
        Directory.CreateDirectory(cacheDir);
        var cachePath = Path.Combine(cacheDir, "miami_empty.rpf");

        if (File.Exists(cachePath))
        {
            if (IsWellFormedRpf(cachePath))
            {
                Debug.WriteLine($"[selguns.empty-template] cache hit: {cachePath}");
                return cachePath;
            }
            Debug.WriteLine($"[selguns.empty-template] cached template failed RPF7 integrity check - evicting and refetching");
            try { File.Delete(cachePath); } catch { }
        }

        var cfg = await _adminConfig.GetAsync();
        var r2 = (cfg.R2PublicUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrEmpty(r2))
        {
            Debug.WriteLine("[selguns.empty-template] R2PublicUrl not configured - falling back to shipped local template");
            return LocalEmptyTemplateOrNull();
        }
        const string CANONICAL_VERSION = "1.0.3751.0";
        var url = $"{r2}/gta_versions/{CANONICAL_VERSION}/miami_empty.rpf";
        var effectiveUrl = await MirrorSelector.RewriteUrlAsync(url, ct);
        Debug.WriteLine($"[selguns.empty-template] downloading {url}");
        if (!string.Equals(url, effectiveUrl, StringComparison.OrdinalIgnoreCase))
            Debug.WriteLine($"[selguns.empty-template] mirror rewrite: {url} -> {effectiveUrl}");
        emit("downloading_empty_template", 2, null, Loc.T("install.downloadingEmptyTemplate"));

        var partPath = cachePath + ".part";
        try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }

        try
        {
            using var http = HttpClientFactory.CreateFragmenting(TimeSpan.FromMinutes(2));
            using var resp = await http.GetAsync(effectiveUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var expectedLen = resp.Content.Headers.ContentLength;
            long written;
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await src.CopyToAsync(dst, ct);
                written = dst.Length;
            }

            if (expectedLen.HasValue && written != expectedLen.Value)
                throw new InvalidOperationException(
                    $"miami_empty.rpf оборван: получено {written} из {expectedLen.Value} байт (DPI/обрыв канала).");
            if (!IsWellFormedRpf(partPath))
                throw new InvalidOperationException(
                    $"miami_empty.rpf повреждён: не проходит проверку RPF7 ({written} байт).");

            if (File.Exists(cachePath)) { try { File.Delete(cachePath); } catch { } }
            File.Move(partPath, cachePath);
            Debug.WriteLine($"[selguns.empty-template] cached at {cachePath} ({new FileInfo(cachePath).Length} bytes)");
            return cachePath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[selguns.empty-template] download FAIL {url}: {ex.Message} - falling back to shipped local template");
            try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }
            return LocalEmptyTemplateOrNull();
        }
    }

    private const uint RPF7_MAGIC = 0x52504637;
    private const long MIN_EMPTY_TEMPLATE_BYTES = 2048;

    private static bool IsWellFormedRpf(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length < MIN_EMPTY_TEMPLATE_BYTES) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> hdr = stackalloc byte[4];
            return fs.Read(hdr) == 4 && BitConverter.ToUInt32(hdr) == RPF7_MAGIC;
        }
        catch { return false; }
    }

    private static string? LocalEmptyTemplateOrNull()
    {
        var local = WalkUpForFile(@"templates\miami_empty.rpf")
                 ?? WalkUpForFile(@"MiamiGraphics.Core\templates\miami_empty.rpf");
        if (local != null && File.Exists(local) && new FileInfo(local).Length > 0)
        {
            Debug.WriteLine($"[selguns.empty-template] using shipped local template: {local}");
            return local;
        }
        Debug.WriteLine("[selguns.empty-template] no shipped local template found either");
        return null;
    }

    private static string? WalkUpForFile(string rel)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var p = Path.Combine(dir, rel);
            if (File.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string? WalkUpForDirectory(string rel)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var p = Path.Combine(dir, rel);
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static string ComputeFileSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: false);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private static void _markUnused() { _ = (object?)ComputeFileSha256; }

    private sealed class TemplateRow
    {
        public string? GunsRpfUrl    { get; set; }
        public string? GunsRpfSha256 { get; set; }
        public long?   GunsRpfSize   { get; set; }
    }

    private async Task<bool> TrySeedTemplateFromR2Async(string destPath, CancellationToken ct)
    {
        if (_supabase is null)
        {
            Debug.WriteLine("[selected.seed] no SupabaseClient injected - cannot auto-seed");
            return false;
        }
        try
        {

            var rows = await _supabase.SelectAsync<TemplateRow>(
                "gta_versions",
                "select=guns_rpf_url,guns_rpf_sha256,guns_rpf_size&guns_rpf_url=not.is.null&limit=1",
                ct);
            var row = rows.FirstOrDefault();
            var url = row?.GunsRpfUrl;
            if (string.IsNullOrWhiteSpace(url))
            {
                Debug.WriteLine("[selected.seed] no guns_rpf_url in gta_versions - bail");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            var partPath = destPath + ".part";

            long expectedSize = row?.GunsRpfSize ?? 0;
            string? expectedSha = row?.GunsRpfSha256;

            var candidates = await BuildSeedCandidatesAsync(url, ct);

            using var http = HttpClientFactory.CreateFragmenting(TimeSpan.FromMinutes(20));
            Exception? lastErr = null;

            for (int ci = 0; ci < candidates.Count; ci++)
            {
                var candidate = candidates[ci];
                try
                {
                    try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }

                    using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    headerCts.CancelAfter(TimeSpan.FromSeconds(90));
                    using (var resp = await http.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, headerCts.Token))
                    {
                        resp.EnsureSuccessStatusCode();
                        await using var src = await resp.Content.ReadAsStreamAsync(ct);
                        await using var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
                        await GuardedDownload.CopyAsync(
                            src, dst, onBytes: null, ct: ct,
                            throttleGuard: ci < candidates.Count - 1,
                            expectedTotal: expectedSize);
                    }

                    if (expectedSize > 0 && new FileInfo(partPath).Length != expectedSize)
                        throw new IOException($"size {new FileInfo(partPath).Length} != expected {expectedSize}");
                    if (!string.IsNullOrEmpty(expectedSha))
                    {
                        var dlSha = await Task.Run(() => ComputeFileSha256(partPath), ct);
                        if (!string.Equals(dlSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException($"SHA mismatch (got {dlSha[..Math.Min(16, dlSha.Length)]}…)");
                    }

                    try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                    File.Move(partPath, destPath);
                    Debug.WriteLine($"[selected.seed] template seeded from {candidate} at {destPath}");
                    return true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    lastErr = ex;
                    Debug.WriteLine($"[selected.seed] candidate {candidate} failed: {ex.GetType().Name}: {ex.Message}");
                    try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }
                }
            }

            Debug.WriteLine($"[selected.seed] all mirrors failed. last: {lastErr?.Message}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[selected.seed] FAIL ({ex.GetType().Name}): {ex.Message}");
            return false;
        }
    }

    private static async Task<List<string>> BuildSeedCandidatesAsync(string url, CancellationToken ct)
    {
        var list = new List<string>();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) { list.Add(url); return list; }

        var ours = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "miamigraphicsstorage.uk", "cdn.miamigraphicsstorage.uk",
            "ru.miamigraphicsstorage.uk", "pub-f3641b214c164277964c1e92c826b19b.r2.dev",
        };
        if (!ours.Contains(uri.Host)) { list.Add(url); return list; }

        void Add(string host)
        {
            var u = new UriBuilder(uri) { Scheme = "https", Host = host, Port = -1 }.Uri.ToString();
            if (!list.Any(x => string.Equals(x, u, StringComparison.OrdinalIgnoreCase))) list.Add(u);
        }

        try
        {
            var rewritten = await MirrorSelector.RewriteUrlAsync(url, ct);
            if (Uri.TryCreate(rewritten, UriKind.Absolute, out var rw) && ours.Contains(rw.Host))
                Add(rw.Host);
        }
        catch {}

        foreach (var h in new[]
                 {
                     "cdn.miamigraphicsstorage.uk", "miamigraphicsstorage.uk",
                     "pub-f3641b214c164277964c1e92c826b19b.r2.dev", "ru.miamigraphicsstorage.uk",
                 })
            Add(h);

        return list;
    }
}
