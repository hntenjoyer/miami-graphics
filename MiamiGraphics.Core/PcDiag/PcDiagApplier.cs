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
using System.Text.Json;
using Microsoft.Win32;

namespace MiamiGraphics.Core.PcDiag;

[SupportedOSPlatform("windows")]
public static class PcDiagApplier
{
    public sealed record TweakResult(bool Ok, string Message, bool RequiresRestart);

    public sealed record JournalEntry(
        string Id,
        DateTime AppliedAtUtc,
        Dictionary<string, string?> Previous,
        bool Reverted,
        DateTime? RevertedAtUtc);

    private sealed record JournalFile(bool RestorePointCreated, List<JournalEntry> Entries);

    private static readonly object Gate = new();

    private static string JournalPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiamiGraphics", "pcdiag", "journal.json");

    public static IReadOnlyList<JournalEntry> ReadJournal()
    {
        lock (Gate) return Load().Entries;
    }

    private static JournalFile Load()
    {
        try
        {
            if (File.Exists(JournalPath))
                return JsonSerializer.Deserialize<JournalFile>(File.ReadAllText(JournalPath))
                       ?? new JournalFile(false, new List<JournalEntry>());
        }
        catch {}
        return new JournalFile(false, new List<JournalEntry>());
    }

    private static void Save(JournalFile f)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        File.WriteAllText(JournalPath, JsonSerializer.Serialize(f, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static readonly Dictionary<string, (Func<Dictionary<string, string?>> Capture,
                                                Func<Dictionary<string, string?>, TweakResult> Do,
                                                Func<Dictionary<string, string?>, TweakResult> Undo)> Tweaks
        = new(StringComparer.Ordinal)
    {
        ["power-balanced"] = (CapturePower, ApplyPowerHigh, RevertPower),
        ["power-saver"] = (CapturePower, ApplyPowerHigh, RevertPower),
        ["gamedvr-on"] = (CaptureGameDvr, ApplyGameDvrOff, RevertGameDvr),
        ["gamemode-off"] = (CaptureGameMode, ApplyGameModeOn, RevertGameMode),
        ["transparency-on"] = (CaptureTransparency, ApplyTransparencyOff, RevertTransparency),
        ["display-not-max-hz"] = (CaptureDisplay, ApplyDisplayMaxHz, RevertDisplay),
        ["sysmain-off"] = (() => CaptureService("SysMain"), _ => RestoreService("SysMain"), d => RevertService("SysMain", d)),
        ["wsearch-off"] = (() => CaptureService("WSearch"), _ => RestoreService("WSearch"), d => RevertService("WSearch", d)),
        ["eventlog-off"] = (() => CaptureService("EventLog"), _ => RestoreService("EventLog"), d => RevertService("EventLog", d)),
        ["prefetch-off"] = (CapturePrefetch, ApplyPrefetchOn, RevertPrefetch),
        ["pagefile-off"] = (CapturePagefile, ApplyPagefileAuto, RevertPagefile),
        ["bcd-useplatformclock"] = (CaptureBcdClock, ApplyBcdClockOff, RevertBcdClock),
        ["game-priority-normal"] = (CapturePriority, ApplyPriorityHigh, RevertPriority),
        ["device-power-savings"] = (CapturePowerDetails, ApplyPowerDetails, RevertPowerDetails),
        ["visualfx-full"] = (CaptureVisualFx, ApplyVisualFxPerf, RevertVisualFx),
        ["mmcss-games"] = (CaptureMmcss, ApplyMmcss, RevertMmcss),
        ["system-responsiveness"] = (CaptureSysResp, ApplySysResp, RevertSysResp),
        ["gamebar-nexus-off"] = (CaptureNexus, ApplyNexusOff, RevertNexus),
        ["stickykeys-off"] = (CaptureSticky, ApplyStickyOff, RevertSticky),
        ["mouse-accel-off"] = (CaptureMouseAccel, ApplyMouseAccelOff, RevertMouseAccel),
        ["w32-priority-separation"] = (CaptureW32Ps, ApplyW32Ps, RevertW32Ps),
        ["network-throttling-off"] = (CaptureNetThrottle, ApplyNetThrottleOff, RevertNetThrottle),
        ["commandline-clean"] = (CaptureCommandline, ApplyCommandlineClean, RevertCommandline),
        ["shader-cache-clean"] = (CaptureNothing, ApplyShaderClean, NoRevert),
        ["temp-clean"] = (CaptureNothing, ApplyTempClean, NoRevert),
        ["nvidia-profile"] = (CaptureNothing, ApplyNvidiaProfile, RevertNvidiaProfile),
        ["hags-on"] = (CaptureHags, ApplyHagsOn, RevertHags),
        ["fso-off-gta"] = (CaptureFso, ApplyFsoOff, RevertFso),
        ["power-throttling-gta"] = (CapturePowerThrottle, ApplyPowerThrottleOff, RevertPowerThrottle),
        ["widgets-off"] = (CaptureWidgets, ApplyWidgetsOff, RevertWidgets),
        ["background-apps-off"] = (CaptureBgApps, ApplyBgAppsOff, RevertBgApps),
    };

    public static bool CanApply(string findingId) => Tweaks.ContainsKey(findingId);

    public static TweakResult Apply(string findingId)
    {
        if (!Tweaks.TryGetValue(findingId, out var t))
            return new TweakResult(false, $"Для «{findingId}» автопочинки нет.", false);

        lock (Gate)
        {
            var journal = Load();

            string restoreNote = "";
            if (!journal.RestorePointCreated)
            {
                var rp = TryCreateRestorePoint();
                restoreNote = rp.Ok ? " Точка восстановления создана." : $" Точка восстановления: {rp.Message}";
                journal = journal with { RestorePointCreated = true };
            }

            Dictionary<string, string?> prev;
            try { prev = t.Capture(); }
            catch (Exception ex) { return new TweakResult(false, $"Не удалось снять прежнее состояние: {ex.Message}", false); }

            TweakResult result;
            try { result = t.Do(prev); }
            catch (Exception ex) { result = new TweakResult(false, ex.Message, false); }

            if (result.Ok)
            {
                journal.Entries.Add(new JournalEntry(findingId, DateTime.UtcNow, prev, false, null));
                Save(journal);
                result = result with { Message = result.Message + restoreNote };
            }
            return result;
        }
    }

    public static TweakResult Revert(string findingId)
    {
        if (!Tweaks.TryGetValue(findingId, out var t))
            return new TweakResult(false, $"Для «{findingId}» отката нет.", false);

        lock (Gate)
        {
            var journal = Load();
            var entry = journal.Entries.LastOrDefault(e => e.Id == findingId && !e.Reverted);
            if (entry is null)
                return new TweakResult(false, "В журнале нет применённого изменения для отката.", false);

            TweakResult result;
            try { result = t.Undo(entry.Previous); }
            catch (Exception ex) { result = new TweakResult(false, ex.Message, false); }

            if (result.Ok)
            {
                var i = journal.Entries.IndexOf(entry);
                journal.Entries[i] = entry with { Reverted = true, RevertedAtUtc = DateTime.UtcNow };
                Save(journal);
            }
            return result;
        }
    }

    private static TweakResult TryCreateRestorePoint()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\default");
            var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            var args = cls.GetMethodParameters("CreateRestorePoint");
            args["Description"] = "Miami Graphics: оптимизация";
            args["RestorePointType"] = 12;
            args["EventType"] = 100;
            var ret = cls.InvokeMethod("CreateRestorePoint", args, null);
            var code = Convert.ToInt32(ret["ReturnValue"], CultureInfo.InvariantCulture);
            return code == 0
                ? new TweakResult(true, "", false)
                : new TweakResult(false, $"система вернула код {code} (частая причина: точка уже создавалась сегодня)", false);
        }
        catch (Exception ex)
        {
            return new TweakResult(false, $"недоступна ({ex.Message})", false);
        }
    }

