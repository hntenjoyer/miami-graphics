import type {
  IAppBridge, BridgeEvents, BridgeEventName, BridgeEventMap,
} from './IAppBridge';
import type {
  SystemInfo, AuthResult, AdminWebAuthResult, ServerStatus, AppUpdateInfo, AppUpdateInstallResult, AppSettings, BackupStatus, BackupResult,
  AdminConfig, TestConnectionResult, ReduxAnalysis, ReduxItem, ReduxVersion, DuplicateHashMatch,
  FeaturedPick,
  QueueItem, InjectResult, GtaVersion,
  GtaVersionAutoFill, LibraryComponent, LibraryUpload, LibraryPatch, CustomizationDraftBridge, ReduxReview, UserBuildReview, UserProfile,
  InstallHistoryEntry,
  Gunpack, GunpackGun, GunpackWhitelistEntry, GunpackPatch, GunpackGunPatch,
  GunpackVariant, GunpackVariantPatch,
  GunpackQueueItem, GunpackUploadRequest,
  GunpackInstalledState, GunpackVerifyReport, GunpackInstallConflict,
  SelectedGun, SelectedGunsVerifyReport,
  HntCode, HntPayload, HntImportResult,
  GtaPreset, GtaPresetUploadRequest, GtaPresetPatch, GtaPresetApplyResult, GtaSettingsAnalysis,
  GtaSettingsModel, GtaSettingsReadResult,
  OptimizationCatalog, OptimizationSelection, OptimizationApplyResult, OptimizationResolution,
  CurrentArmorInfo,
  DlcArmorInspectionResult,
  DlcArmorImportRequest,
  DlcArmorImportResult,
  ArmorLibraryItem,
  UserBuildDto,
  CustomGun, CustomGunLimits, CustomGunPatch, CustomGunSort,
  WorkshopSession, WorkshopOpenRequest, WorkshopPublishMeta,
  WorkshopFlowLimits, UserGunpack,
  Language,
} from './types';

interface BridgeResponse {
  id: string;
  ok: boolean;
  data: unknown;
  error: string | null;
}

interface BridgeEventEnvelope {
  kind: 'event';
  name: string;
  data: unknown;
}

interface Pending {
  resolve: (value: unknown) => void;
  reject: (reason: unknown) => void;
  timer: number;
}

const TIMEOUT_MS = 30_000;

class EventBus implements BridgeEvents {
  private listeners = new Map<BridgeEventName, Set<(data: unknown) => void>>();

  on<K extends BridgeEventName>(name: K, cb: (data: BridgeEventMap[K]) => void) {
    let set = this.listeners.get(name);
    if (!set) { set = new Set(); this.listeners.set(name, set); }
    set.add(cb as (data: unknown) => void);
  }

  off<K extends BridgeEventName>(name: K, cb: (data: BridgeEventMap[K]) => void) {
    this.listeners.get(name)?.delete(cb as (data: unknown) => void);
  }

  emit(name: string, data: unknown) {
    const set = this.listeners.get(name as BridgeEventName);
    if (!set) return;
    for (const cb of set) cb(data);
  }
}

export class WebViewBridge implements IAppBridge {
  private pending = new Map<string, Pending>();
  private bus = new EventBus();
  events: BridgeEvents = this.bus;

  constructor() {
    const wv = window.chrome?.webview;
    if (!wv) throw new Error('chrome.webview is not available');
    wv.addEventListener('message', (e: MessageEvent) => this.onMessage(e));
  }

  private onMessage(e: MessageEvent) {
    const payload = e.data as BridgeResponse | BridgeEventEnvelope;
    if (!payload) return;
    if ((payload as BridgeEventEnvelope).kind === 'event') {
      const env = payload as BridgeEventEnvelope;
      this.bus.emit(env.name, env.data);
      return;
    }
    const resp = payload as BridgeResponse;
    if (typeof resp.id !== 'string') return;
    const entry = this.pending.get(resp.id);
    if (!entry) return;
    this.pending.delete(resp.id);
    window.clearTimeout(entry.timer);
    if (resp.ok) entry.resolve(resp.data);
    else entry.reject(new Error(resp.error ?? 'Unknown bridge error'));
  }

  private send<T>(command: string, payload?: unknown, customTimeoutMs?: number): Promise<T> {
    const id = crypto.randomUUID();
    const ttl = customTimeoutMs ?? TIMEOUT_MS;
    return new Promise<T>((resolve, reject) => {
      const timer = window.setTimeout(() => {
        if (this.pending.delete(id)) {
          reject(new Error(`Bridge command "${command}" timed out after ${ttl}ms`));
        }
      }, ttl);
      this.pending.set(id, { resolve: resolve as (v: unknown) => void, reject, timer });
      const message = JSON.stringify({ id, command, payload: payload ?? null });
      window.chrome!.webview!.postMessage(message);
    });
  }

  getSystemInfo() { return this.send<SystemInfo>('getSystemInfo'); }
  getAppVersion() { return this.send<string>('getAppVersion', null, 5_000); }
  assetCacheContains(urls: string[]) { return this.send<boolean[]>('assetCacheContains', { urls }); }
  assetCachePrewarm(urls: string[]) {
    return this.send<number>('assetCachePrewarm', { urls }, 10 * 60 * 1000);
  }
  gunpackAllGunPreviewUrls() { return this.send<string[]>('gunpackAllGunPreviewUrls'); }
  authenticateGuest() { return this.send<AuthResult>('authenticateGuest'); }
  authenticateUser(login: string, password: string, totp: string | null) {
    return this.send<AuthResult>('authenticateUser', { login, password, totp });
  }
  adminWebAuthenticate() {
    return this.send<AdminWebAuthResult>('adminWebAuthenticate', null, 6 * 60 * 1000);
  }
  async registerRequest(email: string, username: string, password: string) {
    await this.send<null>('registerRequest', { email, username, password });
  }
  registerConfirm(email: string, code: string) {
    return this.send<AuthResult>('registerConfirm', { email, code });
  }
  modmakersList(q?: string)   { return this.send<import('./types').ModmakersList>('modmakersList', { q: q ?? null }); }
  modmakerDetail(code: string){ return this.send<import('./types').ModmakerDetail>('modmakerDetail', { code }); }
  modmakerFollow(code: string, on: boolean) { return this.send<{ ok: boolean; following?: boolean; followers?: number; error?: string }>('modmakerFollow', { code, on }); }
  modmakerFeed(notify?: boolean) { return this.send<import('./types').ModmakerFeed>('modmakerFeed', { notify: !!notify }); }
  modmakerMap()               { return this.send<import('./types').ModmakerMap>('modmakerMap', null); }
  modmakerCanEdit(code: string) { return this.send<{ ok: boolean; can_edit?: boolean; is_self?: boolean }>('modmakerCanEdit', { code }); }
  installerPromo()            { return this.send<string>('installerPromo', null); }
  checkPromo(code: string)    { return this.send<import('./types').PromoCheck>('checkPromo', { code }); }
  attachReferral(code: string){ return this.send<boolean>('attachReferral', { code }); }
  betaCodeCheck(code: string) { return this.send<import('./types').BetaGate>('betaCodeCheck', { code }); }
  betaRedeem(code: string)    { return this.send<import('./types').BetaGate>('betaRedeem', { code }); }
  betaCheck()                 { return this.send<import('./types').BetaGate>('betaCheck', null); }
  betaEnabled()               { return this.send<boolean>('betaEnabled', null); }
  activityLog(eventType: string, detail: string, itemId?: string) { return this.send<boolean>('activityLog', { eventType, detail, itemId }); }
  getServerStatus() { return this.send<ServerStatus>('getServerStatus', null, 30_000); }
  forceExit() { return this.send<void>('forceExit', null, 5_000); }
  appUpdateCheck() { return this.send<AppUpdateInfo>('appUpdateCheck', null, 30_000); }
  appUpdateInstall(version: string) {
    return this.send<AppUpdateInstallResult>('appUpdateInstall', { version }, 12 * 60 * 1000);
  }
  async requestPasswordReset(email: string) {
    await this.send<null>('requestPasswordReset', { email });
  }
  async consumePasswordReset(code: string, newPassword: string) {
    await this.send<null>('consumePasswordReset', { code, newPassword });
  }
  getUserProfile(userId: string) {
    return this.send<UserProfile | null>('getUserProfile', { userId });
  }
  updateUserProfile(userId: string, username: string, avatarUrl: string | null) {
    return this.send<UserProfile>('updateUserProfile', { userId, username, avatarUrl });
  }
  async changePasswordRequest(userId: string, oldPassword: string, newPassword: string) {
    await this.send<null>('changePasswordRequest', { userId, oldPassword, newPassword });
  }
  async changePasswordConfirm(userId: string, code: string) {
    await this.send<null>('changePasswordConfirm', { userId, code });
  }
  async changeEmailRequest(userId: string, currentPassword: string, newEmail: string) {
    await this.send<null>('changeEmailRequest', { userId, currentPassword, newEmail });
  }
  changeEmailConfirm(userId: string, code: string) {
    return this.send<UserProfile>('changeEmailConfirm', { userId, code });
  }

