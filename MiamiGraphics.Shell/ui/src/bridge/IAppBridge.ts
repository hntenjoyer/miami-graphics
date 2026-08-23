import type {
  SystemInfo, GtaPathInfo, AuthResult, AdminWebAuthResult, ServerStatus, AppUpdateInfo, AppUpdateInstallResult, AppSettings, BackupStatus, BackupResult, BackupProgress, BetaGate, PromoCheck, ModmakersList, ModmakerDetail, ModmakerFeed, ModmakerMap,
  AdminConfig, TestConnectionResult, ReduxAnalysis, ReduxItem, ReduxVersion, DuplicateHashMatch,
  FeaturedPick,
  QueueItem, InjectResult, GtaVersion, MinimapTweaks,
  GtaVersionAutoFill, LibraryComponent, LibraryUpload, LibraryPatch, CustomizationDraftBridge, ReduxReview, UserBuildReview, UserProfile,
  InstallHistoryEntry,
  Gunpack, GunpackGun, GunpackWhitelistEntry, GunpackPatch, GunpackGunPatch,
  GunpackVariant, GunpackVariantPatch,
  GunpackQueueItem, GunpackUploadRequest, GunpackBatchEntry,
  GunpackInstalledState, GunpackVerifyReport, GunpackInstallConflict,
  SelectedGun, SelectedGunsVerifyReport, SelectedGunsInstallProgress,
  HntCode, HntPayload, HntImportResult,
  GtaPreset, GtaPresetUploadRequest, GtaPresetPatch, GtaPresetApplyResult, GtaSettingsAnalysis, PresetReactions,
  GtaSettingsModel, GtaSettingsReadResult,
  OptimizationCatalog, OptimizationSelection, OptimizationApplyResult, OptimizationResolution,
  CurrentArmorInfo, CurrentMinimapInfo, CurrentReticleInfo, ReticleSpec, CurrentSoundPackInfo,
  DlcArmorInspectionResult,
  DlcArmorImportRequest,
  DlcArmorImportResult,
  ArmorLibraryItem,
  UserBuildDto,
  NoTracerCategory, NoTracerState, TracerStudioState,
  BigMap, BigMapState, BigMapAnalysis, BigMapPublishRequest, BigMapReview,
  CustomGun, CustomGunLimits, CustomGunPatch, CustomGunSort,
  CustomSkinApplied,
  WorkshopSession, WorkshopOpenRequest, WorkshopPublishMeta,
  WorkshopFlowLimits, UserGunpack,
  LegitReport, LegitCheckProgress, OptimizationScanProgress,
  OptimizationInteriorState,
  DataMoveProgress,
  Language,
} from './types';

export type BridgeEventName =
  | 'backup:progress'
  | 'admin:queueProgress'
  | 'redux:installProgress'
  | 'admin:gunpackQueueProgress'
  | 'admin:rendererBootstrap'
  | 'gunpack:installProgress'
  | 'selectedguns:installProgress'
  | 'download:queue'
  | 'download:slow'
  | 'download:diagnosis'
  | 'app:updateProgress'
  | 'app:criticalOpExitBlocked'
  | 'app:gtaRunning'
  | 'window:state'
  | 'legitcheck:progress'
  | 'optimization:scanProgress'
  | 'optimization:interiorState'
  | 'data:moveProgress';

export interface Ru2QueueStatus {
  active: boolean;
  position: number;
  etaSec: number;
}

export interface RendererBootstrapProgress {
  phase: 'downloading' | 'verifying' | 'extracting' | 'cleanup' | 'done' | 'error';
  percent: number;
  error?: string | null;
  downloadedMb?: number | null;
  alreadyInstalled?: boolean | null;
}

export type ReduxInstallPhase =
  | 'starting' | 'downloading' | 'verifying' | 'extracting' | 'injecting'
  | 'restoring_old_state' | 'computing_diff' | 'downloading_new'
  | 'installing_new' | 'applying_user_changes'
  | 'cancelled'
  | 'done' | 'error';

export interface ReduxInstallProgress {
  reduxId: string;
  name: string;
  phase: ReduxInstallPhase;
  percent: number;
  errorMessage: string | null;

  detailMessage: string | null;
}

export type GunpackInstallPhase =
  | 'starting' | 'resolving_version' | 'downloading_template' | 'downloading_pack'
  | 'preparing' | 'installing' | 'registering'
  | 'restoring'
  | 'done' | 'error';

export interface GunpackInstallProgress {
  gunpackId:      string;
  phase:          GunpackInstallPhase;
  percent:        number;
  errorMessage:   string | null;
  detailMessage:  string | null;
}

export interface AppUpdateProgress {
  active:  boolean;
  percent: number;
  detail:  string;
}

export interface BridgeEventMap {
  'backup:progress': BackupProgress;
  'admin:queueProgress': QueueItem;
  'redux:installProgress': ReduxInstallProgress;
  'admin:gunpackQueueProgress': GunpackQueueItem;
  'admin:rendererBootstrap':    RendererBootstrapProgress;
  'gunpack:installProgress':    GunpackInstallProgress;
  'selectedguns:installProgress': SelectedGunsInstallProgress;
  'download:queue':             Ru2QueueStatus;
  'download:slow':              { host: string; kbps: number };
  'download:diagnosis':         NetworkDoctorReport;
  'app:updateProgress':         AppUpdateProgress;
  'app:criticalOpExitBlocked':  null;
  'app:gtaRunning':             null;
  'window:state':               { maximized: boolean };
  'legitcheck:progress':        LegitCheckProgress;
  'optimization:scanProgress':  OptimizationScanProgress;
  'optimization:interiorState': OptimizationInteriorState;
  'data:moveProgress':          DataMoveProgress;
}

