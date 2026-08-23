export interface SystemInfo {
  gtaPath: string | null;
  gpuName: string;
  gtaExeVersion: string | null;
  isGtaFound: boolean;
}

export interface CacheSettings {
  enabled: boolean;
  rootOverride: string | null;
  effectiveRoot: string;
  defaultRoot: string;
  sizeBytes: number;

  dataRoot: string;
  defaultDataRoot: string;
  backupRoot: string;
  backupBytes: number;
  totalBytes: number;
  workBytes: number;
  workRoot: string;
  limitBytes: number;
  minLimitBytes: number;
  maxLimitBytes: number;
  protectedBytes: number;
  freeSpaceBytes: number;
  backupOnLegacyRoot: boolean;
  otherBytes: number;
}

export interface DataMoveResult {
  success: boolean;
  effectiveRoot: string;
  movedBytes: number;
  sourceRemoved: boolean;
  errorMessage: string | null;
}

export interface DataMoveProgress {
  phase: 'checking' | 'copying' | 'verifying' | 'switching' | 'cleanup' | 'done' | 'error';
  percent: number;
  fileName: string | null;
  bytesProcessed: number;
  bytesTotal: number;
  errorMessage: string | null;
}

export type CacheCleanupReason =
  | 'under_limit'
  | 'freed'
  | 'no_victims'
  | 'delete_failed'
  | 'protected_over_limit'
  | 'busy'
  | 'concurrent'
  | 'error';

export interface QuotaHolder {
  name: string;
  bytes: number;
}

export interface CacheCleanupResult {
  beforeBytes: number;
  afterBytes: number;
  freedBytes: number;
  deletedEntries: number;
  stillOverLimit: boolean;
  reason: CacheCleanupReason;
  protectedBytes: number;
  reclaimableBytes: number;
  otherBytes: number;
  holders: QuotaHolder[];
}

export interface GtaPathInfo {
  resolvedPath: string;
  overridePath: string | null;
  autoDetectedPath: string;
  overrideActive: boolean;
  valid: boolean;
}

export interface UserBuildDto {
  id: string;
  hntCode: string;
  name: string;
  authorUserId: string | null;
  authorUsername: string;
  reduxId: string;
  gunpackId: string;
  reduxNameSnapshot: string;
  gunpackNameSnapshot: string;
  gunSlotsJson: string;
  armorJson: string | null;
  arenaJson: string | null;
  minimapJson: string | null;
  reticleJson: string | null;
  soundsJson: string | null;
  downloadCount: number;
  viewCount: number;
  createdAt: string | null;
  updatedAt: string | null;

  devicesJson: string | null;
  sensitivity: number | null;
  dpi: number | null;
  resolution: string | null;
  videoUrl: string | null;
  settingsXmlUrl: string | null;
  description: string;
  tier: number | null;

  status: 'pending' | 'approved' | 'rejected';
  submittedForReview: boolean;
  reviewedBy: string | null;
  reviewedAt: string | null;
  rejectReason: string | null;

  coverUrl: string | null;

  family?:                    string | null;
  categoryLabel?:             string | null;
  fpsAvg?:                    number | null;
  monitorHz?:                 number | null;
  adminNotes?:                string | null;
}

export interface BuildDevices {
  mouse?:      { name: string };
  keyboard?:   { name: string };
  monitor?:    { name: string; hz?: number };
  headset?:    { name: string };
  surface?:    { name: string };
  audioInput?: { name: string };
  graphics?:   { name: string };
  processor?:  { name: string };
}

export interface AuthResult {
  token: string;
  role: string;
  username: string | null;
  tester?: boolean;
}

export interface BetaGate {
  ok: boolean;
  error: string | null;
}

export interface PromoCheck {
  ok: boolean;
  display: string | null;
}

export interface ModmakerCard {
  promo: string;
  display: string;
  card: string | null;
  mods: number;
  downloads: number;

  cardx?: string;
  cardy?: string;
  cards?: string;
}
export interface ModmakersList { ok: boolean; makers: ModmakerCard[] }

export interface ModmakerMod {
  kind: 'redux' | 'gunpack';
  id: string;
  name: string;
  cover: string;
  downloads: number;
  month: number | null;
  added: string;
}
export interface ModmakerDetail {
  ok: boolean;
  promo?: string;
  display?: string;
  page?: Record<string, string>;
  card?: string | null;
  mods?: ModmakerMod[];
  followers?: number;
}

export interface ModmakerFeed {
  ok: boolean;
  follows?: { promo: string; display: string; fresh: number; due: boolean }[];
  error?: string;
}

export interface ModmakerMap {
  ok: boolean;
  map?: { kind: 'redux' | 'gunpack'; id: string; promo: string; display: string }[];
}

export interface AdminWebAuthResult {
  success: boolean;
  nick: string | null;
  error: string | null;
}

export interface ServerStatus {
  reachable: boolean;
  provisioned: boolean;
  message: string;
}