    internal static (int Code, string Output) RunTool(string file, string args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding oem;
        try { oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch { oem = Encoding.Default; }
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = oem,
            StandardErrorEncoding = oem,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"{file} не запустился");
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return (p.ExitCode, output.Trim());
    }

    private static string? ReadReg(RegistryKey root, string path, string name)
    {
        using var k = root.OpenSubKey(path);
        var v = k?.GetValue(name);
        return v?.ToString();
    }

    private static void WriteRegDword(RegistryKey root, string path, string name, int? value)
    {
        using var k = root.CreateSubKey(path)!;
        if (value is null) { try { k.DeleteValue(name); } catch { } }
        else k.SetValue(name, value.Value, RegistryValueKind.DWord);
    }

    private static int? ParseInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    private static Dictionary<string, string?> CapturePower()
    {
        var (code, output) = RunTool("powercfg", "/getactivescheme");
        if (code != 0) throw new InvalidOperationException("powercfg не отвечает");
        var m = global::System.Text.RegularExpressions.Regex.Match(output,
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        if (!m.Success) throw new InvalidOperationException("не удалось прочитать текущую схему");
        return new() { ["scheme"] = m.Value };
    }

    private static TweakResult ApplyPowerHigh(Dictionary<string, string?> prev)
    {
        var (code, output) = RunTool("powercfg", $"/setactive {HighPerfGuid}");
        return code == 0
            ? new TweakResult(true, "Схема «Высокая производительность» включена.", false)
            : new TweakResult(false, $"powercfg: {output}", false);
    }

    private static TweakResult RevertPower(Dictionary<string, string?> prev)
    {
        var scheme = prev.GetValueOrDefault("scheme");
        if (string.IsNullOrEmpty(scheme)) return new TweakResult(false, "в журнале нет прежней схемы", false);
        var (code, output) = RunTool("powercfg", $"/setactive {scheme}");
        return code == 0
            ? new TweakResult(true, "Прежняя схема питания возвращена.", false)
            : new TweakResult(false, $"powercfg: {output}", false);
    }

    private const string GcsPath = @"System\GameConfigStore";
    private const string DvrPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\GameDVR";

    private static Dictionary<string, string?> CaptureGameDvr() => new()
    {
        ["gcs"] = ReadReg(Registry.CurrentUser, GcsPath, "GameDVR_Enabled"),
        ["cap"] = ReadReg(Registry.CurrentUser, DvrPath, "AppCaptureEnabled"),
    };

    private static TweakResult ApplyGameDvrOff(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, GcsPath, "GameDVR_Enabled", 0);
        WriteRegDword(Registry.CurrentUser, DvrPath, "AppCaptureEnabled", 0);
        return new TweakResult(true, "Фоновая запись Game Bar выключена.", false);
    }

