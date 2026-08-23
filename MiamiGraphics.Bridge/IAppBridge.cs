using System.Text.Json;

namespace MiamiGraphics.Bridge;

public interface IAppBridge
{

    Task<SystemInfoDto> GetSystemInfoAsync();

    Task<string> GetAppVersionAsync();

    Task<List<bool>> AssetCacheContainsAsync(List<string> urls);

    Task<int> AssetCachePrewarmAsync(List<string> urls);
    Task<List<string>> GunpackAllGunPreviewUrlsAsync();
    Task<AuthResultDto> AuthenticateGuestAsync();
    Task<AppSettingsDto> GetAppSettingsAsync();
    Task SaveAppSettingsAsync(AppSettingsDto settings);

    Task SetUiLanguageAsync(string lang);
    Task WindowMinimizeAsync();
    Task WindowMaximizeAsync();
    Task WindowCloseAsync();
    Task WindowStartDragAsync();
    Task WindowSetFullscreenAsync(bool on);
    Task<string?> OpenFolderDialogAsync();
    Task OpenLogsFolderAsync();

    Task<CacheSettingsDto> CacheSettingsGetAsync();
    Task<CacheSettingsDto> CacheSettingsSetAsync(bool enabled, string? rootOverride);

    Task<CacheSettingsDto> CacheLimitSetAsync(long limitBytes);

    Task<DataMoveResultDto> DataRootMoveAsync(string targetDir);

    Task DataRootMoveCancelAsync();

    Task<CacheCleanupResultDto> CacheCleanupNowAsync();
    Task<bool> ValidateGtaPathAsync(string path);

    Task<GtaPathInfoDto> GetGtaPathInfoAsync();
    Task<bool> SetGtaPathOverrideAsync(string path);
    Task<bool> ClearGtaPathOverrideAsync();

    Task<List<GunpackBatchEntryDto>> ScanGunpackBatchFolderAsync(string parentPath);
    Task<AuthResultDto> AuthenticateUserAsync(string login, string password, string? totp);

#if ADMIN
    Task<AdminWebAuthResultDto> AdminWebAuthenticateAsync();
#endif

    Task RegisterRequestAsync(string email, string username, string password);
    Task<AuthResultDto> RegisterConfirmAsync(string email, string code);

    Task<string> InstallerPromoAsync();
    Task<PromoCheckDto> CheckPromoAsync(string code);
    Task<bool> AttachReferralAsync(string code);

    Task<System.Text.Json.Nodes.JsonNode?> ModmakersListAsync(string? q);
    Task<System.Text.Json.Nodes.JsonNode?> ModmakerDetailAsync(string code);
    Task<System.Text.Json.Nodes.JsonNode?> ModmakerFollowAsync(string code, bool on);
    Task<System.Text.Json.Nodes.JsonNode?> ModmakerFeedAsync(bool notify);
    Task<System.Text.Json.Nodes.JsonNode?> ModmakerMapAsync();
    Task<System.Text.Json.Nodes.JsonNode?> ModmakerCanEditAsync(string code);

    Task<BetaGateDto> BetaCodeCheckAsync(string code);
    Task<BetaGateDto> BetaRedeemAsync(string code);
    Task<BetaGateDto> BetaCheckAsync();
    Task<bool> BetaEnabledAsync();
    Task<bool> ActivityLogAsync(string eventType, string detail);
    Task<bool> ActivityLogAsync(string eventType, string detail, string? itemId);
    Task<ServerStatusDto> GetServerStatusAsync();
    Task<AppUpdateInfoDto> AppUpdateCheckAsync();
    Task<AppUpdateInstallResultDto> AppUpdateInstallAsync(string version);

    Task RequestPasswordResetAsync(string email);
    Task ConsumePasswordResetAsync(string code, string newPassword);

    Task<UserProfileDto?> GetUserProfileAsync(string userId);
    Task<UserProfileDto>  UpdateUserProfileAsync(string userId, string username, string? avatarUrl);

    Task ChangePasswordRequestAsync(string userId, string oldPassword, string newPassword);
    Task ChangePasswordConfirmAsync(string userId, string code);
    Task ChangeEmailRequestAsync(string userId, string currentPassword, string newEmail);
    Task<UserProfileDto> ChangeEmailConfirmAsync(string userId, string code);

    Task<string> UploadAvatarAsync(string userId, string localPath);

