using MiamiGraphics.Bridge;
using MiamiGraphics.Shell.Services;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace MiamiGraphics.Shell.Bridge;

internal sealed class WebViewBridgeHost
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static string? Opt(JsonElement? payload, string name)
        => payload is { } p && p.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    private readonly CoreWebView2 _webView;
    private readonly IAppBridge _bridge;
    private readonly Dictionary<string, Func<JsonElement?, Task<object?>>> _handlers;

    private static readonly HashSet<string> CriticalCommands = new(StringComparer.Ordinal)
    {
        "backupRunFull",
        "backupRestoreClean",
        "backupRestoreSnapshot",
        "adminQueueRun",
        "adminInject",
        "adminInjectFromCatalog",
        "adminRestoreCleanUpdate",
        "reduxInstall",
        "reduxInstallForceClean",
        "reduxInstallPreserve",
        "reduxInstallCancel",
        "reduxApplyArmorSwap",
        "reduxClearArmor",
        "reduxUninstall",
        "reduxUninstallForceClean",
        "reduxUninstallPreserve",
        "reduxCustomizeApply",
        "armorInstallStandalone",
        "armorLibraryInstall",
        "gunpackInstallAll",
        "gunpackInstallSelected",
        "gunpackUninstall",
        "selectedGunsInstall",
        "selectedGunsRemove",
        "selectedGunsRebuild",
        "selectedGunsUninstallAll",
        "customGunInstall",
        "workshopApplyToGame",
        "customSkinRemove",
        "userGunpackInstall",
        "hntCodeApply",
        "reduxApplyMinimap",
        "timecycleInstall",
        "timecycleRestoreVanilla",
        "treesInstall",
        "treesRestore",
        "roadsInstall",
        "roadsRestore",
        "graphicsModRestore",
        "minimapSetRangeRings",
        "minimapRestoreVanilla",
        "minimapLayoutApply",
        "minimapLayoutApplyCustom",
        "minimapApplyTweaks",
        "minimapInstallFont",
        "minimapRestoreFont",
        "hotSwapSetEnabled",
        "hotSwapArmNow",
        "hotSwapDisarmNow",
        "hotSwapRebuild",
        "factoryResetAndRestart",
        "launcherUninstall",
        "setGtaPathOverride",
        "clearGtaPathOverride",
        "otherSetZalazy",
        "otherRemoveForeignOverlay",
        "otherSetFastJoin",
        "otherSetGreenZone",
        "otherSetCarLogos",
        "otherSetRukzak",
        "otherSetSmoke",
        "otherSetNoTracer",
        "otherSetTracerStudio",
        "bigMapInstall",
        "bigMapUninstall",
        "improvementInstall",
        "improvementRemove",
        "reduxApplyReticle",
        "reticleApplyCustom",
        "reduxResetCustomization",
        "soundPackInstall",
        "soundPackUninstall",
        "gtaPresetApply",
        "gtaSettingsApplyFromUrl",
        "gtaSettingsWrite",
        "optimizationApply",
        "legitCheckRedux",
        "legitCheckUpdateRpf",
    };

    public WebViewBridgeHost(CoreWebView2 webView, IAppBridge bridge)
    {
        _webView = webView;
        _bridge = bridge;
        if (bridge is AppBridge appBridge) appBridge.AttachEventEmitter(EmitEvent);
        _handlers = BuildHandlers();
        _webView.WebMessageReceived += OnWebMessageReceived;
    }

    private void EmitEvent(string eventName, object? payload)
    {
        var envelope = new { kind = "event", name = eventName, data = payload };
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        PostJsonOnUiThread(json);
    }

    private void PostJsonOnUiThread(string json)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            try { _webView.PostWebMessageAsJson(json); }
            catch (Exception ex) { Debug.WriteLine($"[Bridge] PostWebMessageAsJson failed: {ex.Message}"); }
            return;
        }
        dispatcher.BeginInvoke(() =>
        {
            try { _webView.PostWebMessageAsJson(json); }
            catch (Exception ex) { Debug.WriteLine($"[Bridge] PostWebMessageAsJson (dispatched) failed: {ex.Message}"); }
        });
    }

    private Dictionary<string, Func<JsonElement?, Task<object?>>> BuildHandlers()
    {
        Func<JsonElement?, Task<object?>> todo = _ =>
            throw new NotImplementedException("Not implemented yet");

        return new Dictionary<string, Func<JsonElement?, Task<object?>>>(StringComparer.Ordinal)
        {

            ["getSystemInfo"] = async _ => await _bridge.GetSystemInfoAsync(),
            ["getAppVersion"] = async _ => await _bridge.GetAppVersionAsync(),
            ["pcDiagReport"] = async _ => await _bridge.PcDiagReportAsync(),
            ["pcDiagApply"] = async payload =>
                await _bridge.PcDiagApplyAsync(payload?.GetProperty("id").GetString() ?? ""),
            ["pcDiagRevert"] = async payload =>
                await _bridge.PcDiagRevertAsync(payload?.GetProperty("id").GetString() ?? ""),
            ["pcDiagJournal"] = async _ => await _bridge.PcDiagJournalAsync(),
            ["pcDiagTweaks"] = async _ => await _bridge.PcDiagTweaksAsync(),
            ["pcDiagAi"] = async payload =>
            {
                var userId = payload?.GetProperty("userId").GetString() ?? "";
                string? question = null;
                if (payload is JsonElement pj && pj.TryGetProperty("question", out var q) && q.ValueKind == JsonValueKind.String)
                    question = q.GetString();
                List<PcDiagAiMsgDto>? history = null;
                if (payload is JsonElement ph && ph.TryGetProperty("history", out var h) && h.ValueKind == JsonValueKind.Array)
                {
                    history = new List<PcDiagAiMsgDto>();
                    foreach (var m in h.EnumerateArray())
                        history.Add(new PcDiagAiMsgDto(
                            m.GetProperty("role").GetString() ?? "user",
                            m.GetProperty("text").GetString() ?? ""));
                }
                return await _bridge.PcDiagAiAsync(userId, question, history);
            },
            ["assetCacheContains"] = async payload =>
            {
                var urls = payload?.GetProperty("urls").EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList() ?? new List<string>();
                return await _bridge.AssetCacheContainsAsync(urls);
            },
            ["gunpackAllGunPreviewUrls"] = async _ => await _bridge.GunpackAllGunPreviewUrlsAsync(),
            ["assetCachePrewarm"] = async payload =>
            {
                var urls = payload?.GetProperty("urls").EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList() ?? new List<string>();
                return await _bridge.AssetCachePrewarmAsync(urls);
            },
            ["authenticateGuest"] = async _ => await _bridge.AuthenticateGuestAsync(),
            ["getAppSettings"] = async _ => await _bridge.GetAppSettingsAsync(),
            ["saveAppSettings"] = async payload =>
            {
                if (payload is null)
                    throw new ArgumentException("saveAppSettings payload required");
                var settings = payload.Value.Deserialize<AppSettingsDto>(JsonOptions)
                    ?? throw new ArgumentException("saveAppSettings payload could not be parsed");
                await _bridge.SaveAppSettingsAsync(settings);
                return null;
            },
            ["setUiLanguage"] = async payload =>
            {
                var lang = payload is { } p
                        && p.TryGetProperty("lang", out var v)
                        && v.ValueKind == JsonValueKind.String
                    ? v.GetString()
                    : null;
                await _bridge.SetUiLanguageAsync(lang ?? "ru");
                return null;
            },
            ["windowMinimize"] = async _ => { await _bridge.WindowMinimizeAsync(); return null; },
            ["windowMaximize"] = async _ => { await _bridge.WindowMaximizeAsync(); return null; },
            ["windowClose"] = async _ => { await _bridge.WindowCloseAsync(); return null; },
            ["windowSetFullscreen"] = async payload =>
            {
                var on = payload is { } p && p.TryGetProperty("on", out var v) && v.ValueKind == JsonValueKind.True;
                await _bridge.WindowSetFullscreenAsync(on);
                return null;
            },
            ["windowStartDrag"] = async _ => { await _bridge.WindowStartDragAsync(); return null; },
            ["openFolderDialog"] = async _ => await _bridge.OpenFolderDialogAsync(),
            ["cacheSettingsGet"] = async _ => await _bridge.CacheSettingsGetAsync(),
            ["cacheSettingsSet"] = async payload =>
            {
                var enabled = payload?.GetProperty("enabled").GetBoolean() ?? true;
                string? rootOverride = payload is not null
                    && payload.Value.TryGetProperty("rootOverride", out var r)
                    && r.ValueKind == JsonValueKind.String
                        ? r.GetString() : null;
                return await _bridge.CacheSettingsSetAsync(enabled, rootOverride);
            },
            ["cacheLimitSet"] = async payload =>
            {
                var limit = payload?.GetProperty("limitBytes").GetInt64() ?? 0;
                return await _bridge.CacheLimitSetAsync(limit);
            },
            ["cacheCleanupNow"] = async _ => await _bridge.CacheCleanupNowAsync(),
            ["dataRootMove"] = async payload =>
            {
                var target = payload?.GetProperty("targetDir").GetString() ?? string.Empty;
                return await _bridge.DataRootMoveAsync(target);
            },
            ["dataRootMoveCancel"] = async _ => { await _bridge.DataRootMoveCancelAsync(); return null; },
            ["openLogsFolder"] = async _ => { await _bridge.OpenLogsFolderAsync(); return null; },
            ["validateGtaPath"] = async payload =>
            {
                var path = payload?.GetProperty("path").GetString() ?? string.Empty;
                return await _bridge.ValidateGtaPathAsync(path);
            },
            ["getGtaPathInfo"] = async _ => await _bridge.GetGtaPathInfoAsync(),
            ["setGtaPathOverride"] = async payload =>
            {
                var path = payload?.GetProperty("path").GetString() ?? string.Empty;
                return await _bridge.SetGtaPathOverrideAsync(path);
            },
            ["clearGtaPathOverride"] = async _ => await _bridge.ClearGtaPathOverrideAsync(),
            ["scanGunpackBatchFolder"] = async payload =>
            {
                var parentPath = payload?.GetProperty("parentPath").GetString() ?? string.Empty;
                return await _bridge.ScanGunpackBatchFolderAsync(parentPath);
            },
            ["authenticateUser"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("authenticateUser payload required");
                var p = payload.Value;
                var login = p.GetProperty("login").GetString() ?? string.Empty;
                var password = p.GetProperty("password").GetString() ?? string.Empty;
                string? totp = null;
                if (p.TryGetProperty("totp", out var totpEl) && totpEl.ValueKind != System.Text.Json.JsonValueKind.Null)
                    totp = totpEl.GetString();
                return await _bridge.AuthenticateUserAsync(login, password, totp);
            },
#if ADMIN
            ["adminWebAuthenticate"] = async _ => await _bridge.AdminWebAuthenticateAsync(),
#endif
            ["getServerStatus"] = async _ => await _bridge.GetServerStatusAsync(),

            ["forceExit"] = _ =>
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (System.Windows.Application.Current?.MainWindow is MiamiGraphics.Shell.MainWindow mw)
                        mw.RequestForceExit();
                }));
                return Task.FromResult<object?>(null);
            },
            ["appUpdateCheck"] = async _ => await _bridge.AppUpdateCheckAsync(),
            ["appUpdateInstall"] = async payload =>
            {
                var version = payload?.GetProperty("version").GetString() ?? throw new ArgumentException("version required");
                return await _bridge.AppUpdateInstallAsync(version);
            },
            ["requestPasswordReset"] = async payload =>
            {
                var email = payload?.GetProperty("email").GetString() ?? throw new ArgumentException("email required");
                await _bridge.RequestPasswordResetAsync(email);
                return null;
            },
            ["consumePasswordReset"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("consumePasswordReset payload required");
                var p           = payload.Value;
                var code        = p.GetProperty("code").GetString()        ?? throw new ArgumentException("code required");
                var newPassword = p.GetProperty("newPassword").GetString() ?? throw new ArgumentException("newPassword required");
                await _bridge.ConsumePasswordResetAsync(code, newPassword);
                return null;
            },
            ["getUserProfile"] = async payload =>
            {
                var userId = payload?.GetProperty("userId").GetString() ?? throw new ArgumentException("userId required");
                return await _bridge.GetUserProfileAsync(userId);
            },
            ["updateUserProfile"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("updateUserProfile payload required");
                var p         = payload.Value;
                var userId    = p.GetProperty("userId").GetString()   ?? throw new ArgumentException("userId required");
                var username  = p.GetProperty("username").GetString() ?? throw new ArgumentException("username required");
                string? avatar = null;
                if (p.TryGetProperty("avatarUrl", out var a) && a.ValueKind != System.Text.Json.JsonValueKind.Null)
                    avatar = a.GetString();
                return await _bridge.UpdateUserProfileAsync(userId, username, avatar);
            },
            ["uploadAvatar"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("uploadAvatar payload required");
                var p         = payload.Value;
                var userId    = p.GetProperty("userId").GetString()    ?? throw new ArgumentException("userId required");
                var localPath = p.GetProperty("localPath").GetString() ?? throw new ArgumentException("localPath required");
                return await _bridge.UploadAvatarAsync(userId, localPath);
            },
            ["installHistoryList"] = async payload =>
            {
                var userId = payload?.GetProperty("userId").GetString() ?? throw new ArgumentException("userId required");
                return await _bridge.InstallHistoryListAsync(userId);
            },
            ["installRecord"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("installRecord payload required");
                var p          = payload.Value;
                var userId     = p.GetProperty("userId").GetString()   ?? throw new ArgumentException("userId required");
                var reduxId    = p.GetProperty("reduxId").GetString()  ?? throw new ArgumentException("reduxId required");
                var name       = p.GetProperty("name").GetString()     ?? string.Empty;
                var author     = p.GetProperty("author").GetString()   ?? string.Empty;
                string? previewUrl = null;
                if (p.TryGetProperty("previewUrl", out var pu) && pu.ValueKind != System.Text.Json.JsonValueKind.Null)
                    previewUrl = pu.GetString();
                return await _bridge.InstallRecordAsync(userId, reduxId, name, author, previewUrl);
            },
            ["registerRequest"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("registerRequest payload required");
                var p = payload.Value;
                var email    = p.GetProperty("email").GetString()    ?? string.Empty;
                var username = p.GetProperty("username").GetString() ?? string.Empty;
                var password = p.GetProperty("password").GetString() ?? string.Empty;
                await _bridge.RegisterRequestAsync(email, username, password);
                return null;
            },
            ["registerConfirm"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("registerConfirm payload required");
                var p     = payload.Value;
                var email = p.GetProperty("email").GetString() ?? string.Empty;
                var code  = p.GetProperty("code").GetString()  ?? string.Empty;
                return await _bridge.RegisterConfirmAsync(email, code);
            },
            ["modmakersList"] = async payload =>
            {
                var q = payload?.TryGetProperty("q", out var v) == true ? v.GetString() : null;
                return await _bridge.ModmakersListAsync(q);
            },
            ["modmakerDetail"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("modmakerDetail payload required");
                return await _bridge.ModmakerDetailAsync(payload.Value.GetProperty("code").GetString() ?? string.Empty);
            },
            ["modmakerFollow"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("modmakerFollow payload required");
                var p = payload.Value;
                return await _bridge.ModmakerFollowAsync(
                    p.GetProperty("code").GetString() ?? string.Empty,
                    !p.TryGetProperty("on", out var on) || on.GetBoolean());
            },
            ["modmakerFeed"] = async payload =>
                await _bridge.ModmakerFeedAsync(payload?.TryGetProperty("notify", out var n) == true && n.GetBoolean()),
            ["modmakerMap"] = async _ => await _bridge.ModmakerMapAsync(),
            ["modmakerCanEdit"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("modmakerCanEdit payload required");
                return await _bridge.ModmakerCanEditAsync(payload.Value.GetProperty("code").GetString() ?? string.Empty);
            },
            ["installerPromo"] = async _ => await _bridge.InstallerPromoAsync(),
            ["checkPromo"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("checkPromo payload required");
                var code = payload.Value.GetProperty("code").GetString() ?? string.Empty;
                return await _bridge.CheckPromoAsync(code);
            },
            ["attachReferral"] = async payload =>
            {
                var code = payload is null ? string.Empty
                    : (payload.Value.TryGetProperty("code", out var c) ? (c.GetString() ?? string.Empty) : string.Empty);
                return await _bridge.AttachReferralAsync(code);
            },
            ["betaCodeCheck"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("betaCodeCheck payload required");
                var code = payload.Value.GetProperty("code").GetString() ?? string.Empty;
                return await _bridge.BetaCodeCheckAsync(code);
            },
            ["betaRedeem"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("betaRedeem payload required");
                var code = payload.Value.GetProperty("code").GetString() ?? string.Empty;
                return await _bridge.BetaRedeemAsync(code);
            },
            ["betaCheck"] = async _ => await _bridge.BetaCheckAsync(),
            ["betaEnabled"] = async _ => await _bridge.BetaEnabledAsync(),
            ["activityLog"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("activityLog payload required");
                var et = payload.Value.GetProperty("eventType").GetString() ?? string.Empty;
                var dt = payload.Value.TryGetProperty("detail", out var d) ? (d.GetString() ?? string.Empty) : string.Empty;
                var iid = payload.Value.TryGetProperty("itemId", out var i) ? i.GetString() : null;
                return await _bridge.ActivityLogAsync(et, dt, iid);
            },
            ["changePasswordRequest"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("changePasswordRequest payload required");
                var p           = payload.Value;
                var userId      = p.GetProperty("userId").GetString()      ?? throw new ArgumentException("userId required");
                var oldPassword = p.GetProperty("oldPassword").GetString() ?? throw new ArgumentException("oldPassword required");
                var newPassword = p.GetProperty("newPassword").GetString() ?? throw new ArgumentException("newPassword required");
                await _bridge.ChangePasswordRequestAsync(userId, oldPassword, newPassword);
                return null;
            },
            ["changePasswordConfirm"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("changePasswordConfirm payload required");
                var p      = payload.Value;
                var userId = p.GetProperty("userId").GetString() ?? throw new ArgumentException("userId required");
                var code   = p.GetProperty("code").GetString()   ?? throw new ArgumentException("code required");
                await _bridge.ChangePasswordConfirmAsync(userId, code);
                return null;
            },
            ["changeEmailRequest"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("changeEmailRequest payload required");
                var p               = payload.Value;
                var userId          = p.GetProperty("userId").GetString()          ?? throw new ArgumentException("userId required");
                var currentPassword = p.GetProperty("currentPassword").GetString() ?? throw new ArgumentException("currentPassword required");
                var newEmail        = p.GetProperty("newEmail").GetString()        ?? throw new ArgumentException("newEmail required");
                await _bridge.ChangeEmailRequestAsync(userId, currentPassword, newEmail);
                return null;
            },
            ["changeEmailConfirm"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("changeEmailConfirm payload required");
                var p      = payload.Value;
                var userId = p.GetProperty("userId").GetString() ?? throw new ArgumentException("userId required");
                var code   = p.GetProperty("code").GetString()   ?? throw new ArgumentException("code required");
                return await _bridge.ChangeEmailConfirmAsync(userId, code);
            },
            ["backupGetStatus"] = async _ => await _bridge.BackupGetStatusAsync(),
            ["backupRunFull"] = async _ => await _bridge.BackupRunFullAsync(),
            ["backupCancel"] = async _ => await _bridge.BackupCancelAsync(),
            ["backupRestoreClean"] = async _ => await _bridge.BackupRestoreCleanAsync(),
            ["backupRestoreSnapshot"] = async _ => await _bridge.BackupRestoreSnapshotAsync(),
            ["killProcessesByPid"] = async payload =>
            {
                if (payload is null) return 0;
                var arr = payload.Value.GetProperty("pids");
                var pids = new List<int>();
                foreach (var el in arr.EnumerateArray())
                    if (el.TryGetInt32(out var pid)) pids.Add(pid);
                return await _bridge.KillProcessesByPidAsync(pids.ToArray());
            },
            ["factoryResetAndRestart"] = async _ =>
            {
                await _bridge.FactoryResetAndRestartAsync();
                return null;
            },
            ["launcherUninstall"] = async _ =>
            {
                await _bridge.LauncherUninstallAsync();
                return null;
            },

            ["openFileDialog"] = async payload =>
            {
                var desc = payload?.GetProperty("filterDescription").GetString();
                var pat  = payload?.GetProperty("filterPattern").GetString();
                return await _bridge.OpenFileDialogAsync(desc, pat);
            },

            ["openFileDialogMulti"] = async payload =>
            {
                var desc = payload?.GetProperty("filterDescription").GetString();
                var pat  = payload?.GetProperty("filterPattern").GetString();
                return await _bridge.OpenFileDialogMultiAsync(desc, pat);
            },