  uploadAvatar(userId: string, localPath: string) {
    return this.send<string>('uploadAvatar', { userId, localPath }, 90_000);
  }
  installHistoryList(userId: string) {
    return this.send<InstallHistoryEntry[]>('installHistoryList', { userId });
  }
  installRecord(userId: string, reduxId: string, name: string, author: string, previewUrl: string | null) {
    return this.send<InstallHistoryEntry>('installRecord', { userId, reduxId, name, author, previewUrl });
  }
  getAppSettings() { return this.send<AppSettings>('getAppSettings'); }
  async saveAppSettings(settings: AppSettings) { await this.send<null>('saveAppSettings', settings); }
  async setUiLanguage(lang: Language) { await this.send<null>('setUiLanguage', { lang }, 10_000); }

  async windowMinimize() { await this.send<null>('windowMinimize'); }
  async windowMaximize() { await this.send<null>('windowMaximize'); }
  async windowClose() { await this.send<null>('windowClose'); }
  async windowSetFullscreen(on: boolean) { await this.send<null>('windowSetFullscreen', { on }); }
  async windowStartDrag() { await this.send<null>('windowStartDrag'); }
  openFolderDialog() { return this.send<string | null>('openFolderDialog'); }
  async openLogsFolder() { await this.send<null>('openLogsFolder', null, 10_000); }
  validateGtaPath(path: string) { return this.send<boolean>('validateGtaPath', { path }); }
  getGtaPathInfo() { return this.send<import('./types').GtaPathInfo>('getGtaPathInfo'); }
  setGtaPathOverride(path: string) { return this.send<boolean>('setGtaPathOverride', { path }); }
  clearGtaPathOverride() { return this.send<boolean>('clearGtaPathOverride'); }
  cacheSettingsGet() { return this.send<import('./types').CacheSettings>('cacheSettingsGet', null, 30_000); }
  cacheSettingsSet(enabled: boolean, rootOverride: string | null) {
    return this.send<import('./types').CacheSettings>('cacheSettingsSet', { enabled, rootOverride }, 30_000);
  }
  cacheLimitSet(limitBytes: number) {
    return this.send<import('./types').CacheSettings>('cacheLimitSet', { limitBytes }, 180_000);
  }
  cacheCleanupNow() {
    return this.send<import('./types').CacheCleanupResult>('cacheCleanupNow', null, 300_000);
  }
  dataRootMove(targetDir: string) {
    return this.send<import('./types').DataMoveResult>('dataRootMove', { targetDir }, 3 * 60 * 60_000);
  }
  dataRootMoveCancel() {
    return this.send<void>('dataRootMoveCancel', null, 30_000);
  }
  scanGunpackBatchFolder(parentPath: string) {
    return this.send<import('./types').GunpackBatchEntry[]>(
      'scanGunpackBatchFolder', { parentPath }, 60_000,
    );
  }

  backupGetStatus() { return this.send<BackupStatus>('backupGetStatus'); }

  backupRunFull() { return this.send<BackupResult>('backupRunFull', null, 45 * 60 * 1000); }
  backupCancel() { return this.send<boolean>('backupCancel', null, 10_000); }
  backupRestoreClean() { return this.send<boolean>('backupRestoreClean', null, 45 * 60 * 1000); }
  backupRestoreSnapshot() { return this.send<boolean>('backupRestoreSnapshot', null, 45 * 60 * 1000); }

  killProcessesByPid(pids: number[]) { return this.send<number>('killProcessesByPid', { pids }, 60_000); }

  async factoryResetAndRestart() { await this.send<null>('factoryResetAndRestart', null, 10_000); }

  async launcherUninstall() { await this.send<null>('launcherUninstall', null, 10_000); }

  openFileDialog(filterDescription?: string, filterPattern?: string) {
    return this.send<string | null>('openFileDialog', { filterDescription: filterDescription ?? null, filterPattern: filterPattern ?? null });
  }

  async openFileDialogMulti(filterDescription?: string, filterPattern?: string) {
    const r = await this.send<string[] | null>('openFileDialogMulti', { filterDescription: filterDescription ?? null, filterPattern: filterPattern ?? null });
    return r ?? [];
  }

  adminConfigGet() { return this.send<AdminConfig>('adminConfigGet'); }
  async adminConfigSave(config: AdminConfig) { await this.send<null>('adminConfigSave', config); }
  adminConfigTestR2(config: AdminConfig) { return this.send<TestConnectionResult>('adminConfigTestR2', config, 60_000); }

  adminReduxAnalyze(sourcePath: string) { return this.send<ReduxAnalysis>('adminReduxAnalyze', { sourcePath }, 5 * 60 * 1000); }

  adminQueueList() { return this.send<QueueItem[]>('adminQueueList'); }
  adminQueueAdd(item: QueueItem) { return this.send<QueueItem>('adminQueueAdd', item); }
  async adminQueueRemove(tempId: string) { await this.send<null>('adminQueueRemove', { tempId }); }
  async adminQueueRun() { await this.send<null>('adminQueueRun'); }
  async adminQueueCancel() { await this.send<null>('adminQueueCancel'); }
  adminRebuildReduxComponents() {

    return this.send<number>('adminRebuildReduxComponents', null, 60_000);
  }
  adminRecalculateReduxPatchSizes() {
    return this.send<number>('adminRecalculateReduxPatchSizes', null, 120_000);
  }

  adminCatalogList(search?: string, server?: string, status?: string) {
    return this.send<ReduxItem[]>('adminCatalogList', { search: search ?? null, server: server ?? null, status: status ?? null });
  }
  async adminCatalogUpdate(item: ReduxItem) { await this.send<null>('adminCatalogUpdate', item); }
  async adminCatalogDelete(id: string) { await this.send<null>('adminCatalogDelete', { id }); }
  adminWipeAll(category: string) {

    return this.send<{ deleted: number; failed: number }>('adminWipeAll', { category }, 10 * 60 * 1000);
  }

  reduxVersions(reduxId: string) { return this.send<ReduxVersion[]>('reduxVersions', { reduxId }); }
  adminFindByHash(sha256: string) { return this.send<DuplicateHashMatch | null>('adminFindByHash', { sha256 }); }
  async adminVersionUpsert(version: ReduxVersion) { await this.send<null>('adminVersionUpsert', version); }
  async adminVersionDelete(id: string) { await this.send<null>('adminVersionDelete', { id }); }

  featuredPicksList() { return this.send<FeaturedPick[]>('featuredPicksList'); }
  async adminFeaturedPickSet(slotIndex: number, reduxId: string) {
    await this.send<null>('adminFeaturedPickSet', { slotIndex, reduxId });
  }
  async adminFeaturedPickDelete(slotIndex: number) {
    await this.send<null>('adminFeaturedPickDelete', { slotIndex });
  }

