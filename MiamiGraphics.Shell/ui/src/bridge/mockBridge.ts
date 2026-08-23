import type { IAppBridge, BridgeEvents, BridgeEventName, BridgeEventMap } from './IAppBridge';

import demoData from './demoData.json';
import type {
  SystemInfo, AuthResult, AdminWebAuthResult, ServerStatus, AppUpdateInfo, AppUpdateInstallResult, AppSettings, BackupStatus, BackupResult, BackupProgress, BackupPhase,
  AdminConfig, TestConnectionResult, ReduxAnalysis, ReduxItem, ReduxVersion, DuplicateHashMatch,
  ComponentInfo, R2Urls,
  FeaturedPick,
  QueueItem, InjectResult, GtaVersion,
  GtaVersionAutoFill, LibraryComponent, LibraryUpload, LibraryPatch, ReduxReview, UserBuildReview, UserProfile, InstallHistoryEntry,
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
  CustomizationDraftBridge,
  CustomGun, CustomGunLimits, CustomGunPatch, CustomGunSort,
  WorkshopSession, WorkshopOpenRequest, WorkshopPublishMeta,
  Language,
} from './types';

function randomChars(n: number): string {
  const alpha = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
  let s = '';
  for (let i = 0; i < n; i++) s += alpha[Math.floor(Math.random() * alpha.length)];
  return s;
}

const SETTINGS_KEY = 'hntgraph.appSettings';
const BACKUP_DONE_KEY = 'hntgraph.mockBackupDone';

function mockTexturePng(fill: string, label: string): string {
  const c = document.createElement('canvas');
  c.width = 128; c.height = 128;
  const g = c.getContext('2d')!;
  g.fillStyle = fill; g.fillRect(0, 0, 128, 128);
  g.strokeStyle = 'rgba(255,255,255,0.12)';
  for (let i = 16; i < 128; i += 16) { g.beginPath(); g.moveTo(i, 0); g.lineTo(i, 128); g.moveTo(0, i); g.lineTo(128, i); g.stroke(); }
  g.fillStyle = 'rgba(255,255,255,0.5)'; g.font = '11px monospace'; g.fillText(label, 8, 68);
  return c.toDataURL('image/png');
}

const MJ = (id: number) => `https://cdn.majestic-files.net/public/master/static/img/inventory/items/${id}.webp`;
function seedCustomGuns(): CustomGun[] {
  const base = (o: Partial<CustomGun>): CustomGun => ({
    id: 'cg_' + randomChars(10), ownerId: 'u', ownerName: 'player', baseName: 'carbinerifle',
    weaponPrefix: 'w_ar_', internalName: 'w_ar_carbinerifle', displayName: 'Skin', description: '',
    category: 'assault', glbUrl: null, previewUrl: MJ(275), downloadCount: 0,
    createdAt: '2026-07-10T10:00:00Z', updatedAt: '2026-07-10T10:00:00Z', mine: false,
    status: 'published', submittedForReview: false, reviewedAt: null, rejectReason: null, ...o,
  });
  return [
    base({ displayName: 'NEON VICE PINK', ownerName: 'skinmaster', description: 'Розовый неон по корпусу.', downloadCount: 1240, previewUrl: MJ(274), internalName: 'w_ar_carbineriflemk2', baseName: 'carbineriflemk2' }),
    base({ displayName: 'GOLD SPECIAL', ownerName: 'aurum', description: 'Золото с гравировкой.', downloadCount: 860, category: 'assault', previewUrl: MJ(272), internalName: 'w_ar_specialcarbinemk2', baseName: 'specialcarbinemk2' }),
    base({ displayName: 'ЧЁРНЫЙ МРАМОР', ownerName: 'noir', description: 'Матовый мрамор.', downloadCount: 430, previewUrl: MJ(259), weaponPrefix: 'w_sg_', internalName: 'w_sg_heavyshotgun', baseName: 'heavyshotgun', category: 'shotgun' }),
    base({ displayName: 'CYBER TEAL', ownerName: 'grid', description: 'Бирюзовая сетка.', downloadCount: 210, previewUrl: MJ(279), weaponPrefix: 'w_sr_', internalName: 'w_sr_heavysniper', baseName: 'heavysniper', category: 'sniper' }),
    base({ displayName: 'МОЙ ПЕРВЫЙ СКИН', ownerName: 'Вы', description: 'Проба пера в мастерской.', downloadCount: 12, mine: true, previewUrl: MJ(275) }),
    base({ displayName: 'CRIMSON REVOLVER', ownerName: 'Вы', description: 'Красный револьвер.', downloadCount: 47, mine: true, weaponPrefix: 'w_pi_', internalName: 'w_pi_revolver', baseName: 'revolver', category: 'pistol', previewUrl: MJ(263) }),
    base({ displayName: 'PENDING TESTER', ownerName: 'newbie', description: 'Скин на модерации (тест очереди).', downloadCount: 0, status: 'pending', submittedForReview: true, previewUrl: MJ(272), weaponPrefix: 'w_ar_', internalName: 'w_ar_assaultrifle', baseName: 'assaultrifle' }),
  ];
}

const sleep = (ms: number) => new Promise<void>(r => setTimeout(r, ms));

const defaultSettings: AppSettings = {
  language: 'ru',
  accentColor: 'slate',
  background: 'cubes',
  polygonsEnabled: true,
  sidebarCollapsed: false,
};

const DEMO_EPOCH = '2025-01-01T00:00:00Z';

interface DemoComponentInfo {
  is_found:       boolean;
  source_rpf:     string;
  internal_paths: string[];
  flags:          string[];
}

interface DemoRedux {
  id: string; name: string; author: string; authorLink: string; description: string;
  videoUrl: string; previewUrl: string; galleryUrls: string[];
  r2Urls: R2Urls | null;
  patchSizeBytes: number; targetGtaVersion: string; supportedServers: string[];
  isVerified: boolean;
  components: Record<string, DemoComponentInfo | undefined>;
  uploadedAt: string; uploadedBy: string;
  status: string;
  viewerPriority: number; downloadCount: number;
  tagNew: boolean; tagBest: boolean; armorStandaloneInstallHidden: boolean;
  componentScreenshots: Record<string, string>;
}

interface DemoGunpack {
  id: string; name: string;
  author: string | null; authorLink: string | null; description: string | null;
  weaponsRpfUrl: string; weaponsRpfSize: number; weaponsRpfSha256: string;
  packZipUrl: string | null; packZipSize: number | null; packZipSha256: string | null;
  manifestUrl: string | null;
  coverKind: string;
  coverUrl: string | null; galleryUrls: string[];
  status: string;
  isVerified: boolean; viewerPriority: number; downloadCount: number;
  uploadedAt: string; uploadedBy: string | null; updatedAt: string; notes: string | null;
}

interface DemoGun {
  id: string; gunpackId: string; baseName: string; weaponPrefix: string; category: string;
  displayName: string | null; glbUrl: string | null; previewUrl: string | null;
  files: string[]; sizeBytes: number; isHidden: boolean; sortOrder: number;
}

interface DemoArmor {
  id: string; name: string; author: string; description: string;
  glbUrl: string; armorRpfUrl: string; internalPath: string;
  status: string; isVerified: boolean;
  downloadCount: number; viewerPriority: number;
  uploadedAt: string; supportedServers: string[];
}

interface DemoDump {
  reduxes:           DemoRedux[];
  gunpacks:          DemoGunpack[];
  gunpackGuns:       DemoGun[];
  libraryComponents: LibraryComponent[];
  armorLibrary:      DemoArmor[];
}

const demoDump: DemoDump = demoData;

function toComponentMap(
  raw: Record<string, DemoComponentInfo | undefined>,
  r2Urls: R2Urls | null,
): Record<string, ComponentInfo> {
  const out: Record<string, ComponentInfo> = {};
  for (const [name, info] of Object.entries(raw)) {
    if (!info) continue;
    out[name] = {
      isFound:       info.is_found,
      sourceRpf:     info.source_rpf,
      internalPaths: info.internal_paths,
      flags:         info.flags,
      glbUrl:        name === 'armor' ? (r2Urls?.components['armor_glb'] ?? null) : null,
    };
  }
  return out;
}

function toReduxItem(r: DemoRedux): ReduxItem {
  return {
    id: r.id, name: r.name, author: r.author, authorLink: r.authorLink,
    description: r.description, videoUrl: r.videoUrl, previewUrl: r.previewUrl,
    galleryUrls: r.galleryUrls, r2Urls: r.r2Urls,
    patchSizeBytes: r.patchSizeBytes, targetGtaVersion: r.targetGtaVersion,
    supportedServers: r.supportedServers, isVerified: r.isVerified,
    components: toComponentMap(r.components, r.r2Urls),
    uploadedAt: r.uploadedAt, uploadedBy: r.uploadedBy,
    status: r.status === 'hidden' ? 'hidden' : 'published',
    viewerPriority: r.viewerPriority, downloadCount: r.downloadCount,
    tagNew: r.tagNew, tagBest: r.tagBest,
    armorStandaloneInstallHidden: r.armorStandaloneInstallHidden,
    componentScreenshots: r.componentScreenshots,
  };
}

function toGunpack(p: DemoGunpack): Gunpack {
  return {
    ...p,
    coverKind: p.coverKind === 'youtube' ? 'youtube' : 'image',
    status:    p.status === 'hidden' ? 'hidden' : 'published',
  };
}

function toGunpackGun(g: DemoGun, packUploadedAt: string): GunpackGun {
  return { ...g, createdAt: packUploadedAt };
}

function toArmorLibraryItem(a: DemoArmor): ArmorLibraryItem {
  return {
    id: a.id, name: a.name, author: a.author, description: a.description,
    glbUrl: a.glbUrl,
    previewUrl: null,
    armorRpfUrl: a.armorRpfUrl, internalPath: a.internalPath,
    downloadCount: a.downloadCount, viewerPriority: a.viewerPriority,
    isVerified: a.isVerified, status: a.status, uploadedAt: a.uploadedAt,
    supportedServers: a.supportedServers,
    hasMale: true, hasFemale: true,
  };
}

class MockEventBus implements BridgeEvents {
  private listeners = new Map<BridgeEventName, Set<(data: unknown) => void>>();

  on<K extends BridgeEventName>(name: K, cb: (data: BridgeEventMap[K]) => void) {
    let set = this.listeners.get(name);
    if (!set) { set = new Set(); this.listeners.set(name, set); }
    set.add(cb as (data: unknown) => void);
  }

  off<K extends BridgeEventName>(name: K, cb: (data: BridgeEventMap[K]) => void) {
    this.listeners.get(name)?.delete(cb as (data: unknown) => void);
  }

  emit<K extends BridgeEventName>(name: K, data: BridgeEventMap[K]) {
    const set = this.listeners.get(name);
    if (!set) return;
    for (const cb of set) cb(data as unknown);
  }
}

export class MockBridge implements IAppBridge {
  private bus = new MockEventBus();
  events: BridgeEvents = this.bus;

  async getSystemInfo(): Promise<SystemInfo> {
    await sleep(800);
    const noGta = new URLSearchParams(window.location.search).get('nogta') === '1';
    if (noGta) {
      return { gtaPath: null, gpuName: 'NVIDIA GeForce RTX 4070', gtaExeVersion: null, isGtaFound: false };
    }
    return {
      gtaPath: 'C:\\Program Files\\Rockstar Games\\Grand Theft Auto V',
      gpuName: 'NVIDIA GeForce RTX 4070',
      gtaExeVersion: '1.0.3788.0',
      isGtaFound: true,
    };
  }

  async getAppVersion(): Promise<string> {
    return 'dev';
  }

  async authenticateGuest(): Promise<AuthResult> {
    await sleep(300);
    return { token: 'mock-guest', role: 'Guest', username: null };
  }

  async adminWebAuthenticate(): Promise<AdminWebAuthResult> {
    await sleep(1200);
    return { success: true, nick: 'mock-admin', error: null };
  }

  async assetCacheContains(urls: string[]): Promise<boolean[]> {
    return urls.map(() => false);
  }

  async assetCachePrewarm(urls: string[]): Promise<number> {
    await sleep(200);
    return urls.length;
  }

  async gunpackAllGunPreviewUrls(): Promise<string[]> {
    return [];
  }

  async getAppSettings(): Promise<AppSettings> {
    const raw = window.localStorage.getItem(SETTINGS_KEY);
    if (!raw) return defaultSettings;
    try {
      const parsed = JSON.parse(raw) as Partial<AppSettings>;
      return { ...defaultSettings, ...parsed };
    } catch { return defaultSettings; }
  }

  async saveAppSettings(settings: AppSettings): Promise<void> {
    window.localStorage.setItem(SETTINGS_KEY, JSON.stringify(settings));
  }

  async setUiLanguage(lang: Language): Promise<void> {
    console.debug('[mock] setUiLanguage', lang);
  }

  async appUpdateCheck(): Promise<AppUpdateInfo> {
    return {
      updateAvailable: false,
      required: false,
      currentVersion: '1.0.0-dev',
      latestVersion: null,
      installerUrl: null,
      releaseNotes: null,
      sizeBytes: null,
      sha256: null,
      publishedAt: null,
      errorMessage: null,
    };
  }

  async appUpdateInstall(_version: string): Promise<AppUpdateInstallResult> {
    return { success: false, errorMessage: 'Mock bridge does not install updates.', installerPath: null };
  }

  async windowMinimize(): Promise<void> { console.log('[mock] windowMinimize'); }
  async windowMaximize(): Promise<void> { console.log('[mock] windowMaximize'); }
  async windowClose(): Promise<void> { console.log('[mock] windowClose'); }
  async windowSetFullscreen(on: boolean): Promise<void> { console.log('[mock] windowSetFullscreen', on); }
  async windowStartDrag(): Promise<void> { console.log('[mock] windowStartDrag'); }

  async openFolderDialog(): Promise<string | null> {
    await sleep(500);
    return 'C:\\Users\\Mock\\GTA V';
  }

  async openLogsFolder(): Promise<void> {
    console.info('[mock] openLogsFolder - в реальной сборке открывается %LocalAppData%\\MiamiGraphics\\logs.');
  }

  async validateGtaPath(path: string): Promise<boolean> {
    return path.toUpperCase().includes('GTA');
  }

  private _mockGtaOverride: string | null = null;
  async getGtaPathInfo(): Promise<import('./types').GtaPathInfo> {
    await sleep(120);
    const auto = 'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Grand Theft Auto V';
    const overrideActive = !!this._mockGtaOverride;
    return {
      resolvedPath: this._mockGtaOverride ?? auto,
      overridePath: this._mockGtaOverride,
      autoDetectedPath: auto,
      overrideActive,
      valid: true,
    };
  }
  async setGtaPathOverride(path: string): Promise<boolean> {
    if (!path.toUpperCase().includes('GTA')) return false;
    this._mockGtaOverride = path;
    return true;
  }
  async clearGtaPathOverride(): Promise<boolean> {
    this._mockGtaOverride = null;
    return true;
  }

  private _mockCache: { enabled: boolean; rootOverride: string | null; limitBytes: number } =
    { enabled: true, rootOverride: null, limitBytes: 8_053_063_680 };
  private _mockCacheBytes = 3_540_000_000;
  async cacheSettingsGet(): Promise<import('./types').CacheSettings> {
    await sleep(80);
    const defBase = 'C:\\Users\\user\\AppData\\Local\\MiamiGraphics';
    const base = this._mockCache.rootOverride ?? defBase;
    const backupBytes = 3_900_000_000;
    return {
      enabled: this._mockCache.enabled,
      rootOverride: this._mockCache.rootOverride,
      effectiveRoot: base + '\\cache',
      defaultRoot: defBase + '\\cache',
      sizeBytes: this._mockCacheBytes,
      dataRoot: base,
      defaultDataRoot: defBase,
      backupRoot: base + '\\backup',
      backupBytes,
      totalBytes: this._mockCacheBytes + backupBytes + 4_400_000_000,
      workBytes: 4_400_000_000,
      workRoot: defBase + '\\workdir',
      limitBytes: this._mockCache.limitBytes,
      minLimitBytes: 4 * 1024 ** 3,
      maxLimitBytes: 64 * 1024 ** 3,
      protectedBytes: 3_700_000_000,
      freeSpaceBytes: 120 * 1024 ** 3,
      backupOnLegacyRoot: false,
      otherBytes: 1_100_000_000,
    };
  }
  async cacheSettingsSet(enabled: boolean, rootOverride: string | null): Promise<import('./types').CacheSettings> {
    this._mockCache = { ...this._mockCache, enabled, rootOverride };
    return this.cacheSettingsGet();
  }
  async cacheLimitSet(limitBytes: number): Promise<import('./types').CacheSettings> {
    this._mockCache = { ...this._mockCache, limitBytes };
    return this.cacheSettingsGet();
  }
  async cacheCleanupNow(): Promise<import('./types').CacheCleanupResult> {
    await sleep(600);
    const before = this._mockCacheBytes;
    this._mockCacheBytes = Math.max(400_000_000, before - 1_800_000_000);
    return {
      beforeBytes: before,
      afterBytes: this._mockCacheBytes,
      freedBytes: before - this._mockCacheBytes,
      deletedEntries: 12,
      stillOverLimit: false,
      reason: 'freed',
      protectedBytes: 3_700_000_000,
      reclaimableBytes: 900_000_000,
      otherBytes: 1_100_000_000,
      holders: [
        { name: 'бэкапы для отката игры', bytes: 3_000_000_000 },
        { name: 'разобранные ганпаки',    bytes: 500_000_000 },
        { name: 'редактор ганов',         bytes: 200_000_000 },
      ],
    };
  }
  async dataRootMove(targetDir: string): Promise<import('./types').DataMoveResult> {
    const total = 7_400_000_000;
    for (const [phase, steps] of [['copying', 10], ['verifying', 5]] as const) {
      for (let i = 1; i <= steps; i++) {
        await sleep(180);
        this.bus.emit('data:moveProgress', {
          phase, percent: Math.round((i / steps) * 100), fileName: `update_${i}.rpf`,
          bytesProcessed: Math.round((i / steps) * total), bytesTotal: total, errorMessage: null,
        });
      }
    }
    this._mockCache = { ...this._mockCache, rootOverride: targetDir };
    this.bus.emit('data:moveProgress', {
      phase: 'done', percent: 100, fileName: null,
      bytesProcessed: total, bytesTotal: total, errorMessage: null,
    });
    return { success: true, effectiveRoot: targetDir, movedBytes: total, sourceRemoved: true, errorMessage: null };
  }

  async dataRootMoveCancel(): Promise<void> {
  }

  async scanGunpackBatchFolder(parentPath: string): Promise<import('./types').GunpackBatchEntry[]> {
    await sleep(220);

    const names = ['BANANA', 'GOTHIC', 'KILLA NEON PAINT', 'MINI GUCCI', '161'];
    return names.map(n => ({
      folderName: n,
      folderPath: `${parentPath}/${n}`,
      rpfPath:    `${parentPath}/${n}/dlc.rpf`,
      imagePath:  `${parentPath}/${n}/${n.toLowerCase().replace(/\s+/g, '_')}.png`,
    }));
  }

  async backupGetStatus(): Promise<BackupStatus> {
    const done = (typeof document !== 'undefined' && document.documentElement.dataset.demo === '1')
      ? true
      : window.localStorage.getItem(BACKUP_DONE_KEY) === '1';
    return {
      manifestExists: done,
      cleanUpdatePresent: done,
      cleanDlcPresent: done,
      snapshotUpdatePresent: done,
      snapshotDlcPresent: done,
      lastBackupAt: done ? new Date().toISOString() : null,
      knownExeVersion: done ? '1.0.3788.0' : null,
    };
  }