export interface BridgeEvents {
  on<K extends BridgeEventName>(name: K, cb: (data: BridgeEventMap[K]) => void): void;
  off<K extends BridgeEventName>(name: K, cb: (data: BridgeEventMap[K]) => void): void;
}

export interface IAppBridge {
  events: BridgeEvents;

  getSystemInfo(): Promise<SystemInfo>;

  getAppVersion(): Promise<string>;

  assetCacheContains(urls: string[]): Promise<boolean[]>;

  assetCachePrewarm(urls: string[]): Promise<number>;
  gunpackAllGunPreviewUrls(): Promise<string[]>;
  authenticateGuest(): Promise<AuthResult>;
  authenticateUser(login: string, password: string, totp: string | null): Promise<AuthResult>;
  adminWebAuthenticate(): Promise<AdminWebAuthResult>;

  registerRequest(email: string, username: string, password: string): Promise<void>;

  registerConfirm(email: string, code: string): Promise<AuthResult>;

  modmakersList(q?: string): Promise<ModmakersList>;
  modmakerDetail(code: string): Promise<ModmakerDetail>;
  modmakerFollow(code: string, on: boolean): Promise<{ ok: boolean; following?: boolean; followers?: number; error?: string }>;
  modmakerFeed(notify?: boolean): Promise<ModmakerFeed>;
  modmakerMap(): Promise<ModmakerMap>;
  modmakerCanEdit(code: string): Promise<{ ok: boolean; can_edit?: boolean; is_self?: boolean }>;

  installerPromo(): Promise<string>;
  checkPromo(code: string): Promise<PromoCheck>;
  attachReferral(code: string): Promise<boolean>;

  betaCodeCheck(code: string): Promise<BetaGate>;
  betaRedeem(code: string): Promise<BetaGate>;
  betaCheck(): Promise<BetaGate>;
  betaEnabled(): Promise<boolean>;
  activityLog(eventType: string, detail: string, itemId?: string): Promise<boolean>;

  getServerStatus(): Promise<ServerStatus>;
  forceExit(): Promise<void>;
  appUpdateCheck(): Promise<AppUpdateInfo>;
  appUpdateInstall(version: string): Promise<AppUpdateInstallResult>;

  requestPasswordReset(email: string): Promise<void>;
  consumePasswordReset(code: string, newPassword: string): Promise<void>;

  getUserProfile(userId: string): Promise<UserProfile | null>;
  updateUserProfile(userId: string, username: string, avatarUrl: string | null): Promise<UserProfile>;

  changePasswordRequest(userId: string, oldPassword: string, newPassword: string): Promise<void>;
  changePasswordConfirm(userId: string, code: string): Promise<void>;
  changeEmailRequest(userId: string, currentPassword: string, newEmail: string): Promise<void>;
  changeEmailConfirm(userId: string, code: string): Promise<UserProfile>;
  uploadAvatar(userId: string, localPath: string): Promise<string>;

  installHistoryList(userId: string): Promise<InstallHistoryEntry[]>;
  installRecord(
    userId:     string,
    reduxId:    string,
    name:       string,
    author:     string,
    previewUrl: string | null,
  ): Promise<InstallHistoryEntry>;
  getAppSettings(): Promise<AppSettings>;
  saveAppSettings(settings: AppSettings): Promise<void>;
  setUiLanguage(lang: Language): Promise<void>;
  windowMinimize(): Promise<void>;
  windowMaximize(): Promise<void>;
  windowClose(): Promise<void>;
  windowStartDrag(): Promise<void>;
  windowSetFullscreen(on: boolean): Promise<void>;
  openFolderDialog(): Promise<string | null>;
  openLogsFolder(): Promise<void>;
  validateGtaPath(path: string): Promise<boolean>;

  getGtaPathInfo(): Promise<GtaPathInfo>;
  setGtaPathOverride(path: string): Promise<boolean>;
  clearGtaPathOverride(): Promise<boolean>;

  cacheSettingsGet(): Promise<import('./types').CacheSettings>;
  cacheSettingsSet(enabled: boolean, rootOverride: string | null): Promise<import('./types').CacheSettings>;

  cacheLimitSet(limitBytes: number): Promise<import('./types').CacheSettings>;

  dataRootMove(targetDir: string): Promise<import('./types').DataMoveResult>;

  dataRootMoveCancel(): Promise<void>;

  cacheCleanupNow(): Promise<import('./types').CacheCleanupResult>;

  scanGunpackBatchFolder(parentPath: string): Promise<GunpackBatchEntry[]>;

  backupGetStatus(): Promise<BackupStatus>;
  backupRunFull(): Promise<BackupResult>;
  backupCancel(): Promise<boolean>;
  backupRestoreClean(): Promise<boolean>;
  backupRestoreSnapshot(): Promise<boolean>;
  killProcessesByPid(pids: number[]): Promise<number>;
  factoryResetAndRestart(): Promise<void>;
  launcherUninstall(): Promise<void>;

  openFileDialog(filterDescription?: string, filterPattern?: string): Promise<string | null>;

  openFileDialogMulti(filterDescription?: string, filterPattern?: string): Promise<string[]>;

  adminConfigGet(): Promise<AdminConfig>;
  adminConfigSave(config: AdminConfig): Promise<void>;
  adminConfigTestR2(config: AdminConfig): Promise<TestConnectionResult>;

  adminReduxAnalyze(sourcePath: string): Promise<ReduxAnalysis>;