    Task<List<InstallHistoryEntryDto>> InstallHistoryListAsync(string userId);
    Task<InstallHistoryEntryDto>       InstallRecordAsync(string userId, string reduxId, string name, string author, string? previewUrl);

    Task<BackupStatusDto> BackupGetStatusAsync();
    Task<BackupResultDto> BackupRunFullAsync();
    Task<bool> BackupCancelAsync();
    Task<bool> BackupRestoreCleanAsync();
    Task<bool> BackupRestoreSnapshotAsync();
    Task<int> KillProcessesByPidAsync(int[] pids);
    Task FactoryResetAndRestartAsync();

    Task LauncherUninstallAsync();

    Task<string?> OpenFileDialogAsync(string? filterDescription, string? filterPattern);

    Task<string[]> OpenFileDialogMultiAsync(string? filterDescription, string? filterPattern);

#if ADMIN
    Task<AdminConfigDto> AdminConfigGetAsync();
#endif
#if ADMIN
    Task AdminConfigSaveAsync(AdminConfigDto config);
#endif
#if ADMIN
    Task<TestConnectionResultDto> AdminConfigTestR2Async(AdminConfigDto config);
#endif

#if ADMIN
    Task<ReduxAnalysisDto> AdminReduxAnalyzeAsync(string sourcePath);
#endif

#if ADMIN
    Task<List<QueueItemDto>> AdminQueueListAsync();
#endif
#if ADMIN
    Task<QueueItemDto> AdminQueueAddAsync(QueueItemDto item);
#endif
#if ADMIN
    Task AdminQueueRemoveAsync(string tempId);
#endif
#if ADMIN
    Task AdminQueueRunAsync();
#endif
#if ADMIN
    Task AdminQueueCancelAsync();
#endif

#if ADMIN
    Task<int> AdminRebuildReduxComponentsAsync();
#endif

#if ADMIN
    Task<int> AdminRecalculateReduxPatchSizesAsync();
#endif

#if ADMIN
    Task<List<ReduxItemDto>> AdminCatalogListAsync(string? search, string? server, string? status);
#endif
#if ADMIN
    Task AdminCatalogUpdateAsync(ReduxItemDto item);
#endif
#if ADMIN
    Task AdminCatalogDeleteAsync(string id);
#endif

#if ADMIN
    Task<AdminWipeAllResultDto> AdminWipeAllAsync(string category);
#endif

    Task<List<ReduxVersionDto>>     ReduxVersionsAsync(string reduxId);
#if ADMIN
    Task<DuplicateHashMatchDto?>    AdminFindByHashAsync(string sha256);
#endif
#if ADMIN
    Task                            AdminVersionUpsertAsync(ReduxVersionDto version);
#endif
#if ADMIN
    Task                            AdminVersionDeleteAsync(Guid id);
#endif

    Task<List<FeaturedPickDto>>     FeaturedPicksListAsync();
#if ADMIN
    Task                            AdminFeaturedPickSetAsync(int slotIndex, string reduxId);
#endif
#if ADMIN
    Task                            AdminFeaturedPickDeleteAsync(int slotIndex);
#endif

    Task<List<ReduxReviewDto>> ReduxReviewsListAsync(string reduxId);
    Task<ReduxReviewDto>       ReduxReviewSubmitAsync(string reduxId, string userId, string username, string role, string? avatarUrl, int rating, string body);
    Task<Dictionary<string, object>> ReduxRatingsAggregateAsync();
    Task<bool> ReduxReviewDeleteAsync(string reviewId, string userId, string role);

    Task<List<BigMapReviewDto>> BigMapReviewsListAsync(string mapId);
    Task<BigMapReviewDto>       BigMapReviewSubmitAsync(string mapId, string userId, string username, string role, string? avatarUrl, int rating, string body);
    Task<Dictionary<string, object>> BigMapRatingsAggregateAsync();
    Task<bool> BigMapReviewDeleteAsync(string reviewId, string userId, string role);

    Task<List<UserBuildReviewDto>> UserBuildReviewsListAsync(string buildId);
    Task<UserBuildReviewDto>       UserBuildReviewSubmitAsync(string buildId, string userId, string username, string role, string? avatarUrl, int rating, string body);
    Task<bool>                     UserBuildReviewDeleteAsync(string reviewId, string userId, string role);

#if ADMIN
    Task<InjectResultDto> AdminInjectAsync(string moddedRpfPath);
#endif
#if ADMIN
    Task<bool> AdminRestoreCleanUpdateAsync();
#endif

#if ADMIN
    Task<InjectResultDto> AdminInjectFromCatalogAsync(string reduxId);
#endif

