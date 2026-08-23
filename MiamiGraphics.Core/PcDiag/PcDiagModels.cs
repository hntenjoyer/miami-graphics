#nullable enable
using System;
using System.Collections.Generic;

namespace MiamiGraphics.Core.PcDiag;

public sealed record PcSnapshot(
    DateTime TakenAtUtc,
    CpuInfo? Cpu,
    IReadOnlyList<RamStickInfo> RamSticks,
    int RamSlotsTotal,
    long TotalRamBytes,
    IReadOnlyList<DiskInfo> Disks,
    IReadOnlyList<GpuInfo> Gpus,
    PowerInfo? Power,
    SecurityInfo? Security,
    GameDvrInfo? GameDvr,
    OsInfo? Os,
    bool HasBattery,
    IReadOnlyList<ServiceStateInfo> Services,
    IReadOnlyList<AutostartEntry> Autostart,
    IReadOnlyList<HeavyProcessInfo> HeavyProcesses,
    DisplayModeInfo? Display,
    IReadOnlyList<MonitorInfo> Monitors,
    NetworkInfo? Network,
    PagefileInfo? Pagefile,
    WinFeaturesInfo? WinFeatures,
    BcdInfo? Bcd,
    GameInstallInfo? Game,
    IReadOnlyList<AvProductInfo> AvProducts,
    PrefetchInfo? Prefetch,
    GameProcessInfo? GameProcess,
    PowerDetailsInfo? PowerDetails,
    VisualFxInfo? VisualFx,
    IReadOnlyDictionary<string, string> CollectorErrors
);

public sealed record CpuInfo(
    string Name,
    int Cores,
    int Threads,
    int MaxClockMhz,
    int L3CacheKb
);

public sealed record RamStickInfo(
    string BankLabel,
    string DeviceLocator,
    string SlotName,
    long CapacityBytes,
    int RatedMt,
    int ConfiguredMt,
    int SmbiosMemoryType,
    string Manufacturer
);

public sealed record DiskInfo(
    string Model,
    DiskMedia Media,
    DiskBus Bus,
    long SizeBytes
);

public enum DiskMedia { Unknown = 0, Hdd = 3, Ssd = 4, Scm = 5 }
public enum DiskBus { Unknown = 0, Sata = 11, Nvme = 17, Usb = 7, RaidOrOther = 8 }

public sealed record GpuInfo(
    string Name,
    long VramBytes,
    string DriverVersion,
    DateTime? DriverDate,
    bool IsIntegrated
);

public enum PowerSchemeKind { Unknown, PowerSaver, Balanced, HighPerformance, Ultimate, Custom }

public sealed record PowerInfo(
    Guid SchemeGuid,
    string SchemeName,
    PowerSchemeKind Kind
);

public sealed record SecurityInfo(
    bool VbsRunning,
    bool HvciRunning
);

public sealed record GameDvrInfo(
    bool? GameDvrEnabled,
    bool? AppCaptureEnabled
);

public sealed record OsInfo(string Caption, string Version);

public sealed record ServiceStateInfo(string Name, bool Exists, bool Running, string StartMode);

public sealed record AutostartEntry(string Name, string Command, string Source);

public sealed record HeavyProcessInfo(string Key, string DisplayName, int Count, long WorkingSetBytes);

public sealed record DisplayModeInfo(int Width, int Height, int CurrentHz, int MaxHz);

public sealed record MonitorInfo(
    string Name,
    string DeviceName,
    string Adapter,
    int Width,
    int Height,
    int CurrentHz,
    int MaxHz,
    bool IsPrimary);

public sealed record NetworkInfo(
    bool HasWiredActive,
    bool HasWirelessActive,
    bool HasVpnActive,
    string ActiveAdapterName,
    string VpnAdapterName
);

public sealed record PagefileInfo(bool AutomaticManaged, bool Present, long AllocatedMb);

public sealed record WinFeaturesInfo(
    bool? HagsOn,
    bool? GameModeOn,
    bool? TransparencyOn
);

public sealed record BcdInfo(bool? UsePlatformClock, bool? DisableDynamicTick);

public sealed record GameInstallInfo(
    string GtaPath,
    DiskMedia Media,
    bool? InDefenderExclusions
);

public sealed record AvProductInfo(string Name);

public sealed record PowerDetailsInfo(
    int? MinProcessorPercent,
    bool? UsbSelectiveSuspend,
    int? PcieAspm
);

public sealed record VisualFxInfo(int? Setting);

public sealed record PrefetchInfo(int? EnablePrefetcher, int? EnableSuperfetch);

public sealed record GameProcessInfo(
    string ProcessName,
    string PriorityClass,
    int AffinityCores,
    int TotalCores
);

public enum DiagSeverity
{
    Info,
    Minor,
    Major,
    Critical
}

public enum DiagCategory { Hardware, Windows, Apps, Driver, Game }

public sealed record DiagFinding(
    string Id,
    DiagSeverity Severity,
    DiagCategory Category,
    IReadOnlyDictionary<string, string> Data,
    int? GainMinPercent = null,
    int? GainMaxPercent = null,
    bool AutoFixable = false
);

public enum CpuTier
{
    Unknown,
    S,
    A,
    B,
    C,
    D
}

public sealed record CpuGtaRating(
    CpuTier Tier,
    string FamilyLabel,
    bool IsHybrid,
    bool IsX3D,
    bool IsLaptop,
    bool Parsed
);