  reduxReviewsList(reduxId: string) {
    return this.send<ReduxReview[]>('reduxReviewsList', { reduxId });
  }
  reduxReviewSubmit(
    reduxId: string, userId: string, username: string, role: string,
    avatarUrl: string | null, rating: number, body: string,
  ) {
    return this.send<ReduxReview>('reduxReviewSubmit', {
      reduxId, userId, username, role, avatarUrl, rating, body,
    });
  }
  reduxReviewDelete(reviewId: string, userId: string, role: string) {
    return this.send<boolean>('reduxReviewDelete', { reviewId, userId, role });
  }
  userBuildReviewsList(buildId: string) {
    return this.send<UserBuildReview[]>('userBuildReviewsList', { buildId });
  }
  userBuildReviewSubmit(
    buildId: string, userId: string, username: string, role: string,
    avatarUrl: string | null, rating: number, body: string,
  ) {
    return this.send<UserBuildReview>('userBuildReviewSubmit', {
      buildId, userId, username, role, avatarUrl, rating, body,
    });
  }
  userBuildReviewDelete(reviewId: string, userId: string, role: string) {
    return this.send<boolean>('userBuildReviewDelete', { reviewId, userId, role });
  }
  reduxRatingsAggregate() {
    return this.send<Record<string, { avg: number; count: number }>>('reduxRatingsAggregate', null);
  }

  adminInject(moddedRpfPath: string) {
    return this.send<InjectResult>('adminInject', { moddedRpfPath }, 15 * 60 * 1000);
  }

  adminInjectFromCatalog(reduxId: string) {
    return this.send<InjectResult>('adminInjectFromCatalog', { reduxId }, 15 * 60 * 1000);
  }

  async adminRestoreCleanUpdate() {
    return this.send<boolean>('adminRestoreCleanUpdate', null, 5 * 60 * 1000);
  }

  reduxList(search?: string, server?: string) {
    return this.send<ReduxItem[]>('reduxList', { search: search ?? null, server: server ?? null });
  }
  reduxFavoriteList(userId: string) { return this.send<string[]>('reduxFavoriteList', { userId }); }
  async reduxFavoriteAdd(userId: string, reduxId: string) { await this.send<null>('reduxFavoriteAdd', { userId, reduxId }); }
  async reduxFavoriteRemove(userId: string, reduxId: string) { await this.send<null>('reduxFavoriteRemove', { userId, reduxId }); }
  itemFavoritesList(userId: string, itemType: string) { return this.send<string[]>('itemFavoritesList', { userId, itemType }); }
  async itemFavoriteAdd(userId: string, itemType: string, itemId: string) { await this.send<null>('itemFavoriteAdd', { userId, itemType, itemId }); }
  async itemFavoriteRemove(userId: string, itemType: string, itemId: string) { await this.send<null>('itemFavoriteRemove', { userId, itemType, itemId }); }
  reduxIncrementDownloads(reduxId: string) { return this.send<number>('reduxIncrementDownloads', { reduxId }); }
  reduxInstall(reduxId: string, versionId?: string | null) {
    return this.send<InjectResult>('reduxInstall', { reduxId, versionId: versionId ?? null }, 15 * 60 * 1000);
  }
  reduxDeferArmorReapplyOnce() {
    return this.send<void>('reduxDeferArmorReapplyOnce');
  }
  reduxDeferMinimapReapplyOnce() {
    return this.send<void>('reduxDeferMinimapReapplyOnce');
  }

  reduxDeferFastJoinReapplyOnce() {
    return this.send<void>('reduxDeferFastJoinReapplyOnce');
  }
  reduxInstallForceClean(reduxId: string, versionId?: string | null) {
    return this.send<InjectResult>('reduxInstallForceClean', { reduxId, versionId: versionId ?? null }, 15 * 60 * 1000);
  }

  reduxInstallPreserve(reduxId: string, versionId?: string | null) {
    return this.send<InjectResult>('reduxInstallPreserve', { reduxId, versionId: versionId ?? null }, 30 * 60 * 1000);
  }

  async reduxInstallCancel() {
    await this.send<null>('reduxInstallCancel', null, 5_000);
  }
  installCancel(progressId: string) {
    return this.send<boolean>('installCancel', { progressId }, 5_000);
  }

  reduxCustomizeApply(reduxId: string, draft: CustomizationDraftBridge) {
    return this.send<InjectResult>('reduxCustomizeApply', { reduxId, draft }, 30 * 60 * 1000);
  }

  armorInstallStandalone(reduxId: string, versionId?: string | null, force: boolean = false, confirmWipe: boolean = false) {
    return this.send<InjectResult>(
      'armorInstallStandalone',
      { reduxId, versionId: versionId ?? null, force, confirmWipe },
      15 * 60 * 1000,
    );
  }

  inspectDlcRpfArmor(dlcRpfPath: string) {
    return this.send<DlcArmorInspectionResult>(
      'inspectDlcRpfArmor',
      { dlcRpfPath },
      15 * 60 * 1000,
    );
  }

  inspectDlcRpfArmorCancel() {
    return this.send<boolean>('inspectDlcRpfArmorCancel', null, 10 * 1000);
  }
  readLocalFileBase64(absolutePath: string) {
    return this.send<string | null>('readLocalFileBase64', { path: absolutePath }, 30 * 1000);
  }

  importDlcRpfArmor(request: DlcArmorImportRequest) {
    return this.send<DlcArmorImportResult>(
      'importDlcRpfArmor', request, 5 * 60 * 1000);
  }
  armorLibraryList() {
    return this.send<ArmorLibraryItem[]>('armorLibraryList', null, 10 * 1000);
  }
  armorLibraryListAll() {
    return this.send<ArmorLibraryItem[]>('armorLibraryListAll', null, 10 * 1000);
  }
  armorLibrarySetVisibility(armorLibraryId: string, visible: boolean) {
    return this.send<boolean>('armorLibrarySetVisibility',
      { armorLibraryId, visible }, 10 * 1000);
  }
  armorLibrarySetSupportedServers(armorLibraryId: string, servers: string[]) {
    return this.send<boolean>('armorLibrarySetSupportedServers',
      { armorLibraryId, servers }, 10 * 1000);
  }

  armorLibraryDelete(armorLibraryId: string) {
    return this.send<boolean>('armorLibraryDelete',
      { armorLibraryId }, 60 * 1000);
  }

  armorLibraryRenderVariants(armorLibraryId: string) {
    return this.send<string[]>('armorLibraryRenderVariants',
      { armorLibraryId }, 5 * 60 * 1000);
  }

  armorLibrarySetPreview(armorLibraryId: string, previewUrl: string) {
    return this.send<boolean>('armorLibrarySetPreview',
      { armorLibraryId, previewUrl }, 30 * 1000);
  }

  reduxArmorRenderPreview(reduxId: string) {
    return this.send<string | null>('reduxArmorRenderPreview', { reduxId }, 5 * 60 * 1000);
  }

  reduxArmorBackfillPreviews() {
    return this.send<{ total: number; rendered: number }>(
      'reduxArmorBackfillPreviews', null, 30 * 60 * 1000);
  }

  reduxArmorRenderVariants(reduxId: string) {
    return this.send<string[]>('reduxArmorRenderVariants', { reduxId }, 5 * 60 * 1000);
  }

  reduxArmorVariantUrls(reduxId: string) {
    return this.send<string[]>('reduxArmorVariantUrls', { reduxId }, 30 * 1000);
  }

  reduxArmorSetPreview(reduxId: string, previewUrl: string) {
    return this.send<boolean>('reduxArmorSetPreview', { reduxId, previewUrl }, 30 * 1000);
  }

  armorLibraryInstall(armorLibraryId: string, overlayMode: boolean = false, force: boolean = false, confirmWipe: boolean = false) {
    return this.send<InjectResult>(
      'armorLibraryInstall', { armorLibraryId, overlayMode, force, confirmWipe }, 15 * 60 * 1000);
  }

  reduxApplyArmorSwap(donorReduxId: string, donorVersionId?: string | null) {
    return this.send<InjectResult>(
      'reduxApplyArmorSwap',
      { donorReduxId, donorVersionId: donorVersionId ?? null },
      15 * 60 * 1000,
    );
  }