    Task<List<ReduxItemDto>> ReduxListAsync(string? search, string? server);

    Task<List<string>> ReduxFavoriteListAsync(string userId);
    Task ReduxFavoriteAddAsync(string userId, string reduxId);
    Task ReduxFavoriteRemoveAsync(string userId, string reduxId);

    Task<List<string>> ItemFavoritesListAsync(string userId, string itemType);
    Task ItemFavoriteAddAsync(string userId, string itemType, string itemId);
    Task ItemFavoriteRemoveAsync(string userId, string itemType, string itemId);

    Task<long> ReduxIncrementDownloadsAsync(string reduxId);

    Task<InjectResultDto> ReduxInstallAsync(string reduxId, Guid? versionId);
    Task<InjectResultDto> ReduxInstallForceCleanAsync(string reduxId, Guid? versionId);
    Task<InjectResultDto> ReduxInstallPreserveAsync(string reduxId, Guid? versionId);
    Task ReduxInstallCancelAsync();

    Task ReduxDeferMinimapReapplyOnceAsync();

    Task<bool> InstallCancelAsync(string progressId);

    Task<InjectResultDto> ArmorInstallStandaloneAsync(string reduxId, Guid? versionId);

    Task<InjectResultDto> ArmorInstallStandaloneAsync(string reduxId, Guid? versionId, bool force);

    Task<InjectResultDto> ArmorInstallStandaloneAsync(string reduxId, Guid? versionId, bool force, bool confirmWipe);

    Task<DlcArmorInspectionResultDto> InspectDlcRpfArmorAsync(string dlcRpfPath);

    Task<bool> InspectDlcRpfArmorCancelAsync();

    Task<DlcArmorImportResultDto> ImportDlcRpfArmorAsync(DlcArmorImportRequestDto request);

    Task<string?> ReadLocalFileBase64Async(string absolutePath);

    Task<List<ArmorLibraryItemDto>> ArmorLibraryListAsync();

    Task<List<ArmorLibraryItemDto>> ArmorLibraryListAllAsync();

    Task<bool> ArmorLibrarySetVisibilityAsync(string armorLibraryId, bool visible);

    Task<bool> ArmorLibrarySetSupportedServersAsync(string armorLibraryId, List<string> servers);

    Task<bool> ArmorLibraryDeleteAsync(string armorLibraryId);

    Task<List<string>> ArmorLibraryRenderVariantsAsync(string armorLibraryId);

    Task<bool> ArmorLibrarySetPreviewAsync(string armorLibraryId, string previewUrl);

    Task<string?> ReduxArmorRenderPreviewAsync(string reduxId);

    Task<(int total, int rendered)> ReduxArmorBackfillPreviewsAsync();

    Task<List<string>> ReduxArmorRenderVariantsAsync(string reduxId);

    Task<List<string>> ReduxArmorVariantUrlsAsync(string reduxId);

    Task<bool> ReduxArmorSetPreviewAsync(string reduxId, string previewUrl);

    Task<InjectResultDto> ArmorLibraryInstallAsync(string armorLibraryId);
    Task<InjectResultDto> ArmorLibraryInstallAsync(string armorLibraryId, bool overlayMode);

    Task<InjectResultDto> ArmorLibraryInstallAsync(string armorLibraryId, bool overlayMode, bool force);

    Task<InjectResultDto> ArmorLibraryInstallAsync(string armorLibraryId, bool overlayMode, bool force, bool alreadyLocked, bool confirmWipe);

    Task<InjectResultDto> ReduxApplyArmorSwapAsync(string donorReduxId, Guid? donorVersionId);

    Task<InjectResultDto> ReduxClearArmorAsync();

    Task<CurrentArmorInfoDto?> GetCurrentArmorInfoAsync();

    Task<InjectResultDto> ReduxUninstallAsync();
    Task<InjectResultDto> ReduxUninstallForceCleanAsync();
    Task<InjectResultDto> ReduxUninstallPreserveAsync();

    Task<InjectResultDto> ReduxCustomizeApplyAsync(string reduxId, CustomizationDraftDto draft);

    Task<List<GtaVersionDto>> GtaVersionsListAsync();
    Task GtaVersionsUpsertAsync(GtaVersionDto version);
    Task GtaVersionsDeleteAsync(string exeVersion);

    Task<GtaVersionAutoFillDto> GtaVersionsAutoFillAsync(string cleanRpfPath);