    private static TweakResult RevertGameDvr(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, GcsPath, "GameDVR_Enabled", ParseInt(prev.GetValueOrDefault("gcs")));
        WriteRegDword(Registry.CurrentUser, DvrPath, "AppCaptureEnabled", ParseInt(prev.GetValueOrDefault("cap")));
        return new TweakResult(true, "Настройки записи Game Bar возвращены.", false);
    }

    private const string GameBarPath = @"SOFTWARE\Microsoft\GameBar";
    private const string PersonalizePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static Dictionary<string, string?> CaptureGameMode() => new()
    { ["v"] = ReadReg(Registry.CurrentUser, GameBarPath, "AutoGameModeEnabled") };

    private static TweakResult ApplyGameModeOn(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, GameBarPath, "AutoGameModeEnabled", 1);
        return new TweakResult(true, "Игровой режим включён.", false);
    }

    private static TweakResult RevertGameMode(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, GameBarPath, "AutoGameModeEnabled", ParseInt(prev.GetValueOrDefault("v")));
        return new TweakResult(true, "Прежнее состояние игрового режима возвращено.", false);
    }

    private static Dictionary<string, string?> CaptureTransparency() => new()
    { ["v"] = ReadReg(Registry.CurrentUser, PersonalizePath, "EnableTransparency") };

    private static TweakResult ApplyTransparencyOff(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, PersonalizePath, "EnableTransparency", 0);
        return new TweakResult(true, "Прозрачность выключена. Интерфейс Windows подхватит после перезахода в систему.", false);
    }

    private static TweakResult RevertTransparency(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, PersonalizePath, "EnableTransparency", ParseInt(prev.GetValueOrDefault("v")));
        return new TweakResult(true, "Прозрачность возвращена.", false);
    }

    private static Dictionary<string, string?> CaptureDisplay()
    {
        var d = PcDiagDisplay.Current();
        return new() { ["hz"] = d.CurrentHz.ToString(CultureInfo.InvariantCulture) };
    }

    private static TweakResult ApplyDisplayMaxHz(Dictionary<string, string?> prev)
    {
        var d = PcDiagDisplay.Current();
        if (d.MaxHz <= d.CurrentHz) return new TweakResult(false, "экран уже на максимальной герцовке", false);
        return PcDiagDisplay.SetHz(d.MaxHz)
            ? new TweakResult(true, $"Герцовка переключена на {d.MaxHz} Гц.", false)
            : new TweakResult(false, "монитор отказался переключаться (возможно, мешает полноэкранная игра)", false);
    }

    private static TweakResult RevertDisplay(Dictionary<string, string?> prev)
    {
        var hz = ParseInt(prev.GetValueOrDefault("hz"));
        if (hz is null) return new TweakResult(false, "в журнале нет прежней герцовки", false);
        return PcDiagDisplay.SetHz(hz.Value)
            ? new TweakResult(true, $"Герцовка возвращена на {hz} Гц.", false)
            : new TweakResult(false, "монитор отказался переключаться", false);
    }

    private static Dictionary<string, string?> CaptureService(string name)
    {
        string? startMode = null, state = null;
        using var searcher = new ManagementObjectSearcher($"SELECT State, StartMode FROM Win32_Service WHERE Name = '{name}'");
        foreach (ManagementObject mo in searcher.Get())
        {
            startMode = mo["StartMode"]?.ToString();
            state = mo["State"]?.ToString();
        }
        return new() { ["startMode"] = startMode, ["state"] = state };
    }

    private static TweakResult RestoreService(string name)
    {
        var (c1, o1) = RunTool("sc.exe", $"config {name} start= auto");
        if (c1 != 0) return new TweakResult(false, $"sc config: {o1}", false);
        var (c2, o2) = RunTool("sc.exe", $"start {name}");
        if (c2 != 0 && !o2.Contains("1056"))
            return new TweakResult(false, $"sc start: {o2}", false);
        return new TweakResult(true, $"Служба {name} включена и запущена.", false);
    }

    private static TweakResult RevertService(string name, Dictionary<string, string?> prev)
    {
        var mode = prev.GetValueOrDefault("startMode") switch
        {
            "Disabled" => "disabled",
            "Manual" => "demand",
            _ => "auto"
        };
        var (c1, o1) = RunTool("sc.exe", $"config {name} start= {mode}");
        if (c1 != 0) return new TweakResult(false, $"sc config: {o1}", false);
        if (prev.GetValueOrDefault("state") != "Running")
            RunTool("sc.exe", $"stop {name}");
        return new TweakResult(true, $"Служба {name} возвращена в прежнее состояние.", false);
    }

    private const string PrefetchPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters";

    private static Dictionary<string, string?> CapturePrefetch() => new()
    { ["v"] = ReadReg(Registry.LocalMachine, PrefetchPath, "EnablePrefetcher") };

    private static TweakResult ApplyPrefetchOn(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, PrefetchPath, "EnablePrefetcher", 3);
        return new TweakResult(true, "Prefetch включён (значение 3, штатное).", false);
    }

    private static TweakResult RevertPrefetch(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, PrefetchPath, "EnablePrefetcher", ParseInt(prev.GetValueOrDefault("v")));
        return new TweakResult(true, "Прежнее значение Prefetch возвращено.", false);
    }

    private static Dictionary<string, string?> CapturePagefile()
    {
        bool auto = false;
        using var searcher = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem");
        foreach (ManagementObject mo in searcher.Get())
            auto = mo["AutomaticManagedPagefile"] is bool b && b;
        return new() { ["auto"] = auto ? "1" : "0" };
    }

    private static TweakResult ApplyPagefileAuto(Dictionary<string, string?> prev)
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
        foreach (ManagementObject mo in searcher.Get())
        {
            mo["AutomaticManagedPagefile"] = true;
            mo.Put();
        }
        return new TweakResult(true, "Файл подкачки переведён в автоматический режим.", RequiresRestart: true);
    }

    private static TweakResult RevertPagefile(Dictionary<string, string?> prev)
    {
        if (prev.GetValueOrDefault("auto") == "1")
            return new TweakResult(true, "Файл подкачки и был автоматическим.", false);
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem");
        foreach (ManagementObject mo in searcher.Get())
        {
            mo["AutomaticManagedPagefile"] = false;
            mo.Put();
        }
        return new TweakResult(true, "Автоматическое управление подкачкой снято, как было.", RequiresRestart: true);
    }

    private static Dictionary<string, string?> CaptureBcdClock() => new() { ["was"] = "true" };

    private static TweakResult ApplyBcdClockOff(Dictionary<string, string?> prev)
    {
        var (code, output) = RunTool("bcdedit", "/deletevalue useplatformclock");
        return code == 0
            ? new TweakResult(true, "Флаг useplatformclock убран.", RequiresRestart: true)
            : new TweakResult(false, $"bcdedit: {output}", false);
    }

    private static TweakResult RevertBcdClock(Dictionary<string, string?> prev)
    {
        var (code, output) = RunTool("bcdedit", "/set useplatformclock true");
        return code == 0
            ? new TweakResult(true, "Флаг useplatformclock возвращён.", RequiresRestart: true)
            : new TweakResult(false, $"bcdedit: {output}", false);
    }

    private const string IfeoGta = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\GTA5.exe\PerfOptions";

    private static Dictionary<string, string?> CapturePriority() => new()
    { ["cpuPrio"] = ReadReg(Registry.LocalMachine, IfeoGta, "CpuPriorityClass") };

    private static TweakResult ApplyPriorityHigh(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, IfeoGta, "CpuPriorityClass", 3);

        var live = "";
        foreach (var p in Process.GetProcessesByName("GTA5"))
        {
            try { p.PriorityClass = ProcessPriorityClass.High; live = " Запущенной игре приоритет поднят сразу."; }
            catch { live = " Запущенной игре поднять не удалось, подействует со следующего запуска."; }
            finally { p.Dispose(); }
        }
        return new TweakResult(true, "Приоритет High для GTA5.exe закреплён." + live, false);
    }

    private static TweakResult RevertPriority(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, IfeoGta, "CpuPriorityClass", ParseInt(prev.GetValueOrDefault("cpuPrio")));
        foreach (var p in Process.GetProcessesByName("GTA5"))
        {
            try { p.PriorityClass = ProcessPriorityClass.Normal; } catch { }
            finally { p.Dispose(); }
        }
        return new TweakResult(true, "Приоритет GTA5.exe возвращён.", false);
    }

    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string ProcThrottleMin = "893dee8e-2bef-41e0-89c6-b55d0929964c";
    private const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3";
    private const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
    private const string SubPcie = "501a4d13-42af-4429-9fd1-a8218c268e20";
    private const string PcieAspm = "ee12f906-d277-404b-b6da-e5fa1a576df5";

    private static int? GetAc(string sub, string setting)
    {
        var (code, output) = RunTool("powercfg", $"/getacvalueindex scheme_current {sub} {setting}");
        if (code != 0) return null;
        var m = global::System.Text.RegularExpressions.Regex.Match(output, @"0x([0-9a-fA-F]+)");
        return m.Success ? Convert.ToInt32(m.Groups[1].Value, 16) : null;
    }

    private static TweakResult SetAc(string sub, string setting, int value)
    {
        var (code, output) = RunTool("powercfg", $"/setacvalueindex scheme_current {sub} {setting} {value}");
        return code == 0 ? new TweakResult(true, "", false) : new TweakResult(false, output, false);
    }

    private static Dictionary<string, string?> CapturePowerDetails() => new()
    {
        ["minProc"] = GetAc(SubProcessor, ProcThrottleMin)?.ToString(CultureInfo.InvariantCulture),
        ["usb"] = GetAc(SubUsb, UsbSelectiveSuspend)?.ToString(CultureInfo.InvariantCulture),
        ["aspm"] = GetAc(SubPcie, PcieAspm)?.ToString(CultureInfo.InvariantCulture),
    };

    private static TweakResult ApplyPowerDetails(Dictionary<string, string?> prev)
    {
        var r1 = SetAc(SubProcessor, ProcThrottleMin, 100);
        var r2 = SetAc(SubUsb, UsbSelectiveSuspend, 0);
        var r3 = SetAc(SubPcie, PcieAspm, 0);
        if (!r1.Ok || !r2.Ok || !r3.Ok)
            return new TweakResult(false, string.Join("; ", new[] { r1, r2, r3 }.Where(r => !r.Ok).Select(r => r.Message)), false);
        RunTool("powercfg", "/setactive scheme_current");
        return new TweakResult(true, "Экономия питания устройств отключена (от сети): CPU 100%, USB и PCI Express не засыпают.", false);
    }

    private static TweakResult RevertPowerDetails(Dictionary<string, string?> prev)
    {
        void Restore(string sub, string setting, string key)
        {
            var v = ParseInt(prev.GetValueOrDefault(key));
            if (v is not null) SetAc(sub, setting, v.Value);
        }
        Restore(SubProcessor, ProcThrottleMin, "minProc");
        Restore(SubUsb, UsbSelectiveSuspend, "usb");
        Restore(SubPcie, PcieAspm, "aspm");
        RunTool("powercfg", "/setactive scheme_current");
        return new TweakResult(true, "Прежние параметры питания устройств возвращены.", false);
    }

    private const string VisualFxPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
    private const string DesktopPath = @"Control Panel\Desktop";

    private static Dictionary<string, string?> CaptureVisualFx() => new()
    {
        ["setting"] = ReadReg(Registry.CurrentUser, VisualFxPath, "VisualFXSetting"),
        ["fontSmoothing"] = ReadReg(Registry.CurrentUser, DesktopPath, "FontSmoothing"),
    };

    private static TweakResult ApplyVisualFxPerf(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, VisualFxPath, "VisualFXSetting", 2);
        using (var k = Registry.CurrentUser.CreateSubKey(DesktopPath)!)
            k.SetValue("FontSmoothing", "2", RegistryValueKind.String);
        return new TweakResult(true, "Визуальные эффекты переведены в режим производительности, сглаживание шрифтов сохранено. Windows подхватит после перезахода в систему.", false);
    }

    private static TweakResult RevertVisualFx(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, VisualFxPath, "VisualFXSetting", ParseInt(prev.GetValueOrDefault("setting")));
        var fs = prev.GetValueOrDefault("fontSmoothing");
        using (var k = Registry.CurrentUser.CreateSubKey(DesktopPath)!)
        {
            if (fs is null) { try { k.DeleteValue("FontSmoothing"); } catch { } }
            else k.SetValue("FontSmoothing", fs, RegistryValueKind.String);
        }
        return new TweakResult(true, "Визуальные эффекты возвращены.", false);
    }

    private const string SystemProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string GamesTask = SystemProfile + @"\Tasks\Games";
    private const string PriorityControl = @"SYSTEM\CurrentControlSet\Control\PriorityControl";
    private const string MousePath = @"Control Panel\Mouse";

    private static string? ReadRegAny(RegistryKey root, string path, string name)
    {
        using var k = root.OpenSubKey(path);
        return k?.GetValue(name)?.ToString();
    }

    private static void WriteRegString(RegistryKey root, string path, string name, string? value)
    {
        using var k = root.CreateSubKey(path)!;
        if (value is null) { try { k.DeleteValue(name); } catch { } }
        else k.SetValue(name, value, RegistryValueKind.String);
    }

    private static Dictionary<string, string?> CaptureMmcss() => new()
    {
        ["gpuPrio"] = ReadRegAny(Registry.LocalMachine, GamesTask, "GPU Priority"),
        ["prio"] = ReadRegAny(Registry.LocalMachine, GamesTask, "Priority"),
        ["schedCat"] = ReadRegAny(Registry.LocalMachine, GamesTask, "Scheduling Category"),
        ["sfio"] = ReadRegAny(Registry.LocalMachine, GamesTask, "SFIO Priority"),
    };

    private static TweakResult ApplyMmcss(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, GamesTask, "GPU Priority", 8);
        WriteRegDword(Registry.LocalMachine, GamesTask, "Priority", 6);
        WriteRegString(Registry.LocalMachine, GamesTask, "Scheduling Category", "High");
        WriteRegString(Registry.LocalMachine, GamesTask, "SFIO Priority", "High");
        return new TweakResult(true, "Профиль планировщика мультимедиа для игр поднят (GPU Priority 8, категория High).", false);
    }

    private static TweakResult RevertMmcss(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, GamesTask, "GPU Priority", ParseInt(prev.GetValueOrDefault("gpuPrio")));
        WriteRegDword(Registry.LocalMachine, GamesTask, "Priority", ParseInt(prev.GetValueOrDefault("prio")));
        WriteRegString(Registry.LocalMachine, GamesTask, "Scheduling Category", prev.GetValueOrDefault("schedCat"));
        WriteRegString(Registry.LocalMachine, GamesTask, "SFIO Priority", prev.GetValueOrDefault("sfio"));
        return new TweakResult(true, "Профиль планировщика возвращён.", false);
    }

    private static Dictionary<string, string?> CaptureSysResp() => new()
    { ["v"] = ReadRegAny(Registry.LocalMachine, SystemProfile, "SystemResponsiveness") };

    private static TweakResult ApplySysResp(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, SystemProfile, "SystemResponsiveness", 10);
        return new TweakResult(true, "Резерв CPU под фоновые мультимедиа-задачи снижен с 20% до 10%.", false);
    }

    private static TweakResult RevertSysResp(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, SystemProfile, "SystemResponsiveness", ParseInt(prev.GetValueOrDefault("v")));
        return new TweakResult(true, "SystemResponsiveness возвращён.", false);
    }

    private static Dictionary<string, string?> CaptureNetThrottle() => new()
    { ["v"] = ReadRegAny(Registry.LocalMachine, SystemProfile, "NetworkThrottlingIndex") };

    private static TweakResult ApplyNetThrottleOff(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, SystemProfile, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF));
        return new TweakResult(true, "Троттлинг сетевых пакетов при мультимедиа отключён. Эффект проверяйте замером: на большинстве систем он в пределах погрешности.", false);
    }

    private static TweakResult RevertNetThrottle(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, SystemProfile, "NetworkThrottlingIndex", ParseInt(prev.GetValueOrDefault("v")));
        return new TweakResult(true, "NetworkThrottlingIndex возвращён.", false);
    }

    private static Dictionary<string, string?> CaptureNexus() => new()
    { ["v"] = ReadRegAny(Registry.CurrentUser, GameBarPath, "UseNexusForGameBarEnabled") };

    private static TweakResult ApplyNexusOff(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, GameBarPath, "UseNexusForGameBarEnabled", 0);
        return new TweakResult(true, "Кнопка Xbox на геймпаде больше не открывает оверлей Game Bar.", false);
    }

    private static TweakResult RevertNexus(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, GameBarPath, "UseNexusForGameBarEnabled", ParseInt(prev.GetValueOrDefault("v")));
        return new TweakResult(true, "Поведение кнопки Xbox возвращено.", false);
    }

    private const string StickyPath = @"Control Panel\Accessibility\StickyKeys";
    private const string TogglePath = @"Control Panel\Accessibility\ToggleKeys";
    private const string FilterPath = @"Control Panel\Accessibility\Keyboard Response";

    private static Dictionary<string, string?> CaptureSticky() => new()
    {
        ["sticky"] = ReadRegAny(Registry.CurrentUser, StickyPath, "Flags"),
        ["toggle"] = ReadRegAny(Registry.CurrentUser, TogglePath, "Flags"),
        ["filter"] = ReadRegAny(Registry.CurrentUser, FilterPath, "Flags"),
    };

    private static TweakResult ApplyStickyOff(Dictionary<string, string?> prev)
    {
        WriteRegString(Registry.CurrentUser, StickyPath, "Flags", "506");
        WriteRegString(Registry.CurrentUser, TogglePath, "Flags", "58");
        WriteRegString(Registry.CurrentUser, FilterPath, "Flags", "122");
        return new TweakResult(true, "Горячие клавиши залипания и фильтрации отключены: системные окна больше не выскочат в бою.", false);
    }

    private static TweakResult RevertSticky(Dictionary<string, string?> prev)
    {
        WriteRegString(Registry.CurrentUser, StickyPath, "Flags", prev.GetValueOrDefault("sticky"));
        WriteRegString(Registry.CurrentUser, TogglePath, "Flags", prev.GetValueOrDefault("toggle"));
        WriteRegString(Registry.CurrentUser, FilterPath, "Flags", prev.GetValueOrDefault("filter"));
        return new TweakResult(true, "Настройки спецвозможностей возвращены.", false);
    }

    private static Dictionary<string, string?> CaptureMouseAccel() => new()
    {
        ["speed"] = ReadRegAny(Registry.CurrentUser, MousePath, "MouseSpeed"),
        ["t1"] = ReadRegAny(Registry.CurrentUser, MousePath, "MouseThreshold1"),
        ["t2"] = ReadRegAny(Registry.CurrentUser, MousePath, "MouseThreshold2"),
    };

    private static TweakResult ApplyMouseAccelOff(Dictionary<string, string?> prev)
    {
        WriteRegString(Registry.CurrentUser, MousePath, "MouseSpeed", "0");
        WriteRegString(Registry.CurrentUser, MousePath, "MouseThreshold1", "0");
        WriteRegString(Registry.CurrentUser, MousePath, "MouseThreshold2", "0");
        return new TweakResult(true, "Ускорение указателя выключено: одинаковое движение руки всегда даёт одинаковое движение прицела. Подействует после перезахода в систему.", false);
    }

    private static TweakResult RevertMouseAccel(Dictionary<string, string?> prev)
    {
        WriteRegString(Registry.CurrentUser, MousePath, "MouseSpeed", prev.GetValueOrDefault("speed"));
        WriteRegString(Registry.CurrentUser, MousePath, "MouseThreshold1", prev.GetValueOrDefault("t1"));
        WriteRegString(Registry.CurrentUser, MousePath, "MouseThreshold2", prev.GetValueOrDefault("t2"));
        return new TweakResult(true, "Настройки указателя возвращены.", false);
    }

    private static Dictionary<string, string?> CaptureW32Ps() => new()
    { ["v"] = ReadRegAny(Registry.LocalMachine, PriorityControl, "Win32PrioritySeparation") };

    private static TweakResult ApplyW32Ps(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, PriorityControl, "Win32PrioritySeparation", 0x26);
        return new TweakResult(true, "Кванты планировщика переведены в 0x26. Это эксперимент: эффект на грани погрешности, проверяйте замером и возвращайте, если разницы нет.", false);
    }

    private static TweakResult RevertW32Ps(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, PriorityControl, "Win32PrioritySeparation", ParseInt(prev.GetValueOrDefault("v")) ?? 2);
        return new TweakResult(true, "Кванты планировщика возвращены.", false);
    }

    private const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    private const string CompatLayers = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string DshPolicy = @"SOFTWARE\Policies\Microsoft\Dsh";
    private const string BgAccess = @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";

    private static Dictionary<string, string?> CaptureHags() => new()
    { ["v"] = ReadRegAny(Registry.LocalMachine, GraphicsDrivers, "HwSchMode") };

    private static TweakResult ApplyHagsOn(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, GraphicsDrivers, "HwSchMode", 2);
        return new TweakResult(true, "Аппаратное планирование GPU включено.", true);
    }

    private static TweakResult RevertHags(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, GraphicsDrivers, "HwSchMode", ParseInt(prev.GetValueOrDefault("v")));
        return new TweakResult(true, "Прежний режим планирования GPU возвращён.", true);
    }

    internal static string? FindGta5Exe()
    {
        try
        {
            var gta = new MiamiGraphics.Core.System.HardwareLocator().FindGtaPath();
            if (string.IsNullOrWhiteSpace(gta)) return null;
            var p = Path.Combine(gta, "GTA5.exe");
            return File.Exists(p) ? p : null;
        }
        catch { return null; }
    }

    private const string FsoFlag = "DISABLEDXMAXIMIZEDWINDOWEDMODE";

    private static Dictionary<string, string?> CaptureFso()
    {
        var exe = FindGta5Exe() ?? throw new InvalidOperationException("GTA5.exe не найден");
        return new() { ["exe"] = exe, ["v"] = ReadRegAny(Registry.CurrentUser, CompatLayers, exe) };
    }

    private static TweakResult ApplyFsoOff(Dictionary<string, string?> prev)
    {
        var exe = prev.GetValueOrDefault("exe");
        if (exe is null) return new TweakResult(false, "GTA5.exe не найден", false);
        var cur = prev.GetValueOrDefault("v");
        var next = string.IsNullOrWhiteSpace(cur) ? "~ " + FsoFlag
            : cur.Contains(FsoFlag, StringComparison.OrdinalIgnoreCase) ? cur : cur + " " + FsoFlag;
        WriteRegString(Registry.CurrentUser, CompatLayers, exe, next);
        return new TweakResult(true, "Полноэкранные оптимизации для GTA5.exe выключены.", false);
    }

    private static TweakResult RevertFso(Dictionary<string, string?> prev)
    {
        var exe = prev.GetValueOrDefault("exe");
        if (exe is null) return new TweakResult(false, "GTA5.exe не найден", false);
        WriteRegString(Registry.CurrentUser, CompatLayers, exe, prev.GetValueOrDefault("v"));
        return new TweakResult(true, "Полноэкранные оптимизации возвращены как были.", false);
    }

    private static Dictionary<string, string?> CapturePowerThrottle()
    {
        var exe = FindGta5Exe() ?? throw new InvalidOperationException("GTA5.exe не найден");
        return new() { ["exe"] = exe };
    }

    private static TweakResult ApplyPowerThrottleOff(Dictionary<string, string?> prev)
    {
        var exe = prev.GetValueOrDefault("exe");
        if (exe is null) return new TweakResult(false, "GTA5.exe не найден", false);
        var (code, output) = RunTool("powercfg", $"/powerthrottling disable /path \"{exe}\"");
        return code == 0
            ? new TweakResult(true, "Экономичный троттлинг для GTA5.exe запрещён.", false)
            : new TweakResult(false, $"powercfg: {output}", false);
    }

    private static TweakResult RevertPowerThrottle(Dictionary<string, string?> prev)
    {
        var exe = prev.GetValueOrDefault("exe");
        if (exe is null) return new TweakResult(false, "GTA5.exe не найден", false);
        var (code, output) = RunTool("powercfg", $"/powerthrottling default /path \"{exe}\"");
        return code == 0
            ? new TweakResult(true, "Троттлинг для GTA5.exe возвращён к системному.", false)
            : new TweakResult(false, $"powercfg: {output}", false);
    }

    private static Dictionary<string, string?> CaptureWidgets() => new()
    { ["v"] = ReadRegAny(Registry.LocalMachine, DshPolicy, "AllowNewsAndInterests") };

    private static TweakResult ApplyWidgetsOff(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, DshPolicy, "AllowNewsAndInterests", 0);
        return new TweakResult(true, "Виджеты Windows выключены, их фоновый процесс уйдёт из памяти.", true);
    }

    private static TweakResult RevertWidgets(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.LocalMachine, DshPolicy, "AllowNewsAndInterests", ParseInt(prev.GetValueOrDefault("v")));
        return new TweakResult(true, "Виджеты Windows включены обратно.", true);
    }

    private static Dictionary<string, string?> CaptureBgApps() => new()
    { ["v"] = ReadRegAny(Registry.CurrentUser, BgAccess, "GlobalUserDisabled") };

    private static TweakResult ApplyBgAppsOff(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, BgAccess, "GlobalUserDisabled", 1);
        return new TweakResult(true, "Фоновая работа приложений Store запрещена.", false);
    }

    private static TweakResult RevertBgApps(Dictionary<string, string?> prev)
    {
        WriteRegDword(Registry.CurrentUser, BgAccess, "GlobalUserDisabled", ParseInt(prev.GetValueOrDefault("v")));
        return new TweakResult(true, "Фоновая работа приложений Store разрешена как раньше.", false);
    }

    private static string? FindCommandlinePath()
    {
        try
        {
            var gta = new MiamiGraphics.Core.System.HardwareLocator().FindGtaPath();
            if (string.IsNullOrWhiteSpace(gta)) return null;
            var p = Path.Combine(gta, "commandline.txt");
            return File.Exists(p) ? p : null;
        }
        catch { return null; }
    }

    private static Dictionary<string, string?> CaptureCommandline()
    {
        var p = FindCommandlinePath() ?? throw new InvalidOperationException("commandline.txt не найден");
        return new() { ["path"] = p, ["content"] = File.ReadAllText(p) };
    }

    private static TweakResult ApplyCommandlineClean(Dictionary<string, string?> prev)
    {
        var p = prev.GetValueOrDefault("path");
        var content = prev.GetValueOrDefault("content");
        if (p is null || content is null) return new TweakResult(false, "commandline.txt не прочитан", false);

        var lines = content.Split('\n')
            .Where(line => !PcDiagTweakCatalog.PlaceboFlags.Any(f =>
                line.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        File.WriteAllText(p, string.Join("\n", lines));
        return new TweakResult(true, "Плацебо-флаги убраны из commandline.txt (таких параметров у GTA V не существует). Прежнее содержимое в журнале.", false);
    }

    private static TweakResult RevertCommandline(Dictionary<string, string?> prev)
    {
        var p = prev.GetValueOrDefault("path");
        var content = prev.GetValueOrDefault("content");
        if (p is null || content is null) return new TweakResult(false, "в журнале нет прежнего содержимого", false);
        File.WriteAllText(p, content);
        return new TweakResult(true, "commandline.txt восстановлен из журнала.", false);
    }

    private static Dictionary<string, string?> CaptureNothing() => new();

    private static TweakResult NoRevert(Dictionary<string, string?> prev) =>
        new(false, "Очистка необратима: кэш пересоберётся сам при следующих запусках.", false);

    private static TweakResult ApplyShaderClean(Dictionary<string, string?> prev)
    {
        long freed = 0;
        foreach (var dir in PcDiagTweakCatalog.ShaderCacheDirs())
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    try { var len = f.Length; f.Delete(); freed += len; }
                    catch {}
                }
            }
            catch { }
        }
        return new TweakResult(true,
            $"Кэш шейдеров очищен: освобождено {freed / (1024 * 1024)} МБ. Первые запуски игр будут немного дольше, пока кэш пересобирается.", false);
    }

    private static TweakResult ApplyNvidiaProfile(Dictionary<string, string?> prev)
    {
        var (message, previous) = PcDiagNvidia.Apply();
        foreach (var (k, v) in previous) prev[k] = v;
        return new TweakResult(true, message, false);
    }

    private static TweakResult RevertNvidiaProfile(Dictionary<string, string?> prev)
        => new(true, PcDiagNvidia.Revert(prev), false);

    private static TweakResult ApplyTempClean(Dictionary<string, string?> prev)
    {
        long freed = 0;
        try
        {
            var tmp = Path.GetTempPath();
            var cutoff = DateTime.UtcNow.AddDays(-7);
            foreach (var f in new DirectoryInfo(tmp).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                try
                {
                    if (f.LastWriteTimeUtc >= cutoff) continue;
                    var len = f.Length; f.Delete(); freed += len;
                }
                catch {}
            }
        }
        catch { }
        return new TweakResult(true,
            $"Временные файлы старше недели удалены: освобождено {freed / (1024 * 1024)} МБ. Это место на диске, на FPS не влияет.", false);
    }
}