  reduxClearArmor() {
    return this.send<InjectResult>('reduxClearArmor', null, 15 * 60 * 1000);
  }

  getCurrentArmorInfo() {
    return this.send<CurrentArmorInfo | null>('getCurrentArmorInfo', null, 5 * 1000);
  }

  reduxUninstall() {
    return this.send<InjectResult>('reduxUninstall', undefined, 5 * 60 * 1000);
  }
  reduxUninstallForceClean() {
    return this.send<InjectResult>('reduxUninstallForceClean', undefined, 5 * 60 * 1000);
  }
  reduxUninstallPreserve() {
    return this.send<InjectResult>('reduxUninstallPreserve', undefined, 30 * 60 * 1000);
  }

  gtaVersionsList() { return this.send<GtaVersion[]>('gtaVersionsList'); }
  async gtaVersionsUpsert(version: GtaVersion) { await this.send<null>('gtaVersionsUpsert', version); }
  async gtaVersionsDelete(exeVersion: string) { await this.send<null>('gtaVersionsDelete', { exeVersion }); }

  gtaVersionsAutoFill(cleanRpfPath: string) {
    return this.send<GtaVersionAutoFill>('gtaVersionsAutoFill', { cleanRpfPath }, 2 * 60 * 1000);
  }

  gtaVersionsUpload(cleanRpfPath: string, exeVersion: string, notes: string) {
    return this.send<GtaVersion>('gtaVersionsUpload', { cleanRpfPath, exeVersion, notes }, 30 * 60 * 1000);
  }

  libraryList(type?: string) { return this.send<LibraryComponent[]>('libraryList', { type: type ?? null }); }
  async libraryDelete(id: string) { await this.send<null>('libraryDelete', { id }); }
  libraryUploadComponent(payload: LibraryUpload) {
    return this.send<LibraryComponent>('libraryUploadComponent', payload, 5 * 60 * 1000);
  }
  libraryPatch(payload: LibraryPatch) {
    return this.send<LibraryComponent>('libraryPatch', payload);
  }

  gunpackWhitelistList() { return this.send<GunpackWhitelistEntry[]>('gunpackWhitelistList'); }
  gunpacksList(search?: string, status?: string) {
    return this.send<Gunpack[]>('gunpacksList', { search: search ?? null, status: status ?? null });
  }
  gunpackGet(id: string) { return this.send<Gunpack | null>('gunpackGet', { id }); }
  gunpackGuns(gunpackId: string) { return this.send<GunpackGun[]>('gunpackGuns', { gunpackId }); }
  gunpackAllGuns() {
    return this.send<import('./types').GunpackFlatGun[]>('gunpackAllGuns', undefined, 60_000);
  }
  gunpackIncrementDownloads(id: string) { return this.send<number>('gunpackIncrementDownloads', { id }); }

  customGunsList(search?: string, sort?: CustomGunSort, viewerUserId?: string) {
    return this.send<CustomGun[]>('customGunsList', { search: search ?? null, sort: sort ?? null, viewerUserId: viewerUserId ?? null });
  }
  customGunsMine(ownerUserId: string) { return this.send<CustomGun[]>('customGunsMine', { ownerUserId }); }
  customGunLimits(ownerUserId: string) { return this.send<CustomGunLimits>('customGunLimits', { ownerUserId }); }
  async customGunPatch(id: string, patch: CustomGunPatch) { await this.send<null>('customGunPatch', { id, patch }); }
  async customGunDelete(id: string) { await this.send<null>('customGunDelete', { id }); }
  async customGunInstall(id: string) { await this.send<null>('customGunInstall', { id }); }
  customGunListPending() { return this.send<CustomGun[]>('customGunListPending', null); }
  customGunApprove(id: string, reviewerUserId: string) { return this.send<CustomGun>('customGunApprove', { id, reviewerUserId }); }
  customGunReject(id: string, reviewerUserId: string, reason: string) { return this.send<CustomGun>('customGunReject', { id, reviewerUserId, reason }); }
  customGunAdminList(status?: string | null, search?: string | null) {
    return this.send<CustomGun[]>('customGunAdminList', { status: status ?? null, search: search ?? null });
  }
  customGunAdminPatch(id: string, patch: CustomGunPatch) {
    return this.send<CustomGun>('customGunAdminPatch', {
      id,
      displayName: patch.displayName ?? null,
      description: patch.description ?? null,
      category:    patch.category ?? null,
    });
  }
  customGunAdminDelete(id: string, reason?: string | null, hard?: boolean) {
    return this.send<CustomGun>('customGunAdminDelete', { id, reason: reason ?? null, hard: !!hard });
  }
  async customSkinApplied() { return this.send<import('./types').CustomSkinApplied[]>('customSkinApplied', {}); }
  async customSkinRemove(internalName: string) { return this.send<import('./types').InjectResult>('customSkinRemove', { internalName }, 180000); }

  workshopFlowLimits() { return this.send<WorkshopFlowLimits>('workshopFlowLimits', {}); }
  userGunpacksList() { return this.send<UserGunpack[]>('userGunpacksList', {}); }
  async userGunpackInstall(id: string) { await this.send<null>('userGunpackInstall', { id }, 540_000); }
  async userGunpackDelete(id: string) { await this.send<null>('userGunpackDelete', { id }); }
  customGunPreviewDownload(url: string, name: string) {
    return this.send<string>('customGunPreviewDownload', { url, name }, 90_000);
  }

  workshopOpen(req: WorkshopOpenRequest) { return this.send<WorkshopSession>('workshopOpen', { req }, 60_000); }
  workshopReplaceTexture(draftId: string, textureName: string, pngBase64: string) {
    return this.send<{ glbUrl: string | null }>('workshopReplaceTexture', { draftId, textureName, pngBase64 }, 120_000);
  }
  async workshopSaveDraft(draftId: string) { await this.send<null>('workshopSaveDraft', { draftId }); }
  async workshopApplyToGame(draftId: string) { await this.send<null>('workshopApplyToGame', { draftId }, 180_000); }
  workshopPublish(draftId: string, meta: WorkshopPublishMeta, ownerUserId: string, ownerName: string) {
    return this.send<CustomGun>('workshopPublish', { draftId, meta, ownerUserId, ownerName });
  }

  adminGunpackList() { return this.send<Gunpack[]>('adminGunpackList'); }
  async adminGunpackPatch(id: string, patch: GunpackPatch) { await this.send<null>('adminGunpackPatch', { id, patch }); }
  async adminGunpackDelete(id: string) { await this.send<null>('adminGunpackDelete', { id }); }
  async adminGunpackGunPatch(gunId: string, patch: GunpackGunPatch) { await this.send<null>('adminGunpackGunPatch', { gunId, patch }); }
  async adminGunpackGunDelete(gunId: string) { await this.send<null>('adminGunpackGunDelete', { gunId }); }

  gunpackVariantsList(gunpackId: string) { return this.send<GunpackVariant[]>('gunpackVariantsList', { gunpackId }); }
  async adminGunpackVariantPatch(variantId: string, patch: GunpackVariantPatch) {
    await this.send<null>('adminGunpackVariantPatch', { variantId, patch });
  }
  async adminGunpackVariantDelete(variantId: string) {
    await this.send<null>('adminGunpackVariantDelete', { variantId });
  }
  async adminGunpackVariantSetDefault(variantId: string) {
    await this.send<null>('adminGunpackVariantSetDefault', { variantId });
  }
  adminGunpackVariantUpload(packId: string, name: string, sourceRpfPath: string, coverImagePath?: string) {
    return this.send<GunpackQueueItem>('adminGunpackVariantUpload', {
      packId, name, sourceRpfPath,
      coverImagePath: coverImagePath ?? null,
    }, 2 * 60 * 1000);
  }

  adminGunpackUpload(request: GunpackUploadRequest) {
    return this.send<GunpackQueueItem>('adminGunpackUpload', request, 2 * 60 * 1000);
  }
  adminGunpackQueueList() { return this.send<GunpackQueueItem[]>('adminGunpackQueueList'); }
  async adminGunpackQueueRemove(tempId: string) { await this.send<null>('adminGunpackQueueRemove', { tempId }); }

