#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MiamiGraphics.Core.PcDiag;

public static class PcDiagRules
{
    public static List<DiagFinding> Evaluate(PcSnapshot s)
    {
        var findings = new List<DiagFinding>();

        EvalCpu(s, findings);
        EvalRam(s, findings);
        EvalDisks(s, findings);
        EvalGpu(s, findings);
        EvalPower(s, findings);
        EvalGameDvr(s, findings);
        EvalSecurity(s, findings);
        EvalGame(s, findings);
        EvalPagefile(s, findings);
        EvalServices(s, findings);
        EvalBcd(s, findings);
        EvalDisplay(s, findings);
        EvalNetwork(s, findings);
        EvalWinFeatures(s, findings);
        EvalBackground(s, findings);
        EvalAntivirus(s, findings);
        EvalCompetitiveTraces(s, findings);
        EvalGameProcess(s, findings);
        EvalPowerDetails(s, findings);
        EvalVisualFx(s, findings);

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Category)
            .ToList();
    }

    private static Dictionary<string, string> D(params (string k, string v)[] kv)
        => kv.ToDictionary(p => p.k, p => p.v, StringComparer.Ordinal);

    private static void EvalCpu(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.Cpu is null) return;
        var r = PcDiagCpuRating.Rate(s.Cpu);

        if (r.Parsed)
        {
            var data = D(
                ("name", s.Cpu.Name),
                ("tier", r.Tier.ToString()),
                ("family", r.FamilyLabel),
                ("l3Mb", (s.Cpu.L3CacheKb / 1024).ToString()));

            switch (r.Tier)
            {
                case CpuTier.S:
                    f.Add(new DiagFinding("cpu-tier-s", DiagSeverity.Info, DiagCategory.Hardware, data));
                    break;
                case CpuTier.D:
                    f.Add(new DiagFinding("cpu-tier-d", DiagSeverity.Major, DiagCategory.Hardware, data));
                    break;
                case CpuTier.C:
                    f.Add(new DiagFinding("cpu-tier-c", DiagSeverity.Minor, DiagCategory.Hardware, data));
                    break;
            }

            if (r.IsHybrid)
                f.Add(new DiagFinding("cpu-hybrid", DiagSeverity.Info, DiagCategory.Hardware,
                    D(("name", s.Cpu.Name))));
        }
        else
        {
            f.Add(new DiagFinding("cpu-unrecognized", DiagSeverity.Info, DiagCategory.Hardware,
                D(("name", s.Cpu.Name))));
        }
    }

    private static void EvalRam(PcSnapshot s, List<DiagFinding> f)
    {
        long totalGb = s.TotalRamBytes / (1024L * 1024 * 1024);
        if (totalGb > 0 && totalGb < 12)
            f.Add(new DiagFinding("ram-critical", DiagSeverity.Critical, DiagCategory.Hardware,
                D(("totalGb", totalGb.ToString()))));
        else if (totalGb > 0 && totalGb < 16)
            f.Add(new DiagFinding("ram-low", DiagSeverity.Major, DiagCategory.Hardware,
                D(("totalGb", totalGb.ToString()))));

        if (s.RamSticks.Count == 0) return;

        var slow = s.RamSticks.FirstOrDefault(r => r.RatedMt > 0 && r.ConfiguredMt > 0 &&
                                                   r.ConfiguredMt < r.RatedMt - 132);
        if (slow is not null)
        {
            f.Add(new DiagFinding("ram-xmp-off", DiagSeverity.Major, DiagCategory.Hardware,
                D(("rated", slow.RatedMt.ToString()), ("actual", slow.ConfiguredMt.ToString())),
                GainMinPercent: 10, GainMaxPercent: 25));
        }
        else
        {
            bool ddr4Jedec = s.RamSticks.All(r => r.SmbiosMemoryType == 26 && r.ConfiguredMt > 0 && r.ConfiguredMt <= 2666);
            bool ddr5Jedec = s.RamSticks.All(r => r.SmbiosMemoryType == 34 && r.ConfiguredMt > 0 && r.ConfiguredMt < 4800);
            if (ddr4Jedec || ddr5Jedec)
                f.Add(new DiagFinding("ram-jedec-speed", DiagSeverity.Minor, DiagCategory.Hardware,
                    D(("actual", s.RamSticks[0].ConfiguredMt.ToString()),
                      ("gen", ddr4Jedec ? "DDR4" : "DDR5"))));
        }

        if (s.RamSticks.Count == 1)
        {
            f.Add(new DiagFinding("ram-single-channel", DiagSeverity.Major, DiagCategory.Hardware,
                D(("sticks", "1")), GainMinPercent: 10, GainMaxPercent: 30));
        }
        else
        {
            var channels = s.RamSticks
                .Select(r => ChannelIdentity(r.BankLabel, r.DeviceLocator))
                .Where(c => c is not null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (channels.Count == 1 && s.RamSticks.Count >= 2)
                f.Add(new DiagFinding("ram-single-channel", DiagSeverity.Major, DiagCategory.Hardware,
                    D(("sticks", s.RamSticks.Count.ToString()), ("channel", channels[0]!)),
                    GainMinPercent: 10, GainMaxPercent: 30));
        }
    }

    private static string? ChannelIdentity(string bankLabel, string deviceLocator)
    {
        foreach (var src in new[] { deviceLocator, bankLabel })
        {
            var m = Regex.Match(src, @"(?:(Controller\d+)[-_ ]*)?(Channel\s*[A-Z0-9])", RegexOptions.IgnoreCase);
            if (m.Success)
                return (m.Groups[1].Success ? m.Groups[1].Value + "-" : "") + m.Groups[2].Value.Replace(" ", "");
        }
        return null;
    }

    private static void EvalDisks(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.Game is { Media: not DiskMedia.Unknown }) return;

        if (s.Disks.Any(d => d.Media == DiskMedia.Hdd))
        {
            bool hasSsd = s.Disks.Any(d => d.Media == DiskMedia.Ssd || d.Media == DiskMedia.Scm);
            f.Add(new DiagFinding("disk-hdd-present", hasSsd ? DiagSeverity.Minor : DiagSeverity.Critical,
                DiagCategory.Hardware,
                D(("hddModel", s.Disks.First(d => d.Media == DiskMedia.Hdd).Model),
                  ("hasSsd", hasSsd ? "1" : "0"))));
        }
    }

    private static void EvalGpu(PcSnapshot s, List<DiagFinding> f)
    {
        var discrete = s.Gpus.Where(g => !g.IsIntegrated).ToList();
        var main = discrete.OrderByDescending(g => g.VramBytes).FirstOrDefault()
                   ?? s.Gpus.OrderByDescending(g => g.VramBytes).FirstOrDefault();
        if (main is null) return;

        long vramGb = main.VramBytes / (1024L * 1024 * 1024);
        if (vramGb > 0 && vramGb < 4)
            f.Add(new DiagFinding("vram-critical", DiagSeverity.Critical, DiagCategory.Hardware,
                D(("gpu", main.Name), ("vramGb", vramGb.ToString()))));
        else if (vramGb > 0 && vramGb < 6)
            f.Add(new DiagFinding("vram-low", DiagSeverity.Minor, DiagCategory.Hardware,
                D(("gpu", main.Name), ("vramGb", vramGb.ToString()))));

        if (main.DriverDate is DateTime dd)
        {
            var age = DateTime.UtcNow - dd;
            if (age > TimeSpan.FromDays(548))
                f.Add(new DiagFinding("gpu-driver-old", DiagSeverity.Major, DiagCategory.Driver,
                    D(("gpu", main.Name), ("months", ((int)(age.TotalDays / 30)).ToString()))));
            else if (age > TimeSpan.FromDays(365))
                f.Add(new DiagFinding("gpu-driver-aging", DiagSeverity.Minor, DiagCategory.Driver,
                    D(("gpu", main.Name), ("months", ((int)(age.TotalDays / 30)).ToString()))));
        }

        if (discrete.Count > 0 && s.Gpus.Any(g => g.IsIntegrated) && s.HasBattery)
            f.Add(new DiagFinding("dual-gpu-check-render", DiagSeverity.Info, DiagCategory.Hardware,
                D(("dgpu", discrete[0].Name))));
    }

    private static void EvalPower(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.Power is null) return;
        switch (s.Power.Kind)
        {
            case PowerSchemeKind.PowerSaver:
                f.Add(new DiagFinding("power-saver", DiagSeverity.Major, DiagCategory.Windows,
                    D(("scheme", s.Power.SchemeName)), GainMinPercent: 5, GainMaxPercent: 30, AutoFixable: true));
                break;
            case PowerSchemeKind.Balanced:
                f.Add(new DiagFinding("power-balanced", s.HasBattery ? DiagSeverity.Major : DiagSeverity.Minor,
                    DiagCategory.Windows,
                    D(("scheme", s.Power.SchemeName), ("laptop", s.HasBattery ? "1" : "0")),
                    GainMinPercent: 2, GainMaxPercent: s.HasBattery ? 15 : 5, AutoFixable: true));
                break;
            case PowerSchemeKind.Custom:
                f.Add(new DiagFinding("power-custom", DiagSeverity.Info, DiagCategory.Windows,
                    D(("scheme", s.Power.SchemeName))));
                break;
        }
    }

    private static void EvalGameDvr(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.GameDvr is null) return;
        bool dvrOn = s.GameDvr.GameDvrEnabled ?? true;
        bool captureOn = s.GameDvr.AppCaptureEnabled ?? true;
        if (dvrOn || captureOn)
            f.Add(new DiagFinding("gamedvr-on", DiagSeverity.Minor, DiagCategory.Windows,
                D(("gameDvr", dvrOn ? "1" : "0"), ("appCapture", captureOn ? "1" : "0")),
                GainMinPercent: 1, GainMaxPercent: 5, AutoFixable: true));
    }

    private static void EvalSecurity(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.Security is null) return;
        if (s.Security.VbsRunning)
            f.Add(new DiagFinding("vbs-running", DiagSeverity.Minor, DiagCategory.Windows,
                D(("hvci", s.Security.HvciRunning ? "1" : "0")),
                GainMinPercent: 3, GainMaxPercent: 8, AutoFixable: false));
    }

    private static void EvalGame(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.Game is null) return;

        if (s.Game.Media == DiskMedia.Hdd)
            f.Add(new DiagFinding("game-on-hdd", DiagSeverity.Critical, DiagCategory.Game,
                D(("path", s.Game.GtaPath))));

        if (s.Game.InDefenderExclusions == false)
            f.Add(new DiagFinding("game-not-in-av-exclusions", DiagSeverity.Minor, DiagCategory.Game,
                D(("path", s.Game.GtaPath))));
    }

    private static void EvalPagefile(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.Pagefile is null) return;
        if (!s.Pagefile.Present)
            f.Add(new DiagFinding("pagefile-off", DiagSeverity.Major, DiagCategory.Windows, D(), AutoFixable: true));
    }

    private static void EvalServices(PcSnapshot s, List<DiagFinding> f)
    {
        foreach (var svc in s.Services)
        {
            if (!svc.Exists) continue;
            bool disabled = svc.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
            if (!disabled && svc.Running) continue;

            switch (svc.Name)
            {
                case "SysMain":
                    f.Add(new DiagFinding("sysmain-off", DiagSeverity.Minor, DiagCategory.Windows,
                        D(("state", disabled ? "disabled" : "stopped")), AutoFixable: true));
                    break;
                case "WSearch":
                    f.Add(new DiagFinding("wsearch-off", DiagSeverity.Info, DiagCategory.Windows,
                        D(("state", disabled ? "disabled" : "stopped")), AutoFixable: true));
                    break;
            }
        }
    }

    private static void EvalBcd(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.Bcd is null) return;
        if (s.Bcd.UsePlatformClock == true)
            f.Add(new DiagFinding("bcd-useplatformclock", DiagSeverity.Major, DiagCategory.Windows, D(), AutoFixable: true));
        if (s.Bcd.DisableDynamicTick == true)
            f.Add(new DiagFinding("bcd-disabledynamictick", DiagSeverity.Info, DiagCategory.Windows, D()));
    }

    private static void EvalDisplay(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.Display is null) return;
        if (s.Display.MaxHz >= s.Display.CurrentHz + 20)
            f.Add(new DiagFinding("display-not-max-hz", DiagSeverity.Major, DiagCategory.Windows,
                D(("current", s.Display.CurrentHz.ToString()),
                  ("max", s.Display.MaxHz.ToString()),
                  ("res", $"{s.Display.Width}x{s.Display.Height}")),
                AutoFixable: true));
    }

    private static void EvalNetwork(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.Network is null) return;
        if (s.Network.HasWirelessActive && !s.Network.HasWiredActive)
            f.Add(new DiagFinding("wifi-only", DiagSeverity.Info, DiagCategory.Windows,
                D(("adapter", s.Network.ActiveAdapterName))));

        if (s.Network.HasVpnActive)
            f.Add(new DiagFinding("vpn-active", DiagSeverity.Info, DiagCategory.Windows,
                D(("adapter", s.Network.VpnAdapterName))));
    }

    private static void EvalWinFeatures(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.WinFeatures is null) return;

        if (s.WinFeatures.GameModeOn == false)
            f.Add(new DiagFinding("gamemode-off", DiagSeverity.Minor, DiagCategory.Windows, D(), AutoFixable: true));

        if (s.WinFeatures.HagsOn is bool hags)
            f.Add(new DiagFinding("hags-state", DiagSeverity.Info, DiagCategory.Windows,
                D(("on", hags ? "1" : "0"))));

        if (s.WinFeatures.TransparencyOn == true)
            f.Add(new DiagFinding("transparency-on", DiagSeverity.Info, DiagCategory.Windows, D(), AutoFixable: true));
    }

    private static void EvalBackground(PcSnapshot s, List<DiagFinding> f)
    {
        long totalGb = s.TotalRamBytes / (1024L * 1024 * 1024);
        foreach (var h in s.HeavyProcesses)
        {
            long gb10 = h.WorkingSetBytes * 10 / (1024L * 1024 * 1024);
            switch (h.Key)
            {
                case "browser":
                    if (gb10 >= 15)
                        f.Add(new DiagFinding("bg-browser", totalGb <= 16 ? DiagSeverity.Major : DiagSeverity.Info,
                            DiagCategory.Apps,
                            D(("gb", (gb10 / 10.0).ToString("0.0")), ("count", h.Count.ToString()))));
                    break;
                case "wallpaper":
                    f.Add(new DiagFinding("bg-wallpaper", DiagSeverity.Minor, DiagCategory.Apps,
                        D(("gb", (gb10 / 10.0).ToString("0.0")))));
                    break;
                case "torrent":
                    f.Add(new DiagFinding("bg-torrent", DiagSeverity.Major, DiagCategory.Apps,
                        D(("name", h.DisplayName))));
                    break;
                case "widgets":
                    if (gb10 >= 3)
                        f.Add(new DiagFinding("bg-widgets", DiagSeverity.Info, DiagCategory.Apps,
                            D(("gb", (gb10 / 10.0).ToString("0.0")))));
                    break;
                case "discord":
                    f.Add(new DiagFinding("bg-discord-overlay", DiagSeverity.Info, DiagCategory.Apps, D()));
                    break;
                case "overwolf":
                    f.Add(new DiagFinding("bg-overwolf", DiagSeverity.Minor, DiagCategory.Apps, D()));
                    break;
            }
        }

        if (s.Autostart.Count >= 8)
        {
            var names = string.Join(", ", s.Autostart.Take(6).Select(a => a.Name));
            f.Add(new DiagFinding("autostart-crowded", DiagSeverity.Info, DiagCategory.Apps,
                D(("count", s.Autostart.Count.ToString()), ("sample", names))));
        }
    }

    private static void EvalAntivirus(PcSnapshot s, List<DiagFinding> f)
    {
        var thirdParty = s.AvProducts
            .Where(a => !a.Name.Contains("Defender", StringComparison.OrdinalIgnoreCase) &&
                        !a.Name.Contains("Защитник", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (thirdParty.Count > 0)
            f.Add(new DiagFinding("av-third-party", DiagSeverity.Minor, DiagCategory.Windows,
                D(("names", string.Join(", ", thirdParty.Select(a => a.Name))))));
    }

    private static void EvalCompetitiveTraces(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.Prefetch is { EnablePrefetcher: 0 })
            f.Add(new DiagFinding("prefetch-off", DiagSeverity.Major, DiagCategory.Windows, D(), AutoFixable: true));

        var eventLog = s.Services.FirstOrDefault(x => x.Name.Equals("EventLog", StringComparison.OrdinalIgnoreCase));
        if (eventLog is { Exists: true } &&
            (!eventLog.Running || eventLog.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)))
            f.Add(new DiagFinding("eventlog-off", DiagSeverity.Major, DiagCategory.Windows, D(), AutoFixable: true));
    }

    private static void EvalGameProcess(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.GameProcess is null) return;
        var gp = s.GameProcess;

        if (gp.PriorityClass.Equals("Normal", StringComparison.OrdinalIgnoreCase))
            f.Add(new DiagFinding("game-priority-normal", DiagSeverity.Minor, DiagCategory.Game,
                D(("process", gp.ProcessName)), AutoFixable: true));
        else if (gp.PriorityClass.Equals("RealTime", StringComparison.OrdinalIgnoreCase))
            f.Add(new DiagFinding("game-priority-realtime", DiagSeverity.Major, DiagCategory.Game,
                D(("process", gp.ProcessName))));

        if (gp.AffinityCores > 0 && gp.AffinityCores < gp.TotalCores)
            f.Add(new DiagFinding("game-affinity-limited", DiagSeverity.Info, DiagCategory.Game,
                D(("cores", gp.AffinityCores.ToString()), ("total", gp.TotalCores.ToString()))));
    }

    private static void EvalPowerDetails(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.PowerDetails is null) return;
        var pd = s.PowerDetails;

        bool minLow = pd.MinProcessorPercent is int mp && mp < 100;
        bool usbOn = pd.UsbSelectiveSuspend == true;
        bool aspmOn = pd.PcieAspm is int aspm && aspm != 0;
        if (minLow || usbOn || aspmOn)
        {
            var parts = new List<string>();
            if (minLow) parts.Add($"мин. частота CPU {pd.MinProcessorPercent}%");
            if (usbOn) parts.Add("USB засыпает");
            if (aspmOn) parts.Add("PCI Express экономит");
            f.Add(new DiagFinding("device-power-savings", DiagSeverity.Minor, DiagCategory.Windows,
                D(("what", string.Join(", ", parts))), AutoFixable: true));
        }
    }

    private static void EvalVisualFx(PcSnapshot s, List<DiagFinding> f)
    {
        if (s.VisualFx is { Setting: not null and not 2 })
            f.Add(new DiagFinding("visualfx-full", DiagSeverity.Info, DiagCategory.Windows,
                D(), AutoFixable: true));
    }
}
