using System.Text.Json;

namespace MiamiGraphics.Bridge;

public sealed record SystemInfoDto(
    string? GtaPath,
    string GpuName,
    string? GtaExeVersion,
    bool IsGtaFound
);

public sealed record GtaPathInfoDto(
    string ResolvedPath,
    string? OverridePath,
    string AutoDetectedPath,
    bool OverrideActive,
    bool Valid
);

public sealed record MirrorProbeResultDto(
    bool Success,
    string Mirror,
    double? SpeedMbPerSecond,
    string? ErrorMessage
);

public sealed record ServerRegionStatusDto(
    string Region,
    string Url,
    bool   IsConfigured
);

public sealed record ServerRegionPingDto(
    int? EuMs,
    int? RuMs
);

public sealed record DownloadSourceStatusDto(
    string Source,
    bool   QueueEnabled
);

public sealed record DownloadSourceEvalDto(
    bool    EuWorks,
    double? Mbps,
    bool    ZapretConfigured,
    bool    ZapretRestarted,
    string? Message
);

public sealed record ZapretApplyResultDto(
    bool Success,
    string? ErrorMessage,
    int DomainLinesAdded,
    int IpsetLinesAdded,
    string? ListsDir
);

public sealed record ZapretDetectDto(
    bool Installed,
    bool ConfiguredForUs,
    string? DetectedRoot
);

public sealed record RendererEnsureResultDto(
    bool Success,
    bool AlreadyInstalled,
    string RendererPath,
    long DownloadedBytes,
    string? ErrorMessage
);

public sealed record RendererProbeDto(
    string RendererPath,
    bool   BaseDirExists,
    bool   NodeExeExists,
    bool   RenderJsExists,
    bool   NodeModulesExists,
    long   NodeModulesSizeMb,
    string? NodeVersion,
    string? NodeError,
    bool   IsUsable,
    string? Summary,
    string? ActionableHint,
    bool   ChromiumInstalled = false
);

public sealed record RendererTestRenderDto(
    bool    Success,
    long    ElapsedMs,
    long?   OutputBytes,
    string? OutputPath,
    string? StdoutTail,
    string? StderrTail,
    string? ErrorMessage
);

public sealed record JreEnsureResultDto(
    bool Success,
    bool AlreadyInstalled,
    string JrePath,
    long DownloadedBytes,
    string? ErrorMessage
);

public sealed record AuthResultDto(string Token, string Role, string? Username, bool Tester);

public sealed record BetaGateDto(bool Ok, string? Error);

public sealed record PromoCheckDto(bool Ok, string? Display);

public sealed record AdminWebAuthResultDto(bool Success, string? Nick, string? Error);

public sealed record UserProfileDto(
    string   Id,
    string   Username,
    string?  Email,
    string   Role,
    string?  AvatarUrl,
    DateTime CreatedAt
);

public sealed record InstallHistoryEntryDto(
    string   UserId,
    string   ReduxId,
    string   Name,
    string   Author,
    string?  PreviewUrl,
    DateTime InstalledAt
);

public sealed record ServerStatusDto(
    bool   Reachable,
    bool   Provisioned,
    string Message
);

public sealed record AppUpdateInfoDto(
    bool UpdateAvailable,
    bool Required,
    string CurrentVersion,
    string? LatestVersion,
    string? InstallerUrl,
    string? ReleaseNotes,
    long? SizeBytes,
    string? Sha256,
    DateTime? PublishedAt,
    string? ErrorMessage = null
);

public sealed record AppUpdateInstallResultDto(
    bool Success,
    string? ErrorMessage,
    string? InstallerPath
);

public sealed record AppSettingsDto(
    string Language,
    string AccentColor,
    string Background,
    bool PolygonsEnabled,
    bool SidebarCollapsed
);

public sealed record BackupProgressDto(
    string Phase,
    int Percent,
    string? FileName,
    long? BytesProcessed,
    long? BytesTotal,
    string? ErrorCode,
    string? ErrorMessage
);

public sealed record BackupStatusDto(
    bool ManifestExists,
    bool CleanUpdatePresent,
    bool CleanDlcPresent,
    bool SnapshotUpdatePresent,
    bool SnapshotDlcPresent,
    DateTime? LastBackupAt,
    string? KnownExeVersion
);

public sealed record BackupResultDto(
    bool Success,
    bool HadDirtyUpdate,
    bool VersionUnsupported,
    string? ErrorCode,
    string? ErrorMessage,

    IReadOnlyList<LockerProcessDto>? Lockers = null
);

public sealed record LockerProcessDto(
    int Pid,
    string ProcessName,
    string? FriendlyName
);

public sealed record AdminConfigDto(
    string R2Endpoint,
    string R2Bucket,
    string R2PublicUrl,
    string R2AccessKey,
    string R2SecretKey,
    string CleanUpdateRpfPath,
    string? GtaPathOverride,
    string? WorkDirOverride = null,
    string SupabaseServiceKey = "",
    string AdminApiToken = ""
);

public sealed record TestConnectionResultDto(bool Success, string Message, int? ObjectCount);

public sealed record ComponentInfoDto(
    bool IsFound,
    string SourceRpf,
    string[] InternalPaths,
    string[] Flags,

    string? GlbUrl = null
);