  gunpackInstallAll(gunpackId: string, perGunResolutions?: Record<string, string>, variantId?: string) {
    return this.send<InjectResult>('gunpackInstallAll', {
      gunpackId,
      perGunResolutions: perGunResolutions ?? {},
      variantId: variantId ?? null,
    }, 10 * 60 * 1000);
  }
  gunpackCheckInstallConflicts(gunpackId: string) {
    return this.send<GunpackInstallConflict[]>('gunpackCheckInstallConflicts', { gunpackId }, 30 * 1000);
  }
  gunpackInstallSelected(gunpackId: string, gunIds: string[]) {
    return this.send<InjectResult>('gunpackInstallSelected', { gunpackId, gunIds }, 10 * 60 * 1000);
  }
  gunpackUninstall() { return this.send<boolean>('gunpackUninstall'); }

  gunpackGetInstalledState() {
    return this.send<GunpackInstalledState>('gunpackGetInstalledState');
  }
  gunpackVerifyInstalled() {

    return this.send<GunpackVerifyReport>('gunpackVerifyInstalled', null, 60_000);
  }
  reconcileInstallState() {

    return this.send<boolean>('reconcileInstallState', null, 60_000);
  }

  selectedGunsList() {
    return this.send<SelectedGun[]>('selectedGunsList');
  }
  selectedGunsIsInstalled(internalName: string) {
    return this.send<boolean>('selectedGunsIsInstalled', { internalName });
  }

  selectedGunsInstall(gunpackId: string, internalName: string) {
    return this.send<InjectResult>('selectedGunsInstall', { gunpackId, internalName }, 10 * 60_000);
  }
  selectedGunsRemove(internalName: string) {
    return this.send<InjectResult>('selectedGunsRemove', { internalName }, 10 * 60_000);
  }
  selectedGunsRebuild() {
    return this.send<InjectResult>('selectedGunsRebuild', null, 10 * 60_000);
  }
  selectedGunsUninstallAll() {
    return this.send<InjectResult>('selectedGunsUninstallAll', null, 10 * 60_000);
  }
  selectedGunsVerify() {
    return this.send<SelectedGunsVerifyReport>('selectedGunsVerify', null, 60_000);
  }

  installMod(modId: string, type: string, payload: unknown) { return this.send<unknown>('installMod', { modId, type, payload }); }
  uninstallMod(modId: string) { return this.send<unknown>('uninstallMod', { modId }); }
  compareRpf(path: string) { return this.send<unknown>('compareRpf', { path }); }
  getDownloadQueue() { return this.send<unknown[]>('getDownloadQueue'); }
  async applyColorization(type: string, hex: string) { await this.send<null>('applyColorization', { type, hex }); }
  extractComponent(modId: string, component: string) { return this.send<unknown>('extractComponent', { modId, component }); }
  async rollback(operationId: string) { await this.send<null>('rollback', { operationId }); }
  verifyRpf(path: string) { return this.send<unknown>('verifyRpf', { path }); }
  async applySettingsXml(parameters: unknown) { await this.send<null>('applySettingsXml', parameters); }

  hntCodeExport(userId: string, flags?: {
    includeRedux?:        boolean;
    includeGunpack?:      boolean;
    includeSelectedGuns?: boolean;
    includeComponents?:   boolean;
    gunFilter?:           string[];
  }) {
    return this.send<HntCode>('hntCodeExport', {
      userId,
      includeRedux:        flags?.includeRedux        ?? true,
      includeGunpack:      flags?.includeGunpack      ?? true,
      includeSelectedGuns: flags?.includeSelectedGuns ?? true,
      includeComponents:   flags?.includeComponents   ?? true,
      gunFilter:           flags?.gunFilter           ?? null,
    }, 30 * 1000);
  }
  hntCodePreview(code: string) {
    return this.send<HntCode>('hntCodePreview', { code }, 30 * 1000);
  }
  hntCodeApply(payload: HntPayload) {
    return this.send<HntImportResult>('hntCodeApply', { payload }, 30 * 60 * 1000);
  }
  hntCodeListMy(userId: string) {
    return this.send<HntCode[]>('hntCodeListMy', { userId }, 30 * 1000);
  }
  hntCodeDelete(code: string, userId: string) {
    return this.send<HntCode>('hntCodeDelete', { code, userId }, 30 * 1000);
  }

  userBuildsList(search?: string | null, authorUserId?: string | null) {
    return this.send<UserBuildDto[]>('userBuildsList', {
      search: search ?? null,
      authorUserId: authorUserId ?? null,
    });
  }
  userBuildGet(id: string) {
    return this.send<UserBuildDto | null>('userBuildGet', { id });
  }
  userBuildGetByHntCode(hntCode: string) {
    return this.send<UserBuildDto | null>('userBuildGetByHntCode', { hntCode });
  }
  userBuildCreate(dto: UserBuildDto) {
    return this.send<UserBuildDto>('userBuildCreate', dto);
  }
  async userBuildDelete(id: string) {
    await this.send<null>('userBuildDelete', { id });
  }
  userBuildIncrementDownloads(id: string) {
    return this.send<number>('userBuildIncrementDownloads', { id });
  }
  userBuildIncrementViews(id: string) {
    return this.send<number>('userBuildIncrementViews', { id });
  }
  donorPickCounts(component: string) {
    return this.send<Record<string, number>>('donorPickCounts', { component });
  }
  donorPickIncrement(donorReduxId: string, component: string) {
    return this.send<number>('donorPickIncrement', { donorReduxId, component });
  }

