using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Media.Animation;

namespace MiamiGraphics.Shell;

public partial class App : Application
{
    private const string ElevatedReentryFlag = "--elevated-reentry";

    private System.Threading.Mutex? _singleInstanceMutex;

    static App()
    {
        Timeline.DesiredFrameRateProperty.OverrideMetadata(
            typeof(Timeline),
            new FrameworkPropertyMetadata { DefaultValue = 120 });
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (Array.Exists(e.Args, a => string.Equals(a, "--hotswap-agent", StringComparison.OrdinalIgnoreCase)))
        {
            MiamiGraphics.Core.HotSwap.HotSwapLog.Origin = "агент";
            using var agentMutex = new System.Threading.Mutex(true, @"Global\MiamiGraphicsAgent_SingleInstance", out bool agentNew);
            if (!agentNew)
            {
                MiamiGraphics.Core.HotSwap.HotSwapLog.Write("агент",
                    $"второй экземпляр агента (pid {Environment.ProcessId}) - выходим, работает первый");
                Shutdown(0);
                return;
            }
            try { MiamiGraphics.Core.HotSwap.HotSwapAgentLoop.Run(); }
            catch (Exception agentEx)
            {
                MiamiGraphics.Core.HotSwap.HotSwapLog.Write("агент", "цикл умер", agentEx);
            }
            MiamiGraphics.Core.HotSwap.HotSwapLog.Write("агент", "процесс агента завершается");
            Shutdown(0);
            return;
        }

        try
        {
            MiamiGraphics.Shell.Services.SessionLog.SessionStart(e.Args);
            HookGlobalExceptionLogging();
        }
        catch (Exception logEx) { Debug.WriteLine($"[startup] session log init failed: {logEx.Message}"); }

        try
        {
            var consoleLogPath = System.IO.Path.Combine(
                MiamiGraphics.Shell.Services.SessionLog.LogDir, "console.log");
            try
            {
                var fi = new System.IO.FileInfo(consoleLogPath);
                if (fi.Exists && fi.Length > 8L * 1024 * 1024)
                    System.IO.File.Move(consoleLogPath, consoleLogPath + ".prev", overwrite: true);
            }
            catch {}
            var consoleWriter = new System.IO.StreamWriter(
                new System.IO.FileStream(consoleLogPath, System.IO.FileMode.Append,
                    System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite))
            { AutoFlush = true };
            var synced = System.IO.TextWriter.Synchronized(consoleWriter);
            Console.SetOut(synced);
            Console.SetError(synced);
            synced.WriteLine($"===== console session start {DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={Environment.ProcessId} =====");
        }
        catch (Exception conEx) { Debug.WriteLine($"[startup] console mirror failed: {conEx.Message}"); }

        try { Environment.SetEnvironmentVariable("MIAMI_RENDERER_DIR", MiamiGraphics.Shell.Services.RendererBootstrapper.RendererDirPath); }
        catch (Exception rndEx) { System.Diagnostics.Debug.WriteLine($"[startup] renderer dir pin failed: {rndEx.Message}"); }

        try { MiamiGraphics.Shell.Services.MiamiPathMigration.EnsureMiamiLayout(); }
        catch (Exception migEx) { System.Diagnostics.Debug.WriteLine($"[startup] miami migration failed: {migEx.Message}"); }

        try { MiamiGraphics.Shell.Services.OrphanBackupRecovery.Run(); }
        catch (Exception bakEx) { System.Diagnostics.Debug.WriteLine($"[startup] orphan-bak recovery failed: {bakEx.Message}"); }

        try { MiamiGraphics.Shell.Services.MajesticCrashWatch.ScanInBackground(); }
        catch (Exception cwEx) { System.Diagnostics.Debug.WriteLine($"[startup] crash watch failed: {cwEx.Message}"); }

        try
        {
            MiamiGraphics.Core.System.DataQuota.Logger =
                msg => MiamiGraphics.Shell.Services.SessionLog.Info("quota", msg);
            MiamiGraphics.Core.System.MemoryRelease.Logger =
                msg => MiamiGraphics.Shell.Services.SessionLog.Info("memory", msg);
        }
        catch (Exception qlEx) { System.Diagnostics.Debug.WriteLine($"[startup] quota logger wire failed: {qlEx.Message}"); }

        try { MiamiGraphics.Core.System.DataQuota.SweepInBackground("старт лаунчера"); }
        catch (Exception qEx) { System.Diagnostics.Debug.WriteLine($"[startup] quota sweep failed: {qEx.Message}"); }

        try
        {
            var hsMode = MiamiGraphics.Core.HotSwap.HotSwapModeStore.Read();
            if (hsMode.Enabled && !string.IsNullOrWhiteSpace(hsMode.GtaRoot)
                && System.IO.Directory.Exists(hsMode.GtaRoot))
            {
                MiamiGraphics.Core.HotSwap.HotSwapRecovery.EnsureConsistent(hsMode.GtaRoot!, out var hsMsg);
                System.Diagnostics.Debug.WriteLine($"[startup] hotswap recovery: {hsMsg}");
            }
        }
        catch (Exception hsEx) { System.Diagnostics.Debug.WriteLine($"[startup] hotswap recovery failed: {hsEx.Message}"); }

        try
        {
            _singleInstanceMutex = new System.Threading.Mutex(initiallyOwned: false, @"Local\MiamiGraphicsLauncher_SingleInstance");
            bool owned;
            var wait = HasReentryFlag(e.Args) ? TimeSpan.FromSeconds(10) : TimeSpan.Zero;
            try { owned = _singleInstanceMutex.WaitOne(wait, exitContext: false); }
            catch (System.Threading.AbandonedMutexException) { owned = true; }
            if (!owned)
            {
                MiamiGraphics.Shell.Services.SessionLog.Info("startup",
                    "duplicate instance detected - activating the existing window and exiting");
                TryActivateExistingInstance();
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown(0);
                return;
            }
        }
        catch (Exception muEx)
        {
            Debug.WriteLine($"[startup] single-instance gate failed: {muEx.Message}");
        }

        try
        {
            if (!IsRunningElevated() && !HasReentryFlag(e.Args) && GtaPathRequiresElevation())
            {
                if (TryRelaunchElevated())
                {
                    Shutdown(0);
                    return;
                }

                Debug.WriteLine("[startup] elevation declined - proceeding without admin");
            }
        }
        catch (Exception ex)
        {

            Debug.WriteLine($"[startup] elevation pre-flight failed: {ex.Message}");
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); } catch {}
        try { _singleInstanceMutex?.Dispose(); } catch { }
        _singleInstanceMutex = null;