#if ADMIN
            ["adminConfigGet"] = async _ => await _bridge.AdminConfigGetAsync(),
#endif
#if ADMIN
            ["adminConfigSave"] = async payload =>
            {
                var cfg = payload!.Value.Deserialize<AdminConfigDto>(JsonOptions)
                          ?? throw new ArgumentException("adminConfigSave payload required");
                await _bridge.AdminConfigSaveAsync(cfg);
                return null;
            },
#endif
#if ADMIN
            ["adminConfigTestR2"] = async payload =>
            {
                var cfg = payload!.Value.Deserialize<AdminConfigDto>(JsonOptions)
                          ?? throw new ArgumentException("adminConfigTestR2 payload required");
                return await _bridge.AdminConfigTestR2Async(cfg);
            },
#endif

#if ADMIN
            ["adminReduxAnalyze"] = async payload =>
            {
                var src = payload?.GetProperty("sourcePath").GetString() ?? throw new ArgumentException("sourcePath required");
                return await _bridge.AdminReduxAnalyzeAsync(src);
            },
#endif

#if ADMIN
            ["adminQueueList"]   = async _ => await _bridge.AdminQueueListAsync(),
#endif
#if ADMIN
            ["adminQueueAdd"]    = async payload =>
            {
                var item = payload!.Value.Deserialize<QueueItemDto>(JsonOptions)
                           ?? throw new ArgumentException("adminQueueAdd payload required");
                return await _bridge.AdminQueueAddAsync(item);
            },
#endif
#if ADMIN
            ["adminQueueRemove"] = async payload =>
            {
                var id = payload?.GetProperty("tempId").GetString() ?? throw new ArgumentException("tempId required");
                await _bridge.AdminQueueRemoveAsync(id);
                return null;
            },
#endif
#if ADMIN
            ["adminQueueRun"]    = async _ => { await _bridge.AdminQueueRunAsync(); return null; },
#endif
#if ADMIN
            ["adminQueueCancel"] = async _ => { await _bridge.AdminQueueCancelAsync(); return null; },
#endif
#if ADMIN
            ["adminRebuildReduxComponents"] = async _ => await _bridge.AdminRebuildReduxComponentsAsync(),
#endif
#if ADMIN
            ["adminRecalculateReduxPatchSizes"] = async _ => await _bridge.AdminRecalculateReduxPatchSizesAsync(),
#endif

#if ADMIN
            ["adminCatalogList"] = async payload =>
            {
                var search = payload?.TryGetProperty("search", out var s) == true ? s.GetString() : null;
                var server = payload?.TryGetProperty("server", out var sv) == true ? sv.GetString() : null;
                var status = payload?.TryGetProperty("status", out var st) == true ? st.GetString() : null;
                return await _bridge.AdminCatalogListAsync(search, server, status);
            },
#endif
#if ADMIN
            ["adminCatalogUpdate"] = async payload =>
            {
                var item = payload!.Value.Deserialize<ReduxItemDto>(JsonOptions)
                           ?? throw new ArgumentException("adminCatalogUpdate payload required");
                await _bridge.AdminCatalogUpdateAsync(item);
                return null;
            },
#endif
#if ADMIN
            ["adminCatalogDelete"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                await _bridge.AdminCatalogDeleteAsync(id);
                return null;
            },
#endif
#if ADMIN
            ["adminWipeAll"] = async payload =>
            {
                var category = payload?.GetProperty("category").GetString()
                               ?? throw new ArgumentException("category required");
                return await _bridge.AdminWipeAllAsync(category);
            },
#endif

            ["reduxVersions"] = async payload =>
            {
                var reduxId = payload?.GetProperty("reduxId").GetString()
                              ?? throw new ArgumentException("reduxId required");
                return await _bridge.ReduxVersionsAsync(reduxId);
            },
#if ADMIN
            ["adminFindByHash"] = async payload =>
            {
                var sha = payload?.GetProperty("sha256").GetString()
                          ?? throw new ArgumentException("sha256 required");
                return await _bridge.AdminFindByHashAsync(sha);
            },
#endif
#if ADMIN
            ["adminVersionUpsert"] = async payload =>
            {
                var v = payload!.Value.Deserialize<ReduxVersionDto>(JsonOptions)
                        ?? throw new ArgumentException("adminVersionUpsert payload required");
                await _bridge.AdminVersionUpsertAsync(v);
                return null;
            },
#endif
#if ADMIN
            ["adminVersionDelete"] = async payload =>
            {
                var idStr = payload?.GetProperty("id").GetString()
                            ?? throw new ArgumentException("id required");
                if (!Guid.TryParse(idStr, out var id))
                    throw new ArgumentException($"id is not a valid GUID: {idStr}");
                await _bridge.AdminVersionDeleteAsync(id);
                return null;
            },
#endif

            ["featuredPicksList"] = async _ => await _bridge.FeaturedPicksListAsync(),
#if ADMIN
            ["adminFeaturedPickSet"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("adminFeaturedPickSet payload required");
                var p = payload.Value;
                var slot    = p.GetProperty("slotIndex").GetInt32();
                var reduxId = p.GetProperty("reduxId").GetString()
                              ?? throw new ArgumentException("reduxId required");
                await _bridge.AdminFeaturedPickSetAsync(slot, reduxId);
                return null;
            },
#endif
#if ADMIN
            ["adminFeaturedPickDelete"] = async payload =>
            {
                var slot = payload?.GetProperty("slotIndex").GetInt32()
                           ?? throw new ArgumentException("slotIndex required");
                await _bridge.AdminFeaturedPickDeleteAsync(slot);
                return null;
            },