  userBuildSubmit(dto: UserBuildDto) {
    return this.send<UserBuildDto>('userBuildSubmit', dto);
  }
  userBuildUpdate(id: string, patch: Partial<UserBuildDto>) {
    return this.send<UserBuildDto>('userBuildUpdate', { id, patch });
  }
  userBuildUploadSettingsXml(buildId: string, sourceXmlPath: string) {
    return this.send<string>('userBuildUploadSettingsXml',
      { buildId, sourceXmlPath }, 60 * 1000);
  }
  userBuildUploadCover(sourcePath: string) {
    return this.send<string>('userBuildUploadCover', { sourcePath }, 60 * 1000);
  }
  adminUploadComponentScreenshot(reduxId: string, component: string, sourcePath: string) {

    return this.send<string>('adminUploadComponentScreenshot',
      { reduxId, component, sourcePath }, 60 * 1000);
  }
  adminMirrorImageToR2(reduxId: string, externalUrl: string, slot: string) {
    return this.send<string>('adminMirrorImageToR2',
      { reduxId, externalUrl, slot }, 60 * 1000);
  }
  adminUploadLibraryPreview(libraryId: string, sourcePath: string) {
    return this.send<string>('adminUploadLibraryPreview',
      { libraryId, sourcePath }, 60 * 1000);
  }
  getCurrentMinimapInfo() {
    return this.send<import('./types').CurrentMinimapInfo | null>('getCurrentMinimapInfo', null);
  }
  getInstalledDraft() {
    return this.send<CustomizationDraftBridge | null>('getInstalledDraft', null);
  }
  getCurrentReduxId() {
    return this.send<string>('getCurrentReduxId', null);
  }
  reduxApplyMinimap(source: 'redux' | 'library', id: string, displayName?: string) {
    return this.send<import('./types').InjectResult>('reduxApplyMinimap', { source, id, displayName: displayName ?? null }, 30 * 60 * 1000);
  }
  timecycleInstall(donorReduxId: string, displayName?: string, donorVersionId?: string | null) {
    return this.send<import('./types').InjectResult>('timecycleInstall',
      { donorReduxId, displayName: displayName ?? null, donorVersionId: donorVersionId ?? null }, 30 * 60 * 1000);
  }
  getCurrentTimecycleInfo() {
    return this.send<import('./types').CurrentMinimapInfo | null>('getCurrentTimecycleInfo', null);
  }
  timecycleRestoreVanilla() {
    return this.send<import('./types').InjectResult>('timecycleRestoreVanilla', undefined, 30 * 60 * 1000);
  }
  treesInstall(treeId: string, displayName?: string) {
    return this.send<import('./types').InjectResult>('treesInstall',
      { treeId, displayName: displayName ?? null }, 30 * 60 * 1000);
  }
  getCurrentTreesInfo() {
    return this.send<import('./types').CurrentMinimapInfo | null>('getCurrentTreesInfo', null);
  }
  treesRestore() {
    return this.send<import('./types').InjectResult>('treesRestore', undefined, 30 * 60 * 1000);
  }
  roadsInstall(roadId: string, displayName?: string) {
    return this.send<import('./types').InjectResult>('roadsInstall',
      { roadId, displayName: displayName ?? null }, 30 * 60 * 1000);
  }
  getCurrentRoadsInfo() {
    return this.send<import('./types').CurrentMinimapInfo | null>('getCurrentRoadsInfo', null);
  }
  roadsRestore() {
    return this.send<import('./types').InjectResult>('roadsRestore', undefined, 30 * 60 * 1000);
  }
  getRoadsFixStatus() {
    return this.send<import('./types').RoadsFixStatus>('getRoadsFixStatus', null);
  }
  roadsFixApply() {
    return this.send<import('./types').InjectResult>('roadsFixApply', null, 60_000);
  }
  graphicsModRestore(modId: string) {
    return this.send<import('./types').InjectResult>('graphicsModRestore', { modId }, 30 * 60 * 1000);
  }
  getInstalledGraphicsMods() {
    return this.send<import('./types').GraphicsModInfo[]>('getInstalledGraphicsMods', null);
  }
  minimapLayoutGet() {
    return this.send<{ ratio: string; placement: string; transparent: boolean; posX?: number | null; posY?: number | null }>('minimapLayoutGet', undefined, 30_000);
  }
  minimapLayoutApply(ratio: string, placement: string, transparent: boolean) {
    return this.send<import('./types').InjectResult>('minimapLayoutApply', { ratio, placement, transparent }, 30 * 60 * 1000);
  }
  minimapApplyTweaks(tweaks: import('./types').MinimapTweaks) {
    return this.send<import('./types').InjectResult>('minimapApplyTweaks', { tweaks }, 30 * 60 * 1000);
  }
  minimapInstallFont(path: string, slot?: string | null) {
    return this.send<import('./types').InjectResult>('minimapInstallFont', { path, slot }, 30 * 60 * 1000);
  }
  minimapRestoreFont() {
    return this.send<import('./types').InjectResult>('minimapRestoreFont', undefined, 30 * 60 * 1000);
  }
  minimapGetFontState() {
    return this.send<import('./types').MinimapFontState>('minimapGetFontState', undefined, 30_000);
  }
  minimapGetFontOptions() {
    return this.send<import('./types').MinimapFontOption[]>('minimapGetFontOptions', undefined, 30_000);
  }
  otherGetArchiveFingerprint() {
    return this.send<string | null>('otherGetArchiveFingerprint', undefined, 15_000);
  }
  hotSwapGetStatus() {
    return this.send<import('./types').HotSwapStatus>('hotSwapGetStatus', undefined, 30_000);
  }
  hotSwapSetEnabled(enabled: boolean, method?: number) {
    return this.send<import('./types').InjectResult>('hotSwapSetEnabled', { enabled, method }, 30 * 60 * 1000);
  }
  hotSwapArmNow() {
    return this.send<import('./types').InjectResult>('hotSwapArmNow', undefined, 30 * 60 * 1000);
  }
  hotSwapDisarmNow() {
    return this.send<import('./types').InjectResult>('hotSwapDisarmNow', undefined, 30 * 60 * 1000);
  }
  hotSwapRebuild() {
    return this.send<import('./types').InjectResult>('hotSwapRebuild', undefined, 30 * 60 * 1000);
  }
  hotSwapGetLog(tailKb?: number) {
    return this.send<import('./types').HotSwapLogTail>('hotSwapGetLog', { tailKb }, 30_000);
  }
  featureGetLog(tailKb?: number) {
    return this.send<import('./types').HotSwapLogTail>('featureGetLog', { tailKb }, 30_000);
  }
  downloadGetLog(tailKb?: number) {
    return this.send<import('./types').DownloadLogTail>('downloadGetLog', { tailKb }, 30_000);
  }
  minimapGetTweaks() {
    return this.send<import('./types').MinimapTweaks | null>('minimapGetTweaks', undefined, 30_000);
  }
  minimapGetSave() {
    return this.send<import('./types').MinimapSave | null>('minimapGetSave', undefined, 30_000);
  }
  minimapWriteSave(name: string, tweaks: import('./types').MinimapTweaks) {
    return this.send<import('./types').MinimapSave>('minimapWriteSave', { name, tweaks }, 30_000);
  }
  minimapClearSave() {
    return this.send<void>('minimapClearSave', undefined, 30_000);
  }
  fileToDataUrl(path: string) {
    return this.send<string | null>('fileToDataUrl', { path }, 30_000);
  }
  minimapLayoutApplyCustom(ratio: string, posX: number, posY: number, transparent: boolean) {
    return this.send<import('./types').InjectResult>('minimapLayoutApplyCustom', { ratio, posX, posY, transparent }, 30 * 60 * 1000);
  }
  minimapLayoutGetPresets() {
    return this.send<import('./types').MinimapLayoutPreset[]>('minimapLayoutGetPresets', undefined, 60 * 1000);
  }
  minimapGetSafezone() {
    return this.send<number | null>('minimapGetSafezone');
  }
  minimapGetScreen() {
    return this.send<import('./types').MinimapScreen>('minimapGetScreen');
  }
  minimapSetRangeRings(radiiMeters: number[]) {
    return this.send<import('./types').InjectResult>('minimapSetRangeRings', { radiiMeters }, 30 * 60 * 1000);
  }
  minimapGetRangeRings() {
    return this.send<number[]>('minimapGetRangeRings');
  }
  minimapDetectRings() {
    return this.send<boolean>('minimapDetectRings', undefined, 60_000);
  }
  minimapRestoreVanilla() {
    return this.send<import('./types').InjectResult>('minimapRestoreVanilla', undefined, 30 * 60 * 1000);
  }
  otherSetZalazy(enabled: boolean, server: 'gta5rp' | 'majestic') {
    return this.send<import('./types').InjectResult>('otherSetZalazy', { enabled, server }, 30 * 60 * 1000);
  }
  otherGetZalazy() {
    return this.send<{ enabled: boolean; server: 'gta5rp' | 'majestic' }>('otherGetZalazy');
  }
  otherDetectOverlays() {
    return this.send<{ foreignZalazy: boolean; foreignGreenZone: boolean; foreignBackpack?: boolean }>('otherDetectOverlays');
  }
  otherRemoveForeignOverlay(kind: 'zalazy' | 'greenzone' | 'backpack') {
    return this.send<import('./types').InjectResult>('otherRemoveForeignOverlay', { kind }, 30 * 60 * 1000);
  }
  otherSetFastJoin(enabled: boolean) {
    return this.send<import('./types').InjectResult>('otherSetFastJoin', { enabled }, 30 * 60 * 1000);
  }
  otherGetFastJoin() {
    return this.send<boolean>('otherGetFastJoin');
  }
  otherGetFastJoinStatus() {
    return this.send<{ active: boolean; userInstalled: boolean }>('otherGetFastJoinStatus');
  }
  reduxBundledFeatures(reduxId: string, versionId?: string) {
    return this.send<{ fastJoin: boolean; greenZone: boolean; zalazy: boolean; customMinimap: boolean }>(
      'reduxBundledFeatures', { reduxId, versionId });
  }
  otherSetGreenZone(enabled: boolean) {
    return this.send<import('./types').InjectResult>('otherSetGreenZone', { enabled }, 30 * 60 * 1000);
  }
  otherSetCarLogos(enabled: boolean) {
    return this.send<import('./types').InjectResult>('otherSetCarLogos', { enabled }, 30 * 60 * 1000);
  }
  otherGetCarLogos() {
    return this.send<import('./types').CarLogosStatus>('otherGetCarLogos');
  }
  otherGetGreenZone() {
    return this.send<boolean>('otherGetGreenZone');
  }
  otherSetRukzak(enabled: boolean) {
    return this.send<import('./types').InjectResult>('otherSetRukzak', { enabled }, 30 * 60 * 1000);
  }
  otherGetRukzak() {
    return this.send<boolean>('otherGetRukzak');
  }
  otherGetBackpackStatus() {
    return this.send<import('./types').BackpackStatus>('otherGetBackpackStatus');
  }
  otherApplyBackpack(action: 'remove' | 'vanilla') {
    return this.send<import('./types').InjectResult>('otherApplyBackpack', { action }, 60 * 60 * 1000);
  }
  otherSetSmoke(enabled: boolean) {
    return this.send<import('./types').InjectResult>('otherSetSmoke', { enabled }, 30 * 60 * 1000);
  }
  otherGetSmoke() {
    return this.send<boolean>('otherGetSmoke');
  }
  otherSetNoTracer(enabled: boolean, categories?: import('./types').NoTracerCategory[], keepSnipers?: boolean) {
    return this.send<import('./types').InjectResult>('otherSetNoTracer', { enabled, categories, keepSnipers }, 30 * 60 * 1000);
  }
  improvementsList() {
    return this.send<import('./types').Improvement[]>('improvementsList');
  }
  improvementInstall(id: string) {
    return this.send<import('./types').InjectResult>('improvementInstall', { id }, 30 * 60 * 1000);
  }
  improvementRemove(id: string) {
    return this.send<import('./types').InjectResult>('improvementRemove', { id }, 30 * 60 * 1000);
  }
  otherGetNoTracer() {
    return this.send<import('./types').NoTracerState>('otherGetNoTracer');
  }
  otherSetTracerStudio(settings?: string) {
    return this.send<import('./types').InjectResult>('otherSetTracerStudio', { settings: settings ?? '' }, 30 * 60 * 1000);
  }
  otherGetTracerStudio() {
    return this.send<import('./types').TracerStudioState>('otherGetTracerStudio');
  }
  bigMapList() {
    return this.send<import('./types').BigMap[]>('bigMapList', undefined, 60_000);
  }
  bigMapGetState() {
    return this.send<import('./types').BigMapState>('bigMapGetState', undefined, 60_000);
  }
  bigMapInstall(id: string) {
    return this.send<import('./types').InjectResult>('bigMapInstall', { id }, 30 * 60 * 1000);
  }
  bigMapUninstall() {
    return this.send<import('./types').InjectResult>('bigMapUninstall', undefined, 30 * 60 * 1000);
  }
  bigMapPreviewGlb(id: string) {
    return this.send<string | null>('bigMapPreviewGlb', { id }, 30 * 60 * 1000);
  }
  bigMapReviewsList(mapId: string) {
    return this.send<import('./types').BigMapReview[]>('bigMapReviewsList', { mapId });
  }
  bigMapReviewSubmit(
    mapId: string, userId: string, username: string, role: string,
    avatarUrl: string | null, rating: number, body: string,
  ) {
    return this.send<import('./types').BigMapReview>('bigMapReviewSubmit', {
      mapId, userId, username, role, avatarUrl, rating, body,
    });
  }
  bigMapReviewDelete(reviewId: string, userId: string, role: string) {
    return this.send<boolean>('bigMapReviewDelete', { reviewId, userId, role });
  }
  bigMapRatingsAggregate() {
    return this.send<Record<string, { avg: number; count: number }>>('bigMapRatingsAggregate', null);
  }
  adminBigMapAnalyze(sourcePath: string) {
    return this.send<import('./types').BigMapAnalysis>('adminBigMapAnalyze', { sourcePath }, 10 * 60 * 1000);
  }
  adminBigMapPublish(req: import('./types').BigMapPublishRequest) {
    return this.send<import('./types').BigMap>('adminBigMapPublish', req, 30 * 60 * 1000);
  }
  adminBigMapList() {
    return this.send<import('./types').BigMap[]>('adminBigMapList', undefined, 60_000);
  }
  adminBigMapDelete(id: string) {
    return this.send<void>('adminBigMapDelete', { id }, 60_000);
  }
  adminCreateLibraryStub(type: string, name: string, author: string, description: string, photoPath: string) {
    return this.send<import('./types').LibraryComponent>('adminCreateLibraryStub',
      { type, name, author, description, photoPath }, 60 * 1000);
  }
  adminCreateLibraryMinimap(name: string, author: string, description: string, gfxPath: string, photoPath: string) {

    return this.send<import('./types').LibraryComponent>('adminCreateLibraryMinimap',
      { name, author, description, gfxPath, photoPath }, 90 * 1000);
  }
  getCurrentReticleInfo() {
    return this.send<import('./types').CurrentReticleInfo | null>('getCurrentReticleInfo', null);
  }
  reduxResetCustomization(part: 'crosshair' | 'minimap' | 'all') {
    return this.send<import('./types').InjectResult>('reduxResetCustomization', { part }, 30 * 60 * 1000);
  }
  reduxApplyReticle(source: 'redux' | 'library', id: string, displayName?: string) {
    return this.send<import('./types').InjectResult>('reduxApplyReticle', { source, id, displayName: displayName ?? null }, 30 * 60 * 1000);
  }
  reticleApplyCustom(spec: import('./types').ReticleSpec) {
    return this.send<import('./types').InjectResult>('reticleApplyCustom', { spec }, 30 * 60 * 1000);
  }
  knkShare(userId: string, spec: import('./types').ReticleSpec) {
    return this.send<string>('knkShare', { userId, spec }, 30 * 1000);
  }
  knkFetch(code: string) {
    return this.send<import('./types').ReticleSpec>('knkFetch', { code }, 30 * 1000);
  }
  legitCheckRedux(reduxId: string, versionId?: string | null) {
    return this.send<import('./types').LegitReport>('legitCheckRedux',
      { reduxId, versionId: versionId ?? null }, 30 * 60 * 1000);
  }
  legitCheckUpdateRpf(rpfPath?: string | null) {
    return this.send<import('./types').LegitReport>('legitCheckUpdateRpf',
      { rpfPath: rpfPath ?? null }, 30 * 60 * 1000);
  }
  legitReportShare(userId: string, report: import('./types').LegitReport) {
    return this.send<string>('legitReportShare', { userId, report }, 60 * 1000);
  }
  legitReportFetch(code: string) {
    return this.send<import('./types').LegitReport>('legitReportFetch', { code }, 30 * 1000);
  }
  adminCreateLibraryReticle(name: string, author: string, description: string, gfxPath: string, photoPath: string) {
    return this.send<import('./types').LibraryComponent>('adminCreateLibraryReticle',
      { name, author, description, gfxPath, photoPath }, 90 * 1000);
  }
  adminUploadLibraryGallery(libraryId: string, sourcePaths: string[]) {

    return this.send<string[]>('adminUploadLibraryGallery',
      { libraryId, sourcePaths }, Math.max(60_000, sourcePaths.length * 90_000));
  }
  adminUploadLibraryVideo(libraryId: string, sourcePath: string) {

    return this.send<string>('adminUploadLibraryVideo',
      { libraryId, sourcePath }, 5 * 60 * 1000);
  }
  getCurrentSoundPackInfo() {
    return this.send<import('./types').CurrentSoundPackInfo | null>('getCurrentSoundPackInfo', null);
  }
  soundPackInstall(libraryId: string, displayName?: string) {
    return this.send<import('./types').InjectResult>('soundPackInstall',
      { libraryId, displayName: displayName ?? null },
      30 * 60 * 1000);
  }
  soundPackUninstall() {
    return this.send<import('./types').InjectResult>('soundPackUninstall', null, 5 * 60 * 1000);
  }
  adminCreateLibrarySounds(name: string, author: string, description: string, zipPath: string, photoPath: string) {

    return this.send<import('./types').LibraryComponent>('adminCreateLibrarySounds',
      { name, author, description, zipPath, photoPath }, 30 * 60 * 1000);
  }
  adminCreateLibraryAwc(name: string, author: string, description: string, awcPath: string, photoPath: string) {
    return this.send<import('./types').LibraryComponent>('adminCreateLibraryAwc',
      { name, author, description, awcPath, photoPath }, 30 * 60 * 1000);
  }
  adminUploadGunpackCover(sourcePath: string) {
    return this.send<string>('adminUploadGunpackCover', { sourcePath }, 60 * 1000);
  }
  userBuildListPending() {
    return this.send<UserBuildDto[]>('userBuildListPending', null);
  }
  userBuildListMyPending(authorUserId: string) {
    return this.send<UserBuildDto[]>('userBuildListMyPending', { authorUserId });
  }
  userBuildApprove(id: string, reviewerUserId: string, tier: number | null) {
    return this.send<UserBuildDto>('userBuildApprove', { id, reviewerUserId, tier });
  }
  userBuildReject(id: string, reviewerUserId: string, reason: string) {
    return this.send<UserBuildDto>('userBuildReject', { id, reviewerUserId, reason });
  }
  userBuildResubmit(id: string) {
    return this.send<UserBuildDto>('userBuildResubmit', { id });
  }