export interface AppUpdateInfo {
  updateAvailable: boolean;
  required: boolean;
  currentVersion: string;
  latestVersion: string | null;
  installerUrl: string | null;
  releaseNotes: string | null;
  sizeBytes: number | null;
  sha256: string | null;
  publishedAt: string | null;
  errorMessage: string | null;
}

export interface AppUpdateInstallResult {
  success: boolean;
  errorMessage: string | null;
  installerPath: string | null;
}

export interface UserProfile {
  id:        string;
  username:  string;
  email:     string | null;
  role:      string;
  avatarUrl: string | null;
  createdAt: string;
}

export interface InstallHistoryEntry {
  userId:      string;
  reduxId:     string;
  name:        string;
  author:      string;
  previewUrl:  string | null;
  installedAt: string;
}

export interface GunpackBatchEntry {
  folderName: string;
  folderPath: string;
  rpfPath:    string | null;
  imagePath:  string | null;
}

export interface ReduxReview {
  id:         string;
  reduxId:    string;
  userId:     string;
  username:   string;
  role:       string;
  avatarUrl:  string | null;
  rating:     number;
  body:       string;
  createdAt:  string;
}

export interface UserBuildReview {
  id:          string;
  userBuildId: string;
  userId:      string;
  username:    string;
  role:        string;
  avatarUrl:   string | null;
  rating:      number;
  body:        string;
  createdAt:   string;
}

export type Language = 'ru' | 'en' | 'pl';

export type AccentColor = 'violet' | 'blue' | 'slate' | 'aqua' | 'emerald' | 'rose' | 'amber';

export type Background = 'cubes' | 'aurora' | 'grid' | 'off';

export interface AppSettings {
  language: Language;
  accentColor: AccentColor;
  background: Background;
  polygonsEnabled: boolean;
  sidebarCollapsed: boolean;
}

export type BackupPhase =
  | 'detecting'
  | 'hashing_user_update'
  | 'comparing'
  | 'snapshot_user_update'
  | 'downloading_clean_update'
  | 'writing_working_update'
  | 'snapshot_dlc'
  | 'downloading_clean_dlc'
  | 'writing_working_dlc'
  | 'writing_manifest'
  | 'done'
  | 'error';

export interface BackupProgress {
  phase: BackupPhase;
  percent: number;
  fileName: string | null;
  bytesProcessed: number | null;
  bytesTotal: number | null;
  errorCode: string | null;
  errorMessage: string | null;
}

export interface BackupStatus {
  manifestExists: boolean;
  cleanUpdatePresent: boolean;
  cleanDlcPresent: boolean;
  snapshotUpdatePresent: boolean;
  snapshotDlcPresent: boolean;
  lastBackupAt: string | null;
  knownExeVersion: string | null;
}

export interface BackupResult {
  success: boolean;
  hadDirtyUpdate: boolean;
  versionUnsupported: boolean;
  errorCode: string | null;
  errorMessage: string | null;

  lockers?: LockerProcess[] | null;
}

export interface LockerProcess {
  pid: number;
  processName: string;
  friendlyName: string | null;
}

export interface AdminConfig {
  r2Endpoint: string;
  r2Bucket: string;
  r2PublicUrl: string;
  r2AccessKey: string;
  r2SecretKey: string;
  cleanUpdateRpfPath: string;
  gtaPathOverride: string | null;

  workDirOverride: string | null;
  supabaseServiceKey: string;
  adminApiToken: string;
}

export interface TestConnectionResult {
  success: boolean;
  message: string;
  objectCount: number | null;
}

export interface ComponentInfo {
  isFound: boolean;
  sourceRpf: string;
  internalPaths: string[];
  flags: string[];
  glbUrl?: string | null;
}

export interface ReduxAnalysis {
  resolvedUpdateRpfPath: string;
  sizeBytes: number;
  targetGtaVersion: string;
  components: Record<string, ComponentInfo>;
  tempWorkDir: string;
  sourceSha256: string;
}

export interface R2Urls {
  patch: string | null;
  components: Record<string, string>;
  manifest: string | null;
  componentMap: string | null;
  contentInfo: string | null;
}

export interface ReduxVersion {
  id:               string;
  reduxId:          string;
  slot:             number;
  label:            string;
  patchUrl:         string | null;
  patchSizeBytes:   number;
  patchSha256:      string | null;
  sourceSha256:     string | null;
  targetGtaVersion: string | null;
  components:       Record<string, ComponentInfo>;
  componentUrls:    Record<string, string>;
  manifestUrl:      string | null;
  componentMapUrl:  string | null;
  contentInfoUrl:   string | null;
  createdAt:        string;
  updatedAt:        string;
}

export interface FeaturedPick {
  slotIndex: number;
  reduxId:   string;
  updatedAt: string;
}

export interface DuplicateHashMatch {
  reduxId:   string;
  reduxName: string;
  versionId: string;
  slot:      number;
  label:     string;
}