public sealed record ReduxAnalysisDto(
    string ResolvedUpdateRpfPath,
    long SizeBytes,
    string TargetGtaVersion,
    Dictionary<string, ComponentInfoDto> Components,
    string TempWorkDir,

    string SourceSha256 = ""
);

public sealed record R2UrlsDto
{
    public string? Patch { get; set; }
    public Dictionary<string, string> Components { get; set; } = new();
    public string? Manifest { get; set; }
    public string? ComponentMap { get; set; }
    public string? ContentInfo { get; set; }
}

public sealed record ReduxVersionDto(
    Guid     Id,
    string   ReduxId,
    int      Slot,
    string   Label,
    string?  PatchUrl,
    long     PatchSizeBytes,
    string?  PatchSha256,
    string?  TargetGtaVersion,
    Dictionary<string, ComponentInfoDto> Components,
    Dictionary<string, string>           ComponentUrls,
    string?  ManifestUrl,
    string?  ComponentMapUrl,
    string?  ContentInfoUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt,

    string?  SourceSha256 = null
);

public sealed record FeaturedPickDto(
    int      SlotIndex,
    string   ReduxId,
    DateTime UpdatedAt
);

public sealed record DuplicateHashMatchDto(
    string ReduxId,
    string ReduxName,
    Guid   VersionId,
    int    Slot,
    string Label
);

public sealed record ReduxItemDto(
    string Id,
    string Name,
    string Author,
    string AuthorLink,
    string Description,
    string VideoUrl,
    string PreviewUrl,
    List<string> GalleryUrls,
    R2UrlsDto? R2Urls,
    long PatchSizeBytes,
    string PatchSha256,
    string TargetGtaVersion,
    List<string> SupportedServers,
    bool IsVerified,
    Dictionary<string, ComponentInfoDto> Components,
    DateTime UploadedAt,
    string UploadedBy,
    string Status,
    int ViewerPriority = 0,
    long DownloadCount = 0,
    bool TagNew = false,
    bool TagBest = false,
    bool ArmorStandaloneInstallHidden = false,
    Dictionary<string, string>? ComponentScreenshots = null
);

public sealed record ReduxReviewDto(
    string   Id,
    string   ReduxId,
    string   UserId,
    string   Username,
    string   Role,
    int      Rating,
    string   Body,
    DateTime CreatedAt,
    string?  AvatarUrl = null
);

public sealed record BigMapReviewDto(
    string   Id,
    string   MapId,
    string   UserId,
    string   Username,
    string   Role,
    int      Rating,
    string   Body,
    DateTime CreatedAt,
    string?  AvatarUrl = null
);

public sealed record UserBuildReviewDto(
    string   Id,
    string   UserBuildId,
    string   UserId,
    string   Username,
    string   Role,
    int      Rating,
    string   Body,
    DateTime CreatedAt,
    string?  AvatarUrl = null
);

public sealed record VersionSpecDto(
    int    Slot,
    string Label,
    string SourceUpdateRpfPath,
    string TempWorkDir,
    long   SizeBytes,
    string TargetGtaVersion,
    Dictionary<string, ComponentInfoDto> Components,
    string SourceSha256
);

public sealed record QueueItemDto(
    string TempId,
    ReduxItemDto Metadata,
    string SourceUpdateRpfPath,
    string TempWorkDir,
    bool UploadToR2,
    string Status,
    int? Percent,
    string? CurrentPhase,
    string? ErrorMessage,
    DateTime AddedAt,
    List<VersionSpecDto>? Versions = null,
    string? AppendToReduxId = null
);

public sealed record InjectResultDto(bool Success, string? ErrorMessage, string? WorkDir);

public sealed record BackpackStatusDto(
    string State,
    long SizeBytes,
    bool BackupAvailable,
    bool LegacyOverlay,
    bool GtaFound);

public sealed record CustomSkinAppliedDto(string InternalName, string DisplayName, string PackId);

public sealed record GtaVersionDto(
    string ExeVersion,
    long   UpdateRpfSize,
    string UpdateRpfSha256,
    string CleanUpdateUrl,
    string Notes,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    long?   GunsRpfSize   = null,
    string? GunsRpfSha256 = null
);

public sealed record GtaVersionAutoFillDto(
    string ExeVersion,
    long   UpdateRpfSize,
    string UpdateRpfSha256
);

public sealed record LibraryComponentDto(
    string Id,
    string Type,
    string Name,
    string Author,
    string Description,
    string R2Url,
    string Sha256,
    long   SizeBytes,
    string SourceRpfVersion,
    string UploadedBy,
    DateTime UploadedAt,
    string PreviewUrl = "",
    List<string>? GalleryUrls = null,
    string PreviewVideoUrl = ""
);

public sealed record LibraryUploadDto(
    string WorkDir,
    string ComponentName,
    string Name,
    string Author,
    string Description
);

public sealed record LibraryPatchDto(
    string Id,
    string Name,
    string Author,
    string Description
);

public sealed record GenericSettingDto(
    string Kind,
    string? LibraryItemId,
    string? DonorReduxId,
    Guid? DonorVersionId = null,
    string? ArmorLibraryId = null,
    string? CustomSpecJson = null
);