#endif

            ["reduxReviewsList"] = async payload =>
            {
                var reduxId = payload?.GetProperty("reduxId").GetString() ?? throw new ArgumentException("reduxId required");
                return await _bridge.ReduxReviewsListAsync(reduxId);
            },
            ["reduxReviewSubmit"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("reduxReviewSubmit payload required");
                var p        = payload.Value;
                var reduxId  = p.GetProperty("reduxId").GetString()  ?? throw new ArgumentException("reduxId required");
                var userId   = p.GetProperty("userId").GetString()   ?? throw new ArgumentException("userId required");
                var username = p.GetProperty("username").GetString() ?? string.Empty;
                var role     = p.GetProperty("role").GetString()     ?? "User";
                string? avatarUrl = null;
                if (p.TryGetProperty("avatarUrl", out var a) && a.ValueKind != System.Text.Json.JsonValueKind.Null)
                    avatarUrl = a.GetString();
                var rating   = p.GetProperty("rating").GetInt32();
                var body     = p.GetProperty("body").GetString()     ?? string.Empty;
                return await _bridge.ReduxReviewSubmitAsync(reduxId, userId, username, role, avatarUrl, rating, body);
            },
            ["reduxReviewDelete"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("reduxReviewDelete payload required");
                var p        = payload.Value;
                var reviewId = p.GetProperty("reviewId").GetString() ?? throw new ArgumentException("reviewId required");
                var userId   = p.TryGetProperty("userId", out var u) ? (u.GetString() ?? string.Empty) : string.Empty;
                var role     = p.TryGetProperty("role",   out var r) ? (r.GetString() ?? "User")        : "User";
                return await _bridge.ReduxReviewDeleteAsync(reviewId, userId, role);
            },

            ["bigMapReviewsList"] = async payload =>
            {
                var mapId = payload?.GetProperty("mapId").GetString() ?? throw new ArgumentException("mapId required");
                return await _bridge.BigMapReviewsListAsync(mapId);
            },
            ["bigMapReviewSubmit"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("bigMapReviewSubmit payload required");
                var p        = payload.Value;
                var mapId    = p.GetProperty("mapId").GetString()    ?? throw new ArgumentException("mapId required");
                var userId   = p.GetProperty("userId").GetString()   ?? throw new ArgumentException("userId required");
                var username = p.GetProperty("username").GetString() ?? string.Empty;
                var role     = p.GetProperty("role").GetString()     ?? "User";
                string? avatarUrl = null;
                if (p.TryGetProperty("avatarUrl", out var av) && av.ValueKind != System.Text.Json.JsonValueKind.Null)
                    avatarUrl = av.GetString();
                var rating   = p.GetProperty("rating").GetInt32();
                var body     = p.GetProperty("body").GetString()     ?? string.Empty;
                return await _bridge.BigMapReviewSubmitAsync(mapId, userId, username, role, avatarUrl, rating, body);
            },
            ["bigMapReviewDelete"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("bigMapReviewDelete payload required");
                var p        = payload.Value;
                var reviewId = p.GetProperty("reviewId").GetString() ?? throw new ArgumentException("reviewId required");
                var userId   = p.TryGetProperty("userId", out var bu) ? (bu.GetString() ?? string.Empty) : string.Empty;
                var role     = p.TryGetProperty("role",   out var br) ? (br.GetString() ?? "User")        : "User";
                return await _bridge.BigMapReviewDeleteAsync(reviewId, userId, role);
            },
            ["bigMapRatingsAggregate"] = async _ =>
            {
                return await _bridge.BigMapRatingsAggregateAsync();
            },

            ["userBuildReviewsList"] = async payload =>
            {
                var buildId = payload?.GetProperty("buildId").GetString() ?? throw new ArgumentException("buildId required");
                return await _bridge.UserBuildReviewsListAsync(buildId);
            },
            ["userBuildReviewSubmit"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("userBuildReviewSubmit payload required");
                var p        = payload.Value;
                var buildId  = p.GetProperty("buildId").GetString()  ?? throw new ArgumentException("buildId required");
                var userId   = p.GetProperty("userId").GetString()   ?? throw new ArgumentException("userId required");
                var username = p.GetProperty("username").GetString() ?? string.Empty;
                var role     = p.GetProperty("role").GetString()     ?? "User";
                string? avatarUrl = null;
                if (p.TryGetProperty("avatarUrl", out var a) && a.ValueKind != System.Text.Json.JsonValueKind.Null)
                    avatarUrl = a.GetString();
                var rating   = p.GetProperty("rating").GetInt32();
                var body     = p.GetProperty("body").GetString()     ?? string.Empty;
                return await _bridge.UserBuildReviewSubmitAsync(buildId, userId, username, role, avatarUrl, rating, body);
            },
            ["userBuildReviewDelete"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("userBuildReviewDelete payload required");
                var p        = payload.Value;
                var reviewId = p.GetProperty("reviewId").GetString() ?? throw new ArgumentException("reviewId required");
                var userId   = p.TryGetProperty("userId", out var u) ? (u.GetString() ?? string.Empty) : string.Empty;
                var role     = p.TryGetProperty("role",   out var r) ? (r.GetString() ?? "User")        : "User";
                return await _bridge.UserBuildReviewDeleteAsync(reviewId, userId, role);
            },

#if ADMIN
            ["adminInject"] = async payload =>
            {
                var p = payload?.GetProperty("moddedRpfPath").GetString()
                        ?? throw new ArgumentException("moddedRpfPath required");
                return await _bridge.AdminInjectAsync(p);
            },
#endif
#if ADMIN
            ["adminInjectFromCatalog"] = async payload =>
            {
                var id = payload?.GetProperty("reduxId").GetString()
                         ?? throw new ArgumentException("reduxId required");
                return await _bridge.AdminInjectFromCatalogAsync(id);
            },
#endif
#if ADMIN
            ["adminRestoreCleanUpdate"] = async _ => await _bridge.AdminRestoreCleanUpdateAsync(),