  async backupRunFull(): Promise<BackupResult> {
    const UPDATE_BYTES = 2_147_483_648;
    const DLC_BYTES    =   486_539_264;

    const emit = (p: BackupProgress) => this.bus.emit('backup:progress', p);

    const plain = async (phase: BackupPhase, ms: number, file?: string) => {
      const ticks = 6;
      for (let i = 0; i <= ticks; i++) {
        emit({
          phase, percent: Math.round((i / ticks) * 100), fileName: file ?? null,
          bytesProcessed: null, bytesTotal: null, errorCode: null, errorMessage: null,
        });
        await sleep(ms / ticks);
      }
    };

    const transfer = async (phase: BackupPhase, file: string, totalBytes: number, seconds: number) => {
      const EVENT_MS = 250;
      const ticks = Math.round((seconds * 1000) / EVENT_MS);
      const stallFrom = Math.round(ticks * 0.45);
      const stallTo   = stallFrom + 16;
      let done = 0;
      for (let i = 0; i <= ticks; i++) {
        const stalled = i > stallFrom && i < stallTo;
        if (!stalled) {
          done = Math.min(totalBytes, done + (totalBytes / ticks) * (0.65 + Math.random() * 0.7));
        }
        emit({
          phase, percent: Math.round((done / totalBytes) * 100), fileName: file,
          bytesProcessed: Math.round(done), bytesTotal: totalBytes,
          errorCode: null, errorMessage: null,
        });
        await sleep(EVENT_MS);
      }
    };

    await plain('detecting', 400);
    await plain('hashing_user_update', 1200, 'update.rpf');
    await plain('comparing', 200);
    await plain('snapshot_user_update', 600, 'update.rpf');
    await transfer('downloading_clean_update', 'update.rpf', UPDATE_BYTES, 30);
    await plain('writing_working_update', 2500, 'update.rpf');
    await plain('snapshot_dlc', 400, 'dlc.rpf');
    await transfer('downloading_clean_dlc', 'dlc.rpf', DLC_BYTES, 12);
    await plain('writing_working_dlc', 1200, 'dlc.rpf');
    await plain('writing_manifest', 200);

    this.bus.emit('backup:progress', {
      phase: 'done', percent: 100, fileName: null,
      bytesProcessed: null, bytesTotal: null, errorCode: null, errorMessage: null,
    });

    window.localStorage.setItem(BACKUP_DONE_KEY, '1');
    return { success: true, hadDirtyUpdate: true, versionUnsupported: false, errorCode: null, errorMessage: null };
  }

  async backupCancel(): Promise<boolean> { await sleep(80); return true; }
  async backupRestoreClean(): Promise<boolean> { await sleep(200); return true; }
  async backupRestoreSnapshot(): Promise<boolean> { await sleep(200); return true; }
  async killProcessesByPid(pids: number[]): Promise<number> { await sleep(150); return pids.length; }
  async factoryResetAndRestart(): Promise<void> {

    await sleep(120);
    try { window.localStorage.clear(); } catch {  }
    try { window.sessionStorage.clear(); } catch {  }
    window.location.reload();
  }
  async launcherUninstall(): Promise<void> {
    await sleep(120);
    console.info('[mock] launcherUninstall - в реальной сборке тут запускается деинсталлятор и закрывается приложение.');
  }

  async openFileDialog(): Promise<string | null> { await sleep(300); return 'C:\\Mock\\redux.zip'; }
  async openFileDialogMulti(): Promise<string[]> { await sleep(300); return ['C:\\Mock\\one.xml', 'C:\\Mock\\two.xml']; }

  private mockAdminConfig: AdminConfig = {
    r2Endpoint: 'https://example.r2.cloudflarestorage.com',
    r2Bucket: 'huntergraphics',
    r2PublicUrl: 'https://miamigraphicsstorage.uk',
    r2AccessKey: '',
    r2SecretKey: '',
    cleanUpdateRpfPath: '',
    gtaPathOverride: null,
    workDirOverride: null,
    supabaseServiceKey: '',
    adminApiToken: '',
  };
  private mockQueue: QueueItem[] = [];
  private mockCatalog: ReduxItem[] = [];

  private _libraryComponents: LibraryComponent[] = [];
  private _armorLibrary: ArmorLibraryItem[] = [];

  constructor() {

    try {
      const isDemo = typeof document !== 'undefined'
        && document.documentElement.dataset.demo === '1';
      if (isDemo) {
        this.mockCatalog = demoDump.reduxes.map(toReduxItem);
        this._gunpacks = demoDump.gunpacks.map(toGunpack);

        const packUploadedAt = new Map(demoDump.gunpacks.map(p => [p.id, p.uploadedAt]));
        for (const g of demoDump.gunpackGuns) {
          const gpId = g.gunpackId;
          if (!this._gunpackGuns[gpId]) this._gunpackGuns[gpId] = [];
          this._gunpackGuns[gpId].push(
            toGunpackGun(g, packUploadedAt.get(gpId) ?? DEMO_EPOCH),
          );
        }

        this._libraryComponents = [...demoDump.libraryComponents];

        this._armorLibrary = demoDump.armorLibrary.map(toArmorLibraryItem);
      }
    } catch {  }
  }

  async adminConfigGet() { await sleep(80); return this.mockAdminConfig; }
  async adminConfigSave(config: AdminConfig) { await sleep(120); this.mockAdminConfig = config; }
  async adminConfigTestR2(config: AdminConfig): Promise<TestConnectionResult> {
    await sleep(900);
    if (!config.r2AccessKey || !config.r2SecretKey)
      return { success: false, message: 'Access Key and Secret Key are required.', objectCount: null };
    return { success: true, message: 'Connected (mock).', objectCount: 47 };
  }

  async adminReduxAnalyze(sourcePath: string): Promise<ReduxAnalysis> {
    await sleep(1200);
    return {
      resolvedUpdateRpfPath: sourcePath,
      sizeBytes: 142_000_000,
      targetGtaVersion: '1.0.3788.0',
      components: {
        minimap:   { isFound: true,  sourceRpf: 'x64\\textures\\minimap.ytd', internalPaths: ['minimap_0.dds'], flags: [] },
        crosshair: { isFound: true,  sourceRpf: 'x64\\models\\hud.gtxd',      internalPaths: ['crosshair.ydr'], flags: [] },
        tracers:   { isFound: true,  sourceRpf: 'x64\\weapons\\fx.dat',       internalPaths: ['tracer_main.dat'], flags: ['scenario:main'] },
        timecycle: { isFound: false, sourceRpf: '',                            internalPaths: [],                  flags: [] },
      },
      tempWorkDir: 'C:\\Mock\\temp\\analyze\\abc123',
      sourceSha256: '',
    };
  }

  async adminQueueList() { await sleep(50); return [...this.mockQueue]; }
  async adminQueueAdd(item: QueueItem) {
    await sleep(80);
    const id = item.tempId || Math.random().toString(36).slice(2, 14);
    const stored: QueueItem = { ...item, tempId: id, status: 'pending', addedAt: new Date().toISOString() };
    this.mockQueue.push(stored);
    return stored;
  }
  async adminQueueRemove(tempId: string) {
    this.mockQueue = this.mockQueue.filter(x => x.tempId !== tempId);
  }
  private cancelQueue = false;
  async adminQueueRun() {
    this.cancelQueue = false;
    const bus = this.bus;
    void (async () => {
      const pending = this.mockQueue.filter(x => x.status === 'pending');
      for (const it of pending) {
        if (this.cancelQueue) break;
        for (let p = 0; p <= 100; p += 10) {
          if (this.cancelQueue) break;
          it.status = 'processing';
          it.percent = p;
          it.currentPhase = p < 50 ? 'building' : p < 90 ? 'uploading' : 'registering';
          bus.emit('admin:queueProgress', { ...it });
          await sleep(150);
        }
        it.status = 'done'; it.percent = 100; it.currentPhase = null;
        if (it.uploadToR2) {
          this.mockCatalog.push({ ...it.metadata, uploadedAt: new Date().toISOString(), status: 'published' });
        }
        bus.emit('admin:queueProgress', { ...it });
      }
    })();
  }
  async adminQueueCancel() { this.cancelQueue = true; }
  async adminRebuildReduxComponents() { await sleep(120); return 0; }
  async adminRecalculateReduxPatchSizes() { await sleep(120); return 0; }

  async adminCatalogList(search?: string, server?: string, status?: string) {
    await sleep(80);
    let q = [...this.mockCatalog];
    if (search) {
      const t = search.toLowerCase();
      q = q.filter(x => x.name.toLowerCase().includes(t) || x.author.toLowerCase().includes(t));
    }
    if (server) q = q.filter(x => x.supportedServers.includes(server));
    if (status) q = q.filter(x => x.status === status);
    return q;
  }
  async adminCatalogUpdate(item: ReduxItem) {
    const i = this.mockCatalog.findIndex(x => x.id === item.id);
    if (i >= 0) this.mockCatalog[i] = item;
  }
  async adminCatalogDelete(id: string) {
    this.mockCatalog = this.mockCatalog.filter(x => x.id !== id);
    delete this.mockVersions[id];
  }
  async adminWipeAll(category: string) {

    await sleep(200);
    let count = 0;
    switch (category.toLowerCase()) {
      case 'redux':
        count = this.mockCatalog.length;
        this.mockCatalog = [];
        this.mockVersions = {};
        break;
      case 'gunpacks':
        count = this._gunpacks.length;
        this._gunpacks = [];
        break;
      case 'gtapresets':
        count = this._gtaPresets.length;
        this._gtaPresets = [];
        break;
      default:
        count = 0;
    }
    return { deleted: count, failed: 0 };
  }

  private mockVersions: Record<string, ReduxVersion[]> = {};
  async reduxVersions(reduxId: string): Promise<ReduxVersion[]> {
    await sleep(40);
    return [...(this.mockVersions[reduxId] ?? [])].sort((a, b) => a.slot - b.slot);
  }
  async adminFindByHash(sha256: string): Promise<DuplicateHashMatch | null> {
    if (!sha256) return null;

    for (const [reduxId, list] of Object.entries(this.mockVersions)) {
      const match = list.find(v => v.sourceSha256 === sha256 || v.patchSha256 === sha256);
      if (match) {
        const parent = this.mockCatalog.find(x => x.id === reduxId);
        return {
          reduxId,
          reduxName: parent?.name ?? reduxId,
          versionId: match.id,
          slot:      match.slot,
          label:     match.label,
        };
      }
    }
    return null;
  }
  async adminVersionUpsert(version: ReduxVersion): Promise<void> {
    const list = this.mockVersions[version.reduxId] ?? [];
    const id = version.id || crypto.randomUUID();
    const next: ReduxVersion = { ...version, id, updatedAt: new Date().toISOString() };
    const i = list.findIndex(v => v.id === id);
    if (i >= 0) list[i] = next;
    else        list.push({ ...next, createdAt: new Date().toISOString() });
    this.mockVersions[version.reduxId] = list;
  }
  async adminVersionDelete(id: string): Promise<void> {
    for (const reduxId of Object.keys(this.mockVersions)) {
      this.mockVersions[reduxId] = this.mockVersions[reduxId].filter(v => v.id !== id);
    }
  }

  private mockFeatured: Map<number, FeaturedPick> = new Map();
  async featuredPicksList(): Promise<FeaturedPick[]> {
    await sleep(40);
    return [...this.mockFeatured.values()].sort((a, b) => a.slotIndex - b.slotIndex);
  }
  async adminFeaturedPickSet(slotIndex: number, reduxId: string): Promise<void> {
    this.mockFeatured.set(slotIndex, { slotIndex, reduxId, updatedAt: new Date().toISOString() });
  }
  async adminFeaturedPickDelete(slotIndex: number): Promise<void> {
    this.mockFeatured.delete(slotIndex);
  }

  private mockReviews: Record<string, ReduxReview[]> = {};
  private mockBuildReviews: Record<string, UserBuildReview[]> = {};
  async reduxReviewsList(reduxId: string): Promise<ReduxReview[]> {
    await sleep(120);
    return [...(this.mockReviews[reduxId] ?? [])];
  }
  async userBuildReviewsList(buildId: string): Promise<UserBuildReview[]> {
    await sleep(110);
    return [...(this.mockBuildReviews[buildId] ?? [])];
  }
  async userBuildReviewSubmit(
    buildId: string, userId: string, username: string, role: string,
    avatarUrl: string | null, rating: number, body: string,
  ): Promise<UserBuildReview> {
    await sleep(170);
    const list = this.mockBuildReviews[buildId] ?? [];
    const filtered = list.filter(r => r.userId !== userId);
    const fresh: UserBuildReview = {
      id:          crypto.randomUUID(),
      userBuildId: buildId,
      userId,
      username:    username || 'user',
      role:        role || 'User',
      avatarUrl:   avatarUrl || null,
      rating,
      body:        body.trim(),
      createdAt:   new Date().toISOString(),
    };
    this.mockBuildReviews[buildId] = [fresh, ...filtered];
    return fresh;
  }
  async userBuildReviewDelete(reviewId: string, _userId: string, _role: string): Promise<boolean> {
    await sleep(110);
    let removed = false;
    for (const key of Object.keys(this.mockBuildReviews)) {
      const before = this.mockBuildReviews[key].length;
      this.mockBuildReviews[key] = this.mockBuildReviews[key].filter(r => r.id !== reviewId);
      if (this.mockBuildReviews[key].length !== before) removed = true;
    }
    return removed;
  }
  async reduxReviewSubmit(
    reduxId: string, userId: string, username: string, role: string,
    avatarUrl: string | null, rating: number, body: string,
  ): Promise<ReduxReview> {
    await sleep(180);
    const list = this.mockReviews[reduxId] ?? [];
    const filtered = list.filter(r => r.userId !== userId);
    const fresh: ReduxReview = {
      id:        crypto.randomUUID(),
      reduxId,
      userId,
      username:  username || 'user',
      role:      role || 'User',
      avatarUrl: avatarUrl || null,
      rating,
      body:      body.trim(),
      createdAt: new Date().toISOString(),
    };
    this.mockReviews[reduxId] = [fresh, ...filtered];
    return fresh;
  }
  async reduxRatingsAggregate(): Promise<Record<string, { avg: number; count: number }>> {
    await sleep(80);
    const out: Record<string, { avg: number; count: number }> = {};
    for (const [reduxId, list] of Object.entries(this.mockReviews)) {
      if (list.length === 0) continue;
      const sum = list.reduce((s, r) => s + r.rating, 0);
      out[reduxId] = { avg: sum / list.length, count: list.length };
    }
    return out;
  }
  async reduxReviewDelete(reviewId: string, userId: string, role: string): Promise<boolean> {
    await sleep(120);
    const isAdmin = role === 'Moderator' || role === 'AdminL1' || role === 'AdminL2';
    let removed = false;
    for (const key of Object.keys(this.mockReviews)) {
      const list = this.mockReviews[key];
      const idx = list.findIndex(r => r.id === reviewId);
      if (idx === -1) continue;
      const review = list[idx];
      const allowed = isAdmin || review.userId === userId;
      if (!allowed) return false;
      this.mockReviews[key] = [...list.slice(0, idx), ...list.slice(idx + 1)];
      removed = true;
      break;
    }
    return removed;
  }

  installMod(): Promise<unknown> { throw new Error('Not implemented yet'); }
  uninstallMod(): Promise<unknown> { throw new Error('Not implemented yet'); }

  private mockProfile: UserProfile = {
    id:        'u_admin',
    username:  'admin',
    email:     'admin@example.com',
    role:      'AdminL2',
    avatarUrl: null,
    createdAt: new Date(Date.now() - 30 * 86400_000).toISOString(),
  };
  async getUserProfile(_userId: string): Promise<UserProfile | null> {
    await sleep(120);
    return { ...this.mockProfile };
  }
  async updateUserProfile(_userId: string, username: string, avatarUrl: string | null): Promise<UserProfile> {
    await sleep(180);
    if (!/^[A-Za-z0-9_]{3,32}$/.test(username)) throw new Error('Имя пользователя - латиница, цифры или _, 3–32 символа.');
    this.mockProfile = { ...this.mockProfile, username, avatarUrl: avatarUrl || null };
    return { ...this.mockProfile };
  }