public sealed record ReticleSpecDto(
    bool Dot,
    double DotSize,
    double Gap,
    double Length,
    double Thickness,
    double Tilt,
    bool Outline,
    double OutlineWidth,
    int Opacity,
    int Scale,
    string ColorMain,
    string ColorAds,
    bool Permanent,
    double HipfireSeconds,
    string Code,
    bool Ring = false,
    double RingRadius = 10,
    double RingThickness = 1.5,
    ReticleWeaponOverrideDto[]? WeaponOverrides = null
);

public sealed record ReticleWeaponOverrideDto(
    string Weapon,
    bool Dot,
    double DotSize,
    double Gap,
    double Length,
    double Thickness,
    double Tilt,
    bool Outline,
    double OutlineWidth,
    bool Ring = false,
    double RingRadius = 10,
    double RingThickness = 1.5,
    string? ColorMain = null
);

public sealed record CurrentMinimapInfoDto(
    string Kind,
    string Id,
    string Name
);

public sealed record RoadsFixStatusDto(
    string Vendor,
    bool Applied,
    bool Detectable,
    string? Detail
);

public sealed record GraphicsModInfoDto(
    string Id,
    string Name,
    string VariantLabel
);

public sealed record CurrentReticleInfoDto(
    string Kind,
    string Id,
    string Name
);

public sealed record CurrentSoundPackInfoDto(
    string Id,
    string Name
);

public sealed record MinimapSettingDto(
    bool    Enabled,
    string  HpColor,
    string  ArmorColor,
    string  AspectRatio,
    string  Position,
    string? PngOverlayPath,
    string? ImportedFromReduxId,
    Guid?   DonorVersionId = null,
    string? LibraryItemId = null,
    int[]? RangeRingsMeters = null,
    MinimapTweaksDto? Tweaks = null
);

public sealed record MinimapSaveDto(
    string Name,
    string SavedAt,
    MinimapTweaksDto Tweaks
);

public sealed record MinimapTweaksDto(
    bool Digits = false,
    string? DigitsHpColor = null,
    string? DigitsArmorColor = null,
    double DigitsX = 10,
    double DigitsY = 103,
    double DigitsScale = 100,
    double DigitsHpDx = 0,
    double DigitsHpDy = 0,
    double DigitsArmorDx = 22,
    double DigitsArmorDy = 0,
    double DigitsBigDx = 0,
    double DigitsBigDy = 0,
    bool DamagePopup = false,
    string DamageColor = "#FF4040",
    bool HealPopup = false,
    string HealColor = "#34D399",
    double PopupSize = 18,
    double PopupSeconds = 1.0,
    double PopupX = 46,
    double PopupY = 34,
    int? LowHpThreshold = null,
    string? LowHpColor = null,
    int? HitAlpha = null,
    double? HitFadeSeconds = null,
    double? HitScale = null,
    string BarPosition = "default",
    double BarOffsetX = 0,
    double BarOffsetY = 0,
    bool ArmorPopup = false,
    string ArmorPopupColor = "#60A5FA",
    double ArmorPopupX = 46,
    double ArmorPopupY = 54,
    string? HitPngPath = null,
    string? CustomText = null,
    string CustomTextColor = "#FFFFFF",
    double CustomTextX = 4,
    double CustomTextY = 2,
    double CustomTextScale = 100,
    double BarScale = 100,
    string? BarHpColor = null,
    string? BarArmorColor = null,
    bool BarPulseLowHp = false,
    bool BarHpGradient = false,
    string? BarGradFullColor = null,
    string? BarGradMidColor = null,
    string? BarGradLowColor = null,
    double? BarScaleY = null,
    bool HideNorth = false,
    string? DigitsFont = null,
    double? HitX = null,
    double? HitY = null,
    string? ArrowPngPath = null,
    string? GpsPngPath = null,
    string? BarHpTroughColor = null,
    string? BarArmorTroughColor = null
);

public sealed record MinimapFontOptionDto(string Id, string Title, string Face);

public sealed record HotSwapStatusDto(
    bool Enabled,
    bool Supported,
    bool Frozen,
    bool Armed,
    bool AgentAlive,
    string? ImageRoot = null,
    string? Note = null,
    int Method = 1,
    bool ManualTrigger = false,
    bool Stale = false,
    string? StaleNote = null,
    string? StaleAtUtc = null);

public sealed record HotSwapLogDto(string? Path, string Text);

public sealed record DownloadLogDto(string? Path, string Text);

public sealed record MinimapFontStateDto(
    bool Installed,
    string? Slot = null,
    string? SourceFile = null);

public sealed record MinimapLayoutPresetDto(
    string Ratio,
    string Placement,
    double PosX,
    double PosY,
    double SizeX,
    double SizeY
);

public sealed record MinimapLayoutDto(
    string Ratio,
    string Placement,
    bool Transparent = false,
    double? PosX = null,
    double? PosY = null);

public sealed record MinimapScreenDto(
    int Width,
    int Height,
    int AspectRatio,
    bool Windowed,
    bool FromSettingsXml,
    string SettingsPath);

public sealed record TracerSettingDto(
    string  SourceKind,
    string? ModelFolderName,
    string? DonorReduxId,
    int     R,
    int     G,
    int     B,
    Guid?   DonorVersionId = null,
    bool    TakeDonorBlood = false,
    bool    UseCleanEffects = false,
    bool    OverrideColor = false
);