    Task<GtaVersionDto> GtaVersionsUploadAsync(string cleanRpfPath, string exeVersion, string notes);

    Task<List<GunpackWhitelistEntryDto>> GunpackWhitelistListAsync();

    Task<List<GunpackDto>>     GunpacksListAsync(string? search, string? status);
    Task<GunpackDto?>          GunpackGetAsync(string id);
    Task<List<GunpackGunDto>>  GunpackGunsAsync(string gunpackId);

    Task<List<GunpackFlatGunDto>> GunpackAllGunsAsync();

    Task<long>                 GunpackIncrementDownloadsAsync(string id);

    Task<List<CustomGunDto>>   CustomGunsListAsync(string? search, string? sort, string? viewerUserId);
    Task<List<CustomGunDto>>   CustomGunsMineAsync(string ownerUserId);
    Task<CustomGunLimitsDto>   CustomGunLimitsAsync(string ownerUserId);
    Task                       CustomGunPatchAsync(string id, CustomGunPatchDto patch);
    Task                       CustomGunDeleteAsync(string id);
    Task                       CustomGunInstallAsync(string id);
    Task<CustomSkinAppliedDto[]> CustomSkinAppliedAsync();
    Task<InjectResultDto>       CustomSkinRemoveAsync(string internalName);

    Task<WorkshopSessionDto>   WorkshopOpenAsync(WorkshopOpenRequestDto req);
    Task<WorkshopReplaceResultDto> WorkshopReplaceTextureAsync(string draftId, string textureName, string pngBase64);
    Task                       WorkshopSaveDraftAsync(string draftId);
    Task                       WorkshopApplyToGameAsync(string draftId);
    Task<CustomGunDto>         WorkshopPublishAsync(string draftId, WorkshopPublishMetaDto meta, string ownerUserId, string ownerName);

    Task<List<CustomGunDto>>   CustomGunListPendingAsync();
    Task<CustomGunDto>         CustomGunApproveAsync(string id, string reviewerUserId);
    Task<CustomGunDto>         CustomGunRejectAsync(string id, string reviewerUserId, string reason);

    Task<List<CustomGunDto>>   CustomGunAdminListAsync(string? status, string? search);
    Task<CustomGunDto>         CustomGunAdminPatchAsync(string id, string? displayName, string? description, string? category);
    Task<CustomGunDto>         CustomGunAdminDeleteAsync(string id, string? reason, bool hard);

    Task<WorkshopFlowLimitsDto> WorkshopFlowLimitsAsync();
    Task<List<UserGunpackDto>>  UserGunpacksListAsync();
    Task                        UserGunpackInstallAsync(string id);
    Task                        UserGunpackDeleteAsync(string id);
    Task<string>                CustomGunPreviewDownloadAsync(string url, string suggestedName);

#if ADMIN
    Task<List<GunpackDto>>     AdminGunpackListAsync();
#endif
#if ADMIN
    Task                       AdminGunpackPatchAsync(string id, GunpackPatchDto patch);
#endif
#if ADMIN
    Task                       AdminGunpackDeleteAsync(string id);
#endif
#if ADMIN
    Task                       AdminGunpackGunPatchAsync(Guid gunId, GunpackGunPatchDto patch);
#endif
#if ADMIN
    Task                       AdminGunpackGunDeleteAsync(Guid gunId);
#endif

#if ADMIN
    Task<GunpackQueueItemDto>  AdminGunpackUploadAsync(GunpackUploadRequestDto request);
#endif
#if ADMIN
    Task<List<GunpackQueueItemDto>> AdminGunpackQueueListAsync();
#endif
#if ADMIN
    Task                       AdminGunpackQueueRemoveAsync(string tempId);
#endif

    Task<List<GunpackVariantDto>> GunpackVariantsListAsync(string gunpackId);
#if ADMIN
    Task                          AdminGunpackVariantPatchAsync(Guid variantId, GunpackVariantPatchDto patch);
#endif
#if ADMIN
    Task                          AdminGunpackVariantDeleteAsync(Guid variantId);
#endif
#if ADMIN
    Task                          AdminGunpackVariantSetDefaultAsync(Guid variantId);
#endif
#if ADMIN
    Task<GunpackQueueItemDto>     AdminGunpackVariantUploadAsync(string packId, string name, string sourceRpfPath, string? coverImagePath);
#endif