export interface ReduxItem {
  id: string;
  name: string;
  author: string;
  authorLink: string;
  description: string;
  videoUrl: string;
  previewUrl: string;
  galleryUrls: string[];
  r2Urls: R2Urls | null;
  patchSizeBytes: number;
  targetGtaVersion: string;
  supportedServers: string[];
  isVerified: boolean;
  components: Record<string, ComponentInfo>;
  uploadedAt: string;
  uploadedBy: string;
  status: 'published' | 'hidden';
  viewerPriority: number;
  downloadCount: number;
  tagNew: boolean;
  tagBest: boolean;
  armorStandaloneInstallHidden: boolean;
  componentScreenshots: Record<string, string>;
}

export type QueueItemStatus = 'pending' | 'processing' | 'done' | 'error';
export type QueueItemPhase = 'building' | 'uploading' | 'registering' | null;

export interface VersionSpec {
  slot:                number;
  label:               string;
  sourceUpdateRpfPath: string;
  tempWorkDir:         string;
  sizeBytes:           number;
  targetGtaVersion:    string;
  components:          Record<string, ComponentInfo>;
  sourceSha256:        string;
}

export interface QueueItem {
  tempId: string;
  metadata: ReduxItem;
  sourceUpdateRpfPath: string;
  tempWorkDir: string;
  uploadToR2: boolean;
  status: QueueItemStatus;
  percent: number | null;
  currentPhase: QueueItemPhase;
  errorMessage: string | null;
  addedAt: string;
  versions: VersionSpec[] | null;
  appendToReduxId?: string | null;
}

export interface InjectResult {
  success: boolean;
  errorMessage: string | null;
  workDir: string | null;
}

export interface BackpackStatus {
  state: 'vanilla' | 'removed' | 'removed-foreign' | 'foreign' | 'missing';
  sizeBytes: number;
  backupAvailable: boolean;
  legacyOverlay: boolean;
  gtaFound: boolean;
}

export interface CustomSkinApplied {
  internalName: string;
  displayName: string;
  packId: string;
}

export interface CarLogosStatus {
  installed: boolean;
  foreignPresent: boolean;
  foreignHits: string[];
}

export type NoTracerCategory = 'normal' | 'vehicle' | 'mk2ammo';

export interface TracerStudioState {
  enabled: boolean;
  settings: string;
}

export interface NoTracerState {
  enabled: boolean;
  categories: NoTracerCategory[];
  keepSnipers: boolean;
}

export interface CurrentArmorInfo {
  id:     string;
  name:   string;
  glbUrl: string | null;
  kind:   string;
}

export interface CurrentMinimapInfo {
  kind: 'redux' | 'library';
  id:   string;
  name: string;
}

export interface GraphicsModInfo {
  id: string;
  name: string;
  variantLabel: string;
}

export interface RoadsFixStatus {
  vendor: 'nvidia' | 'amd' | 'other' | 'unknown' | string;
  applied: boolean;
  detectable: boolean;
  detail: string | null;
}

export interface CurrentReticleInfo {
  kind: 'redux' | 'library' | 'custom';
  id:   string;
  name: string;
}

export interface ReticleSpec {
  dot:            boolean;
  dotSize:        number;
  gap:            number;
  length:         number;
  thickness:      number;
  tilt:           number;
  outline:        boolean;
  outlineWidth:   number;
  opacity:        number;
  scale:          number;
  colorMain:      string;
  colorAds:       string;
  permanent:      boolean;
  hipfireSeconds: number;
  code:           string;
  ring:           boolean;
  ringRadius:     number;
  ringThickness:  number;
  weaponOverrides?: ReticleWeaponOverride[] | null;
}

export interface MinimapTweaks {
  digits:           boolean;
  digitsHpColor:    string | null;
  digitsArmorColor: string | null;
  digitsX:          number;
  digitsY:          number;
  digitsScale:      number;
  digitsHpDx:       number;
  digitsHpDy:       number;
  digitsArmorDx:    number;
  digitsArmorDy:    number;
  digitsBigDx:      number;
  digitsBigDy:      number;
  damagePopup:      boolean;
  damageColor:      string;
  healPopup:        boolean;
  healColor:        string;
  popupSize:        number;
  popupSeconds:     number;
  popupX:           number;
  popupY:           number;
  lowHpThreshold:   number | null;
  lowHpColor:       string | null;
  hitAlpha:         number | null;
  hitFadeSeconds:   number | null;
  hitScale:         number | null;
  barPosition:      'default' | 'top' | 'bottom' | 'left' | 'right';
  barOffsetX:       number;
  barOffsetY:       number;
  armorPopup:       boolean;
  armorPopupColor:  string;
  armorPopupX:      number;
  armorPopupY:      number;
  hitPngPath:       string | null;
  customText:       string | null;
  customTextColor:  string;
  customTextX:      number;
  customTextY:      number;
  customTextScale:  number;
  barScale:         number;
  barHpColor:       string | null;
  barArmorColor:    string | null;
  barHpTroughColor:    string | null;
  barArmorTroughColor: string | null;
  barPulseLowHp:    boolean;
  barHpGradient:    boolean;
  barGradFullColor: string | null;
  barGradMidColor:  string | null;
  barGradLowColor:  string | null;
  barScaleY:        number | null;
  hideNorth:        boolean;
  digitsFont:       string | null;
  hitX:             number | null;
  hitY:             number | null;
  arrowPngPath:     string | null;
  gpsPngPath:       string | null;
}