  gtaPresetsList(search: string | null) {
    return this.send<GtaPreset[]>('gtaPresetsList', { search });
  }
  gtaPresetGet(id: string) {
    return this.send<GtaPreset | null>('gtaPresetGet', { id });
  }
  gtaPresetApply(id: string) {

    return this.send<GtaPresetApplyResult>('gtaPresetApply', { id }, 5 * 60 * 1000);
  }
  gtaSettingsApplyFromUrl(xmlUrl: string) {
    return this.send<GtaPresetApplyResult>('gtaSettingsApplyFromUrl', { xmlUrl }, 5 * 60 * 1000);
  }
  gtaPresetIncrementDownloads(id: string) {
    return this.send<number>('gtaPresetIncrementDownloads', { id });
  }
  gtaInstallCounts(eventType: string) {
    return this.send<Record<string, number>>('gtaInstallCounts', { eventType }, 15_000);
  }
  accountStats() {
    return this.send<import('./types').AccountStats>('accountStats', {}, 15_000);
  }
  gtaPresetReactionsGet(presetId: string, userId: string) {
    return this.send<import('./types').PresetReactions>('gtaPresetReactionsGet', { presetId, userId }, 15_000);
  }
  gtaPresetReactionSet(presetId: string, reaction: number) {
    return this.send<import('./types').PresetReactions>('gtaPresetReactionSet', { presetId, reaction }, 15_000);
  }
  adminGtaPresetList() {
    return this.send<GtaPreset[]>('adminGtaPresetList');
  }
  adminGtaPresetUpload(request: GtaPresetUploadRequest) {

    return this.send<GtaPreset>('adminGtaPresetUpload', request, 2 * 60 * 1000);
  }
  async adminGtaPresetPatch(id: string, patch: GtaPresetPatch) {
    await this.send<null>('adminGtaPresetPatch', { id, patch });
  }
  async adminGtaPresetDelete(id: string) {
    await this.send<null>('adminGtaPresetDelete', { id });
  }
  adminGtaPresetAnalyze(sourceXmlPath: string) {
    return this.send<GtaSettingsAnalysis>('adminGtaPresetAnalyze', { sourceXmlPath });
  }