    Task<InjectResultDto>      GunpackInstallAllAsync(string gunpackId, Dictionary<string, string>? perGunResolutions = null, Guid? variantId = null);
    Task<InjectResultDto>      GunpackInstallSelectedAsync(string gunpackId, List<Guid> gunIds);
    Task<bool>                 GunpackUninstallAsync();

    Task<List<GunpackInstallConflictDto>> GunpackCheckInstallConflictsAsync(string gunpackId);

    Task<GunpackInstalledStateDto> GunpackGetInstalledStateAsync();
    Task<GunpackVerifyReportDto>   GunpackVerifyInstalledAsync();

    Task<bool> ReconcileInstallStateAsync();

    Task<List<SelectedGunDto>>           SelectedGunsListAsync();
    Task<bool>                           SelectedGunsIsInstalledAsync(string internalName);
    Task<InjectResultDto>                SelectedGunsInstallAsync(string gunpackId, string internalName);
    Task<InjectResultDto>                SelectedGunsRemoveAsync(string internalName);
    Task<InjectResultDto>                SelectedGunsRebuildAsync();
    Task<InjectResultDto>                SelectedGunsUninstallAllAsync();
    Task<SelectedGunsVerifyReportDto>    SelectedGunsVerifyAsync();

    Task<List<LibraryComponentDto>> LibraryListAsync(string? type);
    Task LibraryDeleteAsync(string id);

    Task<LibraryComponentDto> LibraryUploadComponentAsync(LibraryUploadDto payload);

    Task<LibraryComponentDto> LibraryPatchAsync(LibraryPatchDto payload);

    Task<JsonElement> InstallModAsync(string modId, string type, JsonElement payload, CancellationToken ct);
    Task<JsonElement> UninstallModAsync(string modId, CancellationToken ct);
    Task<JsonElement> CompareRpfAsync(string path, CancellationToken ct);
    Task<JsonElement[]> GetDownloadQueueAsync();
    Task ApplyColorizationAsync(string type, string hex);
    Task<JsonElement> ExtractComponentAsync(string modId, string component, CancellationToken ct);
    Task RollbackAsync(string operationId, CancellationToken ct);
    Task<JsonElement> VerifyRpfAsync(string path, CancellationToken ct);
    Task ApplySettingsXmlAsync(JsonElement parameters);

    Task<HntCodeDto> HntCodeExportAsync(string userId);
    Task<HntCodeDto> HntCodeExportAsync(
        string userId,
        bool includeRedux,
        bool includeGunpack,
        bool includeSelectedGuns,
        bool includeComponents = true,
        IReadOnlyList<string>? gunFilter = null);
    Task<HntCodeDto> HntCodePreviewAsync(string code);
    Task<HntImportResultDto> HntCodeApplyAsync(HntPayloadDto payload);

    Task<List<HntCodeDto>> HntCodeListMyAsync(string userId);

    Task<HntCodeDto> HntCodeDeleteAsync(string code, string userId);

    Task<List<UserBuildDto>> UserBuildsListAsync(string? search, string? authorUserId);
    Task<UserBuildDto?>      UserBuildGetAsync(string id);
    Task<UserBuildDto?>      UserBuildGetByHntCodeAsync(string hntCode);
    Task<UserBuildDto>       UserBuildCreateAsync(UserBuildDto dto);
    Task                     UserBuildDeleteAsync(string id);
    Task<long>               UserBuildIncrementDownloadsAsync(string id);
    Task<long>               UserBuildIncrementViewsAsync(string id);

    Task<Dictionary<string, long>> DonorPickCountsAsync(string component);
    Task<long> DonorPickIncrementAsync(string donorReduxId, string component);

    Task<UserBuildDto> UserBuildSubmitAsync(UserBuildDto dto);

    Task<UserBuildDto> UserBuildUpdateAsync(string id, IReadOnlyDictionary<string, object?> patch);

    Task<string> UserBuildUploadSettingsXmlAsync(string buildId, string sourceXmlPath);

    Task<string> UserBuildUploadCoverAsync(string sourcePath);

#if ADMIN
    Task<string> AdminUploadComponentScreenshotAsync(string reduxId, string component, string sourcePath);
#endif

#if ADMIN
    Task<string> AdminMirrorImageToR2Async(string reduxId, string externalUrl, string slot);
#endif

#if ADMIN
    Task<string> AdminUploadLibraryPreviewAsync(string libraryId, string sourcePath);
#endif

    Task<CurrentMinimapInfoDto?> GetCurrentMinimapInfoAsync();