export interface MinimapFontOption {
  id:    string;
  title: string;
  face:  string;
}

export interface HotSwapStatus {
  enabled:    boolean;
  supported:  boolean;
  frozen:     boolean;
  armed:      boolean;
  agentAlive: boolean;
  imageRoot:  string | null;
  note:       string | null;
  method:     number;
  manualTrigger: boolean;
  stale?: boolean;
  staleNote?: string | null;
  staleAtUtc?: string | null;
}

export interface MinimapSave {
  name: string;
  savedAt: string;
  tweaks: MinimapTweaks;
}

export interface HotSwapLogTail {
  path: string | null;
  text: string;
}

export interface DownloadLogTail {
  path: string | null;
  text: string;
}

export interface MinimapFontState {
  installed:  boolean;
  slot:       string | null;
  sourceFile: string | null;
}

export interface MinimapLayoutPreset {
  ratio:     string;
  placement: string;
  posX:      number;
  posY:      number;
  sizeX:     number;
  sizeY:     number;
}

export interface MinimapScreen {
  width:            number;
  height:           number;
  aspectRatio:      number;
  windowed:         boolean;
  fromSettingsXml:  boolean;
  settingsPath:     string;
}

export type ReticleWeaponGroup = 'pistol' | 'smg' | 'rifle' | 'shotgun';

export interface ReticleWeaponOverride {
  weapon:         ReticleWeaponGroup;
  dot:            boolean;
  dotSize:        number;
  gap:            number;
  length:         number;
  thickness:      number;
  tilt:           number;
  outline:        boolean;
  outlineWidth:   number;
  ring:           boolean;
  ringRadius:     number;
  ringThickness:  number;
  colorMain?:     string | null;
}

export interface CurrentSoundPackInfo {
  id:   string;
  name: string;
}

export interface DlcArmorInspectionResult {
  dlcRpfPath:   string;
  candidates:   DlcArmorCandidate[];
  warnings:     string[];
  errorMessage: string | null;
}

export interface DlcArmorCandidate {
  yddInternalPath:      string;
  yddName:              string;
  drawableInternalName: string | null;
  parseError:           string | null;
  samplerExpectations:  DlcArmorSamplerExpectation[];
  candidateYtds:        DlcArmorYtd[];
  missingExpectedDiffuses: string[];
  hasNameMismatch:         boolean;
  suggestedRename:         DlcArmorRenameSuggestion | null;
  previewGlbUrl:           string | null;
}

export interface DlcArmorSamplerExpectation {
  samplerName:         string;
  expectedTextureName: string;
}

export interface DlcArmorYtd {
  internalPath:      string;
  fileName:          string;
  innerTextureNames: string[];
  parseError:        string | null;
}

export interface DlcArmorRenameSuggestion {
  ytdInternalPath: string;
  oldTextureName:  string;
  newTextureName:  string;
}

export interface DlcArmorImportRequest {
  dlcRpfPath:           string;
  yddInternalPath:      string;
  name:                 string;
  author?:              string | null;
  applyAutoFix:         boolean;
  renameYtdFileName?:   string | null;
  renameOldTextureName?: string | null;
  renameNewTextureName?: string | null;
  extraSources?:        DlcArmorExtraSource[] | null;
}

export interface DlcArmorExtraSource {
  dlcRpfPath:      string;
  yddInternalPath: string;
}

export interface DlcArmorImportResult {
  success:      boolean;
  armorId:      string | null;
  armorRpfUrl:  string | null;
  glbUrl:       string | null;
  errorMessage: string | null;
}

export interface ArmorLibraryItem {
  id:             string;
  name:           string;
  author:         string;
  description:    string;
  glbUrl:         string;
  previewUrl:     string | null;
  previewVariants?: string[] | null;
  armorRpfUrl:    string;
  internalPath:   string;
  downloadCount:  number;
  viewerPriority: number;
  isVerified:     boolean;
  status:         string;
  uploadedAt:     string;
  supportedServers: string[];
  hasMale?:   boolean;
  hasFemale?: boolean;
}

export interface GtaVersion {
  exeVersion: string;
  updateRpfSize: number;
  updateRpfSha256: string;
  cleanUpdateUrl: string;
  notes: string;
  createdAt: string;
  updatedAt: string;
}