  adminQueueList(): Promise<QueueItem[]>;
  adminQueueAdd(item: QueueItem): Promise<QueueItem>;
  adminQueueRemove(tempId: string): Promise<void>;
  adminQueueRun(): Promise<void>;
  adminQueueCancel(): Promise<void>;
  adminRebuildReduxComponents(): Promise<number>;
  adminRecalculateReduxPatchSizes(): Promise<number>;

  adminCatalogList(search?: string, server?: string, status?: string): Promise<ReduxItem[]>;
  adminCatalogUpdate(item: ReduxItem): Promise<void>;
  adminCatalogDelete(id: string): Promise<void>;

  adminWipeAll(category: string): Promise<{ deleted: number; failed: number }>;

  reduxVersions(reduxId: string): Promise<ReduxVersion[]>;
  adminFindByHash(sha256: string): Promise<DuplicateHashMatch | null>;
  adminVersionUpsert(version: ReduxVersion): Promise<void>;
  adminVersionDelete(id: string): Promise<void>;

  featuredPicksList(): Promise<FeaturedPick[]>;
  adminFeaturedPickSet(slotIndex: number, reduxId: string): Promise<void>;
  adminFeaturedPickDelete(slotIndex: number): Promise<void>;

  reduxReviewsList(reduxId: string): Promise<ReduxReview[]>;
  reduxReviewSubmit(
    reduxId:   string,
    userId:    string,
    username:  string,
    role:      string,
    avatarUrl: string | null,
    rating:    number,
    body:      string,
  ): Promise<ReduxReview>;
  reduxReviewDelete(
    reviewId: string,
    userId:   string,
    role:     string,
  ): Promise<boolean>;
  reduxRatingsAggregate(): Promise<Record<string, { avg: number; count: number }>>;

  userBuildReviewsList(buildId: string): Promise<UserBuildReview[]>;
  userBuildReviewSubmit(
    buildId:   string,
    userId:    string,
    username:  string,
    role:      string,
    avatarUrl: string | null,
    rating:    number,
    body:      string,
  ): Promise<UserBuildReview>;
  userBuildReviewDelete(
    reviewId: string,
    userId:   string,
    role:     string,
  ): Promise<boolean>;

  adminInject(moddedRpfPath: string): Promise<InjectResult>;
  adminInjectFromCatalog(reduxId: string): Promise<InjectResult>;
  adminRestoreCleanUpdate(): Promise<boolean>;

  reduxList(search?: string, server?: string): Promise<ReduxItem[]>;
  reduxFavoriteList(userId: string): Promise<string[]>;
  reduxFavoriteAdd(userId: string, reduxId: string): Promise<void>;
  reduxFavoriteRemove(userId: string, reduxId: string): Promise<void>;

  itemFavoritesList(userId: string, itemType: string): Promise<string[]>;
  itemFavoriteAdd(userId: string, itemType: string, itemId: string): Promise<void>;
  itemFavoriteRemove(userId: string, itemType: string, itemId: string): Promise<void>;
  reduxIncrementDownloads(reduxId: string): Promise<number>;
  reduxInstall(reduxId: string, versionId?: string | null): Promise<InjectResult>;
  reduxDeferArmorReapplyOnce(): Promise<void>;
  reduxDeferFastJoinReapplyOnce(): Promise<void>;

  reduxDeferMinimapReapplyOnce(): Promise<void>;
  reduxInstallForceClean(reduxId: string, versionId?: string | null): Promise<InjectResult>;
  reduxInstallPreserve(reduxId: string, versionId?: string | null): Promise<InjectResult>;
  reduxInstallCancel(): Promise<void>;
  installCancel(progressId: string): Promise<boolean>;
  reduxCustomizeApply(reduxId: string, draft: CustomizationDraftBridge): Promise<InjectResult>;

  armorInstallStandalone(
    reduxId: string,
    versionId?: string | null,
    force?: boolean,
    confirmWipe?: boolean,
  ): Promise<InjectResult>;

  inspectDlcRpfArmor(dlcRpfPath: string): Promise<DlcArmorInspectionResult>;

  inspectDlcRpfArmorCancel(): Promise<boolean>;

  readLocalFileBase64(absolutePath: string): Promise<string | null>;

  importDlcRpfArmor(request: DlcArmorImportRequest): Promise<DlcArmorImportResult>;

  armorLibraryList(): Promise<ArmorLibraryItem[]>;

  armorLibraryListAll(): Promise<ArmorLibraryItem[]>;

  armorLibrarySetVisibility(armorLibraryId: string, visible: boolean): Promise<boolean>;

  armorLibrarySetSupportedServers(armorLibraryId: string, servers: string[]): Promise<boolean>;

  armorLibraryDelete(armorLibraryId: string): Promise<boolean>;

  armorLibraryRenderVariants(armorLibraryId: string): Promise<string[]>;

  armorLibrarySetPreview(armorLibraryId: string, previewUrl: string): Promise<boolean>;

  reduxArmorRenderPreview(reduxId: string): Promise<string | null>;

  reduxArmorBackfillPreviews(): Promise<{ total: number; rendered: number }>;

  reduxArmorRenderVariants(reduxId: string): Promise<string[]>;

  reduxArmorVariantUrls(reduxId: string): Promise<string[]>;

  reduxArmorSetPreview(reduxId: string, previewUrl: string): Promise<boolean>;

  armorLibraryInstall(
    armorLibraryId: string,
    overlayMode?: boolean,
    force?: boolean,
    confirmWipe?: boolean,
  ): Promise<InjectResult>;