    Task<CustomizationDraftDto?> GetInstalledDraftAsync();

    Task<string> GetCurrentReduxIdAsync();

    Task<InjectResultDto> ReduxApplyMinimapAsync(string source, string id);
    Task<InjectResultDto> ReduxApplyMinimapAsync(string source, string id, string? displayName);

    Task<InjectResultDto> TimecycleInstallAsync(string donorReduxId, string? displayName, string? donorVersionId = null);

    Task<CurrentMinimapInfoDto?> GetCurrentTimecycleInfoAsync();

    Task<InjectResultDto> TimecycleRestoreVanillaAsync();

    Task<InjectResultDto> TreesInstallAsync(string treeId, string? displayName);

    Task<CurrentMinimapInfoDto?> GetCurrentTreesInfoAsync();

    Task<InjectResultDto> TreesRestoreAsync();

    Task<InjectResultDto> RoadsInstallAsync(string roadId, string? displayName);

    Task<CurrentMinimapInfoDto?> GetCurrentRoadsInfoAsync();

    Task<InjectResultDto> RoadsRestoreAsync();

    Task<RoadsFixStatusDto> GetRoadsFixStatusAsync();

    Task<InjectResultDto> RoadsFixApplyAsync();

    Task<InjectResultDto> GraphicsModRestoreAsync(string modId);

    Task<GraphicsModInfoDto[]> GetInstalledGraphicsModsAsync();

    Task<MinimapLayoutDto> MinimapLayoutGetAsync();
    Task<InjectResultDto> MinimapLayoutApplyAsync(string ratio, string placement, bool transparent);

    Task<InjectResultDto> MinimapLayoutApplyCustomAsync(string ratio, double posX, double posY, bool transparent);

    Task<List<MinimapLayoutPresetDto>> MinimapLayoutPresetsAsync();

    Task<int?> MinimapGetSafezoneAsync();

    Task<MinimapScreenDto> MinimapGetScreenAsync();

    Task<InjectResultDto> MinimapApplyTweaksAsync(MinimapTweaksDto tweaks);

    Task<MinimapTweaksDto?> MinimapGetTweaksAsync();

    Task<MinimapSaveDto?> MinimapGetSaveAsync();

    Task<MinimapSaveDto> MinimapWriteSaveAsync(string name, MinimapTweaksDto tweaks);

    Task MinimapClearSaveAsync();

    Task<InjectResultDto> MinimapInstallFontAsync(string gfxPath, string? slot);

    Task<InjectResultDto> MinimapRestoreFontAsync();

    Task<MinimapFontStateDto> MinimapGetFontStateAsync();

    Task<MinimapFontOptionDto[]> MinimapGetFontOptionsAsync();

    Task<string?> OtherGetArchiveFingerprintAsync();

    Task<HotSwapStatusDto> HotSwapGetStatusAsync();

    Task<InjectResultDto> HotSwapSetEnabledAsync(bool enabled, int? method = null);

    Task<InjectResultDto> HotSwapArmNowAsync();

    Task<InjectResultDto> HotSwapDisarmNowAsync();

    Task<InjectResultDto> HotSwapRebuildAsync();

    Task<HotSwapLogDto> HotSwapGetLogAsync(int tailKb = 64);

    Task<HotSwapLogDto> FeatureGetLogAsync(int tailKb = 64);

    Task<DownloadLogDto> DownloadGetLogAsync(int tailKb = 64);

    Task<string?> FileToDataUrlAsync(string path);

    Task<InjectResultDto> MinimapSetRangeRingsAsync(int[] radiiMeters);
    Task<int[]> MinimapGetRangeRingsAsync();
    Task<bool> MinimapDetectRingsAsync();
    Task<InjectResultDto> MinimapRestoreVanillaAsync();

    Task ReduxDeferArmorReapplyOnceAsync();

    Task ReduxDeferFastJoinReapplyOnceAsync();

    Task<InjectResultDto> OtherSetZalazyAsync(bool enabled, string server);
    Task<ZalazyStateDto> OtherGetZalazyAsync();

    Task<OverlayDetectDto> OtherDetectOverlaysAsync();
    Task<InjectResultDto> OtherRemoveForeignOverlayAsync(string kind);

    Task<InjectResultDto> OtherSetFastJoinAsync(bool enabled);
    Task<ReduxBundledFeaturesDto> ReduxBundledFeaturesAsync(string reduxId, string? versionId);