#endif

            ["reduxList"] = async payload =>
            {
                var search = payload?.TryGetProperty("search", out var s) == true ? s.GetString() : null;
                var server = payload?.TryGetProperty("server", out var sv) == true ? sv.GetString() : null;
                return await _bridge.ReduxListAsync(search, server);
            },

            ["reduxRatingsAggregate"] = async _ =>
            {
                return await _bridge.ReduxRatingsAggregateAsync();
            },
            ["reduxFavoriteList"] = async payload =>
            {
                var userId = payload?.GetProperty("userId").GetString() ?? string.Empty;
                return await _bridge.ReduxFavoriteListAsync(userId);
            },
            ["reduxFavoriteAdd"] = async payload =>
            {
                var p = payload!.Value;
                await _bridge.ReduxFavoriteAddAsync(
                    p.GetProperty("userId").GetString() ?? string.Empty,
                    p.GetProperty("reduxId").GetString() ?? string.Empty);
                return null;
            },
            ["reduxFavoriteRemove"] = async payload =>
            {
                var p = payload!.Value;
                await _bridge.ReduxFavoriteRemoveAsync(
                    p.GetProperty("userId").GetString() ?? string.Empty,
                    p.GetProperty("reduxId").GetString() ?? string.Empty);
                return null;
            },
            ["itemFavoritesList"] = async payload =>
            {
                var p = payload!.Value;
                return await _bridge.ItemFavoritesListAsync(
                    p.GetProperty("userId").GetString() ?? string.Empty,
                    p.GetProperty("itemType").GetString() ?? string.Empty);
            },
            ["itemFavoriteAdd"] = async payload =>
            {
                var p = payload!.Value;
                await _bridge.ItemFavoriteAddAsync(
                    p.GetProperty("userId").GetString() ?? string.Empty,
                    p.GetProperty("itemType").GetString() ?? string.Empty,
                    p.GetProperty("itemId").GetString() ?? string.Empty);
                return null;
            },
            ["itemFavoriteRemove"] = async payload =>
            {
                var p = payload!.Value;
                await _bridge.ItemFavoriteRemoveAsync(
                    p.GetProperty("userId").GetString() ?? string.Empty,
                    p.GetProperty("itemType").GetString() ?? string.Empty,
                    p.GetProperty("itemId").GetString() ?? string.Empty);
                return null;
            },
            ["reduxIncrementDownloads"] = async payload =>
            {
                var id = payload?.GetProperty("reduxId").GetString() ?? string.Empty;
                return await _bridge.ReduxIncrementDownloadsAsync(id);
            },
            ["reduxInstall"] = async payload =>
            {
                var id = payload?.GetProperty("reduxId").GetString()
                         ?? throw new ArgumentException("reduxId required");
                var versionId = ParseOptionalGuid(payload, "versionId");
                return await _bridge.ReduxInstallAsync(id, versionId);
            },
            ["reduxInstallForceClean"] = async payload =>
            {
                var id = payload?.GetProperty("reduxId").GetString()
                         ?? throw new ArgumentException("reduxId required");
                var versionId = ParseOptionalGuid(payload, "versionId");
                return await _bridge.ReduxInstallForceCleanAsync(id, versionId);
            },
            ["reduxInstallPreserve"] = async payload =>
            {
                var id = payload?.GetProperty("reduxId").GetString()
                         ?? throw new ArgumentException("reduxId required");
                var versionId = ParseOptionalGuid(payload, "versionId");
                return await _bridge.ReduxInstallPreserveAsync(id, versionId);
            },
            ["reduxInstallCancel"] = async _ =>
            {
                await _bridge.ReduxInstallCancelAsync();
                return null;
            },
            ["installCancel"] = async payload =>
            {
                var pid = payload?.GetProperty("progressId").GetString() ?? string.Empty;
                return await _bridge.InstallCancelAsync(pid);
            },
            ["armorInstallStandalone"] = async payload =>
            {
                var id = payload?.GetProperty("reduxId").GetString()
                         ?? throw new ArgumentException("reduxId required");
                var versionId = ParseOptionalGuid(payload, "versionId");
                bool force = payload!.Value.TryGetProperty("force", out var f) && f.GetBoolean();
                bool confirmWipe = payload!.Value.TryGetProperty("confirmWipe", out var cw) && cw.GetBoolean();
                return await _bridge.ArmorInstallStandaloneAsync(id, versionId, force, confirmWipe);
            },

            ["inspectDlcRpfArmor"] = async payload =>
            {
                var path = payload?.GetProperty("dlcRpfPath").GetString()
                           ?? throw new ArgumentException("dlcRpfPath required");
                return await _bridge.InspectDlcRpfArmorAsync(path);
            },

            ["inspectDlcRpfArmorCancel"] = async _ =>
            {
                return await _bridge.InspectDlcRpfArmorCancelAsync();
            },

            ["readLocalFileBase64"] = async payload =>
            {
                var path = payload?.GetProperty("path").GetString()
                           ?? throw new ArgumentException("path required");
                return await _bridge.ReadLocalFileBase64Async(path);
            },

            ["armorLibraryList"] = async _ => await _bridge.ArmorLibraryListAsync(),
            ["armorLibraryListAll"] = async _ => await _bridge.ArmorLibraryListAllAsync(),
            ["armorLibrarySetVisibility"] = async payload =>
            {
                var id = payload?.GetProperty("armorLibraryId").GetString()
                         ?? throw new ArgumentException("armorLibraryId required");
                var visible = payload!.Value.TryGetProperty("visible", out var v) && v.GetBoolean();
                return await _bridge.ArmorLibrarySetVisibilityAsync(id, visible);
            },
            ["armorLibraryDelete"] = async payload =>
            {
                var id = payload?.GetProperty("armorLibraryId").GetString()
                         ?? throw new ArgumentException("armorLibraryId required");
                return await _bridge.ArmorLibraryDeleteAsync(id);
            },
            ["armorLibraryRenderVariants"] = async payload =>
            {
                var id = payload?.GetProperty("armorLibraryId").GetString()
                         ?? throw new ArgumentException("armorLibraryId required");
                return await _bridge.ArmorLibraryRenderVariantsAsync(id);
            },
            ["armorLibrarySetPreview"] = async payload =>
            {
                var id = payload?.GetProperty("armorLibraryId").GetString()
                         ?? throw new ArgumentException("armorLibraryId required");
                var url = payload!.Value.GetProperty("previewUrl").GetString()
                          ?? throw new ArgumentException("previewUrl required");
                return await _bridge.ArmorLibrarySetPreviewAsync(id, url);
            },
            ["reduxArmorRenderPreview"] = async payload =>
            {
                var id = payload?.GetProperty("reduxId").GetString()
                         ?? throw new ArgumentException("reduxId required");
                return await _bridge.ReduxArmorRenderPreviewAsync(id);
            },
            ["reduxArmorBackfillPreviews"] = async _ =>
            {
                var (total, rendered) = await _bridge.ReduxArmorBackfillPreviewsAsync();
                return new { total, rendered };
            },
            ["reduxArmorRenderVariants"] = async payload =>
            {
                var id = payload?.GetProperty("reduxId").GetString()
                         ?? throw new ArgumentException("reduxId required");
                return await _bridge.ReduxArmorRenderVariantsAsync(id);
            },
            ["reduxArmorVariantUrls"] = async payload =>
            {
                var id = payload?.GetProperty("reduxId").GetString()
                         ?? throw new ArgumentException("reduxId required");
                return await _bridge.ReduxArmorVariantUrlsAsync(id);
            },
            ["reduxArmorSetPreview"] = async payload =>
            {
                var id = payload?.GetProperty("reduxId").GetString()
                         ?? throw new ArgumentException("reduxId required");
                var url = payload!.Value.GetProperty("previewUrl").GetString()
                          ?? throw new ArgumentException("previewUrl required");
                return await _bridge.ReduxArmorSetPreviewAsync(id, url);
            },
            ["armorLibrarySetSupportedServers"] = async payload =>
            {
                var id = payload?.GetProperty("armorLibraryId").GetString()
                         ?? throw new ArgumentException("armorLibraryId required");
                var servers = new List<string>();
                if (payload!.Value.TryGetProperty("servers", out var arr)
                    && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        if (el.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            var s = el.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) servers.Add(s);
                        }
                    }
                }
                return await _bridge.ArmorLibrarySetSupportedServersAsync(id, servers);
            },
            ["armorLibraryInstall"] = async payload =>
            {
                var id = payload?.GetProperty("armorLibraryId").GetString()
                         ?? throw new ArgumentException("armorLibraryId required");

                bool overlay = payload!.Value.TryGetProperty("overlayMode", out var om) && om.GetBoolean();

                bool force = payload!.Value.TryGetProperty("force", out var f) && f.GetBoolean();
                bool confirmWipe = payload!.Value.TryGetProperty("confirmWipe", out var cw) && cw.GetBoolean();
                return await _bridge.ArmorLibraryInstallAsync(id, overlay, force, alreadyLocked: false, confirmWipe: confirmWipe);
            },
            ["importDlcRpfArmor"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var p = payload.Value;
                var req = new DlcArmorImportRequestDto(
                    DlcRpfPath:           p.GetProperty("dlcRpfPath").GetString()
                                          ?? throw new ArgumentException("dlcRpfPath required"),
                    YddInternalPath:      p.GetProperty("yddInternalPath").GetString()
                                          ?? throw new ArgumentException("yddInternalPath required"),
                    Name:                 p.GetProperty("name").GetString()
                                          ?? throw new ArgumentException("name required"),
                    Author:               p.TryGetProperty("author", out var a) ? a.GetString() : null,
                    ApplyAutoFix:         p.TryGetProperty("applyAutoFix", out var af) && af.GetBoolean(),
                    RenameYtdFileName:    p.TryGetProperty("renameYtdFileName", out var ry) ? ry.GetString() : null,
                    RenameOldTextureName: p.TryGetProperty("renameOldTextureName", out var ro) ? ro.GetString() : null,
                    RenameNewTextureName: p.TryGetProperty("renameNewTextureName", out var rn) ? rn.GetString() : null,
                    ExtraSources:         p.TryGetProperty("extraSources", out var es)
                                          && es.ValueKind == System.Text.Json.JsonValueKind.Array
                        ? es.Deserialize<System.Collections.Generic.List<DlcArmorExtraSourceDto>>(JsonOptions)
                        : null);
                return await _bridge.ImportDlcRpfArmorAsync(req);
            },

            ["reduxApplyArmorSwap"] = async payload =>
            {
                var id = payload?.GetProperty("donorReduxId").GetString()
                         ?? throw new ArgumentException("donorReduxId required");
                var versionId = ParseOptionalGuid(payload, "donorVersionId");
                return await _bridge.ReduxApplyArmorSwapAsync(id, versionId);
            },

            ["reduxClearArmor"] = async _ => await _bridge.ReduxClearArmorAsync(),
            ["getCurrentArmorInfo"] = async _ => await _bridge.GetCurrentArmorInfoAsync(),
            ["reduxUninstall"]           = async _ => await _bridge.ReduxUninstallAsync(),
            ["reduxUninstallForceClean"] = async _ => await _bridge.ReduxUninstallForceCleanAsync(),
            ["reduxUninstallPreserve"]   = async _ => await _bridge.ReduxUninstallPreserveAsync(),
            ["reduxCustomizeApply"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var p = payload.Value;
                var reduxId = p.GetProperty("reduxId").GetString()
                              ?? throw new ArgumentException("reduxId required");
                var draft = p.GetProperty("draft").Deserialize<CustomizationDraftDto>(JsonOptions)
                            ?? throw new ArgumentException("draft required");
                return await _bridge.ReduxCustomizeApplyAsync(reduxId, draft);
            },

            ["gtaVersionsList"] = async _ => await _bridge.GtaVersionsListAsync(),
            ["gtaVersionsUpsert"] = async payload =>
            {
                var v = payload!.Value.Deserialize<GtaVersionDto>(JsonOptions)
                        ?? throw new ArgumentException("gtaVersionsUpsert payload required");
                await _bridge.GtaVersionsUpsertAsync(v);
                return null;
            },
            ["gtaVersionsDelete"] = async payload =>
            {
                var ver = payload?.GetProperty("exeVersion").GetString()
                          ?? throw new ArgumentException("exeVersion required");
                await _bridge.GtaVersionsDeleteAsync(ver);
                return null;
            },
            ["gtaVersionsAutoFill"] = async payload =>
            {
                var path = payload?.GetProperty("cleanRpfPath").GetString()
                           ?? throw new ArgumentException("cleanRpfPath required");
                return await _bridge.GtaVersionsAutoFillAsync(path);
            },
            ["gtaVersionsUpload"] = async payload =>
            {
                var p = payload!.Value;
                var path = p.GetProperty("cleanRpfPath").GetString() ?? throw new ArgumentException("cleanRpfPath required");
                var ver  = p.GetProperty("exeVersion").GetString()   ?? throw new ArgumentException("exeVersion required");
                var notes = p.TryGetProperty("notes", out var n) ? (n.GetString() ?? string.Empty) : string.Empty;
                return await _bridge.GtaVersionsUploadAsync(path, ver, notes);
            },

            ["libraryList"] = async payload =>
            {
                var type = payload?.TryGetProperty("type", out var tEl) == true ? tEl.GetString() : null;
                return await _bridge.LibraryListAsync(type);
            },
            ["libraryDelete"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                await _bridge.LibraryDeleteAsync(id);
                return null;
            },
            ["libraryUploadComponent"] = async payload =>
            {
                var p = payload!.Value.Deserialize<LibraryUploadDto>(JsonOptions)
                        ?? throw new ArgumentException("libraryUploadComponent payload required");
                return await _bridge.LibraryUploadComponentAsync(p);
            },
            ["libraryPatch"] = async payload =>
            {
                var p = payload!.Value.Deserialize<LibraryPatchDto>(JsonOptions)
                        ?? throw new ArgumentException("libraryPatch payload required");
                return await _bridge.LibraryPatchAsync(p);
            },

            ["gunpackWhitelistList"] = async _ => await _bridge.GunpackWhitelistListAsync(),

            ["gunpacksList"] = async payload =>
            {
                var search = payload?.TryGetProperty("search", out var s) == true ? s.GetString() : null;
                var status = payload?.TryGetProperty("status", out var st) == true ? st.GetString() : null;
                return await _bridge.GunpacksListAsync(search, status);
            },
            ["gunpackGet"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                return await _bridge.GunpackGetAsync(id);
            },
            ["gunpackGuns"] = async payload =>
            {
                var id = payload?.GetProperty("gunpackId").GetString() ?? throw new ArgumentException("gunpackId required");
                return await _bridge.GunpackGunsAsync(id);
            },
            ["gunpackAllGuns"] = async _ => await _bridge.GunpackAllGunsAsync(),
            ["gunpackIncrementDownloads"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                return await _bridge.GunpackIncrementDownloadsAsync(id);
            },

            ["customGunsList"] = async payload =>
            {
                var search = payload?.TryGetProperty("search", out var s) == true ? s.GetString() : null;
                var sort   = payload?.TryGetProperty("sort", out var so) == true ? so.GetString() : null;
                var viewer = payload?.TryGetProperty("viewerUserId", out var v) == true ? v.GetString() : null;
                return await _bridge.CustomGunsListAsync(search, sort, viewer);
            },
            ["customGunsMine"] = async payload =>
            {
                var owner = payload?.GetProperty("ownerUserId").GetString() ?? throw new ArgumentException("ownerUserId required");
                return await _bridge.CustomGunsMineAsync(owner);
            },
            ["customGunLimits"] = async payload =>
            {
                var owner = payload?.TryGetProperty("ownerUserId", out var o) == true ? o.GetString() : null;
                return await _bridge.CustomGunLimitsAsync(owner ?? string.Empty);
            },
            ["customGunPatch"] = async payload =>
            {
                var p = payload ?? throw new ArgumentException("customGunPatch payload required");
                var id = p.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                var patch = p.GetProperty("patch").Deserialize<CustomGunPatchDto>(JsonOptions)
                    ?? throw new ArgumentException("patch required");
                await _bridge.CustomGunPatchAsync(id, patch);
                return null;
            },
            ["customGunDelete"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                await _bridge.CustomGunDeleteAsync(id);
                return null;
            },
            ["customGunInstall"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                await _bridge.CustomGunInstallAsync(id);
                return null;
            },
            ["customGunListPending"] = async _ => await _bridge.CustomGunListPendingAsync(),
            ["customGunApprove"] = async payload =>
            {
                var id  = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                var rev = payload?.GetProperty("reviewerUserId").GetString() ?? "";
                return await _bridge.CustomGunApproveAsync(id, rev);
            },
            ["customGunReject"] = async payload =>
            {
                var id  = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                var rev = payload?.GetProperty("reviewerUserId").GetString() ?? "";
                var rsn = payload?.GetProperty("reason").GetString() ?? "";
                return await _bridge.CustomGunRejectAsync(id, rev, rsn);
            },
            ["customGunAdminList"] = async payload => await _bridge.CustomGunAdminListAsync(
                Opt(payload, "status"), Opt(payload, "search")),
            ["customGunAdminPatch"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                return await _bridge.CustomGunAdminPatchAsync(
                    id, Opt(payload, "displayName"), Opt(payload, "description"), Opt(payload, "category"));
            },
            ["customGunAdminDelete"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                bool hard = payload is { } p && p.TryGetProperty("hard", out var h)
                            && h.ValueKind == JsonValueKind.True;
                return await _bridge.CustomGunAdminDeleteAsync(id, Opt(payload, "reason"), hard);
            },
            ["customSkinApplied"] = async _ => await _bridge.CustomSkinAppliedAsync(),
            ["customSkinRemove"]  = async payload =>
            {
                var internalName = payload?.GetProperty("internalName").GetString()
                    ?? throw new ArgumentException("internalName required");
                return await _bridge.CustomSkinRemoveAsync(internalName);
            },

            ["workshopFlowLimits"] = async _ => await _bridge.WorkshopFlowLimitsAsync(),
            ["userGunpacksList"]   = async _ => await _bridge.UserGunpacksListAsync(),
            ["userGunpackInstall"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                await _bridge.UserGunpackInstallAsync(id);
                return null;
            },
            ["userGunpackDelete"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                await _bridge.UserGunpackDeleteAsync(id);
                return null;
            },
            ["customGunPreviewDownload"] = async payload =>
            {
                var url  = payload?.GetProperty("url").GetString() ?? throw new ArgumentException("url required");
                var name = payload?.TryGetProperty("name", out var n) == true ? (n.GetString() ?? "gun_preview") : "gun_preview";
                return await _bridge.CustomGunPreviewDownloadAsync(url, name);
            },
            ["workshopOpen"] = async payload =>
            {
                var req = payload?.GetProperty("req").Deserialize<WorkshopOpenRequestDto>(JsonOptions)
                    ?? new WorkshopOpenRequestDto(null, null);
                return await _bridge.WorkshopOpenAsync(req);
            },
            ["workshopReplaceTexture"] = async payload =>
            {
                var p = payload ?? throw new ArgumentException("workshopReplaceTexture payload required");
                var draftId = p.GetProperty("draftId").GetString() ?? throw new ArgumentException("draftId required");
                var name    = p.GetProperty("textureName").GetString() ?? throw new ArgumentException("textureName required");
                var png     = p.GetProperty("pngBase64").GetString() ?? throw new ArgumentException("pngBase64 required");
                return await _bridge.WorkshopReplaceTextureAsync(draftId, name, png);
            },
            ["workshopSaveDraft"] = async payload =>
            {
                var draftId = payload?.GetProperty("draftId").GetString() ?? throw new ArgumentException("draftId required");
                await _bridge.WorkshopSaveDraftAsync(draftId);
                return null;
            },
            ["workshopApplyToGame"] = async payload =>
            {
                var draftId = payload?.GetProperty("draftId").GetString() ?? throw new ArgumentException("draftId required");
                await _bridge.WorkshopApplyToGameAsync(draftId);
                return null;
            },
            ["workshopPublish"] = async payload =>
            {
                var p = payload ?? throw new ArgumentException("workshopPublish payload required");
                var draftId = p.GetProperty("draftId").GetString() ?? throw new ArgumentException("draftId required");
                var meta = p.GetProperty("meta").Deserialize<WorkshopPublishMetaDto>(JsonOptions)
                    ?? throw new ArgumentException("meta required");
                var owner = p.GetProperty("ownerUserId").GetString() ?? throw new ArgumentException("ownerUserId required");
                var ownerName = p.TryGetProperty("ownerName", out var on) ? (on.GetString() ?? "player") : "player";
                return await _bridge.WorkshopPublishAsync(draftId, meta, owner, ownerName);
            },

#if ADMIN
            ["adminGunpackList"] = async _ => await _bridge.AdminGunpackListAsync(),
#endif
#if ADMIN
            ["adminGunpackPatch"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("adminGunpackPatch payload required");
                var p     = payload.Value;
                var id    = p.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                var patch = p.GetProperty("patch").Deserialize<GunpackPatchDto>(JsonOptions)
                            ?? throw new ArgumentException("patch required");
                await _bridge.AdminGunpackPatchAsync(id, patch);
                return null;
            },
#endif
#if ADMIN
            ["adminGunpackDelete"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString() ?? throw new ArgumentException("id required");
                await _bridge.AdminGunpackDeleteAsync(id);
                return null;
            },
#endif
#if ADMIN
            ["adminGunpackGunPatch"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("adminGunpackGunPatch payload required");
                var p       = payload.Value;
                var gunIdS  = p.GetProperty("gunId").GetString() ?? throw new ArgumentException("gunId required");
                if (!Guid.TryParse(gunIdS, out var gunId))
                    throw new ArgumentException($"gunId is not a valid GUID: {gunIdS}");
                var patch   = p.GetProperty("patch").Deserialize<GunpackGunPatchDto>(JsonOptions)
                              ?? throw new ArgumentException("patch required");
                await _bridge.AdminGunpackGunPatchAsync(gunId, patch);
                return null;
            },
#endif
#if ADMIN
            ["adminGunpackGunDelete"] = async payload =>
            {
                var s = payload?.GetProperty("gunId").GetString() ?? throw new ArgumentException("gunId required");
                if (!Guid.TryParse(s, out var gunId))
                    throw new ArgumentException($"gunId is not a valid GUID: {s}");
                await _bridge.AdminGunpackGunDeleteAsync(gunId);
                return null;
            },