public sealed record CustomizationDraftDto(
    string ReduxId,
    GenericSettingDto Bloodfx,
    GenericSettingDto Crosshair,
    GenericSettingDto Timecycle,
    GenericSettingDto Armor,
    GenericSettingDto Arena,
    MinimapSettingDto Minimap,
    TracerSettingDto  Tracers,
    Guid? BaseVersionId = null,
    bool ZalazyEnabled = false,
    string ZalazyServer = "gta5rp",
    bool SmokeEnabled = false,
    bool NoTracerEnabled = false,
    string NoTracerCategories = "",
    bool NoTracerKeepSnipers = false,
    string TracerStudio = "",
    bool FastJoinEnabled = false,
    bool GreenZoneEnabled = false,
    bool NoBackpackEnabled = false,
    bool BigMapEnabled = false,
    string? BigMapId = null,
    string? BigMapName = null,
    bool FastJoinUserInstalled = false,
    bool CarLogosEnabled = false
);

public sealed record CarLogosStatusDto(
    bool Installed,
    bool ForeignPresent,
    IReadOnlyList<string> ForeignHits);

public sealed record FastJoinStatusDto(bool Active, bool UserInstalled);

public sealed record ReduxBundledFeaturesDto(
    bool FastJoin,
    bool GreenZone,
    bool Zalazy,
    bool CustomMinimap);

public sealed record ZalazyStateDto(
    bool Enabled,
    string Server
);

public sealed record BigMapDto(
    string Id,
    string Name,
    string Author,
    string AuthorLink,
    string Description,
    string PreviewUrl,
    List<string> GalleryUrls,
    string VideoUrl,
    List<string> SupportedServers,
    long SizeBytes,
    string PackFormat,
    long DownloadCount,
    bool IsVerified
);

public sealed record BigMapStateDto(
    bool Enabled,
    string? Id,
    string? Name,
    bool ForeignDetected
);

public sealed record BigMapAnalysisDto(
    bool Installable,
    string PackFormat,
    List<string> TargetPaths,
    List<string> Warnings,
    List<string> PhotoPaths,
    long TotalBytes
);

public sealed record BigMapPublishRequestDto(
    string SourcePath,
    string Name,
    string Author,
    string AuthorLink,
    string Description,
    List<string> SupportedServers,
    List<string> PhotoPaths,
    string? VideoUrl = null,
    string? ExistingId = null
);

public sealed record OverlayDetectDto(
    bool ForeignZalazy,
    bool ForeignGreenZone,
    bool ForeignBackpack = false
);

public sealed record TracerStudioStateDto(
    bool Enabled,
    string Settings
);

public sealed record NoTracerStateDto(
    bool Enabled,
    string[] Categories,
    bool KeepSnipers
);

public sealed record GunpackDto(
    string   Id,
    string   Name,
    string?  Author,
    string?  AuthorLink,
    string?  Description,

    string   WeaponsRpfUrl,
    long     WeaponsRpfSize,
    string   WeaponsRpfSha256,

    string?  PackZipUrl,
    long?    PackZipSize,
    string?  PackZipSha256,

    string?  ManifestUrl,

    string   CoverKind,
    string?  CoverUrl,
    List<string> GalleryUrls,

    string   Status,
    bool     IsVerified,
    int      ViewerPriority,
    long     DownloadCount,

    DateTime UploadedAt,
    string?  UploadedBy,
    DateTime UpdatedAt,
    string?  Notes
);

public sealed record GunpackVariantDto(
    Guid     Id,
    string   GunpackId,
    string   Name,

    string   WeaponsRpfUrl,
    long     WeaponsRpfSize,
    string   WeaponsRpfSha256,

    string?  PackZipUrl,
    long?    PackZipSize,
    string?  PackZipSha256,

    string?  ManifestUrl,
    string?  CoverUrl,

    bool     IsDefault,
    int      SortOrder,
    DateTime CreatedAt,
    DateTime UpdatedAt,

    Dictionary<string, VariantGunDto>? GunPreviews = null
);

public sealed record VariantGunDto(
    string? Glb,
    string? Webp,
    string? DisplayName,
    string? Category,
    string? WeaponPrefix,
    List<string>? Files,
    long?   SizeBytes,
    int?    SortOrder
);

public sealed record GunpackVariantPatchDto(
    string?  Name        = null,
    string?  CoverUrl    = null,
    bool?    IsDefault   = null,
    int?     SortOrder   = null
);

public sealed record GunpackGunDto(
    Guid     Id,
    string   GunpackId,
    string   BaseName,
    string   WeaponPrefix,
    string   Category,
    string?  DisplayName,
    string?  GlbUrl,
    string?  PreviewUrl,
    List<string> Files,
    long     SizeBytes,
    bool     IsHidden,
    int      SortOrder,
    DateTime CreatedAt
);

public sealed record GunpackFlatGunDto(
    Guid    Id,
    string  GunpackId,
    string  BaseName,
    string  WeaponPrefix,
    string  Category,
    string? DisplayName,
    string? GlbUrl,
    string? PreviewUrl
);