  reduxApplyArmorSwap(donorReduxId: string, donorVersionId?: string | null): Promise<InjectResult>;

  reduxClearArmor(): Promise<InjectResult>;

  getCurrentArmorInfo(): Promise<CurrentArmorInfo | null>;

  reduxUninstall(): Promise<InjectResult>;
  reduxUninstallForceClean(): Promise<InjectResult>;
  reduxUninstallPreserve(): Promise<InjectResult>;

  gtaVersionsList(): Promise<GtaVersion[]>;
  gtaVersionsUpsert(version: GtaVersion): Promise<void>;
  gtaVersionsDelete(exeVersion: string): Promise<void>;
  gtaVersionsAutoFill(cleanRpfPath: string): Promise<GtaVersionAutoFill>;
  gtaVersionsUpload(cleanRpfPath: string, exeVersion: string, notes: string): Promise<GtaVersion>;

  libraryList(type?: string): Promise<LibraryComponent[]>;
  libraryDelete(id: string): Promise<void>;
  libraryUploadComponent(payload: LibraryUpload): Promise<LibraryComponent>;

  libraryPatch(payload: LibraryPatch): Promise<LibraryComponent>;

  gunpackWhitelistList(): Promise<GunpackWhitelistEntry[]>;

  gunpacksList(search?: string, status?: string): Promise<Gunpack[]>;
  gunpackGet(id: string): Promise<Gunpack | null>;
  gunpackGuns(gunpackId: string): Promise<GunpackGun[]>;
  gunpackAllGuns?(): Promise<import('./types').GunpackFlatGun[]>;

  gunpackIncrementDownloads(id: string): Promise<number>;

  customGunsList(search?: string, sort?: CustomGunSort, viewerUserId?: string): Promise<CustomGun[]>;
  customGunsMine(ownerUserId: string): Promise<CustomGun[]>;
  customGunLimits(ownerUserId: string): Promise<CustomGunLimits>;
  customGunPatch(id: string, patch: CustomGunPatch): Promise<void>;
  customGunDelete(id: string): Promise<void>;
  customGunInstall(id: string): Promise<void>;

  customGunListPending(): Promise<CustomGun[]>;
  customGunApprove(id: string, reviewerUserId: string): Promise<CustomGun>;
  customGunReject(id: string, reviewerUserId: string, reason: string): Promise<CustomGun>;

  customGunAdminList(status?: string | null, search?: string | null): Promise<CustomGun[]>;
  customGunAdminPatch(id: string, patch: CustomGunPatch): Promise<CustomGun>;
  customGunAdminDelete(id: string, reason?: string | null, hard?: boolean): Promise<CustomGun>;
  workshopFlowLimits(): Promise<WorkshopFlowLimits>;
  userGunpacksList(): Promise<UserGunpack[]>;
  userGunpackInstall(id: string): Promise<void>;
  userGunpackDelete(id: string): Promise<void>;
  customGunPreviewDownload(url: string, name: string): Promise<string>;

  customSkinApplied(): Promise<CustomSkinApplied[]>;
  customSkinRemove(internalName: string): Promise<InjectResult>;

  workshopOpen(req: WorkshopOpenRequest): Promise<WorkshopSession>;
  workshopReplaceTexture(draftId: string, textureName: string, pngBase64: string): Promise<{ glbUrl: string | null }>;
  workshopSaveDraft(draftId: string): Promise<void>;
  workshopApplyToGame(draftId: string): Promise<void>;
  workshopPublish(draftId: string, meta: WorkshopPublishMeta, ownerUserId: string, ownerName: string): Promise<CustomGun>;

  adminGunpackList(): Promise<Gunpack[]>;
  adminGunpackPatch(id: string, patch: GunpackPatch): Promise<void>;
  adminGunpackDelete(id: string): Promise<void>;

  adminGunpackGunPatch(gunId: string, patch: GunpackGunPatch): Promise<void>;
  adminGunpackGunDelete(gunId: string): Promise<void>;

  gunpackVariantsList(gunpackId: string): Promise<GunpackVariant[]>;
  adminGunpackVariantPatch(variantId: string, patch: GunpackVariantPatch): Promise<void>;
  adminGunpackVariantDelete(variantId: string): Promise<void>;
  adminGunpackVariantSetDefault(variantId: string): Promise<void>;
  adminGunpackVariantUpload(packId: string, name: string, sourceRpfPath: string, coverImagePath?: string): Promise<GunpackQueueItem>;

  adminGunpackUpload(request: GunpackUploadRequest): Promise<GunpackQueueItem>;
  adminGunpackQueueList(): Promise<GunpackQueueItem[]>;
  adminGunpackQueueRemove(tempId: string): Promise<void>;

  gunpackInstallAll(gunpackId: string, perGunResolutions?: Record<string, string>, variantId?: string): Promise<InjectResult>;
  gunpackCheckInstallConflicts(gunpackId: string): Promise<GunpackInstallConflict[]>;
  gunpackInstallSelected(gunpackId: string, gunIds: string[]): Promise<InjectResult>;
  gunpackUninstall(): Promise<boolean>;

  gunpackGetInstalledState(): Promise<GunpackInstalledState>;
  gunpackVerifyInstalled(): Promise<GunpackVerifyReport>;
  reconcileInstallState(): Promise<boolean>;