#endif
#if ADMIN
            ["adminGunpackUpload"] = async payload =>
            {
                var req = payload!.Value.Deserialize<GunpackUploadRequestDto>(JsonOptions)
                          ?? throw new ArgumentException("adminGunpackUpload payload required");
                return await _bridge.AdminGunpackUploadAsync(req);
            },
#endif

            ["gunpackVariantsList"] = async payload =>
            {
                var id = payload?.GetProperty("gunpackId").GetString()
                         ?? throw new ArgumentException("gunpackId required");
                return await _bridge.GunpackVariantsListAsync(id);
            },
#if ADMIN
            ["adminGunpackVariantPatch"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("adminGunpackVariantPatch payload required");
                var p   = payload.Value;
                var s   = p.GetProperty("variantId").GetString() ?? throw new ArgumentException("variantId required");
                if (!Guid.TryParse(s, out var variantId))
                    throw new ArgumentException($"variantId not a valid GUID: {s}");
                var patch = p.GetProperty("patch").Deserialize<GunpackVariantPatchDto>(JsonOptions)
                            ?? throw new ArgumentException("patch required");
                await _bridge.AdminGunpackVariantPatchAsync(variantId, patch);
                return null;
            },
#endif
#if ADMIN
            ["adminGunpackVariantDelete"] = async payload =>
            {
                var s = payload?.GetProperty("variantId").GetString() ?? throw new ArgumentException("variantId required");
                if (!Guid.TryParse(s, out var variantId))
                    throw new ArgumentException($"variantId not a valid GUID: {s}");
                await _bridge.AdminGunpackVariantDeleteAsync(variantId);
                return null;
            },
#endif
#if ADMIN
            ["adminGunpackVariantSetDefault"] = async payload =>
            {
                var s = payload?.GetProperty("variantId").GetString() ?? throw new ArgumentException("variantId required");
                if (!Guid.TryParse(s, out var variantId))
                    throw new ArgumentException($"variantId not a valid GUID: {s}");
                await _bridge.AdminGunpackVariantSetDefaultAsync(variantId);
                return null;
            },
#endif
#if ADMIN
            ["adminGunpackVariantUpload"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("adminGunpackVariantUpload payload required");
                var p     = payload.Value;
                var packId    = p.GetProperty("packId").GetString() ?? throw new ArgumentException("packId required");
                var name      = p.GetProperty("name").GetString() ?? throw new ArgumentException("name required");
                var rpfPath   = p.GetProperty("sourceRpfPath").GetString() ?? throw new ArgumentException("sourceRpfPath required");
                string? coverPath = null;
                if (p.TryGetProperty("coverImagePath", out var cEl) && cEl.ValueKind == JsonValueKind.String)
                {
                    var c = cEl.GetString();
                    if (!string.IsNullOrWhiteSpace(c)) coverPath = c;
                }
                return await _bridge.AdminGunpackVariantUploadAsync(packId, name, rpfPath, coverPath);
            },
#endif
#if ADMIN
            ["adminGunpackQueueList"] = async _ => await _bridge.AdminGunpackQueueListAsync(),
#endif
#if ADMIN
            ["adminGunpackQueueRemove"] = async payload =>
            {
                var tempId = payload?.GetProperty("tempId").GetString() ?? throw new ArgumentException("tempId required");
                await _bridge.AdminGunpackQueueRemoveAsync(tempId);
                return null;
            },
#endif

            ["gunpackInstallAll"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("gunpackInstallAll payload required");
                var p = payload.Value;
                var id = p.GetProperty("gunpackId").GetString() ?? throw new ArgumentException("gunpackId required");
                Dictionary<string, string>? perGun = null;
                if (p.TryGetProperty("perGunResolutions", out var rEl) && rEl.ValueKind == JsonValueKind.Object)
                {
                    perGun = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in rEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                            perGun[prop.Name] = prop.Value.GetString() ?? string.Empty;
                    }
                }
                Guid? variantId = null;
                if (p.TryGetProperty("variantId", out var vEl) && vEl.ValueKind == JsonValueKind.String)
                {
                    var s = vEl.GetString();
                    if (!string.IsNullOrEmpty(s) && Guid.TryParse(s, out var vg))
                        variantId = vg;
                }
                return await _bridge.GunpackInstallAllAsync(id, perGun, variantId);
            },
            ["gunpackCheckInstallConflicts"] = async payload =>
            {
                var id = payload?.GetProperty("gunpackId").GetString() ?? throw new ArgumentException("gunpackId required");
                return await _bridge.GunpackCheckInstallConflictsAsync(id);
            },
            ["gunpackInstallSelected"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("gunpackInstallSelected payload required");
                var p      = payload.Value;
                var id     = p.GetProperty("gunpackId").GetString() ?? throw new ArgumentException("gunpackId required");
                var idsArr = p.GetProperty("gunIds");
                var gunIds = new List<Guid>();
                foreach (var el in idsArr.EnumerateArray())
                {
                    var s = el.GetString();
                    if (Guid.TryParse(s, out var g)) gunIds.Add(g);
                }
                return await _bridge.GunpackInstallSelectedAsync(id, gunIds);
            },
            ["gunpackUninstall"] = async _ => await _bridge.GunpackUninstallAsync(),
            ["gunpackGetInstalledState"] = async _ => await _bridge.GunpackGetInstalledStateAsync(),
            ["gunpackVerifyInstalled"]   = async _ => await _bridge.GunpackVerifyInstalledAsync(),
            ["reconcileInstallState"]    = async _ => await _bridge.ReconcileInstallStateAsync(),

            ["selectedGunsList"] = async _ => await _bridge.SelectedGunsListAsync(),
            ["selectedGunsIsInstalled"] = async payload =>
            {
                var n = payload?.GetProperty("internalName").GetString() ?? throw new ArgumentException("internalName required");
                return await _bridge.SelectedGunsIsInstalledAsync(n);
            },
            ["selectedGunsInstall"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("selectedGunsInstall payload required");
                var p = payload.Value;
                var packId = p.GetProperty("gunpackId").GetString() ?? throw new ArgumentException("gunpackId required");
                var inm    = p.GetProperty("internalName").GetString() ?? throw new ArgumentException("internalName required");
                return await _bridge.SelectedGunsInstallAsync(packId, inm);
            },
            ["selectedGunsRemove"] = async payload =>
            {
                var n = payload?.GetProperty("internalName").GetString() ?? throw new ArgumentException("internalName required");
                return await _bridge.SelectedGunsRemoveAsync(n);
            },
            ["selectedGunsRebuild"]      = async _ => await _bridge.SelectedGunsRebuildAsync(),
            ["selectedGunsUninstallAll"] = async _ => await _bridge.SelectedGunsUninstallAllAsync(),
            ["selectedGunsVerify"]       = async _ => await _bridge.SelectedGunsVerifyAsync(),

            ["installMod"] = todo,
            ["uninstallMod"] = todo,
            ["compareRpf"] = todo,
            ["getDownloadQueue"] = todo,
            ["applyColorization"] = todo,
            ["extractComponent"] = todo,
            ["rollback"] = todo,
            ["verifyRpf"] = todo,
            ["applySettingsXml"] = todo,
            ["hntCodeExport"] = async payload =>
            {
                var userId = payload?.GetProperty("userId").GetString()
                             ?? throw new ArgumentException("userId required");

                bool TryFlag(string name)
                {
                    if (payload is null || !payload.Value.TryGetProperty(name, out var el)) return true;
                    return el.ValueKind == System.Text.Json.JsonValueKind.False ? false : true;
                }
                var includeRedux        = TryFlag("includeRedux");
                var includeGunpack      = TryFlag("includeGunpack");
                var includeSelectedGuns = TryFlag("includeSelectedGuns");
                var includeComponents   = TryFlag("includeComponents");
                List<string>? gunFilter = null;
                if (payload!.Value.TryGetProperty("gunFilter", out var gf)
                    && gf.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    gunFilter = gf.EnumerateArray()
                        .Where(x => x.ValueKind == System.Text.Json.JsonValueKind.String)
                        .Select(x => x.GetString()!)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToList();
                    if (gunFilter.Count == 0) gunFilter = null;
                }
                return await _bridge.HntCodeExportAsync(userId,
                    includeRedux, includeGunpack, includeSelectedGuns, includeComponents, gunFilter);
            },
            ["hntCodePreview"] = async payload =>
            {
                var code = payload?.GetProperty("code").GetString()
                           ?? throw new ArgumentException("code required");
                return await _bridge.HntCodePreviewAsync(code);
            },
            ["hntCodeApply"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var p = payload.Value.GetProperty("payload").Deserialize<HntPayloadDto>(JsonOptions)
                        ?? throw new ArgumentException("payload required");
                return await _bridge.HntCodeApplyAsync(p);
            },
            ["hntCodeListMy"] = async payload =>
            {
                var userId = payload?.GetProperty("userId").GetString()
                             ?? throw new ArgumentException("userId required");
                return await _bridge.HntCodeListMyAsync(userId);
            },
            ["hntCodeDelete"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var p = payload.Value;
                var code = p.GetProperty("code").GetString()
                           ?? throw new ArgumentException("code required");
                var userId = p.GetProperty("userId").GetString()
                             ?? throw new ArgumentException("userId required");
                return await _bridge.HntCodeDeleteAsync(code, userId);
            },

            ["userBuildsList"] = async payload =>
            {
                string? search = null;
                string? authorUserId = null;
                if (payload is not null)
                {
                    if (payload.Value.TryGetProperty("search", out var s) &&
                        s.ValueKind != System.Text.Json.JsonValueKind.Null)
                        search = s.GetString();
                    if (payload.Value.TryGetProperty("authorUserId", out var a) &&
                        a.ValueKind != System.Text.Json.JsonValueKind.Null)
                        authorUserId = a.GetString();
                }
                return await _bridge.UserBuildsListAsync(search, authorUserId);
            },
            ["userBuildGet"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");
                return await _bridge.UserBuildGetAsync(id);
            },
            ["userBuildGetByHntCode"] = async payload =>
            {
                var code = payload?.GetProperty("hntCode").GetString()
                           ?? throw new ArgumentException("hntCode required");
                return await _bridge.UserBuildGetByHntCodeAsync(code);
            },
            ["userBuildCreate"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var dto = payload.Value.Deserialize<UserBuildDto>(JsonOptions)
                          ?? throw new ArgumentException("could not parse build dto");
                return await _bridge.UserBuildCreateAsync(dto);
            },
            ["userBuildDelete"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");
                await _bridge.UserBuildDeleteAsync(id);
                return null;
            },
            ["userBuildIncrementDownloads"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");
                return await _bridge.UserBuildIncrementDownloadsAsync(id);
            },

            ["userBuildIncrementViews"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");
                return await _bridge.UserBuildIncrementViewsAsync(id);
            },

            ["donorPickCounts"] = async payload =>
            {
                var component = payload?.GetProperty("component").GetString()
                                ?? throw new ArgumentException("component required");
                return await _bridge.DonorPickCountsAsync(component);
            },
            ["donorPickIncrement"] = async payload =>
            {
                var donor = payload?.GetProperty("donorReduxId").GetString()
                            ?? throw new ArgumentException("donorReduxId required");
                var component = payload?.GetProperty("component").GetString()
                                ?? throw new ArgumentException("component required");
                return await _bridge.DonorPickIncrementAsync(donor, component);
            },

            ["userBuildSubmit"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var dto = payload.Value.Deserialize<UserBuildDto>(JsonOptions)
                          ?? throw new ArgumentException("could not parse build dto");
                return await _bridge.UserBuildSubmitAsync(dto);
            },
            ["userBuildUpdate"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var id = payload.Value.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");

                var patchEl = payload.Value.GetProperty("patch");
                var patch = new Dictionary<string, object?>();
                foreach (var prop in patchEl.EnumerateObject())
                {
                    patch[prop.Name] = JsonElementToObject(prop.Value);
                }
                return await _bridge.UserBuildUpdateAsync(id, patch);
            },
            ["userBuildUploadSettingsXml"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var buildId = payload.Value.GetProperty("buildId").GetString()
                              ?? throw new ArgumentException("buildId required");
                var src     = payload.Value.GetProperty("sourceXmlPath").GetString()
                              ?? throw new ArgumentException("sourceXmlPath required");
                return await _bridge.UserBuildUploadSettingsXmlAsync(buildId, src);
            },
            ["userBuildUploadCover"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var src = payload.Value.GetProperty("sourcePath").GetString()
                          ?? throw new ArgumentException("sourcePath required");
                return await _bridge.UserBuildUploadCoverAsync(src);
            },
#if ADMIN
            ["adminUploadComponentScreenshot"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var reduxId   = payload.Value.GetProperty("reduxId").GetString()
                                ?? throw new ArgumentException("reduxId required");
                var component = payload.Value.GetProperty("component").GetString()
                                ?? throw new ArgumentException("component required");
                var src       = payload.Value.GetProperty("sourcePath").GetString()
                                ?? throw new ArgumentException("sourcePath required");
                return await _bridge.AdminUploadComponentScreenshotAsync(reduxId, component, src);
            },
#endif
#if ADMIN
            ["adminMirrorImageToR2"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var reduxId     = payload.Value.GetProperty("reduxId").GetString()
                                  ?? throw new ArgumentException("reduxId required");
                var externalUrl = payload.Value.GetProperty("externalUrl").GetString()
                                  ?? throw new ArgumentException("externalUrl required");
                var slot        = payload.Value.GetProperty("slot").GetString() ?? "mirror";
                return await _bridge.AdminMirrorImageToR2Async(reduxId, externalUrl, slot);
            },
#endif
#if ADMIN
            ["adminUploadLibraryPreview"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var libraryId = payload.Value.GetProperty("libraryId").GetString()
                                ?? throw new ArgumentException("libraryId required");
                var src       = payload.Value.GetProperty("sourcePath").GetString()
                                ?? throw new ArgumentException("sourcePath required");
                return await _bridge.AdminUploadLibraryPreviewAsync(libraryId, src);
            },
