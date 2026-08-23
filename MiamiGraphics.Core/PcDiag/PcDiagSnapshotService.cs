#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MiamiGraphics.Core.PcDiag;

[SupportedOSPlatform("windows")]
public static class PcDiagSnapshotService
{
    public static PcSnapshot Take(string? gtaPath = null)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);

        var cpu = Collect(errors, "cpu", CollectCpu);
        var ram = Collect(errors, "ram", CollectRam) ?? new List<RamStickInfo>();
        var ramSlots = Collect(errors, "ramSlots", () => MemorySlots().Count);
        var totalRam = Collect(errors, "ramTotal", CollectTotalRam);
        var disks = Collect(errors, "disks", CollectDisks) ?? new List<DiskInfo>();
        var gpus = Collect(errors, "gpus", CollectGpus) ?? new List<GpuInfo>();
        var power = Collect(errors, "power", CollectPower);
        var security = Collect(errors, "security", CollectSecurity);
        var gameDvr = Collect(errors, "gameDvr", CollectGameDvr);
        var os = Collect(errors, "os", CollectOs);
        var battery = Collect(errors, "battery", CollectHasBattery);
        var services = Collect(errors, "services", CollectServices) ?? new List<ServiceStateInfo>();
        var autostart = Collect(errors, "autostart", CollectAutostart) ?? new List<AutostartEntry>();
        var heavy = Collect(errors, "processes", CollectHeavyProcesses) ?? new List<HeavyProcessInfo>();
        var display = Collect(errors, "display", CollectDisplay);
        var monitors = Collect(errors, "monitors", CollectMonitors) ?? new List<MonitorInfo>();
        var network = Collect(errors, "network", CollectNetwork);
        var pagefile = Collect(errors, "pagefile", CollectPagefile);
        var features = Collect(errors, "winFeatures", CollectWinFeatures);
        var bcd = Collect(errors, "bcd", CollectBcd);
        var game = gtaPath is null ? null : Collect(errors, "game", () => CollectGame(gtaPath));
        var av = Collect(errors, "antivirus", CollectAv) ?? new List<AvProductInfo>();
        var prefetch = Collect(errors, "prefetch", CollectPrefetch);
        var gameProc = Collect(errors, "gameProcess", CollectGameProcess);
        var powerDetails = Collect(errors, "powerDetails", CollectPowerDetails);
        var visualFx = Collect(errors, "visualFx", CollectVisualFx);

        return new PcSnapshot(
            TakenAtUtc: DateTime.UtcNow,
            Cpu: cpu,
            RamSticks: ram,
            RamSlotsTotal: (int)ramSlots,
            TotalRamBytes: totalRam,
            Disks: disks,
            Gpus: gpus,
            Power: power,
            Security: security,
            GameDvr: gameDvr,
            Os: os,
            HasBattery: battery,
            Services: services,
            Autostart: autostart,
            HeavyProcesses: heavy,
            Display: display,
            Monitors: monitors,
            Network: network,
            Pagefile: pagefile,
            WinFeatures: features,
            Bcd: bcd,
            Game: game,
            AvProducts: av,
            Prefetch: prefetch,
            GameProcess: gameProc,
            PowerDetails: powerDetails,
            VisualFx: visualFx,
            CollectorErrors: errors);
    }

    private static T? Collect<T>(Dictionary<string, string> errors, string name, Func<T> collector)
        where T : class
    {
        try { return collector(); }
        catch (Exception ex) { errors[name] = ex.Message; return null; }
    }

    private static long Collect(Dictionary<string, string> errors, string name, Func<long> collector)
    {
        try { return collector(); }
        catch (Exception ex) { errors[name] = ex.Message; return 0; }
    }

    private static bool Collect(Dictionary<string, string> errors, string name, Func<bool> collector)
    {
        try { return collector(); }
        catch (Exception ex) { errors[name] = ex.Message; return false; }
    }

    private static IEnumerable<ManagementObject> Query(string wql, string? scope = null)
    {
        using var searcher = scope is null
            ? new ManagementObjectSearcher(wql)
            : new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql));
        foreach (ManagementObject mo in searcher.Get())
            yield return mo;
    }

    private static int ToInt(object? v) => v is null ? 0 : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    private static long ToLong(object? v) => v is null ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture);
    private static string ToStr(object? v) => (v as string)?.Trim() ?? "";

    private static CpuInfo CollectCpu()
    {
        foreach (var mo in Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L3CacheSize FROM Win32_Processor"))
        {
            return new CpuInfo(
                Name: ToStr(mo["Name"]),
                Cores: ToInt(mo["NumberOfCores"]),
                Threads: ToInt(mo["NumberOfLogicalProcessors"]),
                MaxClockMhz: ToInt(mo["MaxClockSpeed"]),
                L3CacheKb: ToInt(mo["L3CacheSize"]));
        }
        throw new InvalidOperationException("Win32_Processor вернул пусто");
    }

    private static List<RamStickInfo> CollectRam()
    {
        var slots = MemorySlots();
        var list = new List<RamStickInfo>();
        foreach (var mo in Query("SELECT BankLabel, DeviceLocator, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType, Manufacturer FROM Win32_PhysicalMemory"))
        {
            var bank = ToStr(mo["BankLabel"]);
            var locator = ToStr(mo["DeviceLocator"]);
            list.Add(new RamStickInfo(
                BankLabel: bank,
                DeviceLocator: locator,
                SlotName: slots.FirstOrDefault(x =>
                    string.Equals(x.Bank, bank, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.Locator, locator, StringComparison.OrdinalIgnoreCase))?.Name ?? "",
                CapacityBytes: ToLong(mo["Capacity"]),
                RatedMt: ToInt(mo["Speed"]),
                ConfiguredMt: ToInt(mo["ConfiguredClockSpeed"]),
                SmbiosMemoryType: ToInt(mo["SMBIOSMemoryType"]),
                Manufacturer: ToStr(mo["Manufacturer"])));
        }
        return list;
    }

    private sealed record MemorySlot(string Bank, string Locator, string Name, bool Populated);

    private static List<MemorySlot> MemorySlots()
    {
        var slots = new List<(string Bank, string Locator, bool Populated)>();
        try
        {
            foreach (var mo in Query("SELECT SMBiosData FROM MSSmBios_RawSMBiosTables", @"\\.\root\wmi"))
            {
                if (mo["SMBiosData"] is not byte[] data) continue;
                slots.AddRange(ParseMemoryDevices(data));
                break;
            }
        }
        catch {}

        if (slots.Count == 0) return new List<MemorySlot>();

        static string Letter(string bank, string locator)
        {
            var m = global::System.Text.RegularExpressions.Regex.Match(
                bank, @"CHANNEL\s*([A-Z])", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
            m = global::System.Text.RegularExpressions.Regex.Match(
                locator, @"([A-Z])\s*\d", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : "";
        }

        static int Number(string locator)
        {
            var m = global::System.Text.RegularExpressions.Regex.Match(locator, @"(\d+)\s*$");
            return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : int.MaxValue;
        }

        var result = new List<MemorySlot>();
        foreach (var group in slots.GroupBy(x => Letter(x.Bank, x.Locator)))
        {
            var ordered = group.OrderBy(x => Number(x.Locator)).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                var (bank, locator, populated) = ordered[i];
                var letter = group.Key;
                var name = letter.Length > 0 ? letter + (i + 1) : locator;
                result.Add(new MemorySlot(bank, locator, name, populated));
            }
        }
        return result;
    }

    private static List<(string Bank, string Locator, bool Populated)> ParseMemoryDevices(byte[] data)
    {
        var found = new List<(string, string, bool)>();
        int i = 0;
        while (i + 4 <= data.Length)
        {
            byte type = data[i];
            byte len = data[i + 1];
            if (len < 4 || i + len > data.Length) break;

            int p = i + len;
            var strings = new List<string>();
            if (p + 1 < data.Length && data[p] == 0 && data[p + 1] == 0)
            {
                p += 2;
            }
            else
            {
                var sb = new global::System.Text.StringBuilder();
                while (p < data.Length)
                {
                    if (data[p] == 0)
                    {
                        strings.Add(sb.ToString());
                        sb.Clear();
                        if (p + 1 < data.Length && data[p + 1] == 0) { p += 2; break; }
                    }
                    else sb.Append((char)data[p]);
                    p++;
                }
            }

            if (type == 17 && len >= 0x15)
            {
                string At(int idx) => idx > 0 && idx <= strings.Count ? strings[idx - 1].Trim() : "";
                var size = BitConverter.ToUInt16(data, i + 0x0C);
                found.Add((At(data[i + 0x11]), At(data[i + 0x10]), size != 0));
            }

            if (type == 127) break;
            i = p;
        }
        return found;
    }

    private static long CollectTotalRam()
    {
        foreach (var mo in Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            return ToLong(mo["TotalPhysicalMemory"]);
        return 0;
    }

    private static List<DiskInfo> CollectDisks()
    {
        var list = new List<DiskInfo>();
        foreach (var mo in Query("SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk",
                                 @"\\.\root\microsoft\windows\storage"))
        {
            var media = ToInt(mo["MediaType"]) switch
            {
                3 => DiskMedia.Hdd,
                4 => DiskMedia.Ssd,
                5 => DiskMedia.Scm,
                _ => DiskMedia.Unknown
            };
            var bus = ToInt(mo["BusType"]) switch
            {
                11 => DiskBus.Sata,
                17 => DiskBus.Nvme,
                7 => DiskBus.Usb,
                8 => DiskBus.RaidOrOther,
                _ => DiskBus.Unknown
            };
            list.Add(new DiskInfo(ToStr(mo["FriendlyName"]), media, bus, ToLong(mo["Size"])));
        }
        return list;
    }

    private static List<GpuInfo> CollectGpus()
    {
        var vramByDesc = CollectVramFromRegistry();
        var list = new List<GpuInfo>();
        foreach (var mo in Query("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController"))
        {
            var name = ToStr(mo["Name"]);
            DateTime? driverDate = null;
            var raw = ToStr(mo["DriverDate"]);
            if (raw.Length >= 8)
            {
                try { driverDate = ManagementDateTimeConverter.ToDateTime(raw); }
                catch {}
            }
            vramByDesc.TryGetValue(name, out var vram);
            list.Add(new GpuInfo(
                Name: name,
                VramBytes: vram,
                DriverVersion: ToStr(mo["DriverVersion"]),
                DriverDate: driverDate,
                IsIntegrated: LooksIntegrated(name)));
        }
        return list;
    }

    private static Dictionary<string, long> CollectVramFromRegistry()
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var cls = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (cls is null) return result;
        foreach (var sub in cls.GetSubKeyNames())
        {
            if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
            using var k = cls.OpenSubKey(sub);
            if (k is null) continue;
            var desc = k.GetValue("DriverDesc") as string;
            var qw = k.GetValue("HardwareInformation.qwMemorySize");
            if (desc is null || qw is null) continue;
            try { result[desc] = Convert.ToInt64(qw, CultureInfo.InvariantCulture); }
            catch {}
        }
        return result;
    }

    private static bool LooksIntegrated(string name) =>
        name.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("UHD Graphics", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("HD Graphics", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(name, @"AMD Radeon\(?TM\)? Graphics$", RegexOptions.IgnoreCase) ||
        name.Contains("Vega ", StringComparison.OrdinalIgnoreCase) && name.Contains("Graphics", StringComparison.OrdinalIgnoreCase);

    private static readonly Guid GuidBalanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    private static readonly Guid GuidHighPerf = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    private static readonly Guid GuidUltimate = new("e9a42b02-d5df-448d-aa66-ad3f9cceb640");
    private static readonly Guid GuidSaver    = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    private static PowerInfo CollectPower()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding oem;
        try { oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch { oem = Encoding.Default; }

        var psi = new ProcessStartInfo("powercfg", "/getactivescheme")
        {
            RedirectStandardOutput = true,
            StandardOutputEncoding = oem,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("powercfg не запустился");
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);

        var m = Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        if (!m.Success) throw new InvalidOperationException("powercfg: GUID не найден в выводе");
        var guid = Guid.Parse(m.Value);

        var nameMatch = Regex.Match(output, @"\(([^)]+)\)\s*$", RegexOptions.Multiline);
        var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : "";

        var kind =
            guid == GuidBalanced ? PowerSchemeKind.Balanced :
            guid == GuidHighPerf ? PowerSchemeKind.HighPerformance :
            guid == GuidUltimate ? PowerSchemeKind.Ultimate :
            guid == GuidSaver ? PowerSchemeKind.PowerSaver :
            PowerSchemeKind.Custom;

        return new PowerInfo(guid, name, kind);
    }

    private static SecurityInfo CollectSecurity()
    {
        foreach (var mo in Query(
            "SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard",
            @"\\.\root\Microsoft\Windows\DeviceGuard"))
        {
            var vbs = ToInt(mo["VirtualizationBasedSecurityStatus"]) == 2;
            var running = mo["SecurityServicesRunning"] as IEnumerable<object>
                          ?? (mo["SecurityServicesRunning"] as Array)?.Cast<object>()
                          ?? Enumerable.Empty<object>();
            var hvci = running.Any(v => ToInt(v) == 2);
            return new SecurityInfo(vbs, hvci);
        }
        return new SecurityInfo(false, false);
    }

    private static GameDvrInfo CollectGameDvr()
    {
        bool? gameDvr = null, appCapture = null;
        using (var k = Registry.CurrentUser.OpenSubKey(@"System\GameConfigStore"))
            if (k?.GetValue("GameDVR_Enabled") is int g) gameDvr = g != 0;
        using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR"))
            if (k?.GetValue("AppCaptureEnabled") is int a) appCapture = a != 0;
        return new GameDvrInfo(gameDvr, appCapture);
    }

    private static OsInfo CollectOs()
    {
        foreach (var mo in Query("SELECT Caption, Version FROM Win32_OperatingSystem"))
            return new OsInfo(ToStr(mo["Caption"]), ToStr(mo["Version"]));
        throw new InvalidOperationException("Win32_OperatingSystem вернул пусто");
    }

    private static bool CollectHasBattery()
    {
        foreach (var _ in Query("SELECT DeviceID FROM Win32_Battery"))
            return true;
        return false;
    }

    private static readonly string[] TrackedServices = { "SysMain", "WSearch", "EventLog" };

    private static List<ServiceStateInfo> CollectServices()
    {
        var found = new Dictionary<string, ServiceStateInfo>(StringComparer.OrdinalIgnoreCase);
        var filter = string.Join(" OR ", TrackedServices.Select(n => $"Name = '{n}'"));
        foreach (var mo in Query($"SELECT Name, State, StartMode FROM Win32_Service WHERE {filter}"))
        {
            var name = ToStr(mo["Name"]);
            found[name] = new ServiceStateInfo(
                Name: name,
                Exists: true,
                Running: string.Equals(ToStr(mo["State"]), "Running", StringComparison.OrdinalIgnoreCase),
                StartMode: ToStr(mo["StartMode"]));
        }
        return TrackedServices
            .Select(n => found.TryGetValue(n, out var s) ? s : new ServiceStateInfo(n, false, false, ""))
            .ToList();
    }

    private static List<AutostartEntry> CollectAutostart()
    {
        var list = new List<AutostartEntry>();
        void Read(RegistryKey root, string path, string label)
        {
            using var k = root.OpenSubKey(path);
            if (k is null) return;
            foreach (var name in k.GetValueNames())
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                list.Add(new AutostartEntry(name, k.GetValue(name)?.ToString() ?? "", label));
            }
        }
        Read(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU");
        Read(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM");
        Read(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM32");
        return list;
    }

    private static readonly (string[] Names, string Key, string Display)[] ProcessGroups =
    {
        (new[] { "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "browser", "yandex" }, "browser", "Браузер"),
        (new[] { "discord" }, "discord", "Discord"),
        (new[] { "telegram" }, "telegram", "Telegram"),
        (new[] { "steam", "steamwebhelper" }, "steam", "Steam"),
        (new[] { "epicgameslauncher" }, "epic", "Epic Games Launcher"),
        (new[] { "wallpaper32", "wallpaper64" }, "wallpaper", "Wallpaper Engine"),
        (new[] { "overwolf" }, "overwolf", "Overwolf"),
        (new[] { "obs64", "obs32" }, "obs", "OBS"),
        (new[] { "widgets", "widgetservice" }, "widgets", "Виджеты Windows"),
        (new[] { "qbittorrent", "utorrent", "bittorrent" }, "torrent", "Торрент-клиент"),
        (new[] { "onedrive" }, "onedrive", "OneDrive"),
    };

    private static List<HeavyProcessInfo> CollectHeavyProcesses()
    {
        var acc = new Dictionary<string, (string Display, int Count, long Bytes)>(StringComparer.Ordinal);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                foreach (var g in ProcessGroups)
                {
                    if (!g.Names.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase))) continue;
                    acc.TryGetValue(g.Key, out var cur);
                    acc[g.Key] = (g.Display, cur.Count + 1, cur.Bytes + p.WorkingSet64);
                    break;
                }
            }
            catch {}
            finally { p.Dispose(); }
        }
        return acc.Select(kv => new HeavyProcessInfo(kv.Key, kv.Value.Display, kv.Value.Count, kv.Value.Bytes))
                  .OrderByDescending(h => h.WorkingSetBytes)
                  .ToList();
    }

    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = global::System.Runtime.InteropServices.CharSet.Unicode)]
    private struct DEVMODE
    {
        [global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [global::System.Runtime.InteropServices.DllImport("user32.dll", CharSet = global::System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool EnumDisplaySettingsW(string? lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

    private const int EnumCurrentSettings = -1;

    private static DisplayModeInfo CollectDisplay()
    {
        var dm = new DEVMODE { dmSize = (ushort)global::System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettingsW(null, EnumCurrentSettings, ref dm))
            throw new InvalidOperationException("EnumDisplaySettings не отдал текущий режим");
        uint w = dm.dmPelsWidth, h = dm.dmPelsHeight, cur = dm.dmDisplayFrequency;

        uint max = cur;
        var probe = new DEVMODE { dmSize = dm.dmSize };
        for (int i = 0; EnumDisplaySettingsW(null, i, ref probe); i++)
        {
            if (probe.dmPelsWidth == w && probe.dmPelsHeight == h && probe.dmDisplayFrequency > max)
                max = probe.dmDisplayFrequency;
        }
        return new DisplayModeInfo((int)w, (int)h, (int)cur, (int)max);
    }

    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }

    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId; public uint id; public uint modeInfoIdx;
        public uint outputTechnology; public uint rotation; public uint scaling;
        public uint refreshNumerator; public uint refreshDenominator;
        public uint scanLineOrdering; public int targetAvailable; public uint statusFlags;
    }

    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINTL { public int x; public int y; }

    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_SOURCE_MODE { public uint width; public uint height; public uint pixelFormat; public POINTL position; }

    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Explicit, Size = 48)]
    private struct DISPLAYCONFIG_MODE_INFO_UNION
    {
        [global::System.Runtime.InteropServices.FieldOffset(0)] public DISPLAYCONFIG_SOURCE_MODE sourceMode;
    }

    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_MODE_INFO { public uint infoType; public uint id; public LUID adapterId; public DISPLAYCONFIG_MODE_INFO_UNION info; }

    [global::System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [global::System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements,
        [global::System.Runtime.InteropServices.Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements,
        [global::System.Runtime.InteropServices.Out] DISPLAYCONFIG_MODE_INFO[] modeArray,
        IntPtr currentTopologyId);

    private const uint QdcOnlyActivePaths = 2;
    private const uint DisplayConfigSourceMode = 1;

    [global::System.Runtime.InteropServices.StructLayout(global::System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = global::System.Runtime.InteropServices.CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public uint StateFlags;
        [global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }

    [global::System.Runtime.InteropServices.DllImport("user32.dll", CharSet = global::System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool EnumDisplayDevicesW(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    private const uint DisplayDeviceAttachedToDesktop = 0x00000001;
    private const uint DisplayDevicePrimary           = 0x00000004;
    private const uint EddGetDeviceInterfaceName      = 0x00000001;

    private static IReadOnlyList<MonitorInfo> CollectMonitors()
    {
        var names = EdidNames();
        var targets = TargetIdsByPosition();
        var edidByTarget = EdidNamesByTargetId();
        var result = new List<MonitorInfo>();

        var dev = new DISPLAY_DEVICE { cb = global::System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevicesW(null, i, ref dev, 0); i++)
        {
            if ((dev.StateFlags & DisplayDeviceAttachedToDesktop) == 0)
            {
                dev = new DISPLAY_DEVICE { cb = dev.cb };
                continue;
            }

            var deviceName = dev.DeviceName;
            bool primary = (dev.StateFlags & DisplayDevicePrimary) != 0;

            var dm = new DEVMODE { dmSize = (ushort)global::System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
            if (EnumDisplaySettingsW(deviceName, EnumCurrentSettings, ref dm))
            {
                uint w = dm.dmPelsWidth, h = dm.dmPelsHeight, cur = dm.dmDisplayFrequency, max = dm.dmDisplayFrequency;
                var probe = new DEVMODE { dmSize = dm.dmSize };
                for (int k = 0; EnumDisplaySettingsW(deviceName, k, ref probe); k++)
                    if (probe.dmPelsWidth == w && probe.dmPelsHeight == h && probe.dmDisplayFrequency > max)
                        max = probe.dmDisplayFrequency;

                string friendly = "";
                var mon = new DISPLAY_DEVICE { cb = dev.cb };
                if (EnumDisplayDevicesW(deviceName, 0, ref mon, EddGetDeviceInterfaceName))
                {
                    var key = Squash(mon.DeviceID);
                    foreach (var (instance, name) in names)
                        if (key.Length > 0 && instance.Length > 0 && key.Contains(instance, StringComparison.Ordinal))
                        {
                            friendly = name;
                            break;
                        }

                    if (friendly.Length == 0) friendly = EdidNameFromRegistry(mon.DeviceID);
                }

                if (friendly.Length == 0
                    && targets.TryGetValue((dm.dmPositionX, dm.dmPositionY), out var targetId)
                    && edidByTarget.TryGetValue(targetId, out var byTarget))
                {
                    friendly = byTarget;
                }

                result.Add(new MonitorInfo(
                    friendly,
                    deviceName.Replace(@"\\.\", ""),
                    dev.DeviceString.Trim(),
                    (int)w, (int)h, (int)cur, (int)max, primary));
            }

            dev = new DISPLAY_DEVICE { cb = dev.cb };
        }

        return result.OrderByDescending(m => m.IsPrimary).ToList();
    }

    private static List<(string Instance, string Name)> EdidNames()
    {
        var list = new List<(string, string)>();
        try
        {
            foreach (var mo in Query("SELECT InstanceName, UserFriendlyName, ManufacturerName FROM WmiMonitorID",
                                     @"\\.\root\wmi"))
            {
                var instance = Squash(ToStr(mo["InstanceName"]));
                var model  = FromCharArray(mo["UserFriendlyName"]);
                var vendor = FromCharArray(mo["ManufacturerName"]);
                var name = string.IsNullOrWhiteSpace(model)
                    ? vendor
                    : (string.IsNullOrWhiteSpace(vendor)
                       || model.StartsWith(vendor, StringComparison.OrdinalIgnoreCase)
                        ? model
                        : vendor + " " + model);
                if (instance.Length > 0 && !string.IsNullOrWhiteSpace(name)) list.Add((instance, name.Trim()));
            }
        }
        catch {}
        return list;
    }

    private static string EdidNameFromRegistry(string? interfacePath)
    {
        if (string.IsNullOrEmpty(interfacePath)) return "";
        var parts = interfacePath!.Split('#');
        if (parts.Length < 3) return "";
        var hardwareId = parts[1];
        var instance = parts[2];

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{hardwareId}\{instance}\Device Parameters");
            if (key?.GetValue("EDID") is byte[] edid && edid.Length >= 126)
            {
                for (int offset = 54; offset + 18 <= 126; offset += 18)
                {
                    if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 2] != 0) continue;
                    if (edid[offset + 3] != 0xFC) continue;

                    var sb = new global::System.Text.StringBuilder(13);
                    for (int k = offset + 5; k < offset + 18; k++)
                    {
                        var ch = (char)edid[k];
                        if (ch == '\n' || ch == '\0') break;
                        sb.Append(ch);
                    }
                    var name = sb.ToString().Trim();
                    if (name.Length > 0) return name;
                }
            }
        }
        catch {}

        return hardwareId.Trim();
    }

    private static Dictionary<(int X, int Y), uint> TargetIdsByPosition()
    {
        var map = new Dictionary<(int, int), uint>();
        try
        {
            if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var nPaths, out var nModes) != 0) return map;
            var paths = new DISPLAYCONFIG_PATH_INFO[nPaths];
            var modes = new DISPLAYCONFIG_MODE_INFO[nModes];
            if (QueryDisplayConfig(QdcOnlyActivePaths, ref nPaths, paths, ref nModes, modes, IntPtr.Zero) != 0) return map;

            for (int i = 0; i < nPaths; i++)
            {
                var idx = paths[i].sourceInfo.modeInfoIdx;
                if (idx >= nModes) continue;
                var mode = modes[idx];
                if (mode.infoType != DisplayConfigSourceMode) continue;
                map[(mode.info.sourceMode.position.x, mode.info.sourceMode.position.y)] = paths[i].targetInfo.id;
            }
        }
        catch {}
        return map;
    }

    private static Dictionary<uint, string> EdidNamesByTargetId()
    {
        var map = new Dictionary<uint, string>();
        try
        {
            using var baseKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Enum\DISPLAY");
            if (baseKey is null) return map;

            foreach (var hardwareId in baseKey.GetSubKeyNames())
            {
                using var hwKey = baseKey.OpenSubKey(hardwareId);
                if (hwKey is null) continue;
                foreach (var instance in hwKey.GetSubKeyNames())
                {
                    int uidAt = instance.LastIndexOf("UID", StringComparison.OrdinalIgnoreCase);
                    if (uidAt < 0 || !uint.TryParse(instance[(uidAt + 3)..], out var targetId)) continue;
                    if (map.ContainsKey(targetId)) continue;

                    using var instKey = hwKey.OpenSubKey(instance + @"\Device Parameters");
                    if (instKey?.GetValue("EDID") is not byte[] edid) continue;
                    var name = EdidMonitorName(edid);
                    if (name.Length > 0) map[targetId] = name;
                }
            }
        }
        catch {}
        return map;
    }

    private static string EdidMonitorName(byte[] edid)
    {
        if (edid.Length < 126) return "";
        for (int offset = 54; offset + 18 <= 126; offset += 18)
        {
            if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 2] != 0) continue;
            if (edid[offset + 3] != 0xFC) continue;

            var sb = new global::System.Text.StringBuilder(13);
            for (int k = offset + 5; k < offset + 18; k++)
            {
                var ch = (char)edid[k];
                if (ch == '\n' || ch == '\0') break;
                sb.Append(ch);
            }
            var name = sb.ToString().Trim();
            if (name.Length > 0) return name;
        }
        return "";
    }

    private static string FromCharArray(object? value)
    {
        if (value is not ushort[] codes) return "";
        var sb = new global::System.Text.StringBuilder(codes.Length);
        foreach (var c in codes)
        {
            if (c == 0) break;
            sb.Append((char)c);
        }
        return sb.ToString().Trim();
    }

    private static string Squash(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new global::System.Text.StringBuilder(s!.Length);
        foreach (var ch in s!)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    private static readonly string[] TunnelMarkers =
        { "Tunnel", "VPN", "TAP-", "WireGuard", "NordLynx", "Hamachi", "Radmin", "ZeroTier",
          "Hyper-V", "VirtualBox", "VMware", "Loopback" };

    private static NetworkInfo CollectNetwork()
    {
        bool wired = false, wireless = false, vpn = false;
        string active = "", vpnName = "";
        foreach (var mo in Query("SELECT Name, NetConnectionID, PhysicalAdapter FROM Win32_NetworkAdapter WHERE NetConnectionStatus = 2"))
        {
            if (mo["PhysicalAdapter"] is bool phys && !phys) continue;
            var name = ToStr(mo["Name"]);
            var connId = ToStr(mo["NetConnectionID"]);

            if (TunnelMarkers.Any(m => name.Contains(m, StringComparison.OrdinalIgnoreCase)))
            {
                vpn = true;
                if (vpnName.Length == 0) vpnName = name;
                continue;
            }

            bool isWireless = new[] { name, connId }.Any(x =>
                x.Contains("Wireless", StringComparison.OrdinalIgnoreCase) ||
                x.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                x.Contains("WiFi", StringComparison.OrdinalIgnoreCase) ||
                x.Contains("802.11", StringComparison.OrdinalIgnoreCase) ||
                x.Contains("Беспроводная", StringComparison.OrdinalIgnoreCase));
            if (isWireless) wireless = true; else wired = true;
            if (active.Length == 0) active = name;
        }
        return new NetworkInfo(wired, wireless, vpn, active, vpnName);
    }

    private static PagefileInfo CollectPagefile()
    {
        bool auto = false;
        foreach (var mo in Query("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem"))
            auto = mo["AutomaticManagedPagefile"] is bool b && b;

        long allocatedMb = 0;
        bool present = false;
        foreach (var mo in Query("SELECT AllocatedBaseSize FROM Win32_PageFileUsage"))
        {
            present = true;
            allocatedMb += ToLong(mo["AllocatedBaseSize"]);
        }
        return new PagefileInfo(auto, present, allocatedMb);
    }

    private static WinFeaturesInfo CollectWinFeatures()
    {
        bool? hags = null, gameMode = null, transparency = null;
        using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers"))
            if (k?.GetValue("HwSchMode") is int h) hags = h == 2;
        using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\GameBar"))
            if (k?.GetValue("AutoGameModeEnabled") is int g) gameMode = g != 0;
        using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            if (k?.GetValue("EnableTransparency") is int t) transparency = t != 0;
        return new WinFeaturesInfo(hags, gameMode, transparency);
    }

    private static BcdInfo CollectBcd()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding oem;
        try { oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch { oem = Encoding.Default; }

        var psi = new ProcessStartInfo("bcdedit", "/enum {current}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oem,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("bcdedit не запустился");
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        if (p.ExitCode != 0)
            throw new InvalidOperationException("bcdedit: нет прав (нужен запуск от администратора)");

        static bool? Flag(string text, string name)
        {
            var m = Regex.Match(text, name + @"\s+(\S+)", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            var v = m.Groups[1].Value;
            return v.Equals("Yes", StringComparison.OrdinalIgnoreCase) || v.Equals("Да", StringComparison.OrdinalIgnoreCase);
        }
        return new BcdInfo(Flag(output, "useplatformclock"), Flag(output, "disabledynamictick"));
    }

    private static GameInstallInfo CollectGame(string gtaPath)
    {
        var media = DiskMedia.Unknown;
        var root = Path.GetPathRoot(gtaPath);
        var letter = string.IsNullOrEmpty(root) ? '\0' : char.ToUpperInvariant(root[0]);
        if (letter is >= 'A' and <= 'Z')
        {
            int diskNumber = -1;
            foreach (var mo in Query($"SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter = '{letter}'",
                                     @"\\.\root\microsoft\windows\storage"))
                diskNumber = ToInt(mo["DiskNumber"]);
            if (diskNumber >= 0)
            {
                foreach (var mo in Query($"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId = '{diskNumber}'",
                                         @"\\.\root\microsoft\windows\storage"))
                {
                    media = ToInt(mo["MediaType"]) switch
                    {
                        3 => DiskMedia.Hdd,
                        4 => DiskMedia.Ssd,
                        5 => DiskMedia.Scm,
                        _ => DiskMedia.Unknown
                    };
                }
            }
        }

        bool? excluded = null;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths");
            if (k is not null)
            {
                var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gtaPath));
                excluded = k.GetValueNames().Any(name =>
                {
                    var ex = Path.TrimEndingDirectorySeparator(name.Trim());
                    return full.StartsWith(ex, StringComparison.OrdinalIgnoreCase) && ex.Length >= 3;
                });
            }
        }
        catch {}

        return new GameInstallInfo(gtaPath, media, excluded);
    }

    private static List<AvProductInfo> CollectAv()
    {
        var list = new List<AvProductInfo>();
        foreach (var mo in Query("SELECT displayName FROM AntiVirusProduct", @"\\.\root\SecurityCenter2"))
        {
            var name = ToStr(mo["displayName"]);
            if (name.Length > 0) list.Add(new AvProductInfo(name));
        }
        return list;
    }

    private static PrefetchInfo CollectPrefetch()
    {
        int? pf = null, sf = null;
        using var k = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters");
        if (k?.GetValue("EnablePrefetcher") is int p) pf = p;
        if (k?.GetValue("EnableSuperfetch") is int s) sf = s;
        return new PrefetchInfo(pf, sf);
    }

    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string ProcThrottleMin = "893dee8e-2bef-41e0-89c6-b55d0929964c";
    private const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3";
    private const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
    private const string SubPcie = "501a4d13-42af-4429-9fd1-a8218c268e20";
    private const string PcieAspm = "ee12f906-d277-404b-b6da-e5fa1a576df5";

    private static int? GetAcValue(string subGuid, string settingGuid)
    {
        var psi = new ProcessStartInfo("powercfg", $"/getacvalueindex scheme_current {subGuid} {settingGuid}")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        if (p is null) return null;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        var m = Regex.Match(output, @"0x([0-9a-fA-F]+)");
        return m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) : null;
    }

    private static PowerDetailsInfo CollectPowerDetails()
    {
        var min = GetAcValue(SubProcessor, ProcThrottleMin);
        var usb = GetAcValue(SubUsb, UsbSelectiveSuspend);
        var aspm = GetAcValue(SubPcie, PcieAspm);
        return new PowerDetailsInfo(min, usb is null ? null : usb != 0, aspm);
    }

    private static VisualFxInfo CollectVisualFx()
    {
        using var k = Registry.CurrentUser.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
        return new VisualFxInfo(k?.GetValue("VisualFXSetting") is int v ? v : null);
    }

    private static readonly string[] GameProcessNames = { "GTA5", "GTA5_Enhanced" };

    private static GameProcessInfo? CollectGameProcess()
    {
        foreach (var name in GameProcessNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    var affinity = (ulong)(long)p.ProcessorAffinity;
                    int cores = 0;
                    for (var m = affinity; m != 0; m >>= 1)
                        if ((m & 1) != 0) cores++;
                    return new GameProcessInfo(
                        ProcessName: p.ProcessName,
                        PriorityClass: p.PriorityClass.ToString(),
                        AffinityCores: cores,
                        TotalCores: Environment.ProcessorCount);
                }
                catch (Exception ex)
                {
                    return new GameProcessInfo(p.ProcessName, $"недоступно: {ex.Message}", 0, Environment.ProcessorCount);
                }
                finally { p.Dispose(); }
            }
        }
        return null;
    }
}