  selectedGunsList(): Promise<SelectedGun[]>;
  selectedGunsIsInstalled(internalName: string): Promise<boolean>;
  selectedGunsInstall(gunpackId: string, internalName: string): Promise<InjectResult>;
  selectedGunsRemove(internalName: string): Promise<InjectResult>;
  selectedGunsRebuild(): Promise<InjectResult>;
  selectedGunsUninstallAll(): Promise<InjectResult>;
  selectedGunsVerify(): Promise<SelectedGunsVerifyReport>;

  installMod(modId: string, type: string, payload: unknown): Promise<unknown>;
  uninstallMod(modId: string): Promise<unknown>;
  compareRpf(path: string): Promise<unknown>;
  getDownloadQueue(): Promise<unknown[]>;
  applyColorization(type: string, hex: string): Promise<void>;
  extractComponent(modId: string, component: string): Promise<unknown>;
  rollback(operationId: string): Promise<void>;
  verifyRpf(path: string): Promise<unknown>;
  applySettingsXml(parameters: unknown): Promise<void>;

  hntCodeExport(userId: string, flags?: {
    includeRedux?:        boolean;
    includeGunpack?:      boolean;
    includeSelectedGuns?: boolean;
    includeComponents?:   boolean;
    gunFilter?:           string[];
  }): Promise<HntCode>;
  hntCodePreview(code: string): Promise<HntCode>;
  hntCodeApply(payload: HntPayload): Promise<HntImportResult>;
  hntCodeListMy(userId: string): Promise<HntCode[]>;
  hntCodeDelete(code: string, userId: string): Promise<HntCode>;

  userBuildsList(search?: string | null, authorUserId?: string | null): Promise<UserBuildDto[]>;
  userBuildGet(id: string): Promise<UserBuildDto | null>;
  userBuildGetByHntCode(hntCode: string): Promise<UserBuildDto | null>;
  userBuildCreate(dto: UserBuildDto): Promise<UserBuildDto>;
  userBuildDelete(id: string): Promise<void>;
  userBuildIncrementDownloads(id: string): Promise<number>;
  userBuildIncrementViews(id: string): Promise<number>;

  donorPickCounts(component: string): Promise<Record<string, number>>;
  donorPickIncrement(donorReduxId: string, component: string): Promise<number>;

  userBuildSubmit(dto: UserBuildDto): Promise<UserBuildDto>;

  userBuildUpdate(id: string, patch: Partial<UserBuildDto>): Promise<UserBuildDto>;

  userBuildUploadSettingsXml(buildId: string, sourceXmlPath: string): Promise<string>;

  userBuildUploadCover(sourcePath: string): Promise<string>;

  adminUploadComponentScreenshot(
    reduxId: string,
    component: string,
    sourcePath: string,
  ): Promise<string>;

  adminMirrorImageToR2(
    reduxId: string,
    externalUrl: string,
    slot: string,
  ): Promise<string>;

  adminUploadLibraryPreview(libraryId: string, sourcePath: string): Promise<string>;

  getCurrentMinimapInfo(): Promise<CurrentMinimapInfo | null>;

  getInstalledDraft(): Promise<CustomizationDraftBridge | null>;

  getCurrentReduxId(): Promise<string>;

  reduxApplyMinimap(source: 'redux' | 'library', id: string, displayName?: string): Promise<InjectResult>;
  timecycleInstall(donorReduxId: string, displayName?: string, donorVersionId?: string | null): Promise<InjectResult>;
  getCurrentTimecycleInfo(): Promise<CurrentMinimapInfo | null>;
  timecycleRestoreVanilla(): Promise<InjectResult>;
  treesInstall(treeId: string, displayName?: string): Promise<InjectResult>;
  getCurrentTreesInfo(): Promise<CurrentMinimapInfo | null>;
  treesRestore(): Promise<InjectResult>;
  roadsInstall(roadId: string, displayName?: string): Promise<InjectResult>;
  getCurrentRoadsInfo(): Promise<CurrentMinimapInfo | null>;
  roadsRestore(): Promise<InjectResult>;
  getRoadsFixStatus(): Promise<import('./types').RoadsFixStatus>;
  roadsFixApply(): Promise<InjectResult>;
  graphicsModRestore(modId: string): Promise<InjectResult>;
  getInstalledGraphicsMods(): Promise<import('./types').GraphicsModInfo[]>;
  minimapLayoutGet(): Promise<{ ratio: string; placement: string; transparent: boolean; posX?: number | null; posY?: number | null }>;
  minimapLayoutApply(ratio: string, placement: string, transparent: boolean): Promise<InjectResult>;
  minimapLayoutApplyCustom(ratio: string, posX: number, posY: number, transparent: boolean): Promise<InjectResult>;
  minimapLayoutGetPresets(): Promise<import('./types').MinimapLayoutPreset[]>;
  minimapGetSafezone(): Promise<number | null>;
  minimapGetScreen(): Promise<import('./types').MinimapScreen>;
  minimapApplyTweaks(tweaks: MinimapTweaks): Promise<InjectResult>;
  minimapGetTweaks(): Promise<MinimapTweaks | null>;

  minimapGetSave?(): Promise<import('./types').MinimapSave | null>;
  minimapWriteSave?(name: string, tweaks: MinimapTweaks): Promise<import('./types').MinimapSave>;
  minimapClearSave?(): Promise<void>;

  minimapInstallFont(path: string, slot?: string | null): Promise<InjectResult>;
  minimapRestoreFont(): Promise<InjectResult>;
  minimapGetFontState(): Promise<import('./types').MinimapFontState>;
  minimapGetFontOptions(): Promise<import('./types').MinimapFontOption[]>;

  otherGetArchiveFingerprint?(): Promise<string | null>;