#endif
            ["getCurrentMinimapInfo"] = async _ =>
            {
                return await _bridge.GetCurrentMinimapInfoAsync();
            },
            ["getInstalledDraft"] = async _ =>
            {
                return await _bridge.GetInstalledDraftAsync();
            },
            ["getCurrentReduxId"] = async _ =>
            {
                return await _bridge.GetCurrentReduxIdAsync();
            },
            ["reduxApplyMinimap"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var source = payload.Value.GetProperty("source").GetString()
                             ?? throw new ArgumentException("source required");
                var id     = payload.Value.GetProperty("id").GetString()
                             ?? throw new ArgumentException("id required");
                string? displayName = null;
                if (payload.Value.TryGetProperty("displayName", out var dnEl)
                    && dnEl.ValueKind == JsonValueKind.String)
                {
                    displayName = dnEl.GetString();
                }
                return await _bridge.ReduxApplyMinimapAsync(source, id, displayName);
            },
            ["timecycleInstall"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var donorReduxId = payload.Value.GetProperty("donorReduxId").GetString()
                                   ?? throw new ArgumentException("donorReduxId required");
                string? displayName = null;
                if (payload.Value.TryGetProperty("displayName", out var tdnEl)
                    && tdnEl.ValueKind == JsonValueKind.String)
                {
                    displayName = tdnEl.GetString();
                }
                string? donorVersionId = null;
                if (payload.Value.TryGetProperty("donorVersionId", out var tdvEl)
                    && tdvEl.ValueKind == JsonValueKind.String)
                {
                    donorVersionId = tdvEl.GetString();
                }
                return await _bridge.TimecycleInstallAsync(donorReduxId, displayName, donorVersionId);
            },
            ["getCurrentTimecycleInfo"] = async _ => await _bridge.GetCurrentTimecycleInfoAsync(),
            ["timecycleRestoreVanilla"] = async _ => await _bridge.TimecycleRestoreVanillaAsync(),
            ["treesInstall"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var treeId = payload.Value.GetProperty("treeId").GetString()
                             ?? throw new ArgumentException("treeId required");
                string? displayName = null;
                if (payload.Value.TryGetProperty("displayName", out var trdnEl)
                    && trdnEl.ValueKind == JsonValueKind.String)
                {
                    displayName = trdnEl.GetString();
                }
                return await _bridge.TreesInstallAsync(treeId, displayName);
            },
            ["getCurrentTreesInfo"] = async _ => await _bridge.GetCurrentTreesInfoAsync(),
            ["treesRestore"] = async _ => await _bridge.TreesRestoreAsync(),
            ["roadsInstall"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var roadId = payload.Value.GetProperty("roadId").GetString()
                             ?? throw new ArgumentException("roadId required");
                string? displayName = null;
                if (payload.Value.TryGetProperty("displayName", out var rddnEl)
                    && rddnEl.ValueKind == JsonValueKind.String)
                {
                    displayName = rddnEl.GetString();
                }
                return await _bridge.RoadsInstallAsync(roadId, displayName);
            },
            ["getCurrentRoadsInfo"] = async _ => await _bridge.GetCurrentRoadsInfoAsync(),
            ["roadsRestore"] = async _ => await _bridge.RoadsRestoreAsync(),
            ["getRoadsFixStatus"] = async _ => await _bridge.GetRoadsFixStatusAsync(),
            ["roadsFixApply"] = async _ => await _bridge.RoadsFixApplyAsync(),
            ["graphicsModRestore"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var modId = payload.Value.GetProperty("modId").GetString()
                            ?? throw new ArgumentException("modId required");
                return await _bridge.GraphicsModRestoreAsync(modId);
            },
            ["getInstalledGraphicsMods"] = async _ => await _bridge.GetInstalledGraphicsModsAsync(),
            ["minimapSetRangeRings"] = async payload =>
            {
                var list = new System.Collections.Generic.List<int>();
                if (payload is not null && payload.Value.TryGetProperty("radiiMeters", out var arr)
                    && arr.ValueKind == JsonValueKind.Array)
                    foreach (var e in arr.EnumerateArray())
                        if (e.TryGetInt32(out var v)) list.Add(v);
                return await _bridge.MinimapSetRangeRingsAsync(list.ToArray());
            },
            ["minimapGetRangeRings"] = async _ => await _bridge.MinimapGetRangeRingsAsync(),
            ["minimapLayoutGet"] = async _ => await _bridge.MinimapLayoutGetAsync(),
            ["minimapLayoutApply"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var ratio = payload.Value.GetProperty("ratio").GetString()
                            ?? throw new ArgumentException("ratio required");
                var placement = payload.Value.GetProperty("placement").GetString()
                                ?? throw new ArgumentException("placement required");
                bool transparent = payload.Value.TryGetProperty("transparent", out var tp)
                                   && tp.ValueKind == System.Text.Json.JsonValueKind.True;
                return await _bridge.MinimapLayoutApplyAsync(ratio, placement, transparent);
            },
            ["minimapLayoutApplyCustom"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                string ratio = payload.Value.TryGetProperty("ratio", out var rj)
                               && rj.ValueKind == System.Text.Json.JsonValueKind.String
                    ? (rj.GetString() ?? "16:9") : "16:9";
                double posX = payload.Value.GetProperty("posX").GetDouble();
                double posY = payload.Value.GetProperty("posY").GetDouble();
                bool transparent = payload.Value.TryGetProperty("transparent", out var tpc)
                                   && tpc.ValueKind == System.Text.Json.JsonValueKind.True;
                return await _bridge.MinimapLayoutApplyCustomAsync(ratio, posX, posY, transparent);
            },
            ["minimapLayoutGetPresets"] = async _ => await _bridge.MinimapLayoutPresetsAsync(),
            ["minimapGetSafezone"] = async _ => await _bridge.MinimapGetSafezoneAsync(),
            ["minimapGetScreen"] = async _ => await _bridge.MinimapGetScreenAsync(),
            ["fileToDataUrl"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var path = payload.Value.GetProperty("path").GetString()
                           ?? throw new ArgumentException("path required");
                return await _bridge.FileToDataUrlAsync(path);
            },
            ["minimapGetTweaks"] = async _ => await _bridge.MinimapGetTweaksAsync(),
            ["minimapGetSave"] = async _ => await _bridge.MinimapGetSaveAsync(),
            ["minimapWriteSave"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var nm = payload.Value.TryGetProperty("name", out var nv) && nv.ValueKind == JsonValueKind.String
                    ? (nv.GetString() ?? "") : "";
                var tw = System.Text.Json.JsonSerializer.Deserialize<MinimapTweaksDto>(
                    payload.Value.GetProperty("tweaks").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new ArgumentException("tweaks required");
                return await _bridge.MinimapWriteSaveAsync(nm, tw);
            },
            ["minimapClearSave"] = async _ => { await _bridge.MinimapClearSaveAsync(); return null; },
            ["minimapApplyTweaks"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var dto = System.Text.Json.JsonSerializer.Deserialize<MinimapTweaksDto>(
                    payload.Value.GetProperty("tweaks").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new ArgumentException("tweaks required");
                return await _bridge.MinimapApplyTweaksAsync(dto);
            },
            ["minimapDetectRings"] = async _ => await _bridge.MinimapDetectRingsAsync(),
            ["minimapGetFontState"] = async _ => await _bridge.MinimapGetFontStateAsync(),
            ["minimapGetFontOptions"] = async _ => await _bridge.MinimapGetFontOptionsAsync(),
            ["otherGetArchiveFingerprint"] = async _ => await _bridge.OtherGetArchiveFingerprintAsync(),
            ["hotSwapGetStatus"] = async _ => await _bridge.HotSwapGetStatusAsync(),
            ["hotSwapSetEnabled"] = async payload =>
            {
                bool en = payload is not null
                    && payload.Value.TryGetProperty("enabled", out var e)
                    && e.ValueKind == JsonValueKind.True;
                int? m = null;
                if (payload is not null
                    && payload.Value.TryGetProperty("method", out var mv)
                    && mv.ValueKind == JsonValueKind.Number
                    && mv.TryGetInt32(out var mi)) m = mi;
                return await _bridge.HotSwapSetEnabledAsync(en, m);
            },
            ["hotSwapArmNow"]    = async _ => await _bridge.HotSwapArmNowAsync(),
            ["hotSwapDisarmNow"] = async _ => await _bridge.HotSwapDisarmNowAsync(),
            ["hotSwapRebuild"]   = async _ => await _bridge.HotSwapRebuildAsync(),
            ["hotSwapGetLog"] = async payload =>
            {
                int tailKb = 64;
                if (payload is not null
                    && payload.Value.TryGetProperty("tailKb", out var tk)
                    && tk.ValueKind == JsonValueKind.Number
                    && tk.TryGetInt32(out var kb)) tailKb = kb;
                return await _bridge.HotSwapGetLogAsync(tailKb);
            },
            ["downloadGetLog"] = async payload =>
            {
                int tailKb = 64;
                if (payload is not null
                    && payload.Value.TryGetProperty("tailKb", out var tk)
                    && tk.ValueKind == JsonValueKind.Number
                    && tk.TryGetInt32(out var kb)) tailKb = kb;
                return await _bridge.DownloadGetLogAsync(tailKb);
            },
            ["featureGetLog"] = async payload =>
            {
                int tailKb = 64;
                if (payload is not null
                    && payload.Value.TryGetProperty("tailKb", out var ftk)
                    && ftk.ValueKind == JsonValueKind.Number
                    && ftk.TryGetInt32(out var fkb)) tailKb = fkb;
                return await _bridge.FeatureGetLogAsync(tailKb);
            },
            ["reduxDeferMinimapReapplyOnce"] = async _ =>
            {
                await _bridge.ReduxDeferMinimapReapplyOnceAsync();
                return null;
            },
            ["minimapInstallFont"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                string path = payload.Value.GetProperty("path").GetString()
                    ?? throw new ArgumentException("path required");
                string? slot = payload.Value.TryGetProperty("slot", out var sl) && sl.ValueKind == JsonValueKind.String
                    ? sl.GetString() : null;
                return await _bridge.MinimapInstallFontAsync(path, slot);
            },
            ["minimapRestoreFont"] = async _ => await _bridge.MinimapRestoreFontAsync(),
            ["reduxDeferArmorReapplyOnce"] = async _ => { await _bridge.ReduxDeferArmorReapplyOnceAsync(); return null; },
            ["reduxDeferFastJoinReapplyOnce"] = async _ => { await _bridge.ReduxDeferFastJoinReapplyOnceAsync(); return null; },
            ["minimapRestoreVanilla"] = async _ => await _bridge.MinimapRestoreVanillaAsync(),
            ["otherSetZalazy"] = async payload =>
            {
                bool enabled = payload is not null
                    && payload.Value.TryGetProperty("enabled", out var e)
                    && e.ValueKind == JsonValueKind.True;
                string server = (payload is not null
                    && payload.Value.TryGetProperty("server", out var s)
                    && s.ValueKind == JsonValueKind.String)
                    ? (s.GetString() ?? "gta5rp") : "gta5rp";
                return await _bridge.OtherSetZalazyAsync(enabled, server);
            },
            ["otherGetZalazy"] = async _ => await _bridge.OtherGetZalazyAsync(),
            ["otherDetectOverlays"] = async _ => await _bridge.OtherDetectOverlaysAsync(),
            ["otherRemoveForeignOverlay"] = async payload =>
            {
                string kind = (payload is not null
                    && payload.Value.TryGetProperty("kind", out var k)
                    && k.ValueKind == JsonValueKind.String)
                    ? (k.GetString() ?? "zalazy") : "zalazy";
                return await _bridge.OtherRemoveForeignOverlayAsync(kind);
            },
            ["otherSetFastJoin"] = async payload =>
            {
                bool enabled = payload is not null
                    && payload.Value.TryGetProperty("enabled", out var e)
                    && e.ValueKind == JsonValueKind.True;
                return await _bridge.OtherSetFastJoinAsync(enabled);
            },
            ["otherGetFastJoin"] = async _ => await _bridge.OtherGetFastJoinAsync(),
            ["otherGetFastJoinStatus"] = async _ => await _bridge.OtherGetFastJoinStatusAsync(),
            ["reduxBundledFeatures"] = async payload =>
            {
                string reduxId = payload is not null && payload.Value.TryGetProperty("reduxId", out var rid)
                    ? rid.GetString() ?? "" : "";
                string? versionId = payload is not null && payload.Value.TryGetProperty("versionId", out var vid)
                    && vid.ValueKind == JsonValueKind.String ? vid.GetString() : null;
                return await _bridge.ReduxBundledFeaturesAsync(reduxId, versionId);
            },
            ["otherSetGreenZone"] = async payload =>
            {
                bool enabled = payload is not null
                    && payload.Value.TryGetProperty("enabled", out var e)
                    && e.ValueKind == JsonValueKind.True;
                return await _bridge.OtherSetGreenZoneAsync(enabled);
            },
            ["otherGetGreenZone"] = async _ => await _bridge.OtherGetGreenZoneAsync(),
            ["otherSetCarLogos"] = async payload =>
            {
                bool enabled = payload is not null
                    && payload.Value.TryGetProperty("enabled", out var e)
                    && e.ValueKind == JsonValueKind.True;
                return await _bridge.OtherSetCarLogosAsync(enabled);
            },
            ["otherGetCarLogos"] = async _ => await _bridge.OtherGetCarLogosAsync(),
            ["otherSetRukzak"] = async payload =>
            {
                bool enabled = payload is not null
                    && payload.Value.TryGetProperty("enabled", out var e)
                    && e.ValueKind == JsonValueKind.True;
                return await _bridge.OtherSetRukzakAsync(enabled);
            },
            ["otherGetRukzak"] = async _ => await _bridge.OtherGetRukzakAsync(),
            ["otherGetBackpackStatus"] = async _ => await _bridge.OtherGetBackpackStatusAsync(),
            ["otherApplyBackpack"] = async payload =>
            {
                var action = payload is not null
                    && payload.Value.TryGetProperty("action", out var a)
                    && a.ValueKind == JsonValueKind.String
                        ? a.GetString() ?? "remove"
                        : "remove";
                return await _bridge.OtherApplyBackpackAsync(action);
            },
            ["otherSetSmoke"] = async payload =>
            {
                bool enabled = payload is not null
                    && payload.Value.TryGetProperty("enabled", out var e)
                    && e.ValueKind == JsonValueKind.True;
                return await _bridge.OtherSetSmokeAsync(enabled);
            },
            ["otherGetSmoke"] = async _ => await _bridge.OtherGetSmokeAsync(),
            ["otherSetNoTracer"] = async payload =>
            {
                bool enabled = payload is not null
                    && payload.Value.TryGetProperty("enabled", out var e)
                    && e.ValueKind == JsonValueKind.True;
                string[]? categories = null;
                if (payload is not null
                    && payload.Value.TryGetProperty("categories", out var c)
                    && c.ValueKind == JsonValueKind.Array)
                {
                    categories = c.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!)
                        .ToArray();
                }
                bool keepSnipers = payload is not null
                    && payload.Value.TryGetProperty("keepSnipers", out var ks)
                    && ks.ValueKind == JsonValueKind.True;
                return await _bridge.OtherSetNoTracerAsync(enabled, categories, keepSnipers);
            },
            ["otherGetNoTracer"] = async _ => await _bridge.OtherGetNoTracerAsync(),
            ["otherSetTracerStudio"] = async payload =>
            {
                string? settings = payload is not null
                    && payload.Value.TryGetProperty("settings", out var s)
                    && s.ValueKind == JsonValueKind.String
                    ? s.GetString() : null;
                return await _bridge.OtherSetTracerStudioAsync(settings);
            },
            ["otherGetTracerStudio"] = async _ => await _bridge.OtherGetTracerStudioAsync(),
            ["improvementsList"] = async _ => await _bridge.ImprovementsListAsync(),
            ["improvementInstall"] = async payload =>
            {
                var id = (payload is not null
                    && payload.Value.TryGetProperty("id", out var i)
                    && i.ValueKind == JsonValueKind.String)
                    ? i.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required");
                return await _bridge.ImprovementInstallAsync(id!);
            },
            ["improvementRemove"] = async payload =>
            {
                var id = (payload is not null
                    && payload.Value.TryGetProperty("id", out var i)
                    && i.ValueKind == JsonValueKind.String)
                    ? i.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required");
                return await _bridge.ImprovementRemoveAsync(id!);
            },
            ["bigMapList"] = async _ => await _bridge.BigMapListAsync(),
            ["bigMapGetState"] = async _ => await _bridge.BigMapGetStateAsync(),
            ["bigMapInstall"] = async payload =>
            {
                var id = (payload is not null
                    && payload.Value.TryGetProperty("id", out var i)
                    && i.ValueKind == JsonValueKind.String)
                    ? i.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required");
                return await _bridge.BigMapInstallAsync(id!);
            },
            ["bigMapUninstall"] = async _ => await _bridge.BigMapUninstallAsync(),
            ["bigMapPreviewGlb"] = async payload =>
            {
                var id = (payload is not null
                    && payload.Value.TryGetProperty("id", out var i)
                    && i.ValueKind == JsonValueKind.String)
                    ? i.GetString() : null;
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required");
                return await _bridge.BigMapPreviewGlbAsync(id!);
            },
#if ADMIN
            ["adminBigMapAnalyze"] = async payload =>
            {
                var src = payload?.GetProperty("sourcePath").GetString()
                          ?? throw new ArgumentException("sourcePath required");
                return await _bridge.AdminBigMapAnalyzeAsync(src);
            },
            ["adminBigMapPublish"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var p = payload.Value;
                string? Str(string name) => p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                List<string> Arr(string name) =>
                    p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
                        ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
                        : new List<string>();
                var req = new BigMapPublishRequestDto(
                    SourcePath: Str("sourcePath") ?? throw new ArgumentException("sourcePath required"),
                    Name: Str("name") ?? throw new ArgumentException("name required"),
                    Author: Str("author") ?? "",
                    AuthorLink: Str("authorLink") ?? "",
                    Description: Str("description") ?? "",
                    SupportedServers: Arr("supportedServers"),
                    PhotoPaths: Arr("photoPaths"),
                    VideoUrl: Str("videoUrl"),
                    ExistingId: Str("existingId"));
                return await _bridge.AdminBigMapPublishAsync(req);
            },
            ["adminBigMapList"] = async _ => await _bridge.AdminBigMapListAsync(),
            ["adminBigMapDelete"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");
                await _bridge.AdminBigMapDeleteAsync(id);
                return null;
            },
#endif
#if ADMIN
            ["adminCreateLibraryStub"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var type        = payload.Value.GetProperty("type").GetString()
                                  ?? throw new ArgumentException("type required");
                var name        = payload.Value.GetProperty("name").GetString()
                                  ?? throw new ArgumentException("name required");
                var author      = payload.Value.TryGetProperty("author", out var a)
                                  ? a.GetString() ?? "" : "";
                var description = payload.Value.TryGetProperty("description", out var d)
                                  ? d.GetString() ?? "" : "";
                var photoPath   = payload.Value.GetProperty("photoPath").GetString()
                                  ?? throw new ArgumentException("photoPath required");
                return await _bridge.AdminCreateLibraryStubAsync(type, name, author, description, photoPath);
            },
#endif
#if ADMIN
            ["adminCreateLibraryMinimap"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var name        = payload.Value.GetProperty("name").GetString()
                                  ?? throw new ArgumentException("name required");
                var author      = payload.Value.TryGetProperty("author", out var a)
                                  ? a.GetString() ?? "" : "";
                var description = payload.Value.TryGetProperty("description", out var d)
                                  ? d.GetString() ?? "" : "";
                var gfxPath     = payload.Value.GetProperty("gfxPath").GetString()
                                  ?? throw new ArgumentException("gfxPath required");
                var photoPath   = payload.Value.GetProperty("photoPath").GetString()
                                  ?? throw new ArgumentException("photoPath required");
                return await _bridge.AdminCreateLibraryMinimapAsync(name, author, description, gfxPath, photoPath);
            },
#endif
            ["getCurrentReticleInfo"] = async _ =>
            {
                return await _bridge.GetCurrentReticleInfoAsync();
            },
            ["reduxApplyReticle"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var source = payload.Value.GetProperty("source").GetString()
                             ?? throw new ArgumentException("source required");
                var id     = payload.Value.GetProperty("id").GetString()
                             ?? throw new ArgumentException("id required");
                string? displayName = null;
                if (payload.Value.TryGetProperty("displayName", out var dnEl)
                    && dnEl.ValueKind == JsonValueKind.String)
                {
                    displayName = dnEl.GetString();
                }
                return await _bridge.ReduxApplyReticleAsync(source, id, displayName);
            },
            ["reduxResetCustomization"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var part = payload.Value.GetProperty("part").GetString()
                           ?? throw new ArgumentException("part required");
                return await _bridge.ReduxResetCustomizationAsync(part);
            },
            ["reticleApplyCustom"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var specEl = payload.Value.GetProperty("spec");
                var spec = JsonSerializer.Deserialize<ReticleSpecDto>(
                        specEl.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new ArgumentException("spec invalid");
                return await _bridge.ReticleApplyCustomAsync(spec);
            },
            ["knkShare"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var userId = payload.Value.GetProperty("userId").GetString()
                             ?? throw new ArgumentException("userId required");
                var specEl = payload.Value.GetProperty("spec");
                var spec = JsonSerializer.Deserialize<ReticleSpecDto>(
                        specEl.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new ArgumentException("spec invalid");
                return await _bridge.KnkShareAsync(userId, spec);
            },
            ["knkFetch"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var code = payload.Value.GetProperty("code").GetString()
                           ?? throw new ArgumentException("code required");
                return await _bridge.KnkFetchAsync(code);
            },
            ["legitCheckRedux"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var reduxId = payload.Value.GetProperty("reduxId").GetString()
                              ?? throw new ArgumentException("reduxId required");
                string? versionId = payload.Value.TryGetProperty("versionId", out var v)
                    && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                return await _bridge.LegitCheckReduxAsync(reduxId, versionId);
            },
            ["legitCheckUpdateRpf"] = async payload =>
            {
                string? rpfPath = payload is not null
                    && payload.Value.TryGetProperty("rpfPath", out var p)
                    && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                return await _bridge.LegitCheckUpdateRpfAsync(rpfPath);
            },
            ["legitReportShare"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var userId = payload.Value.GetProperty("userId").GetString()
                             ?? throw new ArgumentException("userId required");
                var repEl = payload.Value.GetProperty("report");
                var report = JsonSerializer.Deserialize<LegitReportDto>(
                        repEl.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? throw new ArgumentException("report invalid");
                return await _bridge.LegitReportShareAsync(userId, report);
            },
            ["legitReportFetch"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var code = payload.Value.GetProperty("code").GetString()
                           ?? throw new ArgumentException("code required");
                return await _bridge.LegitReportFetchAsync(code);
            },
#if ADMIN
            ["adminCreateLibraryReticle"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var name        = payload.Value.GetProperty("name").GetString()
                                  ?? throw new ArgumentException("name required");
                var author      = payload.Value.TryGetProperty("author", out var a)
                                  ? a.GetString() ?? "" : "";
                var description = payload.Value.TryGetProperty("description", out var d)
                                  ? d.GetString() ?? "" : "";
                var gfxPath     = payload.Value.GetProperty("gfxPath").GetString()
                                  ?? throw new ArgumentException("gfxPath required");
                var photoPath   = payload.Value.GetProperty("photoPath").GetString()
                                  ?? throw new ArgumentException("photoPath required");
                return await _bridge.AdminCreateLibraryReticleAsync(name, author, description, gfxPath, photoPath);
            },
#endif
#if ADMIN
            ["adminUploadLibraryGallery"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var libraryId = payload.Value.GetProperty("libraryId").GetString()
                                ?? throw new ArgumentException("libraryId required");
                var paths = new List<string>();
                if (payload.Value.TryGetProperty("sourcePaths", out var arr)
                    && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in arr.EnumerateArray())
                    {
                        var s = p.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) paths.Add(s!);
                    }
                }
                if (paths.Count == 0)
                    throw new ArgumentException("sourcePaths must be a non-empty string array");
                return await _bridge.AdminUploadLibraryGalleryAsync(libraryId, paths);
            },
#endif
            ["getCurrentSoundPackInfo"] = async _ => await _bridge.GetCurrentSoundPackInfoAsync(),
            ["soundPackInstall"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var libraryId = payload.Value.GetProperty("libraryId").GetString()
                                ?? throw new ArgumentException("libraryId required");
                string? displayName = null;
                if (payload.Value.TryGetProperty("displayName", out var dn)
                    && dn.ValueKind == JsonValueKind.String)
                {
                    displayName = dn.GetString();
                }
                return await _bridge.SoundPackInstallAsync(libraryId, displayName);
            },
            ["soundPackUninstall"] = async _ => await _bridge.SoundPackUninstallAsync(),
#if ADMIN
            ["adminUploadLibraryVideo"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var libraryId  = payload.Value.GetProperty("libraryId").GetString()
                                 ?? throw new ArgumentException("libraryId required");
                var sourcePath = payload.Value.GetProperty("sourcePath").GetString()
                                 ?? throw new ArgumentException("sourcePath required");
                return await _bridge.AdminUploadLibraryVideoAsync(libraryId, sourcePath);
            },
#endif
#if ADMIN
            ["adminCreateLibrarySounds"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var name        = payload.Value.GetProperty("name").GetString()
                                  ?? throw new ArgumentException("name required");
                var author      = payload.Value.TryGetProperty("author", out var a)
                                  ? a.GetString() ?? "" : "";
                var description = payload.Value.TryGetProperty("description", out var d)
                                  ? d.GetString() ?? "" : "";
                var zipPath     = payload.Value.GetProperty("zipPath").GetString()
                                  ?? throw new ArgumentException("zipPath required");
                var photoPath   = payload.Value.GetProperty("photoPath").GetString()
                                  ?? throw new ArgumentException("photoPath required");
                return await _bridge.AdminCreateLibrarySoundsAsync(name, author, description, zipPath, photoPath);
            },
#endif
#if ADMIN
            ["adminCreateLibraryAwc"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var name        = payload.Value.GetProperty("name").GetString()
                                  ?? throw new ArgumentException("name required");
                var author      = payload.Value.TryGetProperty("author", out var a)
                                  ? a.GetString() ?? "" : "";
                var description = payload.Value.TryGetProperty("description", out var d)
                                  ? d.GetString() ?? "" : "";
                var awcPath     = payload.Value.GetProperty("awcPath").GetString()
                                  ?? throw new ArgumentException("awcPath required");
                var photoPath   = payload.Value.TryGetProperty("photoPath", out var p)
                                  ? p.GetString() ?? "" : "";
                return await _bridge.AdminCreateLibraryAwcSoundAsync(name, author, description, awcPath, photoPath);
            },
#endif
#if ADMIN
            ["adminUploadGunpackCover"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var src = payload.Value.GetProperty("sourcePath").GetString()
                          ?? throw new ArgumentException("sourcePath required");
                return await _bridge.AdminUploadGunpackCoverAsync(src);
            },
#endif
            ["userBuildListPending"] = async _ =>
            {
                return await _bridge.UserBuildListPendingAsync();
            },
            ["userBuildListMyPending"] = async payload =>
            {
                var authorUserId = payload?.GetProperty("authorUserId").GetString()
                                   ?? throw new ArgumentException("authorUserId required");
                return await _bridge.UserBuildListMyPendingAsync(authorUserId);
            },
            ["userBuildApprove"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var id        = payload.Value.GetProperty("id").GetString()
                                ?? throw new ArgumentException("id required");
                var reviewer  = payload.Value.GetProperty("reviewerUserId").GetString()
                                ?? throw new ArgumentException("reviewerUserId required");
                int? tier     = null;
                if (payload.Value.TryGetProperty("tier", out var tEl)
                    && tEl.ValueKind != System.Text.Json.JsonValueKind.Null)
                {

                    if (tEl.ValueKind == System.Text.Json.JsonValueKind.Number) tier = tEl.GetInt32();
                    else if (int.TryParse(tEl.GetString(), out var parsed))     tier = parsed;
                }
                return await _bridge.UserBuildApproveAsync(id, reviewer, tier);
            },
            ["userBuildReject"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var id        = payload.Value.GetProperty("id").GetString()
                                ?? throw new ArgumentException("id required");
                var reviewer  = payload.Value.GetProperty("reviewerUserId").GetString()
                                ?? throw new ArgumentException("reviewerUserId required");
                var reason    = payload.Value.GetProperty("reason").GetString()
                                ?? throw new ArgumentException("reason required");
                return await _bridge.UserBuildRejectAsync(id, reviewer, reason);
            },
            ["userBuildResubmit"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");
                return await _bridge.UserBuildResubmitAsync(id);
            },

            ["gtaPresetsList"] = async payload =>
            {
                string? search = null;
                if (payload is not null && payload.Value.TryGetProperty("search", out var s) &&
                    s.ValueKind != System.Text.Json.JsonValueKind.Null)
                    search = s.GetString();
                return await _bridge.GtaPresetsListAsync(search);
            },
            ["gtaPresetGet"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");
                return await _bridge.GtaPresetGetAsync(id);
            },
            ["gtaPresetApply"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");
                return await _bridge.GtaPresetApplyAsync(id);
            },
            ["gtaSettingsApplyFromUrl"] = async payload =>
            {
                var url = payload?.GetProperty("xmlUrl").GetString()
                          ?? throw new ArgumentException("xmlUrl required");
                return await _bridge.GtaSettingsApplyFromUrlAsync(url);
            },
            ["gtaPresetIncrementDownloads"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");
                return await _bridge.GtaPresetIncrementDownloadsAsync(id);
            },
            ["gtaPresetReactionsGet"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var id = payload.Value.GetProperty("presetId").GetString()
                         ?? throw new ArgumentException("presetId required");
                var userId = payload.Value.TryGetProperty("userId", out var u) ? (u.GetString() ?? "") : "";
                return await _bridge.GtaPresetReactionsGetAsync(id, userId);
            },
            ["gtaPresetReactionSet"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var id = payload.Value.GetProperty("presetId").GetString()
                         ?? throw new ArgumentException("presetId required");
                var reaction = payload.Value.GetProperty("reaction").GetInt32();
                return await _bridge.GtaPresetReactionSetAsync(id, reaction);
            },
            ["gtaInstallCounts"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var et = payload.Value.GetProperty("eventType").GetString()
                         ?? throw new ArgumentException("eventType required");
                return await _bridge.GtaInstallCountsAsync(et);
            },
            ["accountStats"] = async _ => await _bridge.AccountStatsGetAsync(),
#if ADMIN
            ["adminGtaPresetList"] = async _ => await _bridge.AdminGtaPresetListAsync(),
#endif
#if ADMIN
            ["adminGtaPresetUpload"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var req = payload.Value.Deserialize<GtaPresetUploadRequestDto>(JsonOptions)
                          ?? throw new ArgumentException("could not parse upload request");
                return await _bridge.AdminGtaPresetUploadAsync(req);
            },
#endif
#if ADMIN
            ["adminGtaPresetPatch"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var p = payload.Value;
                var id = p.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");
                var patchEl = p.GetProperty("patch");
                var patch = patchEl.Deserialize<GtaPresetPatchDto>(JsonOptions)
                            ?? throw new ArgumentException("could not parse patch");
                await _bridge.AdminGtaPresetPatchAsync(id, patch);
                return null;
            },
#endif
#if ADMIN
            ["adminGtaPresetDelete"] = async payload =>
            {
                var id = payload?.GetProperty("id").GetString()
                         ?? throw new ArgumentException("id required");
                await _bridge.AdminGtaPresetDeleteAsync(id);
                return null;
            },
#endif
#if ADMIN
            ["adminGtaPresetAnalyze"] = async payload =>
            {
                var path = payload?.GetProperty("sourceXmlPath").GetString()
                           ?? throw new ArgumentException("sourceXmlPath required");
                return await _bridge.AdminGtaPresetAnalyzeAsync(path);
            },
#endif

            ["gtaSettingsRead"] = async _ => await _bridge.GtaSettingsReadAsync(),
            ["optimizationCatalogGet"] = async _ => await _bridge.OptimizationCatalogGetAsync(),
            ["optimizationStateGet"]   = async _ => await _bridge.OptimizationStateGetAsync(),
            ["optimizationApply"] = async payload =>
            {
                var raw = payload?.GetProperty("selections")
                          ?? throw new ArgumentException("selections required");
                var picks = raw.EnumerateArray().Select(e => new OptimizationSelectionDto(
                    e.GetProperty("groupKey").GetString() ?? throw new ArgumentException("groupKey required"),
                    e.TryGetProperty("optionIdx", out var idx) && idx.ValueKind != JsonValueKind.Null
                        ? idx.GetInt32() : (int?)null)).ToList();
                return await _bridge.OptimizationApplyAsync(picks);
            },
            ["optimizationResolveFromPreset"] = async payload =>
            {
                var id = payload?.GetProperty("presetId").GetString()
                         ?? throw new ArgumentException("presetId required");
                return await _bridge.OptimizationResolveFromPresetAsync(id);
            },
            ["gtaSettingsAnalyzeModel"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var model = payload.Value.Deserialize<GtaSettingsModelDto>(JsonOptions)
                            ?? throw new ArgumentException("could not parse model");
                return await _bridge.GtaSettingsAnalyzeModelAsync(model);
            },
            ["gtaSettingsWrite"] = async payload =>
            {
                if (payload is null) throw new ArgumentException("payload required");
                var model = payload.Value.Deserialize<GtaSettingsModelDto>(JsonOptions)
                            ?? throw new ArgumentException("could not parse model");
                return await _bridge.GtaSettingsWriteAsync(model);
            },

            ["mirrorSetOverride"] = async payload =>
            {
                var choice = payload?.GetProperty("choice").GetString();
                await _bridge.MirrorSetOverrideAsync(choice);
                return null!;
            },
            ["mirrorProbe"] = async payload =>
            {
                var choice = payload?.GetProperty("choice").GetString();
                return await _bridge.MirrorProbeAsync(choice);
            },
            ["zapretApplyWhitelist"] = async payload =>
            {
                var path = payload?.GetProperty("path").GetString()
                           ?? throw new ArgumentException("path required");
                return await _bridge.ZapretApplyWhitelistAsync(path);
            },
            ["zapretDetect"] = async payload =>
            {
                string? path = null;
                if (payload is { } p && p.TryGetProperty("path", out var pv) && pv.ValueKind == JsonValueKind.String)
                    path = pv.GetString();
                return await _bridge.ZapretDetectAsync(path);
            },

            ["rendererEnsureInstalled"] = async _ =>
                await _bridge.RendererEnsureInstalledAsync(),

            ["rendererProbe"] = async _ =>
                await _bridge.RendererProbeAsync(),

            ["rendererTestRender"] = async _ =>
                await _bridge.RendererTestRenderAsync(),

            ["rendererForceReinstall"] = async _ =>
                await _bridge.RendererForceReinstallAsync(),

            ["jreEnsureInstalled"] = async _ =>
                await _bridge.JreEnsureInstalledAsync(),

            ["bypassTestRun"] = async payload =>
            {
                var strategyId = payload?.GetProperty("strategyId").GetInt32() ?? 0;
                var strategy = (Services.BypassTester.Strategy)strategyId;
                return await Services.BypassTester.RunAsync(strategy);
            },

            ["networkDoctorRun"] = async payload =>
            {
                string? url = null;
                if (payload.HasValue && payload.Value.TryGetProperty("url", out var u))
                    url = u.GetString();
                url ??= AppBridge.LastProblemDownloadUrl;
                return await Services.NetworkDoctor.RunAsync(url);
            },

            ["serverRegionGet"]  = async _ => await _bridge.ServerRegionGetAsync(),
            ["serverRegionSet"]  = async payload =>
            {
                var region = payload?.GetProperty("region").GetString()
                             ?? throw new ArgumentException("region required");
                await _bridge.ServerRegionSetAsync(region);
                return null!;
            },
            ["serverRegionPing"] = async _ => await _bridge.ServerRegionPingAsync(),

            ["downloadSourceGet"] = async _ => await _bridge.DownloadSourceGetAsync(),
            ["downloadSourceSet"] = async payload =>
            {
                var source = payload?.GetProperty("source").GetString()
                             ?? throw new ArgumentException("source required");
                await _bridge.DownloadSourceSetAsync(source);
                return null!;
            },
            ["downloadSourceEvaluateEu"] = async payload =>
            {
                string? path = null;
                if (payload.HasValue && payload.Value.TryGetProperty("zapretRootPath", out var p))
                    path = p.GetString();
                return await _bridge.DownloadSourceEvaluateEuAsync(path);
            },
        };
    }

    private static bool HotSwapBlocksMutations(out string msg)
    {
        msg = string.Empty;
        try
        {
            var mode = MiamiGraphics.Core.HotSwap.HotSwapModeStore.Read();
            if (!mode.Enabled || string.IsNullOrWhiteSpace(mode.GtaRoot)) return false;
            if (MiamiGraphics.Core.HotSwap.GameFileSwapper.ReadSet(mode.GtaRoot!).Count == 0)
            {
                msg = "Режим Rockstar включён, но образ модов потерян - защищать нечего. " +
                      "Нажми «Пересобрать» в Настройках либо выключи режим, поставь моды и включи снова.";
                return true;
            }
            msg = "Включён режим Rockstar Launcher - файлы игры сейчас чистые, " +
                  "и установка не сохранится. Выключи режим в Настройках, поставь моды, " +
                  "потом включи режим снова.";
            return true;
        }
        catch { return false; }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string? id = null;
        try
        {
            if (!IsTrustedSource(e.Source))
            {
                Debug.WriteLine($"[Bridge] ignored message from untrusted source: {e.Source}");
                return;
            }
            var raw = e.TryGetWebMessageAsString();
            Debug.WriteLine($"[Bridge] <- {raw}");
            var request = JsonSerializer.Deserialize<BridgeRequest>(raw, JsonOptions);
            if (request is null)
                throw new InvalidOperationException("Empty bridge request");
            id = request.Id;

            if (!_handlers.TryGetValue(request.Command, out var handler))
                throw new InvalidOperationException($"Unknown command: {request.Command}");

            if (CriticalCommands.Contains(request.Command)
                && !string.Equals(request.Command, "hotSwapSetEnabled", StringComparison.Ordinal)
                && !string.Equals(request.Command, "hotSwapArmNow", StringComparison.Ordinal)
                && !string.Equals(request.Command, "hotSwapDisarmNow", StringComparison.Ordinal)
                && !string.Equals(request.Command, "hotSwapRebuild", StringComparison.Ordinal)
                && HotSwapBlocksMutations(out var gateMsg))
                throw new InvalidOperationException(gateMsg);

            using var critical = CriticalCommands.Contains(request.Command)
                ? CriticalOperationGuard.Enter()
                : null;

            var data = await Task.Run(() => handler(request.Payload));
            Send(new BridgeResponse(id, true, data, null));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Bridge] !! {ex.Message}");
            Send(new BridgeResponse(id ?? string.Empty, false, null, ex.Message));
        }
    }

    private void Send(BridgeResponse response)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        Debug.WriteLine($"[Bridge] -> {json}");
        PostJsonOnUiThread(json);
    }

    private static bool IsTrustedSource(string? source)
    {
        if (string.IsNullOrEmpty(source)) return true;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return true;
        var host = uri.Host;
        return host.Equals("app.huntergraphics.local", StringComparison.OrdinalIgnoreCase)
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals("127.0.0.1", StringComparison.Ordinal);
    }

    private static Guid? ParseOptionalGuid(JsonElement? payload, string propName)
    {
        if (payload is null) return null;
        if (!payload.Value.TryGetProperty(propName, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Null) return null;
        var s = el.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        return Guid.TryParse(s, out var g) ? g : null;
    }

    private static object? JsonElementToObject(JsonElement el)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;
            case JsonValueKind.True:   return true;
            case JsonValueKind.False:  return false;
            case JsonValueKind.String: return el.GetString();
            case JsonValueKind.Number:

                if (el.TryGetInt64(out var l)) return l;
                if (el.TryGetDouble(out var d)) return d;
                return el.GetRawText();
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object?>();
                foreach (var p in el.EnumerateObject())
                    obj[p.Name] = JsonElementToObject(p.Value);
                return obj;
            case JsonValueKind.Array:
                var arr = new List<object?>();
                foreach (var item in el.EnumerateArray())
                    arr.Add(JsonElementToObject(item));
                return arr;
            default:
                return el.GetRawText();
        }
    }
}