public sealed record GunpackWhitelistEntryDto(
    string InternalName,
    string DisplayName,
    string Category,
    string WeaponPrefix,
    bool   IsSmgOverride,
    int    SortOrder,

    string? PreviewUrl = null
);

public sealed record CustomGunDto(
    string   Id,
    string   OwnerId,
    string   OwnerName,
    string   BaseName,
    string   WeaponPrefix,
    string   InternalName,
    string   DisplayName,
    string   Description,
    string   Category,
    string?  GlbUrl,
    string?  PreviewUrl,
    long     DownloadCount,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool     Mine,
    string   Status,
    bool     SubmittedForReview,
    DateTime? ReviewedAt,
    string?  RejectReason,
    string?  UserGunpackId = null
);

public sealed record CustomGunLimitsDto(int Used, int Max);

public sealed record WorkshopFlowLimitsDto(
    int StandardMaxPerGun,
    Dictionary<string, int> StandardUsedPerGun,
    int PackBaseUsed, int PackBaseMax,
    int OwnPackUsed,  int OwnPackMax,
    int OwnPackGunCap
);

public sealed record UserGunpackDto(
    string   Id,
    string   OwnerId,
    string   OwnerName,
    string   Name,
    long     DownloadCount,
    DateTime CreatedAt,
    List<CustomGunDto> Guns
);

public sealed record CustomGunPatchDto(string? DisplayName, string? Description, string? Category);

public sealed record WorkshopTextureDto(
    string Name,
    int    Width,
    int    Height,
    string Role,
    string DataUrl
);

public sealed record WorkshopSessionDto(
    string  DraftId,
    string? CustomGunId,
    string  DisplayName,
    string  BaseName,
    string  WeaponPrefix,
    string  Category,
    string? GlbUrl,
    List<WorkshopTextureDto> Textures
);

public sealed record WorkshopOpenRequestDto(string? CustomGunId, string? BaseInternalName);

public sealed record WorkshopPublishMetaDto(string DisplayName, string Description, string Category);

public sealed record WorkshopReplaceResultDto(string? GlbUrl);

public sealed record GunpackUploadRequestDto(
    string SourceDlcRpfPath,
    GunpackDto Metadata,
    bool UploadToR2
);

public sealed record GunpackPatchDto(
    string?       Name             = null,
    string?       Author           = null,
    string?       AuthorLink       = null,
    string?       Description      = null,
    string?       Status           = null,
    bool?         IsVerified       = null,
    int?          ViewerPriority   = null,
    string?       CoverKind        = null,
    string?       CoverUrl         = null,
    List<string>? GalleryUrls      = null,
    string?       Notes            = null
);

public sealed record GunpackGunPatchDto(
    string?  DisplayName  = null,
    bool?    IsHidden     = null,
    int?     SortOrder    = null
);

public sealed record GunpackBatchEntryDto(
    string  FolderName,
    string  FolderPath,
    string? RpfPath,
    string? ImagePath
);

public sealed record GunpackQueueItemDto(
    string   TempId,
    GunpackDto Metadata,
    string   SourceDlcRpfPath,
    string   TempWorkDir,
    bool     UploadToR2,
    string   Status,
    int?     Percent,
    string?  CurrentPhase,
    string?  ErrorMessage,
    DateTime AddedAt,

    List<string>? Warnings = null
);

public sealed record GunpackInstalledStateDto(
    string?  ActiveGunpackId,
    string?  ActiveGunpackName,
    string?  WeaponsRpfSha256,
    DateTime? InstalledAt
);

public sealed record GunpackVerifyReportDto(
    bool    Ok,
    bool    TargetDlcExists,
    bool    RpfPresentInDlc,
    string? StateSha,
    string? ActualSha,
    string  Summary
);

public sealed record GunpackInstallConflictDto(
    string  InternalName,
    string  DisplayName,

    string? GunpackPreviewUrl,
    string? GunpackGlbUrl,
    string  GunpackPackName,

    string? SelectedPreviewUrl,
    string? SelectedGlbUrl,
    string  SelectedFromPackId,
    string  SelectedFromPackName
);

public sealed record SelectedGunDto(
    string   GunpackId,
    string   GunpackName,
    string   GunId,
    string   InternalName,
    string   DisplayName,
    string   BaseName,
    string   WeaponPrefix,
    List<string> Files,
    string   PackZipUrl,
    string   PackZipSha256,
    DateTime SelectedAt
);

public sealed record SelectedGunsVerifyReportDto(
    bool    Ok,
    int     StateGunsCount,
    bool    TargetDlcExists,
    bool    RpfPresentInDlc,
    string? StateSha,
    string? ActualSha,
    string  Summary
);

public sealed record HntPayloadDto(

    string?  ReduxId,
    Guid?    ReduxVersionId,
    string?  ReduxName,
    string?  ReduxAuthor,

    string?  GunpackId,
    string?  GunpackName,

    List<HntSelectedGunDto> SelectedGuns,

    JsonElement? Extras,

    HntComponentRefDto? Armor   = null,
    HntComponentRefDto? Minimap = null,
    HntComponentRefDto? Reticle = null,
    HntComponentRefDto? Sounds  = null,
    HntComponentRefDto? BigMap  = null
);

public sealed record HntComponentRefDto(
    string  Source,
    string  Id,
    string? Name
);