  hotSwapGetStatus?(): Promise<import('./types').HotSwapStatus>;
  hotSwapSetEnabled?(enabled: boolean, method?: number): Promise<InjectResult>;
  hotSwapArmNow?(): Promise<InjectResult>;
  hotSwapDisarmNow?(): Promise<InjectResult>;
  hotSwapRebuild?(): Promise<InjectResult>;
  hotSwapGetLog?(tailKb?: number): Promise<import('./types').HotSwapLogTail>;

  featureGetLog?(tailKb?: number): Promise<import('./types').HotSwapLogTail>;
  downloadGetLog?(tailKb?: number): Promise<import('./types').DownloadLogTail>;
  fileToDataUrl(path: string): Promise<string | null>;
  minimapSetRangeRings(radiiMeters: number[]): Promise<InjectResult>;
  minimapGetRangeRings(): Promise<number[]>;
  minimapDetectRings(): Promise<boolean>;
  minimapRestoreVanilla(): Promise<InjectResult>;
  otherSetZalazy(enabled: boolean, server: 'gta5rp' | 'majestic'): Promise<InjectResult>;
  otherGetZalazy(): Promise<{ enabled: boolean; server: 'gta5rp' | 'majestic' }>;
  otherDetectOverlays(): Promise<{ foreignZalazy: boolean; foreignGreenZone: boolean; foreignBackpack?: boolean }>;
  otherRemoveForeignOverlay(kind: 'zalazy' | 'greenzone' | 'backpack'): Promise<InjectResult>;
  otherSetFastJoin(enabled: boolean): Promise<InjectResult>;
  reduxBundledFeatures?(reduxId: string, versionId?: string): Promise<{
    fastJoin: boolean; greenZone: boolean; zalazy: boolean; customMinimap: boolean;
  }>;
  otherGetFastJoin(): Promise<boolean>;
  otherGetFastJoinStatus?(): Promise<{ active: boolean; userInstalled: boolean }>;
  otherSetGreenZone(enabled: boolean): Promise<InjectResult>;
  otherGetGreenZone(): Promise<boolean>;
  otherSetCarLogos(enabled: boolean): Promise<InjectResult>;
  otherGetCarLogos(): Promise<import('./types').CarLogosStatus>;
  otherSetRukzak(enabled: boolean): Promise<InjectResult>;
  otherGetRukzak(): Promise<boolean>;
  otherGetBackpackStatus(): Promise<import('./types').BackpackStatus>;
  otherApplyBackpack(action: 'remove' | 'vanilla'): Promise<import('./types').InjectResult>;
  otherSetSmoke(enabled: boolean): Promise<InjectResult>;
  otherGetSmoke(): Promise<boolean>;
  otherSetNoTracer(enabled: boolean, categories?: NoTracerCategory[], keepSnipers?: boolean): Promise<InjectResult>;
  otherGetNoTracer(): Promise<NoTracerState>;
  otherSetTracerStudio(settings?: string): Promise<InjectResult>;
  otherGetTracerStudio(): Promise<TracerStudioState>;

  improvementsList(): Promise<import('./types').Improvement[]>;
  improvementInstall(id: string): Promise<InjectResult>;
  improvementRemove(id: string): Promise<InjectResult>;

  bigMapList(): Promise<BigMap[]>;
  bigMapGetState(): Promise<BigMapState>;
  bigMapInstall(id: string): Promise<InjectResult>;
  bigMapUninstall(): Promise<InjectResult>;
  bigMapPreviewGlb(id: string): Promise<string | null>;

  bigMapReviewsList(mapId: string): Promise<BigMapReview[]>;
  bigMapReviewSubmit(
    mapId:     string,
    userId:    string,
    username:  string,
    role:      string,
    avatarUrl: string | null,
    rating:    number,
    body:      string,
  ): Promise<BigMapReview>;
  bigMapReviewDelete(
    reviewId: string,
    userId:   string,
    role:     string,
  ): Promise<boolean>;
  bigMapRatingsAggregate(): Promise<Record<string, { avg: number; count: number }>>;

  adminBigMapAnalyze(sourcePath: string): Promise<BigMapAnalysis>;
  adminBigMapPublish(req: BigMapPublishRequest): Promise<BigMap>;
  adminBigMapList(): Promise<BigMap[]>;
  adminBigMapDelete(id: string): Promise<void>;

  adminCreateLibraryStub(
    type: string, name: string, author: string, description: string, photoPath: string,
  ): Promise<LibraryComponent>;

  adminCreateLibraryMinimap(
    name: string, author: string, description: string, gfxPath: string, photoPath: string,
  ): Promise<LibraryComponent>;

  getCurrentReticleInfo(): Promise<CurrentReticleInfo | null>;

  reduxApplyReticle(source: 'redux' | 'library', id: string, displayName?: string): Promise<InjectResult>;

  reduxResetCustomization(part: 'crosshair' | 'minimap' | 'all'): Promise<InjectResult>;

  reticleApplyCustom(spec: ReticleSpec): Promise<InjectResult>;

  knkShare(userId: string, spec: ReticleSpec): Promise<string>;

  knkFetch(code: string): Promise<ReticleSpec>;

  legitCheckRedux(reduxId: string, versionId?: string | null): Promise<LegitReport>;

  legitCheckUpdateRpf(rpfPath?: string | null): Promise<LegitReport>;

  legitReportShare(userId: string, report: LegitReport): Promise<string>;

  legitReportFetch(code: string): Promise<LegitReport>;

  adminCreateLibraryReticle(
    name: string, author: string, description: string, gfxPath: string, photoPath: string,
  ): Promise<LibraryComponent>;