export interface GtaVersionAutoFill {
  exeVersion: string;
  updateRpfSize: number;
  updateRpfSha256: string;
}

export interface LibraryComponent {
  id: string;
  type: string;
  name: string;
  author: string;
  description: string;
  r2Url: string;
  sha256: string;
  sizeBytes: number;
  sourceRpfVersion: string;
  uploadedBy: string;
  uploadedAt: string;
   previewUrl: string;
  galleryUrls: string[];
  previewVideoUrl: string;
}

export interface LibraryUpload {
  workDir: string;
  componentName: string;
  name: string;
  author: string;
  description: string;
}

export interface LibraryPatch {
  id: string;
  name: string;
  author: string;
  description: string;
}

export interface GenericSettingBridge {
  kind: 'default' | 'library' | 'import' | 'armorLibrary' | 'clear' | 'custom';
  libraryItemId?: string;
  donorReduxId?: string;
  donorVersionId?: string | null;
  armorLibraryId?: string;
  customSpecJson?: string;
}

export interface MinimapSettingBridge {
  enabled: boolean;
  hpColor: string;
  armorColor: string;
  aspectRatio: string;
  position: string;
  pngOverlayPath: string | null;
  importedFromReduxId: string | null;
  donorVersionId: string | null;
  libraryItemId?: string | null;
  tweaks?: MinimapTweaks | null;
}

export interface TracerSettingBridge {
  sourceKind: 'default' | 'model' | 'import';
  modelFolderName: string | null;
  donorReduxId: string | null;
  r: number;
  g: number;
  b: number;
  donorVersionId: string | null;
  takeDonorBlood?: boolean;
  useCleanEffects?: boolean;
  overrideColor?: boolean;
}

export interface Gunpack {
  id:                string;
  name:              string;
  author:            string | null;
  authorLink:        string | null;
  description:       string | null;

  weaponsRpfUrl:     string;
  weaponsRpfSize:    number;
  weaponsRpfSha256:  string;

  packZipUrl:        string | null;
  packZipSize:       number | null;
  packZipSha256:     string | null;

  manifestUrl:       string | null;

  coverKind:         'image' | 'youtube';
  coverUrl:          string | null;
  galleryUrls:       string[];

  status:            'published' | 'hidden';
  isVerified:        boolean;
  viewerPriority:    number;
  downloadCount:     number;

  uploadedAt:        string;
  uploadedBy:        string | null;
  updatedAt:         string;
  notes:             string | null;
}

export interface GunpackVariant {
  id:                string;
  gunpackId:         string;
  name:              string;

  weaponsRpfUrl:     string;
  weaponsRpfSize:    number;
  weaponsRpfSha256:  string;

  packZipUrl:        string | null;
  packZipSize:       number | null;
  packZipSha256:     string | null;

  manifestUrl:       string | null;
  coverUrl:          string | null;

  isDefault:         boolean;
  sortOrder:         number;
  createdAt:         string;
  updatedAt:         string;

  gunPreviews?:      Record<string, VariantGun> | null;
}

export interface VariantGun {
  glb?:           string | null;
  webp?:          string | null;
  displayName?:   string | null;
  category?:      string | null;
  weaponPrefix?:  string | null;
  files?:         string[] | null;
  sizeBytes?:     number | null;
  sortOrder?:     number | null;
}

export interface GunpackVariantPatch {
  name?:       string;
  coverUrl?:   string;
  isDefault?:  boolean;
  sortOrder?:  number;
}

export interface GunpackGun {
  id:            string;
  gunpackId:     string;
  baseName:      string;
  weaponPrefix:  string;
  category:      string;
  displayName:   string | null;
  glbUrl:        string | null;
  previewUrl:    string | null;
  files:         string[];
  sizeBytes:     number;
  isHidden:      boolean;
  sortOrder:     number;
  createdAt:     string;
}

export interface GunpackFlatGun {
  id:            string;
  gunpackId:     string;
  baseName:      string;
  weaponPrefix:  string;
  category:      string;
  displayName:   string | null;
  glbUrl:        string | null;
  previewUrl:    string | null;
}

export interface GunpackWhitelistEntry {
  internalName:    string;
  displayName:     string;
  category:        string;
  weaponPrefix:    string;
  isSmgOverride:   boolean;
  sortOrder:       number;
  previewUrl:      string | null;
}

export interface CustomGun {
  id:            string;
  ownerId:       string;
  ownerName:     string;
  baseName:      string;
  weaponPrefix:  string;
  internalName:  string;
  displayName:   string;
  description:   string;
  category:      string;
  glbUrl:        string | null;
  previewUrl:    string | null;
  downloadCount: number;
  createdAt:     string;
  updatedAt:     string;
  mine:          boolean;
  status:             CustomGunStatus;
  submittedForReview: boolean;
  reviewedAt:         string | null;
  rejectReason:       string | null;
  userGunpackId?:     string | null;
}