        try { MiamiGraphics.Shell.Services.SessionLog.SessionEnd($"exit code {e.ApplicationExitCode}"); }
        catch {}
        base.OnExit(e);
    }

    private static void TryActivateExistingInstance()
    {
        try
        {
            var me = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(me.ProcessName))
            {
                try
                {
                    if (p.Id == me.Id) continue;
                    var hwnd = p.MainWindowHandle;
                    if (hwnd == IntPtr.Zero) continue;
                    const int SW_RESTORE = 9;
                    if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, SW_RESTORE);
                    NativeMethods.SetForegroundWindow(hwnd);
                    return;
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[startup] activate-existing failed: {ex.Message}"); }
    }

    private static class NativeMethods
    {
        [global::System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [global::System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [global::System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: global::System.Runtime.InteropServices.MarshalAs(global::System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);
    }

    private void HookGlobalExceptionLogging()
    {
        DispatcherUnhandledException += (_, args) =>
            MiamiGraphics.Shell.Services.SessionLog.Error("crash", "DispatcherUnhandledException", args.Exception);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            MiamiGraphics.Shell.Services.SessionLog.Error("crash",
                $"AppDomain.UnhandledException (terminating={args.IsTerminating})",
                args.ExceptionObject as Exception);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            MiamiGraphics.Shell.Services.SessionLog.Error("crash", "UnobservedTaskException", args.Exception);
            args.SetObserved();
        };
    }

    private static bool IsRunningElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {

            return false;
        }
    }

    private static bool HasReentryFlag(string[] args)
        => args.Any(a => string.Equals(a, ElevatedReentryFlag, StringComparison.Ordinal));

    private static bool GtaPathRequiresElevation()
    {
        string? gtaPath;
        try
        {
            var locator = new MiamiGraphics.Core.System.HardwareLocator();
            gtaPath = locator.FindGtaPath();
        }
        catch
        {
            return false;
        }
        if (string.IsNullOrWhiteSpace(gtaPath)) return false;

        var pf64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf32 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        return PathStartsWith(gtaPath, pf64) || PathStartsWith(gtaPath, pf32);
    }

    private static bool PathStartsWith(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryRelaunchElevated()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                return false;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Verb = "runas",
                UseShellExecute = true,
                Arguments = ElevatedReentryFlag,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
            };
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {

            Debug.WriteLine($"[startup] runas relaunch failed: {ex.Message}");
            return false;
        }
    }
}