  adminUploadLibraryGallery(libraryId: string, sourcePaths: string[]): Promise<string[]>;

  adminUploadLibraryVideo(libraryId: string, sourcePath: string): Promise<string>;

  getCurrentSoundPackInfo(): Promise<CurrentSoundPackInfo | null>;

  soundPackInstall(libraryId: string, displayName?: string): Promise<InjectResult>;

  soundPackUninstall(): Promise<InjectResult>;

  adminCreateLibrarySounds(
    name: string, author: string, description: string, zipPath: string, photoPath: string,
  ): Promise<LibraryComponent>;

  adminCreateLibraryAwc(
    name: string, author: string, description: string, awcPath: string, photoPath: string,
  ): Promise<LibraryComponent>;

  adminUploadGunpackCover(sourcePath: string): Promise<string>;

  userBuildListPending(): Promise<UserBuildDto[]>;

  userBuildListMyPending(authorUserId: string): Promise<UserBuildDto[]>;

  userBuildApprove(id: string, reviewerUserId: string, tier: number | null): Promise<UserBuildDto>;

  userBuildReject(id: string, reviewerUserId: string, reason: string): Promise<UserBuildDto>;

  userBuildResubmit(id: string): Promise<UserBuildDto>;

  gtaPresetsList(search: string | null): Promise<GtaPreset[]>;
  gtaPresetGet(id: string): Promise<GtaPreset | null>;
  gtaPresetApply(id: string): Promise<GtaPresetApplyResult>;
  gtaSettingsApplyFromUrl(xmlUrl: string): Promise<GtaPresetApplyResult>;
  gtaPresetIncrementDownloads(id: string): Promise<number>;

  gtaInstallCounts(eventType: string): Promise<Record<string, number>>;
  gtaPresetReactionsGet(presetId: string, userId: string): Promise<PresetReactions>;
  gtaPresetReactionSet(presetId: string, reaction: number): Promise<PresetReactions>;

  accountStats(): Promise<import('./types').AccountStats>;

  adminGtaPresetList(): Promise<GtaPreset[]>;
  adminGtaPresetUpload(request: GtaPresetUploadRequest): Promise<GtaPreset>;
  adminGtaPresetPatch(id: string, patch: GtaPresetPatch): Promise<void>;
  adminGtaPresetDelete(id: string): Promise<void>;
  adminGtaPresetAnalyze(sourceXmlPath: string): Promise<GtaSettingsAnalysis>;

  gtaSettingsRead(): Promise<GtaSettingsReadResult>;
  gtaSettingsAnalyzeModel(model: GtaSettingsModel): Promise<GtaSettingsAnalysis>;
  gtaSettingsWrite(model: GtaSettingsModel): Promise<GtaPresetApplyResult>;

  optimizationCatalogGet(): Promise<OptimizationCatalog>;
  optimizationStateGet(): Promise<OptimizationResolution>;
  optimizationApply(selections: OptimizationSelection[]): Promise<OptimizationApplyResult>;
  optimizationResolveFromPreset(presetId: string): Promise<OptimizationResolution>;

  mirrorSetOverride(choice: string | null): Promise<void>;
  mirrorProbe(choice: string | null): Promise<MirrorProbeResult>;
  zapretApplyWhitelist(path: string): Promise<ZapretApplyResult>;
  zapretDetect(path: string | null): Promise<ZapretDetectResult>;
  rendererEnsureInstalled(): Promise<RendererEnsureResult>;
  rendererProbe(): Promise<RendererProbe>;
  rendererTestRender(): Promise<RendererTestRender>;
  rendererForceReinstall(): Promise<RendererEnsureResult>;
  jreEnsureInstalled(): Promise<JreEnsureResult>;

  pcDiagReport(): Promise<PcDiagReport>;

  pcDiagApply(id: string): Promise<PcDiagApplyResult>;
  pcDiagRevert(id: string): Promise<PcDiagApplyResult>;
  pcDiagJournal(): Promise<PcDiagJournalEntry[]>;
  pcDiagTweaks(): Promise<PcDiagTweak[]>;
  pcDiagAi(userId: string, question?: string | null, history?: PcDiagAiMsg[]): Promise<PcDiagAiResult>;

  bypassTestRun(strategyId: number): Promise<BypassTestResult>;

  networkDoctorRun(url?: string | null): Promise<NetworkDoctorReport>;

  serverRegionGet(): Promise<ServerRegionStatus>;
  serverRegionSet(region: 'eu' | 'ru'): Promise<void>;
  serverRegionPing(): Promise<ServerRegionPing>;

  downloadSourceGet(): Promise<DownloadSourceStatus>;
  downloadSourceSet(source: 'eu' | 'ru2'): Promise<void>;
  downloadSourceEvaluateEu(zapretRootPath?: string | null): Promise<DownloadSourceEval>;
}

export interface DownloadSourceStatus {
  source: string;
  queueEnabled: boolean;
}

export interface DownloadSourceEval {
  euWorks: boolean;
  mbps: number | null;
  zapretConfigured: boolean;
  zapretRestarted: boolean;
  message: string | null;
}

export interface ServerRegionStatus {
  region: string;
  url: string;
  isConfigured: boolean;
}

export interface ServerRegionPing {
  euMs: number | null;
  ruMs: number | null;
}

export interface MirrorProbeResult {
  success: boolean;
  mirror: string;
  speedMbPerSecond: number | null;
  errorMessage: string | null;
}

export interface RendererEnsureResult {
  success:           boolean;
  alreadyInstalled:  boolean;
  rendererPath:      string;
  downloadedBytes:   number;
  errorMessage:      string | null;
}