export type CustomGunStatus = 'published' | 'pending' | 'rejected' | 'removed';

export interface CustomGunLimits {
  used: number;
  max:  number;
}

export interface CustomGunPatch {
  displayName?: string;
  description?: string;
  category?:    string;
}

export type CustomGunSort = 'new' | 'downloads';

export interface WorkshopTexture {
  name:    string;
  width:   number;
  height:  number;
  role:    'diffuse' | 'normal' | 'spec' | 'other';
  dataUrl: string;
}

export interface WorkshopSession {
  draftId:      string;
  customGunId:  string | null;
  displayName:  string;
  baseName:     string;
  weaponPrefix: string;
  category:     string;
  glbUrl:       string | null;
  textures:     WorkshopTexture[];
}

export type WorkshopFlow = 'standard' | 'packbase' | 'ownpack';

export interface WorkshopOpenRequest {
  customGunId?:      string | null;
  baseInternalName?: string | null;
  flow?:        WorkshopFlow;
  pack?:        string;
  gun?:         string;
  packName?:    string;
  gunName?:     string;
  session?:     string;
  ownPackId?:   string;
  ownPackName?: string;
}

export interface WorkshopFlowLimits {
  standardMaxPerGun:  number;
  standardUsedPerGun: Record<string, number>;
  packBaseUsed: number;
  packBaseMax:  number;
  ownPackUsed:  number;
  ownPackMax:   number;
  ownPackGunCap: number;
}

export interface UserGunpack {
  id:            string;
  ownerId:       string;
  ownerName:     string;
  name:          string;
  downloadCount: number;
  createdAt:     string;
  guns:          CustomGun[];
}

export interface WorkshopPublishMeta {
  displayName: string;
  description: string;
  category:    string;
}

export interface GunpackPatch {
  name?:           string;
  author?:         string;
  authorLink?:     string;
  description?:    string;
  status?:         'published' | 'hidden';
  isVerified?:     boolean;
  viewerPriority?: number;
  coverKind?:      'image' | 'youtube';
  coverUrl?:       string;
  galleryUrls?:    string[];
  notes?:          string;
}

export interface GunpackGunPatch {
  displayName?: string;
  isHidden?:    boolean;
  sortOrder?:   number;
}

export type GunpackQueueStatus = 'pending' | 'processing' | 'done' | 'error';
export type GunpackQueuePhase =
  | 'analyzing' | 'parsing' | 'filtering' | 'converting'
  | 'compressing' | 'rendering' | 'packing' | 'uploading' | 'registering'
  | null;

export interface GunpackQueueItem {
  tempId:           string;
  metadata:         Gunpack;
  sourceDlcRpfPath: string;
  tempWorkDir:      string;
  uploadToR2:       boolean;
  status:           GunpackQueueStatus;
  percent:          number | null;
  currentPhase:     GunpackQueuePhase;
  errorMessage:     string | null;
  addedAt:          string;
  warnings:         string[] | null;
}

export interface GunpackUploadRequest {
  sourceDlcRpfPath: string;
  metadata:         Gunpack;
  uploadToR2:       boolean;
}

export interface GunpackInstalledState {
  activeGunpackId:   string | null;
  activeGunpackName: string | null;
  weaponsRpfSha256:  string | null;
  installedAt:       string | null;
}

export interface GunpackVerifyReport {
  ok:              boolean;
  targetDlcExists: boolean;
  rpfPresentInDlc: boolean;
  stateSha:        string | null;
  actualSha:       string | null;
  summary:         string;
}

export interface GunpackInstallConflict {
  internalName:           string;
  displayName:            string;

  gunpackPreviewUrl:      string | null;
  gunpackGlbUrl:          string | null;
  gunpackPackName:        string;

  selectedPreviewUrl:     string | null;
  selectedGlbUrl:         string | null;
  selectedFromPackId:     string;
  selectedFromPackName:   string;
}

export interface SelectedGun {
  gunpackId:     string;
  gunpackName:   string;
  gunId:         string;
  internalName:  string;
  displayName:   string;
  baseName:      string;
  weaponPrefix:  string;
  files:         string[];
  packZipUrl:    string;
  packZipSha256: string;
  selectedAt:    string;
}

export interface SelectedGunsVerifyReport {
  ok:              boolean;
  stateGunsCount:  number;
  targetDlcExists: boolean;
  rpfPresentInDlc: boolean;
  stateSha:        string | null;
  actualSha:       string | null;
  summary:         string;
}

export interface SelectedGunsInstallProgress {
  internalName:  string | null;
  phase:         string;
  percent:       number;
  errorMessage:  string | null;
  detailMessage: string | null;
}