[SupportedOSPlatform("windows")]
internal static class PcDiagDisplay
{
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
    private static extern bool EnumDisplaySettingsW(string? deviceName, int modeNum, ref DEVMODE devMode);

    [global::System.Runtime.InteropServices.DllImport("user32.dll", CharSet = global::System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsW(ref DEVMODE devMode, uint flags);

    private const int EnumCurrentSettings = -1;
    private const uint CdsUpdateRegistry = 0x01;
    private const uint DmDisplayFrequency = 0x400000;

    public static (int CurrentHz, int MaxHz) Current()
    {
        var dm = new DEVMODE { dmSize = (ushort)global::System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettingsW(null, EnumCurrentSettings, ref dm))
            throw new InvalidOperationException("текущий режим монитора не читается");
        uint w = dm.dmPelsWidth, h = dm.dmPelsHeight, max = dm.dmDisplayFrequency;
        var probe = new DEVMODE { dmSize = dm.dmSize };
        for (int i = 0; EnumDisplaySettingsW(null, i, ref probe); i++)
            if (probe.dmPelsWidth == w && probe.dmPelsHeight == h && probe.dmDisplayFrequency > max)
                max = probe.dmDisplayFrequency;
        return ((int)dm.dmDisplayFrequency, (int)max);
    }

    public static bool SetHz(int hz)
    {
        var dm = new DEVMODE { dmSize = (ushort)global::System.Runtime.InteropServices.Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettingsW(null, EnumCurrentSettings, ref dm)) return false;
        dm.dmDisplayFrequency = (uint)hz;
        dm.dmFields = DmDisplayFrequency;
        return ChangeDisplaySettingsW(ref dm, CdsUpdateRegistry) == 0;
    }
}
