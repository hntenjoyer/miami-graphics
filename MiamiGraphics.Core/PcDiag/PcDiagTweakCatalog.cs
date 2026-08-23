#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MiamiGraphics.Core.PcDiag;

[SupportedOSPlatform("windows")]
public static class PcDiagTweakCatalog
{
    public enum TweakState
    {
        Ready,
        Done,
        NotApplicable
    }

    public sealed record CatalogItem(
        string Id,
        string Grade,
        bool RequiresRestart,
        bool InAllSafe,
        TweakState State,
        Dictionary<string, string> Data
    );

    private const string GraphicsDriversPath = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string SystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GamesTask = SystemProfile + @"\Tasks\Games";
    private const string PriorityControl = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string MousePath = @"Control Panel\Mouse";
    private const string GameBarPath = @"SOFTWARE\Microsoft\GameBar";

    internal static readonly string[] PlaceboFlags = { "-high", "-norestrictions", "-nomemrestrict", "-veryhigh" };

    public static List<CatalogItem> List(string? gtaPath)
    {
        var items = new List<CatalogItem>
        {
            Item("mmcss-games", "micro", restart: false, inAllSafe: true,
                state: MmcssDone() ? TweakState.Done : TweakState.Ready),

            Item("system-responsiveness", "micro", restart: false, inAllSafe: true,
                state: ReadDword(Registry.LocalMachine, SystemProfile, "SystemResponsiveness") == 10
                    ? TweakState.Done : TweakState.Ready),

            Item("gamebar-nexus-off", "micro", restart: false, inAllSafe: true,
                state: ReadDword(Registry.CurrentUser, GameBarPath, "UseNexusForGameBarEnabled") == 0
                    ? TweakState.Done : TweakState.Ready),

            Item("stickykeys-off", "device", restart: false, inAllSafe: true,
                state: StickyDone() ? TweakState.Done : TweakState.Ready),

            Item("mouse-accel-off", "device", restart: false, inAllSafe: false,
                state: MouseAccelDone() ? TweakState.Done : TweakState.Ready),

            Item("w32-priority-separation", "experiment", restart: false, inAllSafe: false,
                state: ReadDword(Registry.LocalMachine, PriorityControl, "Win32PrioritySeparation") == 0x26
                    ? TweakState.Done : TweakState.Ready),

            Item("network-throttling-off", "experiment", restart: false, inAllSafe: false,
                state: ReadDword(Registry.LocalMachine, SystemProfile, "NetworkThrottlingIndex") == -1
                    ? TweakState.Done : TweakState.Ready),
        };

        items.Add(new CatalogItem("nvidia-profile", "works", RequiresRestart: false,
            InAllSafe: false, NvidiaState(), new Dictionary<string, string>()));

        items.Add(Item("hags-on", "experiment", restart: true, inAllSafe: false,
            state: Environment.OSVersion.Version.Build < 19041 ? TweakState.NotApplicable
                : ReadDword(Registry.LocalMachine, GraphicsDriversPath, "HwSchMode") == 2
                    ? TweakState.Done : TweakState.Ready));

        items.Add(Item("fso-off-gta", "experiment", restart: false, inAllSafe: false,
            state: FsoState(gtaPath)));

        items.Add(Item("power-throttling-gta", "micro", restart: false, inAllSafe: true,
            state: PowerThrottleState(gtaPath)));

        items.Add(Item("widgets-off", "micro", restart: true, inAllSafe: true,
            state: Environment.OSVersion.Version.Build < 22000 ? TweakState.NotApplicable
                : ReadDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests") == 0
                    ? TweakState.Done : TweakState.Ready));

        items.Add(Item("background-apps-off", "micro", restart: false, inAllSafe: true,
            state: ReadDword(Registry.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled") == 1
                ? TweakState.Done : TweakState.Ready));

        var cl = CommandlineState(gtaPath);
        items.Add(new CatalogItem("commandline-clean", "works", RequiresRestart: false,
            InAllSafe: cl.State == TweakState.Ready, cl.State, cl.Data));

        var shader = ShaderCacheState();
        items.Add(new CatalogItem("shader-cache-clean", "maintenance", RequiresRestart: false,
            InAllSafe: false, shader.State, shader.Data));
        var temp = TempState();
        items.Add(new CatalogItem("temp-clean", "maintenance", RequiresRestart: false,
            InAllSafe: false, temp.State, temp.Data));

        return items;

        static CatalogItem Item(string id, string grade, bool restart, bool inAllSafe, TweakState state) =>
            new(id, grade, restart, inAllSafe, state, new Dictionary<string, string>());
    }

    private static TweakState FsoState(string? gtaPath)
    {
        if (string.IsNullOrEmpty(gtaPath)) return TweakState.NotApplicable;
        var exe = Path.Combine(gtaPath, "GTA5.exe");
        if (!File.Exists(exe)) return TweakState.NotApplicable;
        var layers = ReadString(Registry.CurrentUser,
            @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", exe);
        return layers?.Contains("DISABLEDXMAXIMIZEDWINDOWEDMODE", StringComparison.OrdinalIgnoreCase) == true
            ? TweakState.Done : TweakState.Ready;
    }

    private static TweakState PowerThrottleState(string? gtaPath)
    {
        if (string.IsNullOrEmpty(gtaPath)) return TweakState.NotApplicable;
        if (!File.Exists(Path.Combine(gtaPath, "GTA5.exe"))) return TweakState.NotApplicable;
        try
        {
            var (code, output) = PcDiagApplier.RunTool("powercfg", "/powerthrottling list");
            if (code != 0) return TweakState.Ready;
            return output.Contains("GTA5.exe", StringComparison.OrdinalIgnoreCase)
                ? TweakState.Done : TweakState.Ready;
        }
        catch { return TweakState.Ready; }
    }

    private static TweakState NvidiaState()
    {
        try
        {
            if (!PcDiagNvidia.IsAvailable()) return TweakState.NotApplicable;
            var st = PcDiagNvidia.ReadState();
            if (!st.AppBound) return TweakState.Ready;
            for (int i = 0; i < PcDiagNvidia.TargetSettings.Length; i++)
                if (st.CurrentValues[i] != PcDiagNvidia.TargetSettings[i].Value)
                    return TweakState.Ready;
            return TweakState.Done;
        }
        catch { return TweakState.NotApplicable; }
    }

    internal static int? ReadDword(RegistryKey root, string path, string name)
    {
        using var k = root.OpenSubKey(path);
        return k?.GetValue(name) is int v ? v : null;
    }

    private static string? ReadString(RegistryKey root, string path, string name)
    {
        using var k = root.OpenSubKey(path);
        return k?.GetValue(name)?.ToString();
    }

    private static bool MmcssDone() =>
        ReadDword(Registry.LocalMachine, GamesTask, "GPU Priority") == 8 &&
        ReadDword(Registry.LocalMachine, GamesTask, "Priority") == 6 &&
        ReadString(Registry.LocalMachine, GamesTask, "Scheduling Category") == "High";

    private static bool StickyDone() =>
        ReadString(Registry.CurrentUser, @"Control Panel\Accessibility\StickyKeys", "Flags") == "506";

    private static bool MouseAccelDone() =>
        ReadString(Registry.CurrentUser, MousePath, "MouseSpeed") == "0";

    private static (TweakState State, Dictionary<string, string> Data) CommandlineState(string? gtaPath)
    {
        var data = new Dictionary<string, string>();
        if (string.IsNullOrEmpty(gtaPath))
            return (TweakState.NotApplicable, data);
        var path = Path.Combine(gtaPath, "commandline.txt");
        if (!File.Exists(path))
            return (TweakState.Done, data);
        try
        {
            var text = File.ReadAllText(path);
            var found = PlaceboFlags.Where(f => text.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 0) return (TweakState.Done, data);
            data["flags"] = string.Join(", ", found);
            return (TweakState.Ready, data);
        }
        catch { return (TweakState.NotApplicable, data); }
    }

    internal static IEnumerable<string> ShaderCacheDirs()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "NVIDIA", "DXCache");
        yield return Path.Combine(local, "NVIDIA", "GLCache");
        yield return Path.Combine(local, "D3DSCache");
        yield return Path.Combine(local, "AMD", "DxCache");
        yield return Path.Combine(local, "AMD", "DxcCache");
    }

    private static (TweakState State, Dictionary<string, string> Data) ShaderCacheState()
    {
        long bytes = 0;
        foreach (var dir in ShaderCacheDirs())
        {
            try
            {
                if (Directory.Exists(dir))
                    bytes += new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { }
        }
        var data = new Dictionary<string, string> { ["mb"] = (bytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) };
        return (bytes > 64L * 1024 * 1024 ? TweakState.Ready : TweakState.Done, data);
    }

    private static (TweakState State, Dictionary<string, string> Data) TempState()
    {
        long bytes = 0;
        try
        {
            var tmp = Path.GetTempPath();
            var cutoff = DateTime.UtcNow.AddDays(-7);
            bytes = new DirectoryInfo(tmp).EnumerateFiles("*", SearchOption.AllDirectories)
                .Where(f => { try { return f.LastWriteTimeUtc < cutoff; } catch { return false; } })
                .Sum(f => { try { return f.Length; } catch { return 0; } });
        }
        catch { }
        var data = new Dictionary<string, string> { ["mb"] = (bytes / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) };
        return (bytes > 256L * 1024 * 1024 ? TweakState.Ready : TweakState.Done, data);
    }
}