public sealed record HntSelectedGunDto(
    string GunpackId,
    string GunpackName,
    string InternalName,
    string DisplayName
);

public sealed record HntCodeDto(
    string         Code,
    HntPayloadDto  Payload,
    string         CreatedBy,
    DateTime       CreatedAt,
    DateTime       LastDownloadedAt,
    int            DownloadsCount
);

public sealed record HntImportResultDto(
    bool                         Success,
    string?                      ErrorMessage,
    HntInstallStepResultDto?     ReduxStep,
    HntInstallStepResultDto?     GunpackStep,
    HntInstallStepResultDto?     SelectedGunsStep,
    HntInstallStepResultDto?     ComponentsStep = null
);

public sealed record HntInstallStepResultDto(
    bool    Skipped,
    bool    Success,
    string? ErrorMessage
);

public sealed record UserBuildDto(
    string    Id,
    string    HntCode,
    string    Name,
    string?   AuthorUserId,
    string    AuthorUsername,
    string    ReduxId,
    string    GunpackId,
    string    ReduxNameSnapshot,
    string    GunpackNameSnapshot,
    string    GunSlotsJson,
    string?   ArmorJson,
    string?   ArenaJson,
    string?   MinimapJson,
    string?   ReticleJson,
    string?   SoundsJson,
    long      DownloadCount,
    long      ViewCount,

    DateTime? CreatedAt,
    DateTime? UpdatedAt,

    string?   DevicesJson,
    decimal?  Sensitivity,
    int?      Dpi,
    string?   Resolution,
    string?   VideoUrl,
    string?   SettingsXmlUrl,
    string    Description,
    int?      Tier,

    string    Status,
    bool      SubmittedForReview,
    string?   ReviewedBy,
    DateTime? ReviewedAt,
    string?   RejectReason,
    string?   CoverUrl = null,

    string?   Family = null,
    string?   CategoryLabel = null,
    int?      FpsAvg = null,
    int?      MonitorHz = null,
    string?   AdminNotes = null
);

public sealed record PresetReactionsDto(long Likes, long Dislikes, int MyReaction);

public sealed record AccountStatsDto(int AccountNo, long Downloads);

public sealed record GtaPresetDto(
    string   Id,
    string   Name,
    string   Description,
    string   Author,
    string   XmlUrl,
    long     XmlSizeBytes,
    string   XmlSha256,
    int?     ExpectedFpsLow,
    int?     ExpectedFpsHigh,
    string?  BaselineHwLabel,
    int      ComputedGainPercent,
    string   CpuBias,
    bool     IsTournament,
    string   Status,
    int      ViewerPriority,
    long     DownloadCount,
    string   UploadedBy,
    DateTime UploadedAt,
    DateTime UpdatedAt
);

public sealed record GtaPresetUploadRequestDto(
    string  SourceXmlPath,
    string  Name,
    string  Description,
    string  Author,
    int?    ExpectedFpsLow,
    int?    ExpectedFpsHigh,
    string? BaselineHwLabel,
    bool    IsTournament,
    int     ViewerPriority,
    string  Status
);

public sealed record GtaPresetPatchDto(
    string?  Name             = null,
    string?  Description      = null,
    string?  Author           = null,
    int?     ExpectedFpsLow   = null,
    int?     ExpectedFpsHigh  = null,
    string?  BaselineHwLabel  = null,
    bool?    IsTournament     = null,
    string?  Status           = null,
    int?     ViewerPriority   = null
);

public sealed record GtaPresetApplyResultDto(
    bool    Success,
    string? ErrorMessage,
    string  TargetPath,
    string? BackupPath,
    bool    GameWasRunning
);

public sealed record AdminWipeAllResultDto(
    int Deleted,
    int Failed
);

public sealed record GtaSettingsAnalysisDto(
    int      GainPercent,
    string   CpuBias,
    IReadOnlyList<GtaSettingContributionDto> Contributions
);

public sealed record GtaSettingContributionDto(
    string  Key,
    double  GainPercent,
    string  Category
);

public sealed record GtaDisplaySettingsDto(
    int     ScreenWidth,
    int     ScreenHeight,
    int     RefreshRate,
    int     AspectRatio,
    int     Windowed,
    bool    VSync
);

public sealed record GtaQualitySettingsDto(
    int     TextureQuality,
    int     ShaderQuality,
    int     WaterQuality,
    int     ParticleQuality,
    int     PostFx,
    int     ShadowQuality
);

public sealed record GtaAntiAliasingSettingsDto(
    bool    Fxaa,
    bool    Txaa,
    int     Msaa,
    int     ReflectionMsaa
);

public sealed record GtaWorldSettingsDto(
    double  CityDensity,
    double  PedVariety,
    double  VehicleVariety,
    double  LodScale,
    double  VehicleLodBias,
    double  PedLodBias,
    int     GrassQuality,
    int     ReflectionQuality,
    double  ShadowDistance,
    double  MaxLodScale
);

public sealed record GtaAdvancedSettingsDto(
    int     Tessellation,
    int     AnisotropicFiltering,
    int     Ssao,
    int     ShadowSoftShadows,
    double  ShadowSplitZStart,
    double  ShadowSplitZEnd,
    bool    UltraShadows,
    bool    ShadowParticles,
    bool    ShadowLongShadows,
    bool    ReflectionMipBlur,
    int     DxVersion,
    bool    Dof,
    bool    HdStreaming,
    double  MotionBlur,
    bool    FogVolumes
);