  gtaSettingsRead() {
    return this.send<GtaSettingsReadResult>('gtaSettingsRead');
  }

  optimizationCatalogGet() {
    return this.send<OptimizationCatalog>('optimizationCatalogGet');
  }
  optimizationStateGet() {
    return this.send<OptimizationResolution>('optimizationStateGet');
  }
  optimizationApply(selections: OptimizationSelection[]) {
    return this.send<OptimizationApplyResult>('optimizationApply', { selections }, 5 * 60 * 1000);
  }
  optimizationResolveFromPreset(presetId: string) {
    return this.send<OptimizationResolution>('optimizationResolveFromPreset', { presetId });
  }
  gtaSettingsAnalyzeModel(model: GtaSettingsModel) {

    return this.send<GtaSettingsAnalysis>('gtaSettingsAnalyzeModel', model);
  }
  gtaSettingsWrite(model: GtaSettingsModel) {
    return this.send<GtaPresetApplyResult>('gtaSettingsWrite', model);
  }

  mirrorSetOverride(choice: string | null) {
    return this.send<void>('mirrorSetOverride', { choice });
  }
  mirrorProbe(choice: string | null) {
    return this.send<import('./IAppBridge').MirrorProbeResult>('mirrorProbe', { choice });
  }
  zapretApplyWhitelist(path: string) {
    return this.send<import('./IAppBridge').ZapretApplyResult>('zapretApplyWhitelist', { path });
  }
  zapretDetect(path: string | null) {
    return this.send<import('./IAppBridge').ZapretDetectResult>('zapretDetect', { path });
  }
  rendererEnsureInstalled() {

    return this.send<import('./IAppBridge').RendererEnsureResult>('rendererEnsureInstalled', null, 10 * 60 * 1000);
  }
  rendererProbe() {
    return this.send<import('./IAppBridge').RendererProbe>('rendererProbe', null);
  }
  rendererTestRender() {
    return this.send<import('./IAppBridge').RendererTestRender>('rendererTestRender', null, 2 * 60 * 1000);
  }
  rendererForceReinstall() {
    return this.send<import('./IAppBridge').RendererEnsureResult>('rendererForceReinstall', null, 10 * 60 * 1000);
  }
  jreEnsureInstalled() {

    return this.send<import('./IAppBridge').JreEnsureResult>('jreEnsureInstalled', null, 5 * 60 * 1000);
  }
  bypassTestRun(strategyId: number) {

    return this.send<import('./IAppBridge').BypassTestResult>('bypassTestRun', { strategyId }, 30 * 1000);
  }
  networkDoctorRun(url?: string | null) {
    return this.send<import('./IAppBridge').NetworkDoctorReport>(
      'networkDoctorRun', { url: url ?? null }, 4 * 60 * 1000);
  }

  serverRegionGet() {
    return this.send<import('./IAppBridge').ServerRegionStatus>('serverRegionGet');
  }
  serverRegionSet(region: 'eu' | 'ru') {
    return this.send<void>('serverRegionSet', { region });
  }
  serverRegionPing() {
    return this.send<import('./IAppBridge').ServerRegionPing>('serverRegionPing');
  }

  downloadSourceGet() {
    return this.send<import('./IAppBridge').DownloadSourceStatus>('downloadSourceGet');
  }
  downloadSourceSet(source: 'eu' | 'ru2') {
    return this.send<void>('downloadSourceSet', { source });
  }
  downloadSourceEvaluateEu(zapretRootPath?: string | null) {
    return this.send<import('./IAppBridge').DownloadSourceEval>(
      'downloadSourceEvaluateEu', { zapretRootPath: zapretRootPath ?? null }, 30 * 1000);
  }

  pcDiagReport() {
    return this.send<import('./IAppBridge').PcDiagReport>('pcDiagReport', null, 60 * 1000);
  }
  pcDiagApply(id: string) {
    return this.send<import('./IAppBridge').PcDiagApplyResult>('pcDiagApply', { id }, 120 * 1000);
  }
  pcDiagRevert(id: string) {
    return this.send<import('./IAppBridge').PcDiagApplyResult>('pcDiagRevert', { id }, 60 * 1000);
  }
  pcDiagJournal() {
    return this.send<import('./IAppBridge').PcDiagJournalEntry[]>('pcDiagJournal');
  }
  pcDiagTweaks() {
    return this.send<import('./IAppBridge').PcDiagTweak[]>('pcDiagTweaks');
  }
  pcDiagAi(userId: string, question?: string | null, history?: import('./IAppBridge').PcDiagAiMsg[]) {
    return this.send<import('./IAppBridge').PcDiagAiResult>(
      'pcDiagAi', { userId, question: question ?? null, history: history ?? [] }, 100 * 1000);
  }
}