export interface RendererProbe {
  rendererPath:       string;
  baseDirExists:      boolean;
  nodeExeExists:      boolean;
  renderJsExists:     boolean;
  nodeModulesExists:  boolean;
  nodeModulesSizeMb:  number;
  nodeVersion:        string | null;
  nodeError:          string | null;
  isUsable:           boolean;
  summary:            string | null;
  actionableHint:     string | null;
  chromiumInstalled?: boolean;
}

export interface RendererTestRender {
  success:       boolean;
  elapsedMs:     number;
  outputBytes:   number | null;
  outputPath:    string | null;
  stdoutTail:    string | null;
  stderrTail:    string | null;
  errorMessage:  string | null;
}

export interface JreEnsureResult {
  success:           boolean;
  alreadyInstalled:  boolean;
  jrePath:           string;
  downloadedBytes:   number;
  errorMessage:      string | null;
}

export interface ZapretApplyResult {
  success: boolean;
  errorMessage: string | null;
  domainLinesAdded: number;
  ipsetLinesAdded: number;
  listsDir: string | null;
}

export interface ZapretDetectResult {
  installed: boolean;
  configuredForUs: boolean;
  detectedRoot: string | null;
}

export interface BypassTestResult {
  strategyId:     number;
  strategyLabel:  string;
  targetUrl:      string;
  success:        boolean;
  connectMs:      number;
  firstByteMs:    number;
  totalMs:        number;
  bytesReceived:  number;
  kbps:           number;
  httpStatusCode: number;
  errorMessage:   string | null;
}

export interface NetworkDoctorNode {
  id:               string;
  label:            string;
  host:             string;
  role:             string;
  ok:               boolean;
  httpStatus:       number;
  ip:               string | null;
  dnsMs:            number;
  connectMs:        number;
  ttfbMs:           number;
  totalMs:          number;
  rangeOk:          boolean;
  bytes:            number;
  kbPerSec:         number;
  streamsAccepted:  number;
  streamsRefused:   number;
  coldHeadTtfbMs:   number;
  coldMidTtfbMs:    number;
  coldOk:           boolean;
  error:            string | null;
}

export interface NetworkDoctorHub {
  ok:        boolean;
  nodeGiven: string | null;
  urlGiven:  string | null;
  ms:        number;
  status:    string | null;
  error:     string | null;
}

export interface NetworkDoctorReport {
  startedAtUtc: string;
  totalMs:      number;
  nodes:        NetworkDoctorNode[];
  hub:          NetworkDoctorHub | null;
  env:          Record<string, string>;
  problems:     string[];
  verdict:      string;
  bestHost:     string | null;
  coldProbeUrl: string | null;
}

export interface PcDiagFinding {
  id: string;
  severity: 'Info' | 'Minor' | 'Major' | 'Critical';
  category: 'Hardware' | 'Windows' | 'Apps' | 'Driver' | 'Game';
  data: Record<string, string>;
  gainMinPercent: number | null;
  gainMaxPercent: number | null;
  autoFixable: boolean;
}

export interface PcDiagRamStick { slot: string; capacityGb: number; ratedMt: number; configuredMt: number; memType: string }
export interface PcDiagDisk { model: string; media: string; bus: string; sizeGb: number }
export interface PcDiagGpu { name: string; vramGb: number; driverVersion: string; driverDate: string; isIntegrated: boolean }
export interface PcDiagBg { name: string; count: number; gb: number }

export interface PcDiagApplyResult {
  ok: boolean;
  message: string;
  requiresRestart: boolean;
}

export interface PcDiagJournalEntry {
  id: string;
  appliedAtUtc: string;
  reverted: boolean;
}

export interface PcDiagAiResult {
  ok: boolean;
  text: string;
  error: string;
}

export interface PcDiagAiMsg {
  role: 'user' | 'model';
  text: string;
}

export interface PcDiagTweak {
  id: string;
  grade: 'works' | 'micro' | 'experiment' | 'device' | 'maintenance';
  requiresRestart: boolean;
  inAllSafe: boolean;
  state: 'Ready' | 'Done' | 'NotApplicable';
  data: Record<string, string>;
}

export interface PcDiagMonitor {
  name: string;
  deviceName: string;
  adapter: string;
  width: number;
  height: number;
  currentHz: number;
  maxHz: number;
  isPrimary: boolean;
}

export interface PcDiagReport {
  cpuName: string;
  cpuCores: number;
  cpuThreads: number;
  cpuL3Mb: number;
  cpuTier: string;
  cpuFamily: string;
  cpuHybrid: boolean;
  cpuX3D: boolean;
  cpuLaptop: boolean;
  ramTotalGb: number;
  ramSlotsTotal: number;
  ramTier: string;
  ramTierNote: string;
  diskTier: string;
  diskTierNote: string;
  ramSticks: PcDiagRamStick[];
  disks: PcDiagDisk[];
  gpus: PcDiagGpu[];
  powerScheme: string;
  powerKind: string;
  vbsRunning: boolean;
  gameDvrOn: boolean;
  hasBattery: boolean;
  osCaption: string;
  displayWidth: number;
  displayHeight: number;
  displayCurrentHz: number;
  displayMaxHz: number;
  monitors: PcDiagMonitor[];
  netWired: boolean;
  netWireless: boolean;
  netVpn: boolean;
  gtaPath: string;
  gtaDiskMedia: string;
  background: PcDiagBg[];
  sensorErrors: string[];
  findings: PcDiagFinding[];
  elapsedMs: number;
}