public sealed record OptimizationOptionDto(
    int    Idx,
    string Name,
    string PreviewUrl,
    string FpsLabel,
    int    SettingsCount
);

public sealed record OptimizationGroupDto(
    string Key,
    string Style,
    string Title,
    string Description,
    string IconUrl,
    int    ResetIndex,
    bool   Beta,
    IReadOnlyList<OptimizationOptionDto> Options
);

public sealed record OptimizationCatalogDto(
    IReadOnlyList<OptimizationGroupDto> Groups,
    IReadOnlyList<string> Problems
);

public sealed record OptimizationSelectionDto(
    string Key,
    int?   OptionIdx
);

public sealed record OptimizationKeyChangeDto(
    string  Key,
    string? From,
    string  To,
    string  GroupKey
);

public sealed record OptimizationApplyResultDto(
    bool    Success,
    string? ErrorMessage,
    IReadOnlyList<OptimizationKeyChangeDto> Changes,
    IReadOnlyList<string> Warnings,
    string  TargetPath,
    string? BackupPath,
    bool    GameWasRunning,
    bool    BaselineCaptured
);

public sealed record OptimizationResolutionDto(
    IReadOnlyDictionary<string, int?> Selections,
    IReadOnlyList<string> UnmappedKeys,
    IReadOnlyList<string> CustomGroups,
    IReadOnlyDictionary<string, string>? Markers = null
);

public sealed record GtaSettingsModelDto(
    GtaDisplaySettingsDto       Display,
    GtaQualitySettingsDto       Quality,
    GtaAntiAliasingSettingsDto  AntiAliasing,
    GtaWorldSettingsDto         World,
    GtaAdvancedSettingsDto      Advanced
);

public sealed record GtaSettingsReadResultDto(
    GtaSettingsModelDto Model,
    bool                ExistedOnDisk,
    string              SourcePath
);

public sealed record BridgeRequest(
    string Id,
    string Command,
    JsonElement? Payload
);

public sealed record BridgeResponse(
    string Id,
    bool Ok,
    object? Data,
    string? Error
);

public sealed record CurrentArmorInfoDto(
    string Id,
    string Name,
    string? GlbUrl,
    string Kind
);

public sealed record DlcArmorInspectionResultDto(
    string DlcRpfPath,
    System.Collections.Generic.List<DlcArmorCandidateDto> Candidates,
    System.Collections.Generic.List<string> Warnings,
    string? ErrorMessage
);

public sealed record DlcArmorCandidateDto(
    string  YddInternalPath,
    string  YddName,
    string? DrawableInternalName,
    string? ParseError,
    System.Collections.Generic.List<DlcArmorSamplerExpectationDto> SamplerExpectations,
    System.Collections.Generic.List<DlcArmorYtdDto> CandidateYtds,
    System.Collections.Generic.List<string> MissingExpectedDiffuses,
    bool    HasNameMismatch,
    DlcArmorRenameSuggestionDto? SuggestedRename,
    string? PreviewGlbUrl
);

public sealed record DlcArmorSamplerExpectationDto(
    string SamplerName,
    string ExpectedTextureName
);

public sealed record DlcArmorYtdDto(
    string  InternalPath,
    string  FileName,
    System.Collections.Generic.List<string> InnerTextureNames,
    string? ParseError
);

public sealed record DlcArmorRenameSuggestionDto(
    string YtdInternalPath,
    string OldTextureName,
    string NewTextureName
);

public sealed record DlcArmorImportRequestDto(
    string  DlcRpfPath,
    string  YddInternalPath,
    string  Name,
    string? Author,
    bool    ApplyAutoFix,
    string? RenameYtdFileName,
    string? RenameOldTextureName,
    string? RenameNewTextureName,
    IReadOnlyList<DlcArmorExtraSourceDto>? ExtraSources = null
);

public sealed record DlcArmorExtraSourceDto(
    string DlcRpfPath,
    string YddInternalPath
);

public sealed record DlcArmorImportResultDto(
    bool    Success,
    string? ArmorId,
    string? ArmorRpfUrl,
    string? GlbUrl,
    string? ErrorMessage
);

public sealed record ArmorLibraryItemDto(
    string Id,
    string Name,
    string Author,
    string Description,
    string GlbUrl,
    string ArmorRpfUrl,
    string InternalPath,
    long   DownloadCount,
    int    ViewerPriority,
    bool   IsVerified,
    string Status,
    DateTime UploadedAt,

    List<string> SupportedServers,
    string? PreviewUrl = null,
    List<string>? PreviewVariants = null,
    bool HasMale = true,
    bool HasFemale = true
);

public sealed record CacheSettingsDto(
    bool Enabled,
    string? RootOverride,
    string EffectiveRoot,
    string DefaultRoot,
    long SizeBytes,

    string DataRoot = "",
    string DefaultDataRoot = "",
    string BackupRoot = "",
    long BackupBytes = 0,
    long TotalBytes = 0,
    long WorkBytes = 0,
    string WorkRoot = "",
    long LimitBytes = 0,
    long MinLimitBytes = 0,
    long MaxLimitBytes = 0,
    long ProtectedBytes = 0,
    long FreeSpaceBytes = 0,
    bool BackupOnLegacyRoot = false,
    long OtherBytes = 0
);