  private mockPendingNewEmail: string | null = null;
  private mockPendingPasswordChange: boolean = false;
  async changePasswordRequest(_userId: string, oldPassword: string, newPassword: string): Promise<void> {
    await sleep(220);
    if (oldPassword !== 'old') throw new Error('Текущий пароль введён неверно.');
    if (newPassword === oldPassword) throw new Error('Новый пароль не должен совпадать со старым.');
    if (newPassword.length < 8) throw new Error('Пароль должен быть не короче 8 символов.');
    this.mockPendingPasswordChange = true;
  }
  async changePasswordConfirm(_userId: string, code: string): Promise<void> {
    await sleep(220);
    if (!/^\d{6}$/.test(code)) throw new Error('Код не найден. Запросите новое письмо.');
    if (!this.mockPendingPasswordChange) throw new Error('Код не найден. Запросите новое письмо.');
    this.mockPendingPasswordChange = false;
  }
  async changeEmailRequest(_userId: string, currentPassword: string, newEmail: string): Promise<void> {
    await sleep(220);
    if (currentPassword !== 'old') throw new Error('Текущий пароль введён неверно.');
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(newEmail)) throw new Error('Некорректный email.');
    this.mockPendingNewEmail = newEmail.toLowerCase();
  }
  async changeEmailConfirm(_userId: string, code: string): Promise<UserProfile> {
    await sleep(220);
    if (!/^\d{6}$/.test(code)) throw new Error('Код не найден. Запросите новое письмо.');
    if (this.mockPendingNewEmail) {
      this.mockProfile = { ...this.mockProfile, email: this.mockPendingNewEmail };
      this.mockPendingNewEmail = null;
    }
    return { ...this.mockProfile };
  }
  async uploadAvatar(_userId: string, localPath: string): Promise<string> {
    await sleep(600);

    const base = localPath.split(/[\\/]/).pop() ?? 'avatar.png';
    return `https://cdn.example/avatars/${Date.now()}-${base}`;
  }

  private mockInstalls: Record<string, InstallHistoryEntry[]> = {};
  async installHistoryList(userId: string): Promise<InstallHistoryEntry[]> {
    await sleep(120);
    return [...(this.mockInstalls[userId] ?? [])];
  }
  async installRecord(
    userId: string, reduxId: string, name: string, author: string, previewUrl: string | null,
  ): Promise<InstallHistoryEntry> {
    await sleep(120);
    const list = this.mockInstalls[userId] ?? [];
    const filtered = list.filter(e => e.reduxId !== reduxId);
    const fresh: InstallHistoryEntry = {
      userId,
      reduxId,
      name:        name || reduxId,
      author:      author || '',
      previewUrl:  previewUrl || null,
      installedAt: new Date().toISOString(),
    };
    this.mockInstalls[userId] = [fresh, ...filtered];
    return fresh;
  }

  async adminInject(moddedRpfPath: string): Promise<InjectResult> {
    await sleep(2500);
    if (!moddedRpfPath) return { success: false, errorMessage: 'Путь не указан', workDir: null };
    return { success: true, errorMessage: null, workDir: 'C:\\Mock\\workdir\\admin_inject_abc123' };
  }
  async adminInjectFromCatalog(reduxId: string): Promise<InjectResult> {
    await sleep(3500);
    if (!reduxId) return { success: false, errorMessage: 'reduxId required', workDir: null };
    return { success: true, errorMessage: null, workDir: `C:\\Mock\\workdir\\catalog_inject_${reduxId}` };
  }
  async adminRestoreCleanUpdate(): Promise<boolean> {
    await sleep(800);
    return true;
  }

  async reduxList(search?: string, server?: string): Promise<ReduxItem[]> {
    await sleep(150);
    const q = (search ?? '').toLowerCase();
    return this.mockCatalog
      .filter(i => i.status === 'published')
      .filter(i => !server || i.supportedServers.includes(server))
      .filter(i => !q || i.name.toLowerCase().includes(q) || i.author.toLowerCase().includes(q) || i.id.toLowerCase().includes(q))
      .sort((a, b) => (b.viewerPriority - a.viewerPriority) || b.uploadedAt.localeCompare(a.uploadedAt));
  }
  async reduxFavoriteList(_userId: string): Promise<string[]> { await sleep(50); return []; }
  async reduxFavoriteAdd(_userId: string, _reduxId: string): Promise<void> { await sleep(50); }
  async reduxFavoriteRemove(_userId: string, _reduxId: string): Promise<void> { await sleep(50); }
  private _mockItemFavs: Record<string, string[]> = {};
  async itemFavoritesList(_userId: string, itemType: string): Promise<string[]> { await sleep(30); return this._mockItemFavs[itemType] ?? []; }
  async itemFavoriteAdd(_userId: string, itemType: string, itemId: string): Promise<void> {
    await sleep(30);
    this._mockItemFavs[itemType] = [...new Set([...(this._mockItemFavs[itemType] ?? []), itemId])];
  }
  async itemFavoriteRemove(_userId: string, itemType: string, itemId: string): Promise<void> {
    await sleep(30);
    this._mockItemFavs[itemType] = (this._mockItemFavs[itemType] ?? []).filter(x => x !== itemId);
  }
  async reduxIncrementDownloads(_reduxId: string): Promise<number> { await sleep(50); return Math.floor(Math.random() * 9999); }
  async reduxInstall(reduxId: string, _versionId?: string | null): Promise<InjectResult> {
    await sleep(2000);
    if (!reduxId) return { success: false, errorMessage: 'reduxId required', workDir: null };
    return { success: true, errorMessage: null, workDir: `C:\\Mock\\workdir\\install_${reduxId}` };
  }
  async reduxDeferArmorReapplyOnce(): Promise<void> {}
  async reduxDeferFastJoinReapplyOnce(): Promise<void> {}
  async reduxDeferMinimapReapplyOnce(): Promise<void> {}
  async reduxInstallForceClean(reduxId: string, _versionId?: string | null): Promise<InjectResult> {
    await sleep(2500);
    return { success: true, errorMessage: null, workDir: `C:\\Mock\\workdir\\install_${reduxId}_force` };
  }
  async reduxInstallCancel() {  }
  async installCancel(_progressId: string): Promise<boolean> { return true; }
  async reduxInstallPreserve(reduxId: string, _versionId?: string | null): Promise<InjectResult> {
    await sleep(4000);
    return { success: true, errorMessage: null, workDir: `C:\\Mock\\workdir\\install_${reduxId}_preserve` };
  }
  async reduxCustomizeApply(reduxId: string, draft: unknown): Promise<InjectResult> {
    await sleep(2500);
    this._installedDraft = draft as CustomizationDraftBridge;
    return { success: true, errorMessage: 'Кастомизация применена (mock).', workDir: `C:\\Mock\\workdir\\customize_${reduxId}` };
  }
  private _installedDraft: CustomizationDraftBridge | null = null;
  async armorInstallStandalone(reduxId: string, _versionId?: string | null, _force: boolean = false, _confirmWipe: boolean = false): Promise<InjectResult> {
    await sleep(2000);
    if (!reduxId) return { success: false, errorMessage: 'reduxId required', workDir: null };
    return { success: true, errorMessage: null, workDir: `C:\\Mock\\workdir\\armor_only_${reduxId}` };
  }

  async readLocalFileBase64(_path: string): Promise<string | null> {
    await sleep(20);
    return null;
  }
  async inspectDlcRpfArmorCancel(): Promise<boolean> {
    await sleep(10);
    return false;
  }
  async inspectDlcRpfArmor(dlcRpfPath: string): Promise<DlcArmorInspectionResult> {
    await sleep(800);
    if (!dlcRpfPath) {
      return {
        dlcRpfPath: '',
        candidates: [],
        warnings: [],
        errorMessage: 'dlcRpfPath required',
      };
    }
    return {
      dlcRpfPath,
      candidates: [
        {
          yddInternalPath: 'x64/levels/gta5/mock.rpf/mp_f_freemode_01_mp_f_january2016/task_008_u.ydd',
          yddName: 'task_008_u.ydd',
          drawableInternalName: 'task_005_u',
          parseError: null,
          samplerExpectations: [
            { samplerName: 'DiffuseSampler', expectedTextureName: 'task_diff_005_a_uni' },
          ],
          candidateYtds: [
            { internalPath: 'x64/levels/gta5/mock.rpf/mp_f_freemode_01_mp_f_january2016/task_diff_008_a_uni.ytd',
              fileName: 'task_diff_008_a_uni.ytd',
              innerTextureNames: ['task_diff_003_d_uni'],
              parseError: null },
          ],
          missingExpectedDiffuses: ['task_diff_005_a_uni'],
          hasNameMismatch: true,
          suggestedRename: {
            ytdInternalPath: 'x64/levels/gta5/mock.rpf/mp_f_freemode_01_mp_f_january2016/task_diff_008_a_uni.ytd',
            oldTextureName: 'task_diff_003_d_uni',
            newTextureName: 'task_diff_005_a_uni',
          },
          previewGlbUrl: null,
        },
      ],
      warnings: ['Это mock-данные - реальная инспекция требует real WebView2.'],
      errorMessage: null,
    };
  }
  async importDlcRpfArmor(request: DlcArmorImportRequest): Promise<DlcArmorImportResult> {
    await sleep(1500);
    if (!request.name) {
      return { success: false, armorId: null, armorRpfUrl: null, glbUrl: null, errorMessage: 'name required' };
    }
    return {
      success: true,
      armorId: 'mock_' + request.name.toLowerCase().replace(/\s+/g, '_'),
      armorRpfUrl: 'https://mock.r2/armor_library/mock/armor.rpf',
      glbUrl: 'https://mock.r2/armor_library/mock/armor.glb',
      errorMessage: request.applyAutoFix ? 'Renamed inner texture (mock).' : null,
    };
  }
  async armorLibraryList(): Promise<ArmorLibraryItem[]> {
    await sleep(80);
    return this._armorLibrary.filter(a => a.status === 'published');
  }
  async armorLibraryListAll(): Promise<ArmorLibraryItem[]> {
    await sleep(80);
    return [...this._armorLibrary];
  }
  async armorLibrarySetVisibility(_id: string, _visible: boolean): Promise<boolean> {
    await sleep(40);
    return true;
  }
  async armorLibrarySetSupportedServers(_id: string, _servers: string[]): Promise<boolean> {
    await sleep(40);
    return true;
  }
  async armorLibraryDelete(_id: string): Promise<boolean> {
    await sleep(60);
    return true;
  }
  async armorLibraryRenderVariants(_id: string): Promise<string[]> {
    await sleep(80);
    return [];
  }
  async armorLibrarySetPreview(_id: string, _previewUrl: string): Promise<boolean> {
    await sleep(40);
    return true;
  }
  async reduxArmorRenderPreview(_reduxId: string): Promise<string | null> {
    await sleep(80);
    return null;
  }
  async reduxArmorBackfillPreviews(): Promise<{ total: number; rendered: number }> {
    await sleep(120);
    return { total: 0, rendered: 0 };
  }
  async reduxArmorRenderVariants(_reduxId: string): Promise<string[]> {
    await sleep(80);
    return [];
  }
  async reduxArmorVariantUrls(_reduxId: string): Promise<string[]> {
    await sleep(40);
    return [];
  }
  async reduxArmorSetPreview(_reduxId: string, _previewUrl: string): Promise<boolean> {
    await sleep(40);
    return true;
  }
  async armorLibraryInstall(_id: string, _overlayMode: boolean = false, _force: boolean = false, _confirmWipe: boolean = false): Promise<InjectResult> {
    await sleep(800);
    return { success: true, errorMessage: null, workDir: null };
  }
  async reduxApplyArmorSwap(donorReduxId: string, _donorVersionId?: string | null): Promise<InjectResult> {
    await sleep(1800);
    if (!donorReduxId) return { success: false, errorMessage: 'donorReduxId required', workDir: null };
    return { success: true, errorMessage: null, workDir: `C:\\Mock\\workdir\\armor_swap_${donorReduxId}` };
  }
  async reduxClearArmor(): Promise<InjectResult> {
    await sleep(1500);
    return { success: true, errorMessage: null, workDir: `C:\\Mock\\workdir\\armor_clear` };
  }
  async getCurrentArmorInfo(): Promise<CurrentArmorInfo | null> { await sleep(20); return null; }
  async reduxUninstall(): Promise<InjectResult> {
    await sleep(1500);
    return { success: true, errorMessage: null, workDir: null };
  }
  async reduxUninstallForceClean(): Promise<InjectResult> {
    await sleep(2000);
    return { success: true, errorMessage: null, workDir: null };
  }
  async reduxUninstallPreserve(): Promise<InjectResult> {
    await sleep(3500);
    return { success: true, errorMessage: null, workDir: null };
  }

  async gtaVersionsList(): Promise<GtaVersion[]> {
    await sleep(80);
    return [{
      exeVersion: '1.0.3788.0',
      updateRpfSize: 2010816512,
      updateRpfSha256: '089f0824689bf9fea508eedccafa67d07fd6b48644514d3b3e9359ae8d06a40f',
      cleanUpdateUrl: 'https://mock-r2.example/clean_update_1.0.3788.0.rpf',
      notes: 'Mock seed version',
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    }];
  }
  async gtaVersionsUpsert(_v: GtaVersion): Promise<void> { await sleep(120); }
  async gtaVersionsDelete(_exeVersion: string): Promise<void> { await sleep(80); }
  async gtaVersionsAutoFill(_p: string): Promise<GtaVersionAutoFill> {
    await sleep(800);
    return { exeVersion: '1.0.3788.0', updateRpfSize: 2010816512, updateRpfSha256: '0'.repeat(64) };
  }
  async gtaVersionsUpload(_p: string, exeVersion: string, notes: string): Promise<GtaVersion> {
    await sleep(2500);
    return {
      exeVersion, updateRpfSize: 2010816512, updateRpfSha256: '0'.repeat(64),
      cleanUpdateUrl: `https://mock-r2.example/gta_versions/${exeVersion}/clean_update.rpf`,
      notes, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
    };
  }

  async libraryList(type?: string): Promise<LibraryComponent[]> {
    await sleep(80);
    const all: LibraryComponent[] = this._libraryComponents.length > 0
      ? this._libraryComponents
      : [{
          id: 'lib_timecycle_demo', type: 'timecycle', name: 'Demo timecycle',
          author: 'mock', description: '', r2Url: 'https://mock-r2.example/library/timecycle/lib_demo.zip',
          sha256: '0'.repeat(64), sizeBytes: 1024 * 1024, sourceRpfVersion: '1.0.3788.0',
          uploadedBy: 'admin', uploadedAt: new Date().toISOString(),
          previewUrl: '', galleryUrls: [], previewVideoUrl: '',
        }];
    return type ? all.filter(c => c.type === type) : all;
  }
  async libraryDelete(_id: string): Promise<void> { await sleep(80); }
  async libraryUploadComponent(p: LibraryUpload): Promise<LibraryComponent> {
    await sleep(1500);
    return {
      id: `lib_${p.componentName}_${Date.now()}`, type: p.componentName, name: p.name,
      author: p.author, description: p.description,
      r2Url: `https://mock-r2.example/library/${p.componentName}/mock.zip`,
      sha256: '0'.repeat(64), sizeBytes: 1024 * 256, sourceRpfVersion: '1.0.3788.0',
      uploadedBy: 'admin', uploadedAt: new Date().toISOString(),
      previewUrl: '', galleryUrls: [], previewVideoUrl: '',
    };
  }
  async libraryPatch(p: LibraryPatch): Promise<LibraryComponent> {
    await sleep(120);
    return {
      id: p.id, type: 'timecycle', name: p.name, author: p.author, description: p.description,
      r2Url: `https://mock-r2.example/library/mock/${p.id}.zip`,
      sha256: '0'.repeat(64), sizeBytes: 1024 * 256, sourceRpfVersion: '1.0.3788.0',
      uploadedBy: 'admin', uploadedAt: new Date().toISOString(),
      previewUrl: '', galleryUrls: [], previewVideoUrl: '',
    };
  }
  async authenticateUser(login: string, password: string): Promise<AuthResult> {
    await sleep(300);
    const seeds: Record<string, { password: string; role: string }> = {
      admin: { password: 'admin', role: 'AdminL2' },
      mod:   { password: 'mod',   role: 'Moderator' },
      user:  { password: 'user',  role: 'User' },
    };
    const seed = seeds[login.toLowerCase()];
    if (!seed || seed.password !== password) {
      throw new Error('Invalid login or password');
    }
    return { token: `mock-${login}`, role: seed.role, username: login, tester: true };
  }

  private _pendingRegister: { email: string; username: string; password: string } | null = null;
  async registerRequest(email: string, username: string, password: string): Promise<void> {
    await sleep(400);
    if (!email || !email.includes('@')) throw new Error('Некорректный email.');
    if (!/^[A-Za-z0-9_]{3,32}$/.test(username)) {
      throw new Error('Имя может содержать только латиницу, цифры и `_` (3–32 символа).');
    }
    if (password.length < 8) throw new Error('Пароль слишком короткий.');
    this._pendingRegister = { email, username, password };
  }
  async registerConfirm(email: string, code: string): Promise<AuthResult> {
    await sleep(400);
    if (!/^\d{6}$/.test(code)) throw new Error('Код должен содержать 6 цифр.');
    const pending = this._pendingRegister;
    if (!pending || pending.email.toLowerCase() !== email.toLowerCase()) {
      throw new Error('Код не найден. Запросите новое письмо.');
    }
    this._pendingRegister = null;
    return { token: `mock-${pending.username}`, role: 'User', username: pending.username, tester: true };
  }
  private _mockFollows = new Set<string>();
  async modmakersList(q?: string): Promise<import('./types').ModmakersList> {
    await sleep(300);
    const makers = [
      { promo: 'MMTEST', display: 'Test Mods', card: null, mods: 2, downloads: 4435 },
      { promo: 'BLXGO',  display: 'BLXGO',     card: null, mods: 0, downloads: 0 },
    ].filter(m => !q || m.display.toLowerCase().includes(q.toLowerCase()));
    return { ok: true, makers };
  }
  async modmakerDetail(code: string): Promise<import('./types').ModmakerDetail> {
    await sleep(300);
    if (code !== 'MMTEST') return { ok: true, promo: code, display: code, page: {}, card: null, mods: [], followers: 0 };
    return { ok: true, promo: 'MMTEST', display: 'Test Mods',
      page: { twitch: 'testmods', telegram: 'testmods' }, card: null, followers: 3,
      mods: [
        { kind: 'redux', id: 'allegri_v3', name: 'Allegri V3', cover: '', downloads: 4074, month: 120, added: '2026-07-31' },
        { kind: 'gunpack', id: 'g1', name: 'allegri black-white', cover: '', downloads: 361, month: null, added: '2026-07-31' },
      ] };
  }
  async modmakerFollow(code: string, on: boolean) {
    await sleep(150);
    if (on) this._mockFollows.add(code); else this._mockFollows.delete(code);
    return { ok: true, following: on, followers: on ? 4 : 3 };
  }
  async modmakerFeed(_notify?: boolean): Promise<import('./types').ModmakerFeed> {
    await sleep(150);
    return { ok: true, follows: [...this._mockFollows].map(p =>
      ({ promo: p, display: p === 'MMTEST' ? 'Test Mods' : p, fresh: p === 'MMTEST' ? 2 : 0, due: p === 'MMTEST' })) };
  }
  async modmakerCanEdit(code: string) { await sleep(100); return { ok: true, can_edit: code === 'MMTEST', is_self: false }; }
  async modmakerMap(): Promise<import('./types').ModmakerMap> {
    await sleep(100);
    return { ok: true, map: [
      { kind: 'redux', id: 'allegri_v3', promo: 'MMTEST', display: 'Test Mods' },
    ] };
  }

  async installerPromo(): Promise<string> { await sleep(80); return 'SISTIM'; }
  async checkPromo(code: string): Promise<import('./types').PromoCheck> {
    await sleep(200);
    const known: Record<string, string> = { SISTIM: 'Sistim', KIBARA: 'Kibara' };
    const c = code.trim().toUpperCase();
    return known[c] ? { ok: true, display: known[c] } : { ok: false, display: null };
  }
  async attachReferral(_code: string): Promise<boolean> { await sleep(150); return true; }
  async betaCodeCheck(code: string): Promise<import('./types').BetaGate> {
    await sleep(200); return { ok: /^ZBT-/i.test(code.trim()), error: null };
  }
  async betaRedeem(_code: string): Promise<import('./types').BetaGate> {
    await sleep(200); return { ok: true, error: null };
  }
  async betaCheck(): Promise<import('./types').BetaGate> {
    await sleep(120); return { ok: true, error: null };
  }
  async betaEnabled(): Promise<boolean> {
    await sleep(60); return true;
  }
  async activityLog(_eventType: string, _detail: string, _itemId?: string): Promise<boolean> {
    await sleep(20); return true;
  }

  async requestPasswordReset(_email: string): Promise<void> {
    await sleep(400);
  }
  async consumePasswordReset(code: string, newPassword: string): Promise<void> {
    await sleep(300);
    if (!code) throw new Error('Код обязателен.');
    if (newPassword.length < 8) throw new Error('Пароль должен быть не короче 8 символов.');
  }
  async getServerStatus(): Promise<ServerStatus> {
    return { reachable: true, provisioned: true, message: 'Mock backend (no Supabase).' };
  }
  async forceExit(): Promise<void> {  }
  compareRpf(): Promise<unknown> { throw new Error('Not implemented yet'); }
  getDownloadQueue(): Promise<unknown[]> { throw new Error('Not implemented yet'); }
  applyColorization(): Promise<void> { throw new Error('Not implemented yet'); }
  extractComponent(): Promise<unknown> { throw new Error('Not implemented yet'); }
  rollback(): Promise<void> { throw new Error('Not implemented yet'); }
  verifyRpf(): Promise<unknown> { throw new Error('Not implemented yet'); }
  applySettingsXml(): Promise<void> { throw new Error('Not implemented yet'); }

  private _hntCodes = new Map<string, HntCode>();
  async hntCodeExport(userId: string, _flags?: {
    includeRedux?: boolean; includeGunpack?: boolean; includeSelectedGuns?: boolean;
    includeComponents?: boolean; gunFilter?: string[];
  }): Promise<HntCode> {
    await sleep(400);
    const code = `HNT-${randomChars(4)}-${randomChars(4)}`;
    const row: HntCode = {
      code,
      payload: {
        reduxId: null, reduxVersionId: null, reduxName: null, reduxAuthor: null,
        gunpackId: null, gunpackName: null, selectedGuns: [], extras: null,
      },
      createdBy: userId,
      createdAt: new Date().toISOString(),
      lastDownloadedAt: new Date().toISOString(),
      downloadsCount: 0,
    };
    this._hntCodes.set(code, row);
    return row;
  }
  async hntCodePreview(code: string): Promise<HntCode> {
    await sleep(300);
    const row = this._hntCodes.get(code.toUpperCase());
    if (!row) throw new Error('HNT_CODE_NOT_FOUND');
    row.downloadsCount++;
    row.lastDownloadedAt = new Date().toISOString();
    return row;
  }
  async hntCodeApply(_payload: HntPayload): Promise<HntImportResult> {
    await sleep(800);
    return {
      success: true, errorMessage: null,
      reduxStep:        { skipped: true, success: true, errorMessage: null },
      gunpackStep:      { skipped: true, success: true, errorMessage: null },
      selectedGunsStep: { skipped: true, success: true, errorMessage: null },
    };
  }
  async hntCodeListMy(userId: string): Promise<HntCode[]> {
    await sleep(120);
    return Array.from(this._hntCodes.values())
      .filter(c => c.createdBy === userId)
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  }
  async hntCodeDelete(code: string, userId: string): Promise<HntCode> {
    await sleep(150);
    const row = this._hntCodes.get(code.toUpperCase());
    if (!row) throw new Error('HNT_CODE_NOT_FOUND');
    if (row.createdBy !== userId) throw new Error('HNT_CODE_FORBIDDEN');
    this._hntCodes.delete(code.toUpperCase());
    return row;
  }

  private _gunpacks: Gunpack[] = [];
  private _gunpackGuns: Record<string, GunpackGun[]> = {};
  private _gunpackQueue: GunpackQueueItem[] = [];

  async gunpackWhitelistList(): Promise<GunpackWhitelistEntry[]> {
    await sleep(150);
    const M = (id: number) => `https://cdn.majestic-files.net/public/master/static/img/inventory/items/${id}.webp`;
    return [
      { internalName: 'w_sg_assaultshotgun',     displayName: 'Штурмовой дробовик',                category: 'shotgun', weaponPrefix: 'w_sg_', isSmgOverride: false, sortOrder: 10,  previewUrl: M(257) },
      { internalName: 'w_sg_heavyshotgun',       displayName: 'Тяжёлый дробовик',                  category: 'shotgun', weaponPrefix: 'w_sg_', isSmgOverride: false, sortOrder: 20,  previewUrl: M(259) },
      { internalName: 'w_pi_revolver',           displayName: 'Револьвер',                          category: 'pistol',  weaponPrefix: 'w_pi_', isSmgOverride: false, sortOrder: 30,  previewUrl: M(263) },
      { internalName: 'w_sb_smgmk2',             displayName: 'ПП Mk2',                             category: 'smg',     weaponPrefix: 'w_sb_', isSmgOverride: true,  sortOrder: 40,  previewUrl: M(267) },
      { internalName: 'w_sb_minismg',            displayName: 'Мини ПП',                            category: 'smg',     weaponPrefix: 'w_sb_', isSmgOverride: true,  sortOrder: 50,  previewUrl: M(270) },
      { internalName: 'w_sb_microsmg',           displayName: 'Микро ПП',                           category: 'smg',     weaponPrefix: 'w_sb_', isSmgOverride: true,  sortOrder: 60,  previewUrl: M(472) },
      { internalName: 'w_ar_specialcarbinemk2',  displayName: 'Спец. карабин Mk2',                  category: 'assault', weaponPrefix: 'w_ar_', isSmgOverride: false, sortOrder: 70,  previewUrl: M(272) },
      { internalName: 'w_ar_specialcarbine',     displayName: 'Спец. карабин',                      category: 'assault', weaponPrefix: 'w_ar_', isSmgOverride: false, sortOrder: 80,  previewUrl: M(273) },
      { internalName: 'w_ar_carbineriflemk2',    displayName: 'Карабинная винтовка Mk2',            category: 'assault', weaponPrefix: 'w_ar_', isSmgOverride: false, sortOrder: 90,  previewUrl: M(274) },
      { internalName: 'w_ar_carbinerifle',       displayName: 'Карабинная винтовка',                category: 'assault', weaponPrefix: 'w_ar_', isSmgOverride: false, sortOrder: 100, previewUrl: M(275) },
      { internalName: 'w_sr_heavysniper',        displayName: 'Тяжёлая снайперская винтовка',       category: 'sniper',  weaponPrefix: 'w_sr_', isSmgOverride: false, sortOrder: 110, previewUrl: M(279) },
      { internalName: 'w_sr_heavysnipermk2',     displayName: 'Тяжёлая снайперская винтовка Mk2',   category: 'sniper',  weaponPrefix: 'w_sr_', isSmgOverride: false, sortOrder: 120, previewUrl: M(328) },
      { internalName: 'w_mg_combatmgmk2',        displayName: 'Ручной пулемёт Mk2',                 category: 'mg',      weaponPrefix: 'w_mg_', isSmgOverride: false, sortOrder: 130, previewUrl: M(333) },
      { internalName: 'w_sr_marksmanriflemk2',   displayName: 'Винтовка Marksman Mk2',              category: 'sniper',  weaponPrefix: 'w_sr_', isSmgOverride: false, sortOrder: 140, previewUrl: M(337) },
      { internalName: 'w_sr_precisionrifle',     displayName: 'Прецизионная винтовка',              category: 'sniper',  weaponPrefix: 'w_sr_', isSmgOverride: false, sortOrder: 150, previewUrl: M(630) },
    ];
  }
  async gunpacksList(_search?: string, _status?: string): Promise<Gunpack[]> {
    await sleep(120);
    return [...this._gunpacks];
  }
  async gunpackGet(id: string): Promise<Gunpack | null> {
    await sleep(80);
    return this._gunpacks.find(p => p.id === id) ?? null;
  }
  async gunpackGuns(gunpackId: string): Promise<GunpackGun[]> {
    await sleep(80);
    return [...(this._gunpackGuns[gunpackId] ?? [])];
  }
  async gunpackAllGuns(): Promise<import('./types').GunpackFlatGun[]> {
    await sleep(140);
    const out: import('./types').GunpackFlatGun[] = [];
    for (const [gunpackId, guns] of Object.entries(this._gunpackGuns)) {
      for (const g of guns) {
        if (g.isHidden) continue;
        out.push({
          id:           g.id,
          gunpackId,
          baseName:     g.baseName,
          weaponPrefix: g.weaponPrefix,
          category:     g.category,
          displayName:  g.displayName,
          glbUrl:       g.glbUrl,
          previewUrl:   g.previewUrl,
        });
      }
    }
    return out;
  }
  async gunpackIncrementDownloads(id: string): Promise<number> {
    await sleep(40);
    const p = this._gunpacks.find(x => x.id === id);
    if (!p) return 0;
    p.downloadCount += 1;
    return p.downloadCount;
  }

  private _customGuns: CustomGun[] = seedCustomGuns();
  private _customMax = 5;
  private _drafts: Record<string, WorkshopSession> = {};

  async customGunsList(search?: string, sort?: CustomGunSort, _viewerUserId?: string): Promise<CustomGun[]> {
    await sleep(120);
    let list = [...this._customGuns];
    const q = (search ?? '').trim().toLowerCase();
    if (q) list = list.filter(g =>
      g.displayName.toLowerCase().includes(q) || g.ownerName.toLowerCase().includes(q));
    list.sort((a, b) => sort === 'downloads'
      ? b.downloadCount - a.downloadCount
      : Date.parse(b.createdAt) - Date.parse(a.createdAt));
    return list;
  }
  async customGunsMine(_ownerUserId?: string): Promise<CustomGun[]> {
    await sleep(100);
    return this._customGuns.filter(g => g.mine);
  }
  async customGunLimits(_ownerUserId?: string): Promise<CustomGunLimits> {
    await sleep(40);
    return { used: this._customGuns.filter(g => g.mine).length, max: this._customMax };
  }
  async customGunPatch(id: string, patch: CustomGunPatch): Promise<void> {
    await sleep(80);
    const g = this._customGuns.find(x => x.id === id);
    if (g) { Object.assign(g, patch); g.updatedAt = new Date().toISOString(); }
  }
  async customGunDelete(id: string): Promise<void> {
    await sleep(80);
    this._customGuns = this._customGuns.filter(x => x.id !== id);
  }
  async customGunInstall(id: string): Promise<void> {
    await sleep(500);
    const g = this._customGuns.find(x => x.id === id);
    if (g) g.downloadCount += 1;
  }
  async customGunListPending(): Promise<CustomGun[]> {
    await sleep(80);
    return this._customGuns.filter(g => g.status === 'pending' && g.submittedForReview)
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  }
  async customGunApprove(id: string, _reviewerUserId: string): Promise<CustomGun> {
    await sleep(120);
    const g = this._customGuns.find(x => x.id === id); if (!g) throw new Error('not found');
    g.status = 'published'; g.submittedForReview = false; g.reviewedAt = new Date().toISOString(); g.rejectReason = null; g.updatedAt = g.reviewedAt;
    return g;
  }
  async customGunReject(id: string, _reviewerUserId: string, reason: string): Promise<CustomGun> {
    await sleep(120);
    const g = this._customGuns.find(x => x.id === id); if (!g) throw new Error('not found');
    g.status = 'rejected'; g.reviewedAt = new Date().toISOString(); g.rejectReason = reason; g.updatedAt = g.reviewedAt;
    return g;
  }
  async customGunAdminList(status?: string | null, search?: string | null): Promise<CustomGun[]> {
    await sleep(80);
    const q = (search ?? '').trim().toLowerCase();
    return this._customGuns
      .filter(g => (!status ? g.status !== 'removed' : status === 'all' ? true : g.status === status))
      .filter(g => !q || g.displayName.toLowerCase().includes(q) || g.ownerName.toLowerCase().includes(q)
                || g.internalName.toLowerCase().includes(q))
      .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
  }
  async customGunAdminPatch(id: string, patch: import('./types').CustomGunPatch): Promise<CustomGun> {
    await sleep(120);
    const g = this._customGuns.find(x => x.id === id); if (!g) throw new Error('not found');
    if (patch.displayName) g.displayName = patch.displayName;
    if (patch.description !== undefined && patch.description !== null) g.description = patch.description;
    if (patch.category) g.category = patch.category;
    g.updatedAt = new Date().toISOString();
    return g;
  }
  async customGunAdminDelete(id: string, reason?: string | null, hard?: boolean): Promise<CustomGun> {
    await sleep(120);
    const i = this._customGuns.findIndex(x => x.id === id); if (i < 0) throw new Error('not found');
    const g = this._customGuns[i];
    if (hard) { this._customGuns.splice(i, 1); return g; }
    g.status = 'removed'; g.submittedForReview = false;
    if (reason) g.rejectReason = reason;
    g.updatedAt = new Date().toISOString();
    return g;
  }
  async customSkinApplied() { await sleep(60); return [] as import('./types').CustomSkinApplied[]; }
  async customSkinRemove(_internalName: string) { await sleep(200); return { success: true, errorMessage: null, workDir: null }; }

  async workshopFlowLimits(): Promise<import('./types').WorkshopFlowLimits> {
    await sleep(60);
    return {
      standardMaxPerGun: 2,
      standardUsedPerGun: { w_ar_carbinerifle: 1 },
      packBaseUsed: 1, packBaseMax: 4,
      ownPackUsed: 0, ownPackMax: 2,
      ownPackGunCap: 3,
    };
  }
  async userGunpacksList(): Promise<import('./types').UserGunpack[]> {
    await sleep(120);
    return [];
  }
  async userGunpackInstall(_id: string): Promise<void> { await sleep(800); }
  async userGunpackDelete(_id: string): Promise<void> { await sleep(120); }
  async customGunPreviewDownload(_url: string, name: string): Promise<string> {
    await sleep(300);
    return `C:\\Users\\mock\\Downloads\\${name}.webp`;
  }

  async workshopOpen(req: WorkshopOpenRequest): Promise<WorkshopSession> {
    await sleep(400);
    const existing = req.customGunId ? this._customGuns.find(g => g.id === req.customGunId) : null;
    const draftId = 'draft_' + randomChars(8);
    const session: WorkshopSession = {
      draftId,
      customGunId: existing?.id ?? null,
      displayName: existing?.displayName ?? 'Новый скин',
      baseName:    existing?.baseName ?? (req.baseInternalName?.replace(/^w_[a-z]+_/, '') ?? 'carbinerifle'),
      weaponPrefix: existing?.weaponPrefix ?? 'w_ar_',
      category:    existing?.category ?? 'assault',
      glbUrl:      null,
      textures: [
        { name: 'diffuse_01', width: 512, height: 512, role: 'diffuse', dataUrl: mockTexturePng('#3a3a46', 'DIFFUSE') },
        { name: 'normal_01',  width: 512, height: 512, role: 'normal',  dataUrl: mockTexturePng('#7c86ff', 'NORMAL') },
        { name: 'spec_01',    width: 256, height: 256, role: 'spec',    dataUrl: mockTexturePng('#2a2a1a', 'SPEC') },
      ],
    };
    this._drafts[draftId] = session;
    return session;
  }
  async workshopReplaceTexture(draftId: string, textureName: string, pngBase64: string): Promise<{ glbUrl: string | null }> {
    await sleep(120);
    const d = this._drafts[draftId];
    const tex = d?.textures.find(t => t.name === textureName);
    if (tex) tex.dataUrl = 'data:image/png;base64,' + pngBase64;
    return { glbUrl: null };
  }
  async workshopSaveDraft(draftId: string): Promise<void> { await sleep(150); void draftId; }
  async workshopApplyToGame(draftId: string): Promise<void> { await sleep(1400); void draftId; }
  async workshopPublish(draftId: string, meta: WorkshopPublishMeta, _ownerUserId?: string, _ownerName?: string): Promise<CustomGun> {
    await sleep(600);
    const mineCount = this._customGuns.filter(g => g.mine).length;
    const d = this._drafts[draftId];
    if (d?.customGunId) {
      const g = this._customGuns.find(x => x.id === d.customGunId)!;
      Object.assign(g, meta); g.updatedAt = new Date().toISOString();
      return g;
    }
    if (mineCount >= this._customMax)
      throw new Error(`Достигнут лимит: ${this._customMax} скинов на аккаунт. Удали один или оформи премиум.`);
    const now = new Date().toISOString();
    const gun: CustomGun = {
      id: 'cg_' + randomChars(10),
      ownerId: 'me', ownerName: 'Вы',
      baseName: d?.baseName ?? 'carbinerifle',
      weaponPrefix: d?.weaponPrefix ?? 'w_ar_',
      internalName: (d?.weaponPrefix ?? 'w_ar_') + (d?.baseName ?? 'carbinerifle'),
      displayName: meta.displayName, description: meta.description, category: meta.category,
      glbUrl: null,
      previewUrl: 'https://cdn.majestic-files.net/public/master/static/img/inventory/items/275.webp',
      downloadCount: 0, createdAt: now, updatedAt: now, mine: true,
      status: 'pending', submittedForReview: true, reviewedAt: null, rejectReason: null,
    };
    this._customGuns.unshift(gun);
    return gun;
  }

  async adminGunpackList(): Promise<Gunpack[]> {
    await sleep(120);
    return [...this._gunpacks];
  }
  async adminGunpackPatch(id: string, patch: GunpackPatch): Promise<void> {
    await sleep(80);
    const p = this._gunpacks.find(x => x.id === id);
    if (!p) return;
    Object.assign(p, patch);
    p.updatedAt = new Date().toISOString();
  }
  async adminGunpackDelete(id: string): Promise<void> {
    await sleep(80);
    this._gunpacks = this._gunpacks.filter(x => x.id !== id);
    delete this._gunpackGuns[id];
  }
  async adminGunpackGunPatch(gunId: string, patch: GunpackGunPatch): Promise<void> {
    await sleep(60);
    for (const arr of Object.values(this._gunpackGuns)) {
      const g = arr.find(x => x.id === gunId);
      if (g) { Object.assign(g, patch); return; }
    }
  }
  async adminGunpackGunDelete(gunId: string): Promise<void> {
    await sleep(60);
    for (const k of Object.keys(this._gunpackGuns)) {
      this._gunpackGuns[k] = this._gunpackGuns[k].filter(g => g.id !== gunId);
    }
  }

  async gunpackVariantsList(gunpackId: string): Promise<GunpackVariant[]> {
    await sleep(40);
    const pack = this._gunpacks.find(p => p.id === gunpackId);
    if (!pack) return [];
    return [{
      id:               `mock-default-${gunpackId}`,
      gunpackId,
      name:             'Default',
      weaponsRpfUrl:    pack.weaponsRpfUrl,
      weaponsRpfSize:   pack.weaponsRpfSize,
      weaponsRpfSha256: pack.weaponsRpfSha256,
      packZipUrl:       pack.packZipUrl,
      packZipSize:      pack.packZipSize,
      packZipSha256:    pack.packZipSha256,
      manifestUrl:      pack.manifestUrl,
      coverUrl:         null,
      isDefault:        true,
      sortOrder:        0,
      createdAt:        pack.uploadedAt,
      updatedAt:        pack.updatedAt,
    }];
  }
  async adminGunpackVariantPatch(_variantId: string, _patch: GunpackVariantPatch): Promise<void> {
    await sleep(30);
  }
  async adminGunpackVariantDelete(_variantId: string): Promise<void> {
    await sleep(30);
  }
  async adminGunpackVariantSetDefault(_variantId: string): Promise<void> {
    await sleep(30);
  }
  async adminGunpackVariantUpload(packId: string, name: string, _sourceRpfPath: string, _coverImagePath?: string): Promise<GunpackQueueItem> {
    await sleep(500);
    const tempId = `mock-variant-${crypto.randomUUID().slice(0, 8)}`;
    const variantId = crypto.randomUUID();
    const now = new Date().toISOString();
    const pack = this._gunpacks.find(p => p.id === packId);
    const variantName = name.trim() || 'New variant';
    const item: GunpackQueueItem = {
      tempId,
      status: 'pending',
      addedAt: now,
      sourceDlcRpfPath: _sourceRpfPath,
      tempWorkDir: `C:\\mock\\workdir\\${tempId}`,
      uploadToR2: true,
      percent: 0,
      currentPhase: null,
      errorMessage: null,
      warnings: null,
      metadata: {
        id: variantId,
        name: `${pack?.name ?? packId} / ${variantName}`,
        author: pack?.author ?? null,
        authorLink: pack?.authorLink ?? null,
        description: pack?.description ?? null,
        weaponsRpfUrl: '', weaponsRpfSize: 0, weaponsRpfSha256: '',
        packZipUrl: null, packZipSize: null, packZipSha256: null,
        manifestUrl: null, coverKind: 'image', coverUrl: null,
        galleryUrls: [], status: 'published', isVerified: false,
        viewerPriority: 0, downloadCount: 0,
        uploadedAt: now, uploadedBy: null, updatedAt: now, notes: null,
      },
    };
    return item;
  }

  async adminGunpackUpload(req: GunpackUploadRequest): Promise<GunpackQueueItem> {
    await sleep(100);
    const tempId = `mock-${crypto.randomUUID().slice(0, 8)}`;
    const id = req.metadata.id || crypto.randomUUID();
    const now = new Date().toISOString();
    const meta: Gunpack = {
      ...req.metadata,
      id,
      uploadedAt: now,
      updatedAt: now,
      weaponsRpfUrl: `https://mock.r2/gunpacks/${id}/pack/hunter_weapon.rpf`,
      weaponsRpfSize: 4_500_000,
      weaponsRpfSha256: 'mock-sha-' + id.slice(0, 12),
      packZipUrl: `https://mock.r2/gunpacks/${id}/pack/pack.zip`,
      packZipSize: 18_000_000,
      packZipSha256: 'mock-sha-' + id.slice(0, 12),
      manifestUrl: `https://mock.r2/gunpacks/${id}/manifest.json`,
    };
    const item: GunpackQueueItem = {
      tempId,
      metadata: meta,
      sourceDlcRpfPath: req.sourceDlcRpfPath,
      tempWorkDir: `C:\\mock\\workdir\\${tempId}`,
      uploadToR2: req.uploadToR2,
      status: 'pending',
      percent: 0,
      currentPhase: null,
      errorMessage: null,
      addedAt: now,
      warnings: null,
    };
    this._gunpackQueue.push(item);

    void (async () => {
      const phases: { phase: NonNullable<GunpackQueueItem['currentPhase']>; pct: number }[] = [
        { phase: 'analyzing',   pct: 5 },
        { phase: 'parsing',     pct: 22 },
        { phase: 'filtering',   pct: 30 },
        { phase: 'converting',  pct: 50 },
        { phase: 'compressing', pct: 65 },
        { phase: 'rendering',   pct: 83 },
        { phase: 'packing',     pct: 88 },
        { phase: 'uploading',   pct: 97 },
        { phase: 'registering', pct: 99 },
      ];
      item.status = 'processing';
      for (const { phase, pct } of phases) {
        await sleep(450);
        item.currentPhase = phase;
        item.percent = pct;
        this.bus.emit('admin:gunpackQueueProgress', { ...item });
      }

      item.status = 'done';
      item.currentPhase = null;
      item.percent = 100;
      this._gunpacks.push(meta);
      this._gunpackGuns[id] = [
        { id: crypto.randomUUID(), gunpackId: id, baseName: 'carbinerifle',  weaponPrefix: 'w_ar_', category: 'assault', displayName: 'Карабинная винтовка',  glbUrl: null, previewUrl: null, files: ['w_ar_carbinerifle.ydr', 'w_ar_carbinerifle_hi.ydr', 'w_ar_carbinerifle.ytd'], sizeBytes: 524288, isHidden: false, sortOrder: 100, createdAt: now },
        { id: crypto.randomUUID(), gunpackId: id, baseName: 'heavysniper',   weaponPrefix: 'w_sr_', category: 'sniper',  displayName: 'Тяжёлая снайперская винтовка', glbUrl: null, previewUrl: null, files: ['w_sr_heavysniper.ydr', 'w_sr_heavysniper.ytd'], sizeBytes: 432128, isHidden: false, sortOrder: 110, createdAt: now },
        { id: crypto.randomUUID(), gunpackId: id, baseName: 'minismg',       weaponPrefix: 'w_sb_', category: 'smg',     displayName: 'Мини ПП',                       glbUrl: null, previewUrl: null, files: ['w_sb_minismg.ydr', 'w_sb_minismg.ytd'], sizeBytes: 312540, isHidden: false, sortOrder: 50, createdAt: now },
      ];
      this.bus.emit('admin:gunpackQueueProgress', { ...item });
    })();

    return { ...item };
  }

  async adminGunpackQueueList(): Promise<GunpackQueueItem[]> {
    await sleep(60);
    return this._gunpackQueue.map(x => ({ ...x }));
  }
  async adminGunpackQueueRemove(tempId: string): Promise<void> {
    await sleep(40);
    this._gunpackQueue = this._gunpackQueue.filter(x => x.tempId !== tempId);
  }

  async gunpackCheckInstallConflicts(_gunpackId: string): Promise<GunpackInstallConflict[]> {
    await sleep(80);
    return [];
  }

  async gunpackInstallAll(id: string, _perGunResolutions: Record<string, string> = {}, _variantId?: string): Promise<InjectResult> {

    const phases: { phase: 'starting'|'resolving_version'|'downloading_template'|'downloading_pack'|'preparing'|'installing'|'registering'|'done'; pct: number; detail?: string }[] = [
      { phase: 'starting',             pct: 0,   detail: null as unknown as string },
      { phase: 'resolving_version',    pct: 5,   detail: 'Версия GTA: 1.0.3788.0' },
      { phase: 'downloading_template', pct: 30,  detail: 'чистый patchday18ng/dlc.rpf: готово' },
      { phase: 'downloading_pack',     pct: 65,  detail: 'hunter_weapon.rpf: готово' },
      { phase: 'preparing',            pct: 67,  detail: 'Готовлю gunpack_info.json' },
      { phase: 'installing',           pct: 90,  detail: 'Сборка patchday18ng/dlc.rpf' },
      { phase: 'registering',          pct: 97,  detail: null as unknown as string },
      { phase: 'done',                 pct: 100, detail: null as unknown as string },
    ];
    for (const p of phases) {
      await sleep(350);
      this.bus.emit('gunpack:installProgress', {
        gunpackId:     id,
        phase:         p.phase,
        percent:       p.pct,
        errorMessage:  null,
        detailMessage: p.detail ?? null,
      });
    }

    const pack = this._gunpacks.find(p => p.id === id);
    this._installedGunpack = {
      activeGunpackId:   id,
      activeGunpackName: pack?.name ?? 'Mock pack',
      weaponsRpfSha256:  'mock-sha-' + id.slice(0, 12),
      installedAt:       new Date().toISOString(),
    };
    return { success: true, errorMessage: null, workDir: 'C:\\mock\\workdir' };
  }
  async gunpackInstallSelected(_id: string, _ids: string[]): Promise<InjectResult> {
    await sleep(800);
    return { success: true, errorMessage: null, workDir: 'C:\\mock\\workdir' };
  }
  async gunpackUninstall(): Promise<boolean> {
    const phases: { phase: 'starting'|'restoring'|'done'; pct: number }[] = [
      { phase: 'starting',  pct: 0 },
      { phase: 'restoring', pct: 50 },
      { phase: 'done',      pct: 100 },
    ];
    for (const p of phases) {
      await sleep(250);
      this.bus.emit('gunpack:installProgress', {
        gunpackId: '', phase: p.phase, percent: p.pct,
        errorMessage: null, detailMessage: null,
      });
    }
    this._installedGunpack = { activeGunpackId: null, activeGunpackName: null, weaponsRpfSha256: null, installedAt: null };
    return true;
  }

  private _installedGunpack: GunpackInstalledState = {
    activeGunpackId: null, activeGunpackName: null, weaponsRpfSha256: null, installedAt: null,
  };
  private _selectedGuns: SelectedGun[] = [];

  async gunpackGetInstalledState(): Promise<GunpackInstalledState> {
    await sleep(50);
    return { ...this._installedGunpack };
  }
  async gunpackVerifyInstalled(): Promise<GunpackVerifyReport> {
    await sleep(80);
    const has = !!this._installedGunpack.activeGunpackId;
    return {
      ok: true,
      targetDlcExists: has,
      rpfPresentInDlc: has,
      stateSha:  this._installedGunpack.weaponsRpfSha256,
      actualSha: this._installedGunpack.weaponsRpfSha256,
      summary: has ? `Mock OK: ${this._installedGunpack.activeGunpackName}` : 'Ничего не установлено.',
    };
  }
  async reconcileInstallState(): Promise<boolean> {

    await sleep(40);
    return false;
  }
  async selectedGunsList(): Promise<SelectedGun[]> {
    await sleep(50);
    return this._selectedGuns.map(g => ({ ...g }));
  }
  async selectedGunsIsInstalled(internalName: string): Promise<boolean> {
    return this._selectedGuns.some(g => g.internalName.toLowerCase() === internalName.toLowerCase());
  }
  async selectedGunsInstall(gunpackId: string, internalName: string): Promise<InjectResult> {
    await sleep(300);
    const pack = this._gunpacks.find(p => p.id === gunpackId);
    const guns = this._gunpackGuns[gunpackId] ?? [];
    const gun = guns.find(g => (g.weaponPrefix + g.baseName).toLowerCase() === internalName.toLowerCase());
    if (!pack || !gun) return { success: false, errorMessage: 'mock: pack/gun not found', workDir: null };
    this._selectedGuns = this._selectedGuns.filter(s => s.internalName.toLowerCase() !== internalName.toLowerCase());
    this._selectedGuns.push({
      gunpackId: pack.id, gunpackName: pack.name, gunId: gun.id, internalName,
      displayName: gun.displayName ?? gun.baseName, baseName: gun.baseName,
      weaponPrefix: gun.weaponPrefix, files: gun.files,
      packZipUrl: pack.packZipUrl ?? '', packZipSha256: pack.packZipSha256 ?? '',
      selectedAt: new Date().toISOString(),
    });
    this.bus.emit('selectedguns:installProgress', {
      internalName, phase: 'done', percent: 100, errorMessage: null, detailMessage: 'mock: ok',
    });
    return { success: true, errorMessage: null, workDir: null };
  }
  async selectedGunsRemove(internalName: string): Promise<InjectResult> {
    await sleep(150);
    this._selectedGuns = this._selectedGuns.filter(g => g.internalName.toLowerCase() !== internalName.toLowerCase());
    return { success: true, errorMessage: null, workDir: null };
  }
  async selectedGunsRebuild(): Promise<InjectResult> {
    await sleep(200);
    return { success: true, errorMessage: null, workDir: null };
  }
  async selectedGunsUninstallAll(): Promise<InjectResult> {
    await sleep(200);
    this._selectedGuns = [];
    return { success: true, errorMessage: null, workDir: null };
  }
  async selectedGunsVerify(): Promise<SelectedGunsVerifyReport> {
    await sleep(80);
    return {
      ok: true,
      stateGunsCount: this._selectedGuns.length,
      targetDlcExists: !!this._installedGunpack.activeGunpackId,
      rpfPresentInDlc: this._selectedGuns.length > 0,
      stateSha: null, actualSha: null,
      summary: `Mock: ${this._selectedGuns.length} selected.`,
    };
  }

  private _gtaPresets: GtaPreset[] = [
    {
      id: 'mock-blago-tournament',
      name: 'Blago Tournament 240Hz',
      description: 'Турнирный конфиг от Blago. 1728x1080, 240Hz, тени отключены, MSAA off.',
      author: 'Blago',
      xmlUrl: 'https://example.invalid/mock/blago.xml',
      xmlSizeBytes: 4096,
      xmlSha256: '0000000000000000000000000000000000000000000000000000000000000001',
      expectedFpsLow: 200,
      expectedFpsHigh: 240,
      baselineHwLabel: 'i5-13600KF + RTX 4070',
      computedGainPercent: 38,
      cpuBias: 'cpu',
      isTournament: true,
      status: 'published',
      viewerPriority: 100,
      downloadCount: 1247,
      uploadedBy: 'admin',
      uploadedAt: new Date(Date.now() - 86_400_000 * 7).toISOString(),
      updatedAt:  new Date(Date.now() - 86_400_000 * 1).toISOString(),
    },
    {
      id: 'mock-balanced',
      name: 'Balanced 144Hz',
      description: 'Сбалансированный конфиг для основной массы машин - 1080p 144Hz, средние тени.',
      author: 'Hunter Graphics',
      xmlUrl: 'https://example.invalid/mock/balanced.xml',
      xmlSizeBytes: 4096,
      xmlSha256: '0000000000000000000000000000000000000000000000000000000000000002',
      expectedFpsLow: 130,
      expectedFpsHigh: 165,
      baselineHwLabel: 'Ryzen 5 + RTX 5060',
      computedGainPercent: 22,
      cpuBias: 'balanced',
      isTournament: false,
      status: 'published',
      viewerPriority: 80,
      downloadCount: 642,
      uploadedBy: 'admin',
      uploadedAt: new Date(Date.now() - 86_400_000 * 14).toISOString(),
      updatedAt:  new Date(Date.now() - 86_400_000 * 2).toISOString(),
    },
    {
      id: 'mock-low-spec',
      name: 'Low-spec rescue',
      description: 'Минимум всего для слабых машин. Население 0, тени off, MSAA off, particles -1.',
      author: 'Hunter Graphics',
      xmlUrl: 'https://example.invalid/mock/lowspec.xml',
      xmlSizeBytes: 4096,
      xmlSha256: '0000000000000000000000000000000000000000000000000000000000000003',
      expectedFpsLow: 90,
      expectedFpsHigh: 120,
      baselineHwLabel: 'i3 13gen + RTX 3050 Ti',
      computedGainPercent: 43,
      cpuBias: 'cpu',
      isTournament: false,
      status: 'published',
      viewerPriority: 60,
      downloadCount: 219,
      uploadedBy: 'admin',
      uploadedAt: new Date(Date.now() - 86_400_000 * 30).toISOString(),
      updatedAt:  new Date(Date.now() - 86_400_000 * 5).toISOString(),
    },
  ];

  private _userBuilds: UserBuildDto[] = [];

  private _profileDefaults(): Pick<UserBuildDto,
      'devicesJson'|'sensitivity'|'dpi'|'resolution'|'videoUrl'|'settingsXmlUrl'
      |'description'|'tier'|'status'|'submittedForReview'|'reviewedBy'|'reviewedAt'|'rejectReason'> {
    return {
      devicesJson: null, sensitivity: null, dpi: null, resolution: null,
      videoUrl: null, settingsXmlUrl: null, description: '', tier: null,

      status: 'approved', submittedForReview: false,
      reviewedBy: null, reviewedAt: null, rejectReason: null,
    };
  }

  async userBuildsList(search?: string | null, authorUserId?: string | null): Promise<UserBuildDto[]> {
    await sleep(80);

    let list = this._userBuilds.filter(b => b.status === 'approved');
    if (authorUserId) list = list.filter(b => b.authorUserId === authorUserId);
    if (search && search.trim()) {
      const q = search.trim().toLowerCase();
      list = list.filter(b =>
        b.name.toLowerCase().includes(q)
     || b.authorUsername.toLowerCase().includes(q)
     || b.hntCode.toLowerCase().includes(q));
    }
    return list.sort((a, b) => (b.createdAt ?? '').localeCompare(a.createdAt ?? ''));
  }
  async userBuildGet(id: string): Promise<UserBuildDto | null> {
    await sleep(60);
    return this._userBuilds.find(b => b.id === id) ?? null;
  }
  async userBuildGetByHntCode(hntCode: string): Promise<UserBuildDto | null> {
    await sleep(60);
    return this._userBuilds.find(b => b.hntCode === hntCode) ?? null;
  }
  async userBuildCreate(dto: UserBuildDto): Promise<UserBuildDto> {
    await sleep(120);
    const saved: UserBuildDto = {
      ...this._profileDefaults(),
      ...dto,
      id: dto.id || `mock-build-${Date.now().toString(36)}`,
      createdAt: dto.createdAt || new Date().toISOString(),
      updatedAt: new Date().toISOString(),
    };
    this._userBuilds = [saved, ...this._userBuilds.filter(b => b.id !== saved.id)];
    return saved;
  }
  async userBuildDelete(id: string): Promise<void> {
    await sleep(60);
    this._userBuilds = this._userBuilds.filter(b => b.id !== id);
  }
  async userBuildIncrementDownloads(id: string): Promise<number> {
    await sleep(40);
    const b = this._userBuilds.find(x => x.id === id);
    if (!b) return 0;
    b.downloadCount += 1;
    return b.downloadCount;
  }
  async userBuildIncrementViews(id: string): Promise<number> {
    await sleep(30);
    const b = this._userBuilds.find(x => x.id === id);
    if (!b) return 0;
    b.viewCount = (b.viewCount ?? 0) + 1;
    return b.viewCount;
  }

  async userBuildSubmit(dto: UserBuildDto): Promise<UserBuildDto> {
    await sleep(140);

    const saved: UserBuildDto = {
      ...this._profileDefaults(),
      ...dto,
      id: dto.id || `mock-build-${Date.now().toString(36)}`,
      createdAt: dto.createdAt || new Date().toISOString(),
      updatedAt: new Date().toISOString(),
      status: 'pending',
      submittedForReview: true,
      reviewedBy: null,
      reviewedAt: null,
      rejectReason: null,
    };
    this._userBuilds = [saved, ...this._userBuilds.filter(b => b.id !== saved.id)];
    return saved;
  }

  async userBuildUpdate(id: string, patch: Partial<UserBuildDto>): Promise<UserBuildDto> {
    await sleep(80);
    const idx = this._userBuilds.findIndex(b => b.id === id);
    if (idx === -1) throw new Error('build not found');
    const merged = { ...this._userBuilds[idx], ...patch, id, updatedAt: new Date().toISOString() };
    this._userBuilds[idx] = merged;
    return merged;
  }

  async userBuildUploadSettingsXml(buildId: string, _sourceXmlPath: string): Promise<string> {
    await sleep(220);

    return `https://mock-r2.invalid/hntgraph-user-builds/user-builds/${buildId}/settings.xml`;
  }

  async userBuildUploadCover(_sourcePath: string): Promise<string> {
    await sleep(220);

    const fakeGuid = Math.random().toString(36).slice(2, 14);
    return `https://mock-r2.invalid/hntgraph-user-builds/user-builds/covers/${fakeGuid}.png`;
  }

  async adminUploadComponentScreenshot(reduxId: string, component: string, _sourcePath: string): Promise<string> {
    await sleep(180);
    return `https://mock-r2.invalid/redux/${reduxId}/screenshots/${component}.png`;
  }

  async adminMirrorImageToR2(reduxId: string, externalUrl: string, slot: string): Promise<string> {
    await sleep(120);
    const host = (() => { try { return new URL(externalUrl).host; } catch { return ''; } })();
    if (host.endsWith('miamigraphicsstorage.uk') || host.endsWith('.r2.dev') || host.endsWith('.r2.cloudflarestorage.com')) {
      return externalUrl;
    }
    return `https://mock-r2.invalid/redux/${reduxId}/mirror/${slot}.jpg`;
  }

  async adminUploadLibraryPreview(libraryId: string, _sourcePath: string): Promise<string> {
    await sleep(180);
    return `https://mock-r2.invalid/library/preview/${libraryId}.png`;
  }
  async getCurrentMinimapInfo() { await sleep(40); return null; }
  async getInstalledDraft() { await sleep(40); return this._installedDraft; }
  private _donorPicks = new Map<string, number>();
  async donorPickCounts(component: string): Promise<Record<string, number>> {
    await sleep(60);
    const out: Record<string, number> = {};
    for (const [k, v] of this._donorPicks) {
      const [comp, id] = k.split('::');
      if (comp === component) out[id] = v;
    }
    return out;
  }
  async donorPickIncrement(donorReduxId: string, component: string): Promise<number> {
    await sleep(40);
    const k = `${component}::${donorReduxId}`;
    const next = (this._donorPicks.get(k) ?? 0) + 1;
    this._donorPicks.set(k, next);
    return next;
  }
  async getCurrentReduxId() { await sleep(40); return ''; }
  async reduxApplyMinimap(_source: 'redux' | 'library', _id: string, _displayName?: string) {
    await sleep(800);
    return { success: true, errorMessage: null, workDir: null };
  }
  async timecycleInstall(_donorReduxId: string, _displayName?: string, _donorVersionId?: string | null) {
    await sleep(800);
    return { success: true, errorMessage: null, workDir: null };
  }
  async getCurrentTimecycleInfo() { await sleep(40); return null; }
  async timecycleRestoreVanilla() {
    await sleep(600);
    return { success: true, errorMessage: null, workDir: null };
  }
  async treesInstall(_treeId: string, _displayName?: string) {
    await sleep(800);
    return { success: true, errorMessage: null, workDir: null };
  }
  async getCurrentTreesInfo() { await sleep(40); return null; }
  async treesRestore() {
    await sleep(600);
    return { success: true, errorMessage: null, workDir: null };
  }
  async roadsInstall(_roadId: string, _displayName?: string) {
    await sleep(800);
    return { success: true, errorMessage: null, workDir: null };
  }
  async getCurrentRoadsInfo() { await sleep(40); return null; }
  async roadsRestore() {
    await sleep(600);
    return { success: true, errorMessage: null, workDir: null };
  }
  private _roadsFixApplied = false;
  async getRoadsFixStatus() {
    await sleep(60);
    return { vendor: 'nvidia', applied: this._roadsFixApplied, detectable: true, detail: 'mock' };
  }
  async roadsFixApply() {
    await sleep(700);
    this._roadsFixApplied = true;
    return { success: true, errorMessage: null, workDir: null };
  }
  private _graphicsMods: Record<string, { name: string; variantLabel: string }> = {};
  async graphicsModRestore(modId: string) {
    await sleep(500);
    delete this._graphicsMods[modId];
    return { success: true, errorMessage: null, workDir: null };
  }
  async getInstalledGraphicsMods() {
    await sleep(40);
    return Object.entries(this._graphicsMods).map(([id, v]) => ({ id, name: v.name, variantLabel: v.variantLabel }));
  }
  private _mockLayout: { ratio: string; placement: string; transparent: boolean; posX?: number | null; posY?: number | null } =
    { ratio: '16:9', placement: 'default', transparent: false };
  async minimapLayoutGet() { return { ...this._mockLayout }; }
  async minimapLayoutApply(ratio: string, placement: string, transparent: boolean) {
    await sleep(800);
    this._mockLayout = { ...this._mockLayout, ratio, placement, transparent };
    return { success: true, errorMessage: null, workDir: null };
  }
  async fileToDataUrl(_path: string) { return null as string | null; }
  private _mockTweaks: import('./types').MinimapTweaks | null = null;
  async minimapGetTweaks() { return this._mockTweaks; }
  private _mockMinimapSave: import('./types').MinimapSave | null = null;
  async minimapGetSave() { return this._mockMinimapSave; }
  async minimapWriteSave(name: string, tweaks: import('./types').MinimapTweaks) {
    this._mockMinimapSave = { name: name || 'Моя миникарта', savedAt: new Date().toISOString(), tweaks };
    return this._mockMinimapSave;
  }
  async minimapClearSave() { this._mockMinimapSave = null; }
  private _mockFont: import('./types').MinimapFontState = { installed: false, slot: null, sourceFile: null };
  async minimapGetFontState() { return this._mockFont; }
  async minimapGetFontOptions(): Promise<import('./types').MinimapFontOption[]> {
    return [
      { id: 'stock',       title: 'Стоковый (как в игре)',  face: '' },
      { id: 'chaletcond',  title: 'Узкий (как сейчас)',     face: 'ChaletComprime-CologneSixty' },
      { id: 'chalet',      title: 'Основной HUD',           face: 'Chalet-LondonNineteenSixty' },
      { id: 'leaderboard', title: 'Как в таблице лидеров',  face: 'GTAV LeaderBoard' },
      { id: 'fixednum',    title: 'Цифры фикс. ширины',     face: 'ChaletLondonNineteenSixtyNumbers' },
    ];
  }
  private _mockFp = 1;
  async otherGetArchiveFingerprint() { return 'mock-fp-' + this._mockFp; }
  private _mockHotSwap: import('./types').HotSwapStatus = {
    enabled: false, supported: true, frozen: false, armed: false,
    agentAlive: false, imageRoot: 'E:\\MiamiGraphics\\hotswap', note: null,
    method: 1, manualTrigger: false,
    stale: false, staleNote: null, staleAtUtc: null,
  };
  async hotSwapGetStatus() { return this._mockHotSwap; }
  async hotSwapSetEnabled(enabled: boolean, method?: number) {
    await new Promise(r => setTimeout(r, 800));
    const m = enabled ? (method ?? this._mockHotSwap.method) : this._mockHotSwap.method;
    const manual = m === 3 || m === 4;
    this._mockHotSwap = {
      ...this._mockHotSwap, enabled, frozen: enabled,
      agentAlive: enabled && !manual,
      armed: enabled ? this._mockHotSwap.armed : false,
      method: m, manualTrigger: manual,
    };
    return { success: true, errorMessage: null, workDir: null };
  }
  async hotSwapArmNow() {
    await new Promise(r => setTimeout(r, 600));
    this._mockHotSwap = { ...this._mockHotSwap, armed: true };
    return { success: true, errorMessage: null, workDir: null };
  }
  async hotSwapDisarmNow() {
    await new Promise(r => setTimeout(r, 600));
    this._mockHotSwap = { ...this._mockHotSwap, armed: false };
    return { success: true, errorMessage: null, workDir: null };
  }
  async hotSwapRebuild() {
    await new Promise(r => setTimeout(r, 1200));
    this._mockHotSwap = {
      ...this._mockHotSwap,
      enabled: false, frozen: false, armed: false, agentAlive: false,
      stale: false, staleNote: null, staleAtUtc: null, note: null,
    };
    return { success: true, errorMessage: null, workDir: null };
  }
  async featureGetLog(_tailKb?: number): Promise<import('./types').HotSwapLogTail> {
    await new Promise(r => setTimeout(r, 200));
    return {
      path: 'C:\\Users\\mock\\AppData\\Local\\MiamiGraphics\\logs\\features.log',
      text: [
        '2026-08-11 01:10:04.201 [прицел] Прицел: свой (KNK): starting 2% - Собираю прицел...',
        '2026-08-11 01:10:06.880 [прицел] reticle.custom: по содержимому заменено копий 3, перешифровка ок. Пути: x64/patch/data/cdimages/scaleform_generic.rpf/hud_reticle.gfx',
        '2026-08-11 01:10:09.014 [прицел] Прицел: свой (KNK): done 100% - Готово.',
        '2026-08-11 01:12:31.402 [залазы] Добавление залазов: starting 5% - Готовлю залазы...',
        '2026-08-11 01:12:44.117 [залазы] Добавление залазов: done 100% - Готово.',
      ].join('\n'),
    };
  }

  async hotSwapGetLog(_tailKb?: number): Promise<import('./types').HotSwapLogTail> {
    await new Promise(r => setTimeout(r, 300));
    return {
      path: 'C:\\Users\\mock\\AppData\\Local\\MiamiGraphics\\logs\\hotswap.log',
      text: [
        '2026-08-05 14:00:01.120 [лаунчер:мост] SetEnabled(true): способ 1 (Агент следит сам, копии на диске игры), storeRoot (дефолт), gta E:\\GTA V, агент нужен: да',
        '2026-08-05 14:00:01.371 [лаунчер:freeze] старт: способ 1 (Агент следит сам, копии на диске игры), gta E:\\GTA V, storeRoot (дефолт), чистых источников 2',
        '2026-08-05 14:00:01.402 [лаунчер:store] привязка записана: способ 1, корень образа E:\\MiamiGraphics\\hotswap',
        '2026-08-05 14:00:01.455 [лаунчер:journal] фаза Idle -> Freezing',
        '2026-08-05 14:00:01.523 [лаунчер:freeze] update\\update.rpf: замораживаю (2147532800 байт, чистый источник: E:\\MG\\backup\\update.rpf)',
        '2026-08-05 14:01:14.812 [лаунчер:freeze] update\\update.rpf: готово за 73289 мс',
        '2026-08-05 14:01:15.020 [лаунчер:journal] фаза Freezing -> Idle',
        '2026-08-05 14:01:15.031 [лаунчер:freeze] готово: 1 файл(ов) [update\\update.rpf] за 73576 мс',
        '2026-08-05 14:01:16.220 [агент:агент] старт цикла: pid 20816, exe C:\\Program Files\\Miami Graphics\\Miami Graphics.exe',
        '2026-08-05 14:03:22.114 [агент:агент] обнаружена игра: GTA5 (pid 31544) - решение: армить',
        '2026-08-05 14:03:22.161 [агент:journal] фаза Idle -> Arming (pid игры 31544)',
        '2026-08-05 14:03:22.208 [агент:arm] update\\update.rpf: моды подставлены за 41 мс',
        '2026-08-05 14:03:22.240 [агент:journal] фаза Arming -> Armed (pid игры 31544)',
      ].join('\n'),
    };
  }
  async downloadGetLog(_tailKb?: number): Promise<import('./types').DownloadLogTail> {
    await new Promise(r => setTimeout(r, 300));
    return {
      path: 'C:\\Users\\mock\\AppData\\Local\\MiamiGraphics\\logs\\downloads.log',
      text: [
        '2026-08-06 13:59:58.410 [probe] проба зеркал: ru.miamigraphicsstorage.uk - 3.1 МБ/с, cdn.miamigraphicsstorage.uk - 0.4 МБ/с, miamigraphicsstorage.uk - молчит (таймаут/TLS/обрыв), rf.miamigraphicsstorage.uk - 5.2 МБ/с; выбрано ru.miamigraphicsstorage.uk; хранилище региона rf.miamigraphicsstorage.uk: пригодно, ставим первым',
        '2026-08-06 14:02:11.120 [download] старт redux_pack.zip (ключ /mods/redux/redux_pack.zip): кандидаты rf.miamigraphicsstorage.uk > cdn.miamigraphicsstorage.uk > pub-f3641b214c164277964c1e92c826b19b.r2.dev > ru.miamigraphicsstorage.uk; хранилище rf.miamigraphicsstorage.uk: по пробе пригодно',
        '2026-08-06 14:02:12.030 [chunk] rf.miamigraphicsstorage.uk: файла нет (404 на HEAD)',
        '2026-08-06 14:02:12.031 [download] переезд redux_pack.zip: файла нет на этом зеркале (rf.miamigraphicsstorage.uk, 404) - беру следующего кандидата',
        '2026-08-06 14:02:12.150 [hub] грант: узел hnt (hnt.miamigraphicsstorage.uk) отдаёт mods/redux/redux_pack.zip',
        '2026-08-06 14:03:31.480 [download] успех redux_pack.zip: hnt.miamigraphicsstorage.uk, 812.4 МБ за 79.3 с = 10.2 МБ/с (проход 1/3, кандидат 2/5)',
      ].join('\n'),
    };
  }
  async minimapInstallFont(path: string, slot?: string | null) {
    await new Promise(r => setTimeout(r, 600));
    this._mockFont = { installed: true, slot: slot ?? 'font_lib_efigs_pc.gfx', sourceFile: path.split(/[\\/]/).pop() ?? path };
    return { success: true, errorMessage: null, workDir: null };
  }
  async minimapRestoreFont() {
    await new Promise(r => setTimeout(r, 400));
    this._mockFont = { installed: false, slot: null, sourceFile: null };
    return { success: true, errorMessage: null, workDir: null };
  }
  async minimapApplyTweaks(tweaks: import('./types').MinimapTweaks) {
    this._mockFp++;
    await sleep(900);
    this._mockTweaks = tweaks;
    return { success: true, errorMessage: null, workDir: null };
  }
  async minimapLayoutGetPresets() {
    await sleep(120);
    const van = { posX: -0.0045, posY: 0.002, sizeX: 0.150, sizeY: 0.188888 };
    return (['16:9', '4:3', '1:1', '5:4', '5:3', '3:2'] as const).flatMap(ratio => ([
      { ratio, placement: 'default', ...van },
      { ratio, placement: 'center', posX: 0.4109, posY: -0.10, sizeX: van.sizeX, sizeY: van.sizeY },
    ]));
  }
  async minimapGetSafezone() {
    await sleep(80);
    return 3;
  }
  async minimapGetScreen() {
    await sleep(60);
    return { width: 1920, height: 1080, aspectRatio: 0, windowed: false, fromSettingsXml: true, settingsPath: 'C:\\Users\\mock\\Documents\\Rockstar Games\\GTA V\\settings.xml' };
  }
  async minimapLayoutApplyCustom(_ratio: string, posX: number, posY: number, transparent: boolean) {
    await sleep(800);
    this._mockLayout = { ...this._mockLayout, placement: 'custom', transparent, posX, posY };
    return { success: true, errorMessage: null, workDir: null };
  }
  private _mockRings: number[] = [];
  async minimapSetRangeRings(radiiMeters: number[]) {
    this._mockFp++;
    await sleep(600);
    this._mockRings = [...radiiMeters];
    return { success: true, errorMessage: null, workDir: null };
  }
  async minimapGetRangeRings() { return [...this._mockRings]; }
  async minimapDetectRings() { return this._mockRings.length > 0; }
  async minimapRestoreVanilla() {
    await sleep(600);
    this._mockRings = [];
    return { success: true, errorMessage: null, workDir: null };
  }
  private _mockZalazy = false;
  private _mockZalazyServer: 'gta5rp' | 'majestic' = 'gta5rp';
  async otherSetZalazy(enabled: boolean, server: 'gta5rp' | 'majestic') {
    await sleep(600);
    this._mockZalazy = enabled;
    this._mockZalazyServer = server;
    return { success: true, errorMessage: null, workDir: null };
  }
  async otherGetZalazy() { return { enabled: this._mockZalazy, server: this._mockZalazyServer }; }
  async otherDetectOverlays() { return { foreignZalazy: false, foreignGreenZone: false, foreignBackpack: false }; }
  async otherRemoveForeignOverlay(_kind: 'zalazy' | 'greenzone' | 'backpack') {
    await sleep(800);
    return { success: true, errorMessage: null, workDir: null };
  }
  private _mockBigMapId: string | null = null;
  private static readonly _mockBigMaps: import('./types').BigMap[] = [
    {
      id: 'dinero', name: 'DINERO', author: 'dineromods', authorLink: '',
      description: 'Векторная карта by DINERO. Чёткие улицы, читаемые районы.',
      previewUrl: '', galleryUrls: [], videoUrl: '',
      supportedServers: ['majestic'], sizeBytes: 10_480_093, packFormat: 'A',
      downloadCount: 128, isVerified: true,
    },
    {
      id: 'gucci-5rp', name: 'GUCCI 5 RP', author: 'gucci', authorLink: '',
      description: 'Векторка для 5RP (mirz-формат).',
      previewUrl: '', galleryUrls: [], videoUrl: '',
      supportedServers: ['gta5rp'], sizeBytes: 18_329_954, packFormat: 'E',
      downloadCount: 42, isVerified: false,
    },
  ];
  async bigMapList() { await sleep(300); return MockBridge._mockBigMaps; }
  async bigMapGetState(): Promise<import('./types').BigMapState> {
    const cur = MockBridge._mockBigMaps.find(m => m.id === this._mockBigMapId) ?? null;
    return { enabled: !!cur, id: cur?.id ?? null, name: cur?.name ?? null, foreignDetected: false };
  }
  async bigMapPreviewGlb(_id: string): Promise<string | null> {
    await sleep(1500);
    return 'http://localhost:8917/minimap_full.glb';
  }
  async bigMapInstall(id: string) {
    await sleep(1200);
    this._mockBigMapId = id;
    return { success: true, errorMessage: null, workDir: null };
  }
  async bigMapUninstall() {
    await sleep(800);
    this._mockBigMapId = null;
    return { success: true, errorMessage: null, workDir: null };
  }
  private _mockBigMapReviews: import('./types').BigMapReview[] = [
    {
      id: 'bmr-1', mapId: 'dinero-mock', userId: 'u2', username: 'tester',
      role: 'User', avatarUrl: null, rating: 5, body: 'Отличная векторка, всё видно.',
      createdAt: new Date(Date.now() - 86_400_000).toISOString(),
    },
  ];
  async bigMapReviewsList(mapId: string) {
    await sleep(250);
    return this._mockBigMapReviews.filter(r => r.mapId === mapId);
  }
  async bigMapReviewSubmit(
    mapId: string, userId: string, username: string, _role: string,
    avatarUrl: string | null, rating: number, body: string,
  ): Promise<import('./types').BigMapReview> {
    await sleep(350);
    const fresh: import('./types').BigMapReview = {
      id: 'bmr-' + Math.random().toString(36).slice(2, 8), mapId, userId,
      username: username || 'you', role: 'User', avatarUrl, rating, body,
      createdAt: new Date().toISOString(),
    };
    this._mockBigMapReviews = [fresh, ...this._mockBigMapReviews.filter(r => !(r.mapId === mapId && r.userId === userId))];
    return fresh;
  }
  async bigMapReviewDelete(reviewId: string, _userId: string, _role: string) {
    await sleep(200);
    this._mockBigMapReviews = this._mockBigMapReviews.filter(r => r.id !== reviewId);
    return true;
  }
  async bigMapRatingsAggregate() {
    await sleep(150);
    const agg: Record<string, { avg: number; count: number }> = {};
    for (const r of this._mockBigMapReviews) {
      const a = (agg[r.mapId] ??= { avg: 0, count: 0 });
      a.avg = (a.avg * a.count + r.rating) / (a.count + 1);
      a.count++;
    }
    return agg;
  }
  async adminBigMapAnalyze(_sourcePath: string): Promise<import('./types').BigMapAnalysis> {
    await sleep(500);
    return {
      installable: true, packFormat: 'A',
      targetPaths: ['x64/levels/gta5/minimap.rpf', 'x64/data/tune/minimap.ymt'],
      warnings: [], photoPaths: [], totalBytes: 10_480_093,
    };
  }
  async adminBigMapPublish(req: import('./types').BigMapPublishRequest): Promise<import('./types').BigMap> {
    await sleep(900);
    return {
      id: req.existingId ?? 'mock-' + Date.now(), name: req.name, author: req.author,
      authorLink: req.authorLink, description: req.description, previewUrl: '',
      galleryUrls: [], videoUrl: req.videoUrl ?? '', supportedServers: req.supportedServers,
      sizeBytes: 0, packFormat: 'A', downloadCount: 0, isVerified: false,
    };
  }
  async adminBigMapList() { return MockBridge._mockBigMaps; }
  async adminBigMapDelete(_id: string) { await sleep(200); }

  private _mockFastJoinUser = false;
  private _mockFastJoinRedux = true;
  async otherSetFastJoin(enabled: boolean) {
    await sleep(600);
    this._mockFastJoinUser = enabled;
    return { success: true, errorMessage: null, workDir: null };
  }
  async otherGetFastJoin() { return this._mockFastJoinUser || this._mockFastJoinRedux; }
  async otherGetFastJoinStatus() {
    return { active: this._mockFastJoinUser || this._mockFastJoinRedux, userInstalled: this._mockFastJoinUser };
  }
  async reduxBundledFeatures(_reduxId: string, _versionId?: string) {
    return { fastJoin: true, greenZone: false, zalazy: false, customMinimap: true };
  }
  private _mockGreenZone = false;
  async otherSetGreenZone(enabled: boolean) {
    await sleep(600);
    this._mockGreenZone = enabled;
    return { success: true, errorMessage: null, workDir: null };
  }
  async otherGetGreenZone() { return this._mockGreenZone; }
  private _mockCarLogos = false;
  async otherSetCarLogos(enabled: boolean) {
    await sleep(900);
    this._mockCarLogos = enabled;
    return { success: true, errorMessage: null, workDir: null };
  }
  async otherGetCarLogos() {
    return { installed: this._mockCarLogos, foreignPresent: false, foreignHits: [] as string[] };
  }
  private _mockRukzak = false;
  async otherSetRukzak(enabled: boolean) {
    await sleep(600);
    this._mockRukzak = enabled;
    return { success: true, errorMessage: null, workDir: null };
  }
  async otherGetRukzak() { return this._mockRukzak; }
  private _mockBackpack: 'vanilla' | 'removed' | 'foreign' = 'vanilla';
  async otherGetBackpackStatus() {
    return {
      state: this._mockBackpack,
      sizeBytes: this._mockBackpack === 'removed' ? 610060800 : 9955328,
      backupAvailable: false,
      legacyOverlay: false,
      gtaFound: true,
    };
  }
  async otherApplyBackpack(action: 'remove' | 'vanilla') {
    await sleep(900);
    this._mockBackpack = action === 'remove' ? 'removed' : 'vanilla';
    return { success: true, errorMessage: null, workDir: null };
  }
  private _mockSmoke = false;
  async otherSetSmoke(enabled: boolean) {
    await sleep(600);
    this._mockSmoke = enabled;
    return { success: true, errorMessage: null, workDir: null };
  }
  async otherGetSmoke() { return this._mockSmoke; }
  private _mockNoTracer = false;
  private _mockNoTracerCats: import('./types').NoTracerCategory[] = [];
  private _mockNoTracerKeepSnipers = false;
  async otherSetNoTracer(enabled: boolean, categories?: import('./types').NoTracerCategory[], keepSnipers?: boolean) {
    await sleep(600);
    this._mockNoTracer = enabled;
    this._mockNoTracerCats = enabled
      ? (categories && categories.length ? categories : ['normal', 'vehicle', 'mk2ammo'])
      : [];
    this._mockNoTracerKeepSnipers = enabled && !!keepSnipers;
    return { success: true, errorMessage: null, workDir: null };
  }
  async otherGetNoTracer(): Promise<import('./types').NoTracerState> {
    return { enabled: this._mockNoTracer, categories: this._mockNoTracerCats, keepSnipers: this._mockNoTracerKeepSnipers };
  }

  private _mockTracerStudio = '';
  async otherSetTracerStudio(settings?: string) {
    await sleep(900);
    this._mockTracerStudio = settings ?? '';
    return { success: true, errorMessage: null, workDir: null };
  }
  async otherGetTracerStudio(): Promise<import('./types').TracerStudioState> {
    return { enabled: this._mockTracerStudio.length > 0, settings: this._mockTracerStudio };
  }

  private _mockImprovements: import('./types').Improvement[] = [
    {
      id: 'ls2_gasstation', name: 'LS 2.0 - Gas Station', author: '',
      description: 'Брендинг MetaOil плюс новые колонки и голограммы. Уживается с Flower Field.',
      source: 'Network Graphics', exclusiveGroup: 'gas_stations', category: 'gas_stations',
      previewUrl: '', videoUrl: '', galleryUrls: [], sizeBytes: 98587623, installed: false,
      slots: ['mpvalentines2:/ntwstuff2.rpf (replace)', 'mpvalentines2:/ntwstuff3.rpf (replace)',
              'patchday17ng:/ntwstuff.rpf (merge)'],
      popularity: 3120,
    },
    {
      id: 'ls2_neon', name: 'LS 2.0 - Неоновые заправки', author: '',
      description: 'Неоновая подсветка заправок. Видно вечером и ночью.',
      source: 'Network Graphics', exclusiveGroup: 'gas_stations', category: 'gas_stations',
      previewUrl: '', videoUrl: '', galleryUrls: [], sizeBytes: 82229937, installed: false,
      slots: ['mpvalentines2:/ntwstuff2.rpf (replace)', 'mpvalentines2:/ntwstuff3.rpf (replace)'],
      popularity: 1840,
    },
    {
      id: 'flower_field', name: 'Flower Field', author: '',
      description: 'Цветочные поля за городом. Свои файлы кладутся в общий слот, поэтому уживается с любой заправкой.',
      source: 'Network Graphics', exclusiveGroup: '', category: 'misc',
      previewUrl: '', videoUrl: '', galleryUrls: [], sizeBytes: 47725812, installed: false,
      slots: ['patchday17ng:/ntwstuff.rpf (merge)'],
      popularity: 0,
    },
  ];

  async improvementsList(): Promise<import('./types').Improvement[]> {
    await sleep(160);
    return this._mockImprovements.map(x => ({ ...x }));
  }

  async improvementInstall(id: string): Promise<import('./types').InjectResult> {
    await sleep(900);
    const target = this._mockImprovements.find(x => x.id === id);
    if (!target) return { success: false, errorMessage: 'нет такого улучшения', workDir: null };
    const clash = this._mockImprovements.find(
      x => x.installed && x.id !== id && !!x.exclusiveGroup && x.exclusiveGroup === target.exclusiveGroup);
    if (clash)
      return { success: false, errorMessage: `Вместе не встанут: «${clash.name}» уже стоит.`, workDir: null };
    target.installed = true;
    return { success: true, errorMessage: null, workDir: null };
  }

  async improvementRemove(id: string): Promise<import('./types').InjectResult> {
    await sleep(700);
    const target = this._mockImprovements.find(x => x.id === id);
    if (target) target.installed = false;
    return { success: true, errorMessage: null, workDir: null };
  }
  async adminCreateLibraryStub(type: string, name: string, _author: string, _description: string, _photoPath: string) {
    await sleep(180);
    return {
      id: `mock-stub-${Date.now()}`,
      type, name,
      author: _author,
      description: _description,
      r2Url: '',
      sha256: '',
      sizeBytes: 0,
      sourceRpfVersion: '',
      uploadedBy: '',
      uploadedAt: new Date().toISOString(),
      previewUrl: 'https://mock-r2.invalid/preview.png',
      galleryUrls: [],
      previewVideoUrl: '',
    };
  }
  async adminCreateLibraryMinimap(name: string, _author: string, _description: string, _gfxPath: string, _photoPath: string) {
    await sleep(220);
    return {
      id: `mock-libmm-${Date.now()}`,
      type: 'minimap',
      name,
      author: _author,
      description: _description,
      r2Url: 'https://mock-r2.invalid/library/minimap/full.zip',
      sha256: 'deadbeef',
      sizeBytes: 50000,
      sourceRpfVersion: '',
      uploadedBy: '',
      uploadedAt: new Date().toISOString(),
      previewUrl: 'https://mock-r2.invalid/preview.png',
      galleryUrls: [],
      previewVideoUrl: '',
    };
  }
  async getCurrentReticleInfo() { await sleep(40); return null; }
  async reduxApplyReticle(_source: 'redux' | 'library', _id: string, _displayName?: string) {
    await sleep(800);
    return { success: true, errorMessage: null, workDir: null };
  }
  async reduxResetCustomization(_part: 'crosshair' | 'minimap' | 'all') {
    await sleep(900);
    return { success: true, errorMessage: null, workDir: null };
  }
  async reticleApplyCustom(_spec: import('./types').ReticleSpec) {
    await sleep(900);
    return { success: true, errorMessage: null, workDir: null };
  }
  async knkShare(_userId: string, _spec: import('./types').ReticleSpec) {
    await sleep(300);
    return 'KNK-DEMO-CODE1';
  }
  async knkFetch(_code: string): Promise<import('./types').ReticleSpec> {
    await sleep(300);
    throw new Error('KNK_CODE_NOT_FOUND');
  }
  async legitCheckRedux(_reduxId: string, _versionId?: string | null): Promise<import('./types').LegitReport> {
    for (let p = 0; p <= 100; p += 20) {
      this.bus.emit('legitcheck:progress', { percent: p, stage: p < 100 ? 'scan' : 'done', currentFile: p < 100 ? 'common/data/ai/weapons.meta' : '' });
      await sleep(250);
    }
    return {
      verdict: 'danger',
      verdictTitle: 'Обнаружены изменения, дающие игровое преимущество',
      verdictText: 'Сборка меняет файлы, которые отвечают за стрельбу и наведение. Найдены отклонения в категориях: отдача, разброс. Такой набор изменений характерен для нечестных модификаций (антиотдача/аим). Использовать такую сборку на RP-серверах нельзя.',
      verdictReasons: [
        'Затронуты боевые значения: отдача, разброс.',
        'Изменено оружие (2): WEAPON_CARBINERIFLE, WEAPON_PISTOL.',
        'Самое сильное отклонение: WEAPON_CARBINERIFLE → AccuracySpread 2.35 → 0.10 (-96%).',
      ],
      source: 'Demo Redux · v1',
      checkedAt: new Date().toISOString(),
      dangerCount: 2, warningCount: 1, changedCount: 42, addedCount: 2, deletedCount: 0,
      findings: [
        {
          path: 'common/data/ai/weapons.meta', change: 'changed', severity: 'danger',
          categoryLabel: 'отдача · разброс', note: '', formatOnly: false, size: 1110684,
          fieldDiffs: [
            { owner: 'WEAPON_CARBINERIFLE', field: 'RecoilShakeAmplitude', cleanValue: '0.42', modValue: '0.05', deltaPercent: -88.1, isRed: true },
            { owner: 'WEAPON_CARBINERIFLE', field: 'AccuracySpread', cleanValue: '2.35', modValue: '0.10', deltaPercent: -95.7, isRed: true },
            { owner: 'WEAPON_PISTOL', field: 'Damage', cleanValue: '26.0', modValue: '80.0', deltaPercent: 207.7, isRed: true },
          ],
        },
        {
          path: 'x64/data/tune/playertargetting.ymt', change: 'changed', severity: 'danger',
          categoryLabel: 'аим/тюнинг', note: 'бинарный файл - байты не совпали с чистыми',
          formatOnly: false, size: 8632, fieldDiffs: [],
        },
        {
          path: 'common/data/ai/loadouts.meta', change: 'changed', severity: 'warning',
          categoryLabel: 'изменён', note: '', formatOnly: false, size: 46711, fieldDiffs: [],
        },
        {
          path: 'common/data/timecycle/w_clear.xml', change: 'changed', severity: 'visual',
          categoryLabel: 'визуал', note: '', formatOnly: false, size: 126108, fieldDiffs: [],
        },
      ],
      unverified: [],
      checkedCount: 46,
    };
  }
  async legitCheckUpdateRpf(_rpfPath?: string | null): Promise<import('./types').LegitReport> {
    const rep = await this.legitCheckRedux('mock', null);
    return { ...rep, verdict: 'safe', verdictTitle: 'Безопасно', dangerCount: 0, warningCount: 0,
      verdictText: 'Сборка чистая: все изменения касаются графики и оформления. Ни один файл, влияющий на стрельбу, наведение или урон, не изменён.',
      verdictReasons: ['Изменения затрагивают только графику/визуал: 40 файлов.', 'Файлы отдачи, разброса, аима, урона и вьюмодела не тронуты.'],
      source: 'Мой установленный update.rpf',
      findings: rep.findings.filter(f => f.severity === 'visual'),
      checkedCount: 24187 };
  }
  async legitReportShare(_userId: string, _report: import('./types').LegitReport) {
    await sleep(300);
    return 'LGT-DEMO-CODE1';
  }
  async legitReportFetch(_code: string): Promise<import('./types').LegitReport> {
    await sleep(300);
    return this.legitCheckRedux('mock', null);
  }
  async adminCreateLibraryReticle(name: string, _author: string, _description: string, _gfxPath: string, _photoPath: string) {
    await sleep(220);
    return {
      id: `mock-libret-${Date.now()}`,
      type: 'crosshair',
      name,
      author: _author,
      description: _description,
      r2Url: 'https://mock-r2.invalid/library/crosshair/full.zip',
      sha256: 'deadbeef',
      sizeBytes: 12000,
      sourceRpfVersion: '',
      uploadedBy: '',
      uploadedAt: new Date().toISOString(),
      previewUrl: 'https://mock-r2.invalid/preview.png',
      galleryUrls: [],
      previewVideoUrl: '',
    };
  }
  async adminUploadLibraryGallery(_libraryId: string, sourcePaths: string[]): Promise<string[]> {
    await sleep(180 * sourcePaths.length);

    return sourcePaths.map((_, i) =>
      `https://mock-r2.invalid/library/gallery/${Date.now()}_${i}.png`);
  }
  async adminUploadLibraryVideo(_libraryId: string, _sourcePath: string): Promise<string> {
    await sleep(280);
    return `https://mock-r2.invalid/library/video/${Date.now()}.mp4`;
  }
  async getCurrentSoundPackInfo() { await sleep(40); return null; }
  async soundPackInstall(_libraryId: string, _displayName?: string) {
    await sleep(900);
    return { success: true, errorMessage: null, workDir: null };
  }
  async soundPackUninstall() {
    await sleep(400);
    return { success: true, errorMessage: null, workDir: null };
  }
  async adminCreateLibrarySounds(name: string, _author: string, _description: string, _zipPath: string, _photoPath: string) {
    await sleep(450);
    return {
      id: `mock-libsnd-${Date.now()}`,
      type: 'sounds',
      name,
      author: _author,
      description: _description,
      r2Url: 'https://mock-r2.invalid/library/sounds/full.zip',
      sha256: 'deadbeef',
      sizeBytes: 250_000_000,
      sourceRpfVersion: '',
      uploadedBy: '',
      uploadedAt: new Date().toISOString(),
      previewUrl: 'https://mock-r2.invalid/preview.png',
      galleryUrls: [],
      previewVideoUrl: '',
    };
  }
  async adminCreateLibraryAwc(name: string, _author: string, _description: string, _awcPath: string, _photoPath: string) {
    await sleep(300);
    return {
      id: `mock-libsnd-awc-${Date.now()}`,
      type: 'sounds',
      name,
      author: _author,
      description: _description,
      r2Url: 'https://mock-r2.invalid/library/sounds/weapon.awc',
      sha256: 'deadbeef',
      sizeBytes: 360_000,
      sourceRpfVersion: '',
      uploadedBy: '',
      uploadedAt: new Date().toISOString(),
      previewUrl: 'https://mock-r2.invalid/preview.png',
      galleryUrls: [],
      previewVideoUrl: '',
    };
  }
  async adminUploadGunpackCover(_sourcePath: string): Promise<string> {
    await sleep(180);
    return `https://mock-r2.invalid/gunpacks/covers/${Date.now()}.png`;
  }

  async userBuildListPending(): Promise<UserBuildDto[]> {
    await sleep(80);
    return this._userBuilds
      .filter(b => b.status === 'pending' && b.submittedForReview)
      .sort((a, b) => (b.createdAt ?? '').localeCompare(a.createdAt ?? ''));
  }

  async userBuildListMyPending(authorUserId: string): Promise<UserBuildDto[]> {
    await sleep(80);
    return this._userBuilds
      .filter(b => b.authorUserId === authorUserId
                && (b.status === 'pending' || b.status === 'rejected'))
      .sort((a, b) => (b.createdAt ?? '').localeCompare(a.createdAt ?? ''));
  }

  async userBuildApprove(id: string, reviewerUserId: string, tier: number | null): Promise<UserBuildDto> {
    await sleep(120);
    const idx = this._userBuilds.findIndex(b => b.id === id && b.status === 'pending');
    if (idx === -1) throw new Error('not pending');
    const merged: UserBuildDto = {
      ...this._userBuilds[idx],
      status: 'approved',
      reviewedBy: reviewerUserId,
      reviewedAt: new Date().toISOString(),
      rejectReason: null,
      tier: tier ?? this._userBuilds[idx].tier,
      updatedAt: new Date().toISOString(),
    };
    this._userBuilds[idx] = merged;
    return merged;
  }

  async userBuildReject(id: string, reviewerUserId: string, reason: string): Promise<UserBuildDto> {
    await sleep(120);
    const idx = this._userBuilds.findIndex(b => b.id === id && b.status === 'pending');
    if (idx === -1) throw new Error('not pending');
    const merged: UserBuildDto = {
      ...this._userBuilds[idx],
      status: 'rejected',
      reviewedBy: reviewerUserId,
      reviewedAt: new Date().toISOString(),
      rejectReason: reason,
      updatedAt: new Date().toISOString(),
    };
    this._userBuilds[idx] = merged;
    return merged;
  }

  async userBuildResubmit(id: string): Promise<UserBuildDto> {
    await sleep(100);
    const idx = this._userBuilds.findIndex(b => b.id === id && b.status === 'rejected');
    if (idx === -1) throw new Error('not rejected');
    const merged: UserBuildDto = {
      ...this._userBuilds[idx],
      status: 'pending',
      submittedForReview: true,
      reviewedBy: null,
      reviewedAt: null,
      rejectReason: null,
      updatedAt: new Date().toISOString(),
    };
    this._userBuilds[idx] = merged;
    return merged;
  }

  async gtaPresetsList(search: string | null): Promise<GtaPreset[]> {
    await sleep(120);
    let list = this._gtaPresets.filter(p => p.status === 'published');
    if (search && search.trim()) {
      const q = search.trim().toLowerCase();
      list = list.filter(p => p.name.toLowerCase().includes(q) || p.author.toLowerCase().includes(q));
    }
    return [...list].sort((a, b) =>
      b.viewerPriority - a.viewerPriority
      || b.uploadedAt.localeCompare(a.uploadedAt));
  }
  async gtaPresetGet(id: string): Promise<GtaPreset | null> {
    await sleep(80);
    return this._gtaPresets.find(p => p.id === id) ?? null;
  }
  async gtaPresetApply(id: string): Promise<GtaPresetApplyResult> {
    await sleep(800);
    const preset = this._gtaPresets.find(p => p.id === id);
    if (!preset) return {
      success: false, errorMessage: 'Пресет не найден.',
      targetPath: '', backupPath: null, gameWasRunning: false,
    };
    return {
      success: true, errorMessage: null,
      targetPath: 'C:/Mock/Documents/Rockstar Games/GTA V/settings.xml',
      backupPath: 'C:/Mock/Documents/Rockstar Games/GTA V/settings.xml.backup-mock',
      gameWasRunning: false,
    };
  }
  async gtaSettingsApplyFromUrl(xmlUrl: string): Promise<GtaPresetApplyResult> {
    await sleep(800);
    if (!xmlUrl) return {
      success: false, errorMessage: 'URL пустой.',
      targetPath: '', backupPath: null, gameWasRunning: false,
    };
    return {
      success: true, errorMessage: null,
      targetPath: 'C:/Mock/Documents/Rockstar Games/GTA V/settings.xml',
      backupPath: 'C:/Mock/Documents/Rockstar Games/GTA V/settings.xml.backup-mock',
      gameWasRunning: false,
    };
  }
  private _mockReactions: Record<string, { likes: number; dislikes: number; myReaction: number }> = {};
  async gtaInstallCounts(_eventType: string): Promise<Record<string, number>> {
    await sleep(40);
    return {};
  }
  async accountStats(): Promise<import('./types').AccountStats> {
    await sleep(40);
    return { accountNo: 1337, downloads: 42 };
  }
  async gtaPresetReactionsGet(presetId: string, _userId: string): Promise<import('./types').PresetReactions> {
    await sleep(40);
    return { ...(this._mockReactions[presetId] ?? { likes: 0, dislikes: 0, myReaction: 0 }) };
  }
  async gtaPresetReactionSet(presetId: string, reaction: number): Promise<import('./types').PresetReactions> {
    await sleep(120);
    const cur = this._mockReactions[presetId] ?? { likes: 0, dislikes: 0, myReaction: 0 };
    if (cur.myReaction === 1)  cur.likes    = Math.max(0, cur.likes - 1);
    if (cur.myReaction === -1) cur.dislikes = Math.max(0, cur.dislikes - 1);
    if (cur.myReaction === reaction) {
      cur.myReaction = 0;
    } else {
      cur.myReaction = reaction;
      if (reaction === 1) cur.likes += 1; else cur.dislikes += 1;
    }
    this._mockReactions[presetId] = cur;
    return { ...cur };
  }
  async gtaPresetIncrementDownloads(id: string): Promise<number> {
    await sleep(40);
    const p = this._gtaPresets.find(x => x.id === id);
    if (!p) return 0;
    p.downloadCount += 1;
    return p.downloadCount;
  }
  async adminGtaPresetList(): Promise<GtaPreset[]> {
    await sleep(120);
    return [...this._gtaPresets].sort((a, b) => b.uploadedAt.localeCompare(a.uploadedAt));
  }
  async adminGtaPresetUpload(request: GtaPresetUploadRequest): Promise<GtaPreset> {
    await sleep(900);
    const id = `mock-${Date.now()}`;
    const row: GtaPreset = {
      id,
      name: request.name,
      description: request.description,
      author: request.author,
      xmlUrl: `https://example.invalid/mock/${id}.xml`,
      xmlSizeBytes: 4096,
      xmlSha256: id.padEnd(64, '0').slice(0, 64),
      expectedFpsLow: request.expectedFpsLow,
      expectedFpsHigh: request.expectedFpsHigh,
      baselineHwLabel: request.baselineHwLabel,

      computedGainPercent: Math.min(43, 15 + Math.floor(Math.random() * 30)),
      cpuBias: 'balanced',
      isTournament: request.isTournament,
      status: request.status,
      viewerPriority: request.viewerPriority,
      downloadCount: 0,
      uploadedBy: 'admin',
      uploadedAt: new Date().toISOString(),
      updatedAt:  new Date().toISOString(),
    };
    this._gtaPresets.unshift(row);
    return row;
  }
  async adminGtaPresetPatch(id: string, patch: GtaPresetPatch): Promise<void> {
    await sleep(150);
    const p = this._gtaPresets.find(x => x.id === id);
    if (!p) return;
    if (patch.name             !== undefined) p.name            = patch.name;
    if (patch.description      !== undefined) p.description     = patch.description;
    if (patch.author           !== undefined) p.author          = patch.author;
    if (patch.expectedFpsLow   !== undefined) p.expectedFpsLow  = patch.expectedFpsLow;
    if (patch.expectedFpsHigh  !== undefined) p.expectedFpsHigh = patch.expectedFpsHigh;
    if (patch.baselineHwLabel  !== undefined) p.baselineHwLabel = patch.baselineHwLabel;
    if (patch.isTournament     !== undefined) p.isTournament    = patch.isTournament;
    if (patch.status           !== undefined) p.status          = patch.status;
    if (patch.viewerPriority   !== undefined) p.viewerPriority  = patch.viewerPriority;
    p.updatedAt = new Date().toISOString();
  }
  async adminGtaPresetDelete(id: string): Promise<void> {
    await sleep(150);
    this._gtaPresets = this._gtaPresets.filter(p => p.id !== id);
  }
  async adminGtaPresetAnalyze(_sourceXmlPath: string): Promise<GtaSettingsAnalysis> {
    await sleep(400);
    return {
      gainPercent: 32,
      cpuBias: 'cpu',
      contributions: [
        { key: 'CityDensity',          gainPercent: 12,  category: 'Cpu' },
        { key: 'LodScale',             gainPercent: 7,   category: 'Cpu' },
        { key: 'PedVarietyMultiplier', gainPercent: 6,   category: 'Cpu' },
        { key: 'ShadowQuality',        gainPercent: 5.5, category: 'GpuShadow' },
        { key: 'MSAA',                 gainPercent: 1.5, category: 'GpuOther' },
      ],
    };
  }

  private _gtaManual: GtaSettingsModel = {
    display: {
      screenWidth: 1920, screenHeight: 1080, refreshRate: 144,
      aspectRatio: 0, windowed: 0, vSync: false,
    },
    quality: {
      textureQuality: 2, shaderQuality: 2, waterQuality: 1,
      particleQuality: 1, postFx: 2, shadowQuality: 3,
    },
    antiAliasing: { fxaa: false, txaa: false, msaa: 0, reflectionMsaa: 0 },
    world: {
      cityDensity: 1, pedVariety: 1, vehicleVariety: 1,
      lodScale: 1, maxLodScale: 0, vehicleLodBias: 1, pedLodBias: 1,
      grassQuality: 2, reflectionQuality: 3, shadowDistance: 1,
    },
    advanced: {
      tessellation: 3, anisotropicFiltering: 16, ssao: 2,
      shadowSoftShadows: 3, ultraShadows: false, shadowParticles: false,
      shadowLongShadows: false, reflectionMipBlur: false,
      shadowSplitZStart: 0.93, shadowSplitZEnd: 0.89,
      dxVersion: 3, dof: false, hdStreaming: false, motionBlur: 0,
      fogVolumes: false,
    },
  };

  private _optCatalog: OptimizationCatalog = {
    problems: [],
    groups: [
      {
        key: 'graphics', style: 'slider', beta: false, resetIndex: 0, iconUrl: '',
        title: 'Настройки графики',
        description: 'Готовый набор от минималки до ультра. Параметры, которые слабо влияют на FPS, не режем.',
        options: [
          { idx: 0, name: 'Не изменять', previewUrl: '/previews/graphics-dont-change.jpg', fpsLabel: '',    settingsCount: 0 },
          { idx: 1, name: 'Низкие',      previewUrl: '/previews/graphics-min.jpg',         fpsLabel: '+30', settingsCount: 10 },
          { idx: 2, name: 'Средние',     previewUrl: '/previews/graphics-medium.jpg',      fpsLabel: '+18', settingsCount: 9 },
          { idx: 3, name: 'Высокие',     previewUrl: '/previews/graphics-high.jpg',        fpsLabel: '+6',  settingsCount: 9 },
        ],
      },
      {
        key: 'shadows', style: 'toggle', beta: false, resetIndex: 0, iconUrl: '',
        title: 'Тени',
        description: 'Полностью убирает тени: и качество, и дальность каскадов. Самый заметный прирост из дешёвых.',
        options: [
          { idx: 0, name: 'Вкл',  previewUrl: '/previews/shadows-on.jpg',  fpsLabel: '0',   settingsCount: 1 },
          { idx: 1, name: 'Выкл', previewUrl: '/previews/shadows-off.jpg', fpsLabel: '+25', settingsCount: 5 },
        ],
      },
      {
        key: 'garbage', style: 'toggle', beta: false, resetIndex: 0, iconUrl: '',
        title: 'Объекты мусора',
        description: 'Убирает валяющиеся пакеты и бумажки по всему городу. В глаза не бросается, а считать их игре больше не нужно.',
        options: [
          { idx: 0, name: 'Вкл',  previewUrl: '/previews/garbage-on.jpg',  fpsLabel: '0',   settingsCount: 0 },
          { idx: 1, name: 'Выкл', previewUrl: '/previews/garbage-off.jpg', fpsLabel: '+10', settingsCount: 2 },
        ],
      },
      {
        key: 'grass', style: 'toggle', beta: false, resetIndex: 0, iconUrl: '',
        title: 'Трава',
        description: 'Убирает траву и мелкую растительность по всей карте. Меньше визуального шума, но и прирост скромный.',
        options: [
          { idx: 0, name: 'Вкл',  previewUrl: '/previews/grass-on.jpg',  fpsLabel: '0',  settingsCount: 0 },
          { idx: 1, name: 'Выкл', previewUrl: '/previews/grass-off.jpg', fpsLabel: '+2', settingsCount: 1 },
        ],
      },
      {
        key: 'rain', style: 'toggle', beta: false, resetIndex: 0, iconUrl: '',
        title: 'Дождь и снег',
        description: 'Выключает осадки целиком. Заметно на глаз: погоды в игре не будет вовсе.',
        options: [
          { idx: 0, name: 'Вкл',  previewUrl: '/previews/rain-on.jpg',  fpsLabel: '0',   settingsCount: 0 },
          { idx: 1, name: 'Выкл', previewUrl: '/previews/rain-off.jpg', fpsLabel: '+10', settingsCount: 2 },
        ],
      },
      {
        key: 'particles', style: 'toggle', beta: false, resetIndex: 0, iconUrl: '',
        title: 'Частицы',
        description: 'Дым, искры, эффекты взрывов. Без них стрельба выглядит заметно беднее.',
        options: [
          { idx: 0, name: 'Вкл',  previewUrl: '/previews/particles-on.jpg',  fpsLabel: '0',  settingsCount: 0 },
          { idx: 1, name: 'Выкл', previewUrl: '/previews/particles-off.jpg', fpsLabel: '+5', settingsCount: 1 },
        ],
      },
      {
        key: 'deffect', style: 'toggle', beta: true, resetIndex: 0, iconUrl: '',
        title: 'Туман, блики и лучи',
        description: 'Атмосферные эффекты. На FPS почти не влияет, но картинка становится чище и контрастнее.',
        options: [
          { idx: 0, name: 'Вкл',  previewUrl: '/previews/deffect-on.jpg',  fpsLabel: '0', settingsCount: 0 },
          { idx: 1, name: 'Выкл', previewUrl: '/previews/deffect-off.jpg', fpsLabel: '0', settingsCount: 1 },
        ],
      },
    ],
  };
  private _optState: Record<string, number | null> = {
    graphics: 0, shadows: 0, garbage: 0, grass: 0, rain: 0, particles: 0, deffect: 0,
  };

  async optimizationCatalogGet(): Promise<OptimizationCatalog> {
    await sleep(120);
    return structuredClone(this._optCatalog);
  }
  async optimizationStateGet(): Promise<OptimizationResolution> {
    await sleep(90);
    return { selections: { ...this._optState }, unmappedKeys: [], customGroups: [] };
  }
  async optimizationApply(selections: OptimizationSelection[]): Promise<OptimizationApplyResult> {
    await sleep(900);
    const changes = selections.flatMap(s => {
      const g = this._optCatalog.groups.find(x => x.key === s.groupKey);
      const o = g?.options.find(x => x.idx === s.optionIdx);
      if (!g || !o) return [];
      this._optState[s.groupKey] = s.optionIdx;
      return Array.from({ length: o.settingsCount }, (_, i) => ({
        key: `${g.key === 'shadows' ? 'Shadow' : 'Gfx'}Key${i + 1}`,
        from: '2', to: '0', groupKey: g.key,
      }));
    });
    return {
      success: true, errorMessage: null, changes, warnings: [],
      targetPath: 'C:/Mock/Documents/Rockstar Games/GTA V/settings.xml',
      backupPath: 'C:/Mock/.../settings.xml.backup-20260819-141800',
      gameWasRunning: false, baselineCaptured: changes.length > 0,
    };
  }
  async optimizationResolveFromPreset(presetId: string): Promise<OptimizationResolution> {
    await sleep(300);
    return presetId
      ? { selections: { graphics: 1, shadows: 1 }, unmappedKeys: ['AnisotropicFiltering'], customGroups: [] }
      : { selections: { ...this._optState }, unmappedKeys: [], customGroups: [] };
  }

  async gtaSettingsRead(): Promise<GtaSettingsReadResult> {
    await sleep(80);
    return {
      model: structuredClone(this._gtaManual),
      existedOnDisk: false,
      sourcePath: 'C:/Mock/Documents/Rockstar Games/GTA V/settings.xml',
    };
  }
  async gtaSettingsAnalyzeModel(model: GtaSettingsModel): Promise<GtaSettingsAnalysis> {

    let g = 0;
    g += (1 - model.world.cityDensity) * 12;
    g += (1 - model.world.lodScale) * 7;
    g += (1 - model.world.pedVariety) * 6;
    g += (1 - model.world.vehicleVariety) * 5;
    if (model.quality.shadowQuality < 3) g += (3 - model.quality.shadowQuality) * 1.5;
    if (model.antiAliasing.msaa === 0)   g += 3;
    if (!model.antiAliasing.fxaa)        g += 0.3;
    return {
      gainPercent: Math.min(43, Math.round(g)),
      cpuBias: g > 8 ? 'cpu' : 'balanced',
      contributions: [],
    };
  }
  async gtaSettingsWrite(model: GtaSettingsModel): Promise<GtaPresetApplyResult> {
    await sleep(400);
    this._gtaManual = structuredClone(model);
    return {
      success: true, errorMessage: null,
      targetPath: 'C:/Mock/Documents/Rockstar Games/GTA V/settings.xml',
      backupPath: 'C:/Mock/Documents/Rockstar Games/GTA V/settings.xml.backup-mock',
      gameWasRunning: false,
    };
  }

  async mirrorSetOverride(_choice: string | null): Promise<void> { await sleep(50); }
  async mirrorProbe(choice: string | null) {
    await sleep(400);
    return { success: true, mirror: choice ?? 'auto', speedMbPerSecond: 12.5, errorMessage: null };
  }
  async zapretApplyWhitelist(path: string) {
    await sleep(200);
    return {
      success: true, errorMessage: null,
      domainLinesAdded: 1, ipsetLinesAdded: 12,
      listsDir: `${path}/lists`,
    };
  }
  async zapretDetect(path: string | null) {
    await sleep(80);
    return { installed: !!path, configuredForUs: false, detectedRoot: path ?? null };
  }
  async rendererEnsureInstalled() {
    await sleep(500);
    return {
      success: true, alreadyInstalled: true,
      rendererPath: '/mock/Renderer', downloadedBytes: 0, errorMessage: null,
    };
  }
  async rendererProbe() {
    await sleep(200);
    return {
      rendererPath: '/mock/Renderer',
      baseDirExists: true, nodeExeExists: true, renderJsExists: true,
      nodeModulesExists: true, nodeModulesSizeMb: 152,
      nodeVersion: 'v18.19.0', nodeError: null, isUsable: true,
      summary: 'baseDir=true, node.exe=true, render.js=true, node_modules=true (152 MB), chromium=true, node=v18.19.0',
      actionableHint: 'Renderer готов к работе.',
      chromiumInstalled: true,
    };
  }
  async rendererTestRender() {
    await sleep(1500);
    return {
      success: true, elapsedMs: 1450, outputBytes: 84231,
      outputPath: '/tmp/mock_render.png', stdoutTail: null, stderrTail: null,
      errorMessage: null,
    };
  }
  async rendererForceReinstall() {
    await sleep(2000);
    return {
      success: true, alreadyInstalled: false,
      rendererPath: '/mock/Renderer', downloadedBytes: 140 * 1024 * 1024,
      errorMessage: null,
    };
  }
  async jreEnsureInstalled() {
    await sleep(300);
    return {
      success: true, alreadyInstalled: true,
      jrePath: '/mock/jre', downloadedBytes: 0, errorMessage: null,
    };
  }
  async pcDiagApply(id: string): Promise<import('./IAppBridge').PcDiagApplyResult> {
    await sleep(900);
    this._pcDiagApplied.add(id);
    return { ok: true, message: 'Применено (мок). Точка восстановления создана.', requiresRestart: id === 'pagefile-off' };
  }
  async pcDiagRevert(id: string): Promise<import('./IAppBridge').PcDiagApplyResult> {
    await sleep(500);
    this._pcDiagApplied.delete(id);
    return { ok: true, message: 'Возвращено как было (мок).', requiresRestart: false };
  }
  private _pcDiagApplied = new Set<string>();
  async pcDiagAi(_userId: string, question?: string | null): Promise<import('./IAppBridge').PcDiagAiResult> {
    await sleep(1500);
    return {
      ok: true,
      error: '',
      text: question
        ? `Мок-ответ на вопрос «${question}»: в браузерной разработке модель не вызывается.`
        : 'Мок-разбор ПК: главная находка - схема питания, дальше драйвер. Реальный текст приходит с сервера.',
    };
  }
  async pcDiagTweaks(): Promise<import('./IAppBridge').PcDiagTweak[]> {
    await sleep(200);
    const done = (id: string) => this._pcDiagApplied.has(id) ? 'Done' as const : 'Ready' as const;
    return [
      { id: 'mmcss-games', grade: 'micro', requiresRestart: false, inAllSafe: true, state: done('mmcss-games'), data: {} },
      { id: 'system-responsiveness', grade: 'micro', requiresRestart: false, inAllSafe: true, state: done('system-responsiveness'), data: {} },
      { id: 'gamebar-nexus-off', grade: 'micro', requiresRestart: false, inAllSafe: true, state: done('gamebar-nexus-off'), data: {} },
      { id: 'stickykeys-off', grade: 'device', requiresRestart: false, inAllSafe: true, state: done('stickykeys-off'), data: {} },
      { id: 'mouse-accel-off', grade: 'device', requiresRestart: false, inAllSafe: false, state: done('mouse-accel-off'), data: {} },
      { id: 'w32-priority-separation', grade: 'experiment', requiresRestart: false, inAllSafe: false, state: done('w32-priority-separation'), data: {} },
      { id: 'network-throttling-off', grade: 'experiment', requiresRestart: false, inAllSafe: false, state: done('network-throttling-off'), data: {} },
      { id: 'commandline-clean', grade: 'works', requiresRestart: false, inAllSafe: true, state: 'Ready', data: { flags: '-high, -norestrictions' } },
      { id: 'shader-cache-clean', grade: 'maintenance', requiresRestart: false, inAllSafe: false, state: 'Ready', data: { mb: '1840' } },
      { id: 'temp-clean', grade: 'maintenance', requiresRestart: false, inAllSafe: false, state: 'Ready', data: { mb: '620' } },
    ];
  }
  async pcDiagJournal(): Promise<import('./IAppBridge').PcDiagJournalEntry[]> {
    return [...this._pcDiagApplied].map(id => ({ id, appliedAtUtc: new Date().toISOString(), reverted: false }));
  }
  async pcDiagReport(): Promise<import('./IAppBridge').PcDiagReport> {
    await sleep(600);
    return {
      cpuName: '12th Gen Intel(R) Core(TM) i7-12700H',
      cpuCores: 14, cpuThreads: 20, cpuL3Mb: 24,
      cpuTier: 'B', cpuFamily: 'Intel 12 поколение, ноутбук',
      cpuHybrid: true, cpuX3D: false, cpuLaptop: true,
      ramTotalGb: 32, ramSlotsTotal: 4, ramTier: 'S', ramTierNote: '32 ГБ, двухканал, 6000 МТ/с, профиль включён', diskTier: 'S', diskTierNote: 'игра на NVMe - быстрее для GTA уже некуда',
      ramSticks: [
        { slot: 'Controller0-ChannelA-DIMM0', capacityGb: 16, ratedMt: 4800, configuredMt: 4800, memType: 'DDR5' },
        { slot: 'Controller1-ChannelA-DIMM0', capacityGb: 16, ratedMt: 4800, configuredMt: 4800, memType: 'DDR5' },
      ],
      disks: [{ model: 'Micron 3400 NVMe', media: 'Ssd', bus: 'Nvme', sizeGb: 954 }],
      gpus: [
        { name: 'Intel(R) Iris(R) Xe Graphics', vramGb: 0, driverVersion: '31.0.101.4032', driverDate: '21.12.2022', isIntegrated: true },
        { name: 'NVIDIA GeForce RTX 3070 Ti Laptop GPU', vramGb: 8, driverVersion: '32.0.15.6607', driverDate: '20.10.2024', isIntegrated: false },
      ],
      powerScheme: 'Сбалансированная', powerKind: 'Balanced',
      vbsRunning: true, gameDvrOn: true, hasBattery: true,
      osCaption: 'Майкрософт Windows 11 Домашняя',
      displayWidth: 2560, displayHeight: 1600, displayCurrentHz: 60, displayMaxHz: 165,
      monitors: [
        { name: 'LG ULTRAGEAR', deviceName: 'DISPLAY1', adapter: 'NVIDIA GeForce RTX 4070', width: 2560, height: 1600, currentHz: 60, maxHz: 165, isPrimary: true },
        { name: 'AOC 24G2W1G4', deviceName: 'DISPLAY2', adapter: 'NVIDIA GeForce RTX 4070', width: 1920, height: 1080, currentHz: 144, maxHz: 144, isPrimary: false },
      ],
      netWired: false, netWireless: true, netVpn: true,
      gtaPath: 'D:\\Games\\GTAV', gtaDiskMedia: 'Ssd',
      background: [
        { name: 'Браузер', count: 14, gb: 2.4 },
        { name: 'Discord', count: 5, gb: 0.6 },
        { name: 'Торрент-клиент', count: 1, gb: 0.2 },
      ],
      sensorErrors: [],
      findings: [
        { id: 'power-balanced', severity: 'Major', category: 'Windows', data: { scheme: 'Сбалансированная', laptop: '1' }, gainMinPercent: 2, gainMaxPercent: 15, autoFixable: true },
        { id: 'gpu-driver-old', severity: 'Major', category: 'Driver', data: { gpu: 'NVIDIA GeForce RTX 3070 Ti Laptop GPU', months: '22' }, gainMinPercent: null, gainMaxPercent: null, autoFixable: false },
        { id: 'gamedvr-on', severity: 'Minor', category: 'Windows', data: { gameDvr: '1', appCapture: '1' }, gainMinPercent: 1, gainMaxPercent: 5, autoFixable: true },
        { id: 'vbs-running', severity: 'Minor', category: 'Windows', data: { hvci: '0' }, gainMinPercent: 3, gainMaxPercent: 8, autoFixable: false },
        { id: 'cpu-hybrid', severity: 'Info', category: 'Hardware', data: {}, gainMinPercent: null, gainMaxPercent: null, autoFixable: false },
        { id: 'dual-gpu-check-render', severity: 'Info', category: 'Hardware', data: { dgpu: 'NVIDIA GeForce RTX 3070 Ti Laptop GPU' }, gainMinPercent: null, gainMaxPercent: null, autoFixable: false },
        { id: 'display-not-max-hz', severity: 'Major', category: 'Windows', data: { current: '60', max: '165', res: '2560x1600' }, gainMinPercent: null, gainMaxPercent: null, autoFixable: true },
        { id: 'bg-torrent', severity: 'Major', category: 'Apps', data: { name: 'Торрент-клиент' }, gainMinPercent: null, gainMaxPercent: null, autoFixable: false },
        { id: 'bg-browser', severity: 'Info', category: 'Apps', data: { gb: '2.4', count: '14' }, gainMinPercent: null, gainMaxPercent: null, autoFixable: false },
        { id: 'bg-discord-overlay', severity: 'Info', category: 'Apps', data: {}, gainMinPercent: null, gainMaxPercent: null, autoFixable: false },
        { id: 'autostart-crowded', severity: 'Info', category: 'Apps', data: { count: '12', sample: 'Discord, Steam, OneDrive, Wallpaper Engine, Telegram, Overwolf' }, gainMinPercent: null, gainMaxPercent: null, autoFixable: false },
        { id: 'wifi-only', severity: 'Info', category: 'Windows', data: { adapter: 'Intel(R) Wi-Fi 6 AX201' }, gainMinPercent: null, gainMaxPercent: null, autoFixable: false },
        { id: 'vpn-active', severity: 'Info', category: 'Windows', data: { adapter: 'NordLynx Tunnel' }, gainMinPercent: null, gainMaxPercent: null, autoFixable: false },
        { id: 'transparency-on', severity: 'Info', category: 'Windows', data: {}, gainMinPercent: null, gainMaxPercent: null, autoFixable: true },
      ],
      elapsedMs: 255,
    };
  }
  async bypassTestRun(strategyId: number) {
    await sleep(800 + Math.random() * 1500);
    const labels = [
      'Baseline (без bypass)', 'TLS fragmentation 3×', 'TLS fragmentation 8×',
      'DoH Cloudflare (1.1.1.1)', 'DoH Quad9 (9.9.9.9)', 'DoH NextDNS',
    ];
    const ok = Math.random() > 0.3;
    return {
      strategyId, strategyLabel: labels[strategyId] ?? '?',
      targetUrl: 'https://cdn.miamigraphicsstorage.uk/releases/MiamiGraphics_Setup_1.2.5.exe',
      success: ok,
      connectMs: ok ? 80 + Math.floor(Math.random() * 200) : 0,
      firstByteMs: ok ? 80 + Math.floor(Math.random() * 200) : 0,
      totalMs: ok ? 400 + Math.floor(Math.random() * 1500) : 5000,
      bytesReceived: ok ? 262144 : 0,
      kbps: ok ? 200 + Math.random() * 5000 : 0,
      httpStatusCode: ok ? 206 : 0,
      errorMessage: ok ? null : 'Connection timed out (mock)',
    };
  }

  async networkDoctorRun(url?: string | null) {
    await sleep(2000);
    const mk = (
      id: string, label: string, role: string,
      kb: number, accepted: number, coldHead: number, coldMid: number,
    ) => ({
      id, label, host: `${id}.miamigraphicsstorage.uk`, role,
      ok: true, httpStatus: 206, ip: '10.0.0.1',
      dnsMs: 12, connectMs: 40, ttfbMs: 180, totalMs: 900,
      rangeOk: true, bytes: 2097152, kbPerSec: kb,
      streamsAccepted: accepted, streamsRefused: 8 - accepted,
      coldHeadTtfbMs: coldHead, coldMidTtfbMs: coldMid, coldOk: true,
      error: null,
    });
    return {
      startedAtUtc: new Date().toISOString(),
      totalMs: 2000,
      nodes: [
        mk('rf',   'РФ-хранилище (S3) - основной источник', 'ru', 9800, 8, 210, 260),
        mk('cdn',  'Cloudflare CDN (хранилище EU)', 'cf', 180,  8, 320, 400),
        mk('ru1',  'ru1 (РФ)',      'ru', 4200, 2, 360, 6900),
        mk('spb1', 'spb1 (Питер)',  'ru', 3800, 4, 340, 520),
        mk('msk',  'msk (Москва)',  'ru', 4000, 4, 300, 480),
      ],
      hub: { ok: true, nodeGiven: 'msk', urlGiven: null, ms: 120, status: 'grant', error: null },
      env: {
        'Регион БД': 'Ru',
        'Источник загрузок': 'ru2',
        'Zapret': 'не найден',
        'Свободно на диске': '240 ГБ',
      },
      problems: [
        'ru1 (РФ): принял 2 соединений из 8, остальные отбил. Качать оттуда можно только в 2 потока.',
        'ru1 (РФ): середина мода отдаётся за 6900 мс против 360 мс у начала. Узел тянет файл с нуля, многопоточная качалка на нём буксует.',
        'Cloudflare отдаёт 180 КБ/с против 4 МБ/с у русских узлов.',
      ],
      verdict: 'Провайдер режет Cloudflare. Переключи источник загрузок на RU, тогда качать будет с русских серверов.'
        + ' Основной источник (rf.miamigraphicsstorage.uk) отдаёт 9.6 МБ/с.',
      bestHost: 'rf.miamigraphicsstorage.uk',
      coldProbeUrl: url ?? 'https://cdn.miamigraphicsstorage.uk/redux/mock/patch.zip',
    };
  }

  async serverRegionGet() {
    await sleep(40);
    return {
      region:       _mockRegion ?? '',
      url:          _mockRegion === 'ru'
                      ? 'https://ru.miamigraphicsstorage.uk'
                      : 'https://eu.miamigraphicsstorage.uk',
      isConfigured: _mockRegion !== null,
    };
  }
  async serverRegionSet(region: 'eu' | 'ru'): Promise<void> {
    await sleep(80);
    _mockRegion = region;
  }
  async serverRegionPing() {
    await sleep(600);

    return { euMs: 62, ruMs: 18 };
  }

  async downloadSourceGet() {
    await sleep(30);
    return { source: _mockDownloadSource, queueEnabled: _mockDownloadSource === 'ru2' };
  }
  async downloadSourceSet(source: 'eu' | 'ru2'): Promise<void> {
    await sleep(60);
    _mockDownloadSource = source;
  }
  async downloadSourceEvaluateEu(_zapretRootPath?: string | null) {
    await sleep(1500);
    return {
      euWorks: true,
      mbps: 7.3,
      zapretConfigured: true,
      zapretRestarted: true,
      message: null as string | null,
    };
  }
}

let _mockDownloadSource: 'eu' | 'ru2' = 'eu';

let _mockRegion: 'eu' | 'ru' | null =
  (typeof document !== 'undefined' && document.documentElement.dataset.demo === '1')
    ? 'eu'
    : null;
