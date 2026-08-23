using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace MiamiGraphics.Installer.Services;

public sealed class UninstallPipeline
{
    private const string AppName = "Miami Graphics";

    public string InstallRoot { get; }
    public bool   WipeUserData { get; set; }

    public event Action<string, string>? StepStarted;
    public event Action<double>?         OverallProgress;
    public event Action<string>?         DetailUpdated;

    public UninstallPipeline()
    {
        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        InstallRoot = Path.Combine(lad, AppName);
    }

    public async Task RunAsync()
    {
        await Step("stop",  "Остановка",        weight: 10, RunStopProcess);
        await Step("rm",    "Удаление",         weight: 75, RunRemoveFiles);
        await Step("clean", "Финальный штрих",  weight: 15, RunCleanup);
    }

    private double _done;
    private async Task Step(string id, string label, int weight, Func<Action<double>, Task> body)
    {
        StepStarted?.Invoke(id, label);
        var prev = _done;
        await body(p => OverallProgress?.Invoke(prev + (weight * p / 100.0)));
        _done += weight;
        OverallProgress?.Invoke(_done);
    }

    private async Task RunStopProcess(Action<double> progress)
    {
        progress(20);
        var procs = Process.GetProcessesByName("Miami Graphics")
            .Concat(Process.GetProcessesByName("MiamiGraphics.Shell"))
            .ToArray();
        foreach (var p in procs)
        {
            try
            {
                DetailUpdated?.Invoke("Закрываю Miami Graphics");
                p.CloseMainWindow();
            }
            catch {}
        }
        if (procs.Length > 0) await Task.Delay(2500);
        foreach (var p in procs)
        {
            try
            {
                if (!p.HasExited) p.Kill(entireProcessTree: true);
                p.Dispose();
            }
            catch {}
        }
        progress(100);
    }

    private async Task RunRemoveFiles(Action<double> progress)
    {
        DetailUpdated?.Invoke("Удаление файлов");
        var myExe = Process.GetCurrentProcess().MainModule?.FileName;
        await Task.Run(() =>
        {
            if (Directory.Exists(InstallRoot))
            {
                var files = Directory.EnumerateFileSystemEntries(InstallRoot, "*", SearchOption.TopDirectoryOnly).ToList();
                for (int i = 0; i < files.Count; i++)
                {
                    var path = files[i];
                    if (myExe is not null && string.Equals(path, myExe, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                        else                         File.Delete(path);
                    }
                    catch (Exception ex) { Debug.WriteLine($"[uninstall] rm {path}: {ex.Message}"); }
                    progress((i + 1) * 90.0 / Math.Max(1, files.Count));
                }
            }
        });

        if (WipeUserData)
        {
            DetailUpdated?.Invoke("Удаление пользовательских данных");
            var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var ad  = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var tmp = Path.GetTempPath();
            foreach (var name in new[] { "MiamiGraphics", "Miami Graphics", "MiamiGraphics" })
            {
                foreach (var root in new[] { lad, ad, tmp })
                {
                    var p = Path.Combine(root, name);
                    if (Directory.Exists(p) && !string.Equals(p, InstallRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        try { Directory.Delete(p, recursive: true); }
                        catch (Exception ex) { Debug.WriteLine($"[uninstall] wipe {p}: {ex.Message}"); }
                    }
                }
            }
        }
        progress(100);
    }

    private Task RunCleanup(Action<double> progress)
    {
        return Task.Run(() =>
        {
            DetailUpdated?.Invoke("Чистка ярлыков и реестра");
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                var lnks = new[]
                {
                    Path.Combine(desktop, "Miami Graphics.lnk"),
                    Path.Combine(startMenu, "Programs", "Miami Graphics.lnk"),
                };
                foreach (var p in lnks)
                    try { if (File.Exists(p)) File.Delete(p); } catch {}
            }
            catch (Exception ex) { Debug.WriteLine($"[uninstall] shortcuts: {ex.Message}"); }
            progress(50);

            try
            {
                using var hkcu = Registry.CurrentUser;
                hkcu.DeleteSubKeyTree(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MiamiGraphics",
                    throwOnMissingSubKey: false);
            }
            catch (Exception ex) { Debug.WriteLine($"[uninstall] reg: {ex.Message}"); }
            progress(100);

            ScheduleSelfDelete();
        });
    }

    private void ScheduleSelfDelete()
    {
        try
        {
            var myExe = Process.GetCurrentProcess().MainModule?.FileName;
            if (myExe is null) return;
            var cmd = $"/c timeout /t 1 /nobreak >nul & del /f /q \"{myExe}\" & rd /s /q \"{InstallRoot}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmd,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Exception ex) { Debug.WriteLine($"[uninstall] self-delete: {ex.Message}"); }
    }
}