public sealed record DataMoveResultDto(
    bool Success,
    string EffectiveRoot,
    long MovedBytes,
    bool SourceRemoved,
    string? ErrorMessage
);

public sealed record DataMoveProgressDto(
    string Phase,
    int Percent,
    string? FileName,
    long BytesProcessed,
    long BytesTotal,
    string? ErrorMessage
);

public sealed record QuotaHolderDto(string Name, long Bytes);

public sealed record CacheCleanupResultDto(
    long BeforeBytes,
    long AfterBytes,
    long FreedBytes,
    int DeletedEntries,
    bool StillOverLimit,
    string Reason = "freed",
    long ProtectedBytes = 0,
    long ReclaimableBytes = 0,
    long OtherBytes = 0,
    IReadOnlyList<QuotaHolderDto>? Holders = null
);

public sealed record LegitFieldDiffDto(
    string Owner,
    string Field,
    string CleanValue,
    string ModValue,
    double? DeltaPercent,
    bool IsRed
);

public sealed record LegitFileFindingDto(
    string Path,
    string Change,
    string Severity,
    string CategoryLabel,
    string Note,
    bool FormatOnly,
    long Size,
    List<LegitFieldDiffDto> FieldDiffs
);

public sealed record LegitReportDto(
    string Verdict,
    string VerdictTitle,
    string VerdictText,
    List<string> VerdictReasons,
    string Source,
    DateTime CheckedAt,
    int DangerCount,
    int WarningCount,
    int ChangedCount,
    int AddedCount,
    int DeletedCount,
    List<LegitFileFindingDto> Findings,
    List<string> Unverified,
    int CheckedCount = 0
);

public sealed record OptimizationInteriorStateDto(
    string GroupKey,
    int? OptionIdx,
    string? Marker
);

public sealed record OptimizationScanProgressDto(
    int Percent,
    string Stage,
    string Detail
);

public sealed record LegitCheckProgressDto(
    int Percent,
    string Stage,
    string CurrentFile
);

public sealed record ImprovementDto(
    string Id,
    string Name,
    string Author,
    string Description,
    string Source,
    string ExclusiveGroup,
    string Category,
    string PreviewUrl,
    string VideoUrl,
    string[] GalleryUrls,
    long   SizeBytes,
    bool   Installed,
    string[] Slots,
    long   Popularity = 0
);

public sealed record PcDiagFindingDto(
    string Id,
    string Severity,
    string Category,
    Dictionary<string, string> Data,
    int? GainMinPercent,
    int? GainMaxPercent,
    bool AutoFixable
);

public sealed record PcDiagRamStickDto(string Slot, int CapacityGb, int RatedMt, int ConfiguredMt, string MemType);
public sealed record PcDiagDiskDto(string Model, string Media, string Bus, int SizeGb);
public sealed record PcDiagGpuDto(string Name, int VramGb, string DriverVersion, string DriverDate, bool IsIntegrated);

public sealed record PcDiagBgDto(string Name, int Count, double Gb);

public sealed record PcDiagReportDto(
    string CpuName,
    int CpuCores,
    int CpuThreads,
    int CpuL3Mb,
    string CpuTier,
    string CpuFamily,
    bool CpuHybrid,
    bool CpuX3D,
    bool CpuLaptop,
    int RamTotalGb,
    int RamSlotsTotal,
    string RamTier,
    string RamTierNote,
    List<PcDiagRamStickDto> RamSticks,
    List<PcDiagDiskDto> Disks,
    List<PcDiagGpuDto> Gpus,
    string PowerScheme,
    string PowerKind,
    bool VbsRunning,
    bool GameDvrOn,
    bool HasBattery,
    string OsCaption,
    int DisplayWidth,
    int DisplayHeight,
    int DisplayCurrentHz,
    int DisplayMaxHz,
    List<PcDiagMonitorDto> Monitors,
    bool NetWired,
    bool NetWireless,
    bool NetVpn,
    string GtaPath,
    string GtaDiskMedia,
    string DiskTier,
    string DiskTierNote,
    List<PcDiagBgDto> Background,
    List<string> SensorErrors,
    List<PcDiagFindingDto> Findings,
    long ElapsedMs
);

public sealed record PcDiagMonitorDto(
    string Name,
    string DeviceName,
    string Adapter,
    int Width,
    int Height,
    int CurrentHz,
    int MaxHz,
    bool IsPrimary
);

public sealed record PcDiagApplyResultDto(
    bool Ok,
    string Message,
    bool RequiresRestart
);

public sealed record PcDiagJournalEntryDto(
    string Id,
    string AppliedAtUtc,
    bool Reverted
);

public sealed record PcDiagTweakDto(
    string Id,
    string Grade,
    bool RequiresRestart,
    bool InAllSafe,
    string State,
    Dictionary<string, string> Data
);

public sealed record PcDiagAiResultDto(bool Ok, string Text, string Error);

public sealed record PcDiagAiMsgDto(string Role, string Text);