export interface CustomizationDraftBridge {
  reduxId: string;
  bloodfx:   GenericSettingBridge;
  crosshair: GenericSettingBridge;
  timecycle: GenericSettingBridge;
  armor:     GenericSettingBridge;
  arena:     GenericSettingBridge;
  minimap:   MinimapSettingBridge;
  tracers:   TracerSettingBridge;
  baseVersionId?: string;
  bigMapEnabled?: boolean;
  bigMapId?:   string | null;
  bigMapName?: string | null;
}

export interface HntSelectedGun {
  gunpackId:    string;
  gunpackName:  string;
  internalName: string;
  displayName:  string;
}

export interface HntExtras {
  customizeDraft?: CustomizationDraftBridge;
}

export interface HntComponentRef {
  source: string;
  id:     string;
  name:   string | null;
}

export interface HntPayload {
  reduxId:        string | null;
  reduxVersionId: string | null;
  reduxName:      string | null;
  reduxAuthor:    string | null;
  gunpackId:      string | null;
  gunpackName:    string | null;
  selectedGuns:   HntSelectedGun[];
  extras:         HntExtras | null;
  armor?:   HntComponentRef | null;
  minimap?: HntComponentRef | null;
  reticle?: HntComponentRef | null;
  sounds?:  HntComponentRef | null;
  bigMap?:  HntComponentRef | null;
}

export interface HntCode {
  code:             string;
  payload:          HntPayload;
  createdBy:        string;
  createdAt:        string;
  lastDownloadedAt: string;
  downloadsCount:   number;
}

export interface HntInstallStepResult {
  skipped:      boolean;
  success:      boolean;
  errorMessage: string | null;
}

export interface HntImportResult {
  success:          boolean;
  errorMessage:     string | null;
  reduxStep:        HntInstallStepResult | null;
  gunpackStep:      HntInstallStepResult | null;
  selectedGunsStep: HntInstallStepResult | null;
  componentsStep?:  HntInstallStepResult | null;
}

export type GtaCpuBias = 'cpu' | 'gpu' | 'balanced';
export type GtaPresetStatus = 'published' | 'hidden';

export interface PresetReactions {
  likes:      number;
  dislikes:   number;
  myReaction: number;
}

export interface AccountStats {
  accountNo: number;
  downloads: number;
}

export interface GtaPreset {
  id:                  string;
  name:                string;
  description:         string;
  author:              string;
  xmlUrl:              string;
  xmlSizeBytes:        number;
  xmlSha256:           string;
  expectedFpsLow:      number | null;
  expectedFpsHigh:     number | null;
  baselineHwLabel:     string | null;
  computedGainPercent: number;
  cpuBias:             GtaCpuBias;
  isTournament:        boolean;
  status:              GtaPresetStatus;
  viewerPriority:      number;
  downloadCount:       number;
  uploadedBy:          string;
  uploadedAt:          string;
  updatedAt:           string;
}

export interface GtaPresetUploadRequest {
  sourceXmlPath:    string;
  name:             string;
  description:      string;
  author:           string;
  expectedFpsLow:   number | null;
  expectedFpsHigh:  number | null;
  baselineHwLabel:  string | null;
  isTournament:     boolean;
  viewerPriority:   number;
  status:           GtaPresetStatus;
}

export interface GtaPresetPatch {
  name?:             string;
  description?:      string;
  author?:           string;
  expectedFpsLow?:   number | null;
  expectedFpsHigh?:  number | null;
  baselineHwLabel?:  string | null;
  isTournament?:     boolean;
  status?:           GtaPresetStatus;
  viewerPriority?:   number;
}

export interface GtaPresetApplyResult {
  success:        boolean;
  errorMessage:   string | null;
  targetPath:     string;
  backupPath:     string | null;
  gameWasRunning: boolean;
}

export interface GtaSettingsAnalysis {
  gainPercent:   number;
  cpuBias:       GtaCpuBias;
  contributions: GtaSettingContribution[];
}

export interface GtaSettingContribution {
  key:         string;
  gainPercent: number;
  category:    string;
}

export interface GtaDisplaySettings {
  screenWidth:  number;
  screenHeight: number;
  refreshRate:  number;
  aspectRatio:  number;
  windowed:     number;
  vSync:        boolean;
}

export interface GtaQualitySettings {
  textureQuality:  number;
  shaderQuality:   number;
  waterQuality:    number;
  particleQuality: number;
  postFx:          number;
  shadowQuality:   number;
}

export interface GtaAntiAliasingSettings {
  fxaa:           boolean;
  txaa:           boolean;
  msaa:           number;
  reflectionMsaa: number;
}

export interface GtaWorldSettings {
  cityDensity:       number;
  pedVariety:        number;
  vehicleVariety:    number;
  lodScale:          number;
  vehicleLodBias:    number;
  pedLodBias:        number;
  grassQuality:      number;
  reflectionQuality: number;
  shadowDistance:    number;
  maxLodScale:       number;
}