    Task<bool> OtherGetFastJoinAsync();
    Task<FastJoinStatusDto> OtherGetFastJoinStatusAsync();

    Task<InjectResultDto> OtherSetGreenZoneAsync(bool enabled);
    Task<bool> OtherGetGreenZoneAsync();

    Task<InjectResultDto> OtherSetCarLogosAsync(bool enabled);
    Task<CarLogosStatusDto> OtherGetCarLogosAsync();

    Task<InjectResultDto> OtherSetRukzakAsync(bool enabled);
    Task<bool> OtherGetRukzakAsync();

    Task<BackpackStatusDto> OtherGetBackpackStatusAsync();

    Task<InjectResultDto> OtherApplyBackpackAsync(string action);

    Task<InjectResultDto> OtherSetSmokeAsync(bool enabled);
    Task<bool> OtherGetSmokeAsync();

    Task<InjectResultDto> OtherSetNoTracerAsync(bool enabled, string[]? categories = null, bool keepSnipers = false);
    Task<NoTracerStateDto> OtherGetNoTracerAsync();

    Task<InjectResultDto> OtherSetTracerStudioAsync(string? settings);
    Task<TracerStudioStateDto> OtherGetTracerStudioAsync();

    Task<List<ImprovementDto>> ImprovementsListAsync();

    Task<InjectResultDto> ImprovementInstallAsync(string id);

    Task<InjectResultDto> ImprovementRemoveAsync(string id);

    Task<List<BigMapDto>> BigMapListAsync();
    Task<BigMapStateDto> BigMapGetStateAsync();
    Task<InjectResultDto> BigMapInstallAsync(string id);
    Task<InjectResultDto> BigMapUninstallAsync();
    Task<string?> BigMapPreviewGlbAsync(string id);

#if ADMIN
    Task<LibraryComponentDto> AdminCreateLibraryStubAsync(
        string type, string name, string author, string description, string photoPath);
#endif

#if ADMIN
    Task<LibraryComponentDto> AdminCreateLibraryMinimapAsync(
        string name, string author, string description, string gfxPath, string photoPath);
#endif

    Task<CurrentReticleInfoDto?> GetCurrentReticleInfoAsync();

    Task<InjectResultDto> ReduxApplyReticleAsync(string source, string id);
    Task<InjectResultDto> ReduxApplyReticleAsync(string source, string id, string? displayName);

    Task<InjectResultDto> ReduxResetCustomizationAsync(string part);

    Task<InjectResultDto> ReticleApplyCustomAsync(ReticleSpecDto spec);

    Task<string> KnkShareAsync(string userId, ReticleSpecDto spec);

    Task<ReticleSpecDto> KnkFetchAsync(string code);

    Task<LegitReportDto> LegitCheckReduxAsync(string reduxId, string? versionId = null);

    Task<LegitReportDto> LegitCheckUpdateRpfAsync(string? rpfPath = null);

    Task<string> LegitReportShareAsync(string userId, LegitReportDto report);

    Task<LegitReportDto> LegitReportFetchAsync(string code);

#if ADMIN
    Task<LibraryComponentDto> AdminCreateLibraryReticleAsync(
        string name, string author, string description, string gfxPath, string photoPath);
#endif

#if ADMIN
    Task<List<string>> AdminUploadLibraryGalleryAsync(string libraryId, IReadOnlyList<string> sourcePaths);
#endif

#if ADMIN
    Task<string> AdminUploadLibraryVideoAsync(string libraryId, string sourcePath);
#endif

    Task<CurrentSoundPackInfoDto?> GetCurrentSoundPackInfoAsync();

    Task<InjectResultDto> SoundPackInstallAsync(string libraryId);
    Task<InjectResultDto> SoundPackInstallAsync(string libraryId, string? displayName);

    Task<InjectResultDto> SoundPackUninstallAsync();

#if ADMIN
    Task<LibraryComponentDto> AdminCreateLibrarySoundsAsync(
        string name, string author, string description, string zipPath, string photoPath);
#endif
#if ADMIN
    Task<LibraryComponentDto> AdminCreateLibraryAwcSoundAsync(
        string name, string author, string description, string awcPath, string photoPath);
#endif

#if ADMIN
    Task<string> AdminUploadGunpackCoverAsync(string sourcePath);
#endif

    Task<List<UserBuildDto>> UserBuildListPendingAsync();

    Task<List<UserBuildDto>> UserBuildListMyPendingAsync(string authorUserId);

    Task<UserBuildDto> UserBuildApproveAsync(string id, string reviewerUserId, int? tier);

    Task<UserBuildDto> UserBuildRejectAsync(string id, string reviewerUserId, string reason);

    Task<UserBuildDto> UserBuildResubmitAsync(string id);

    Task<List<GtaPresetDto>> GtaPresetsListAsync(string? search);
    Task<GtaPresetDto?>      GtaPresetGetAsync(string id);

    Task<GtaPresetApplyResultDto> GtaPresetApplyAsync(string id);

    Task<GtaPresetApplyResultDto> GtaSettingsApplyFromUrlAsync(string xmlUrl);

    Task<long>                    GtaPresetIncrementDownloadsAsync(string id);

    Task<Dictionary<string, long>> GtaInstallCountsAsync(string eventType);

    Task<PresetReactionsDto>      GtaPresetReactionsGetAsync(string presetId, string userId);
    Task<PresetReactionsDto>      GtaPresetReactionSetAsync(string presetId, int reaction);

    Task<AccountStatsDto>         AccountStatsGetAsync();

#if ADMIN
    Task<List<GtaPresetDto>>      AdminGtaPresetListAsync();
#endif

#if ADMIN
    Task<GtaPresetDto>            AdminGtaPresetUploadAsync(GtaPresetUploadRequestDto request);
#endif

#if ADMIN
    Task<BigMapAnalysisDto>       AdminBigMapAnalyzeAsync(string sourcePath);
    Task<BigMapDto>               AdminBigMapPublishAsync(BigMapPublishRequestDto request);
    Task<List<BigMapDto>>         AdminBigMapListAsync();
    Task                          AdminBigMapDeleteAsync(string id);
#endif
#if ADMIN
    Task                          AdminGtaPresetPatchAsync(string id, GtaPresetPatchDto patch);
#endif
#if ADMIN
    Task                          AdminGtaPresetDeleteAsync(string id);
#endif

#if ADMIN
    Task<GtaSettingsAnalysisDto>  AdminGtaPresetAnalyzeAsync(string sourceXmlPath);
#endif

    Task<GtaSettingsReadResultDto>  GtaSettingsReadAsync();

    Task<GtaSettingsAnalysisDto>    GtaSettingsAnalyzeModelAsync(GtaSettingsModelDto model);

    Task<GtaPresetApplyResultDto>   GtaSettingsWriteAsync(GtaSettingsModelDto model);

    Task<OptimizationCatalogDto>    OptimizationCatalogGetAsync();
    Task<OptimizationResolutionDto> OptimizationStateGetAsync();
    Task<OptimizationApplyResultDto> OptimizationApplyAsync(IReadOnlyList<OptimizationSelectionDto> selections);
    Task<OptimizationResolutionDto> OptimizationResolveFromPresetAsync(string presetId);

    Task MirrorSetOverrideAsync(string? choice);

    Task<MirrorProbeResultDto> MirrorProbeAsync(string? choice);

    Task<ServerRegionStatusDto> ServerRegionGetAsync();
    Task ServerRegionSetAsync(string region);
    Task<ServerRegionPingDto> ServerRegionPingAsync();

    Task<DownloadSourceStatusDto> DownloadSourceGetAsync();

    Task DownloadSourceSetAsync(string source);

    Task<DownloadSourceEvalDto> DownloadSourceEvaluateEuAsync(string? zapretRootPath);

    Task<ZapretApplyResultDto> ZapretApplyWhitelistAsync(string zapretRootPath);

    Task<ZapretDetectDto> ZapretDetectAsync(string? zapretRootPath);

    Task<RendererEnsureResultDto> RendererEnsureInstalledAsync();

    Task<RendererProbeDto> RendererProbeAsync();

    Task<RendererTestRenderDto> RendererTestRenderAsync();

    Task<RendererEnsureResultDto> RendererForceReinstallAsync();

    Task<JreEnsureResultDto> JreEnsureInstalledAsync();

    Task<PcDiagReportDto> PcDiagReportAsync();

    Task<PcDiagApplyResultDto> PcDiagApplyAsync(string findingId);

    Task<PcDiagApplyResultDto> PcDiagRevertAsync(string findingId);

    Task<List<PcDiagJournalEntryDto>> PcDiagJournalAsync();

    Task<List<PcDiagTweakDto>> PcDiagTweaksAsync();

    Task<PcDiagAiResultDto> PcDiagAiAsync(string userId, string? question, List<PcDiagAiMsgDto>? history);
}