export interface GtaAdvancedSettings {
  tessellation:         number;
  anisotropicFiltering: number;
  ssao:                 number;
  shadowSoftShadows:    number;
  shadowSplitZStart:    number;
  shadowSplitZEnd:      number;
  ultraShadows:         boolean;
  shadowParticles:      boolean;
  shadowLongShadows:    boolean;
  reflectionMipBlur:    boolean;
  dxVersion:            number;
  dof:                  boolean;
  hdStreaming:          boolean;
  motionBlur:           number;
  fogVolumes:           boolean;
}

export interface GtaSettingsModel {
  display:      GtaDisplaySettings;
  quality:      GtaQualitySettings;
  antiAliasing: GtaAntiAliasingSettings;
  world:        GtaWorldSettings;
  advanced:     GtaAdvancedSettings;
}

export interface OptimizationOption {
  idx:             number;
  name:            string;
  previewUrl:      string;
  fpsLabel:        string;
  settingsCount:   number;
}

export interface OptimizationGroup {
  key:         string;
  style:       'toggle' | 'slider';
  title:       string;
  description: string;
  iconUrl:     string;
  resetIndex:  number;
  beta:        boolean;
  options:     OptimizationOption[];
}

export interface OptimizationCatalog {
  groups: OptimizationGroup[];
  problems: string[];
}

export interface OptimizationSelection {
  groupKey:  string;
  optionIdx: number | null;
}

export interface OptimizationKeyChange {
  key:      string;
  from:     string | null;
  to:       string;
  groupKey: string;
}

export interface OptimizationApplyResult {
  success:          boolean;
  errorMessage:     string | null;
  changes:          OptimizationKeyChange[];
  warnings:         string[];
  targetPath:       string;
  backupPath:       string | null;
  gameWasRunning:   boolean;
  baselineCaptured: boolean;
}

export interface OptimizationScanProgress {
  percent: number;
  stage:   'catalog' | 'settings' | 'archive' | 'gamefiles' | 'done';
  detail:  string;
}

export interface OptimizationInteriorState {
  groupKey:  string;
  optionIdx: number | null;
  marker:    string | null;
}

export interface OptimizationResolution {
  selections:   Record<string, number | null>;
  unmappedKeys: string[];
  customGroups: string[];
  markers?:     Record<string, string> | null;
}

export interface GtaSettingsReadResult {
  model:         GtaSettingsModel;
  existedOnDisk: boolean;
  sourcePath:    string;
}

export interface BigMap {
  id:               string;
  name:             string;
  author:           string;
  authorLink:       string;
  description:      string;
  previewUrl:       string;
  galleryUrls:      string[];
  videoUrl:         string;
  supportedServers: string[];
  sizeBytes:        number;
  packFormat:       string;
  downloadCount:    number;
  isVerified:       boolean;
}

export interface BigMapReview {
  id:         string;
  mapId:      string;
  userId:     string;
  username:   string;
  role:       string;
  avatarUrl:  string | null;
  rating:     number;
  body:       string;
  createdAt:  string;
}

export interface BigMapState {
  enabled:         boolean;
  id:              string | null;
  name:            string | null;
  foreignDetected: boolean;
}

export interface BigMapAnalysis {
  installable: boolean;
  packFormat:  string;
  targetPaths: string[];
  warnings:    string[];
  photoPaths:  string[];
  totalBytes:  number;
}

export interface BigMapPublishRequest {
  sourcePath:       string;
  name:             string;
  author:           string;
  authorLink:       string;
  description:      string;
  supportedServers: string[];
  photoPaths:       string[];
  videoUrl?:        string | null;
  existingId?:      string | null;
}

export interface LegitFieldDiff {
  owner:        string;
  field:        string;
  cleanValue:   string;
  modValue:     string;
  deltaPercent: number | null;
  isRed:        boolean;
}

export interface LegitFileFinding {
  path:          string;
  change:        'changed' | 'added' | 'deleted';
  severity:      'danger' | 'warning' | 'visual' | 'neutral';
  categoryLabel: string;
  note:          string;
  formatOnly:    boolean;
  size:          number;
  fieldDiffs:    LegitFieldDiff[];
}

export interface LegitReport {
  verdict:        'safe' | 'mixed' | 'danger';
  verdictTitle:   string;
  verdictText:    string;
  verdictReasons: string[];
  source:         string;
  checkedAt:      string;
  dangerCount:    number;
  warningCount:   number;
  changedCount:   number;
  addedCount:     number;
  deletedCount:   number;
  findings:       LegitFileFinding[];
  unverified:     string[];
  checkedCount:   number;
}

export interface LegitCheckProgress {
  percent:     number;
  stage:       'manifest' | 'download' | 'scan' | 'done';
  currentFile: string;
}

export interface Improvement {
  id: string;
  name: string;
  author: string;
  description: string;
  source: string;
  exclusiveGroup: string;
  category: string;
  previewUrl: string;
  videoUrl: string;
  galleryUrls: string[];
  sizeBytes: number;
  installed: boolean;
  slots: string[];
  popularity: number;
}
