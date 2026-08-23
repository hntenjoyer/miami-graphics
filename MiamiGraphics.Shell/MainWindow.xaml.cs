using MiamiGraphics.Shell.Bridge;
using MiamiGraphics.Shell.Repositories;
using MiamiGraphics.Shell.Repositories.Supabase;
using MiamiGraphics.Shell.Services;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;

namespace MiamiGraphics.Shell;

public partial class MainWindow : Window
{
    private const string WebView2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private const int WM_NCHITTEST = 0x0084;
    private const int HTCAPTION = 2;
    private const double NativeTitlebarHeight = 36;
    private const double NativeTitlebarLeftSafeWidth = 245;
    private const double NativeTitlebarRightSafeWidth = 152;
    private WebViewBridgeHost? _bridgeHost;
    private MiamiGraphics.Shell.Services.GunsmithApiInterceptor? _gunsmithApi;
    private System.Windows.Threading.DispatcherTimer? _splashFallback;

    private Services.AssetCache? _assetCache;
    private bool _splashHidden;
    private bool _webViewNavigated;

    private DateTime _splashShownAt = DateTime.UtcNow;

    private const int MinSplashDurationMs = 1800;
    private const int FadeOutMs           = 1100;

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {

        string? msg = null;
        try { msg = e.TryGetWebMessageAsString(); } catch {  }
        if (string.Equals(msg, "app-ready", StringComparison.Ordinal))
        {
            RequestHideSplashOverlay();
        }
    }

    private void RequestHideSplashOverlay()
    {
        if (_splashHidden) return;
        var elapsed = (DateTime.UtcNow - _splashShownAt).TotalMilliseconds;
        if (elapsed >= MinSplashDurationMs)
        {
            HideSplashOverlay();
            return;
        }
        var remaining = (int)(MinSplashDurationMs - elapsed);
        var holdTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(remaining),
        };
        holdTimer.Tick += (_, _) =>
        {
            holdTimer.Stop();
            HideSplashOverlay();
        };
        holdTimer.Start();
    }

    private void HideSplashOverlay()
    {
        if (_splashHidden) return;
        _splashHidden = true;
        _splashFallback?.Stop();

        if (SplashOverlay is null) return;

        var fade = new DoubleAnimation
        {
            From = SplashOverlay.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(FadeOutMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        fade.Completed += (_, _) =>
        {
            if (SplashOverlay is not null)
                SplashOverlay.Visibility = Visibility.Collapsed;
        };
        SplashOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    public MainWindow()
    {
        ConfigureWebViewUserDataFolder();
        InitializeComponent();
        PreviewKeyDown += BlockBrowserInspectionKeys;
    }

    private static void ConfigureWebViewUserDataFolder()
    {
        var webViewDataDir = GetWebViewUserDataFolder();
        Directory.CreateDirectory(webViewDataDir);
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", webViewDataDir);
    }

    private static string GetWebViewUserDataFolder() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiamiGraphics",
        "WebView2");

    private AppBridge? _appBridge;

    private bool _fullscreen;
    private WindowState _preFullscreenState = WindowState.Normal;

    public void SetFullscreen(bool on)
    {
        if (_fullscreen == on) return;
        if (on) _preFullscreenState = WindowState;
        _fullscreen = on;
        if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
        WindowState = on ? WindowState.Maximized : _preFullscreenState;
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        try { _appBridge?.NotifyWindowState(WindowState == WindowState.Maximized); }
        catch { }

        bool minimized = WindowState == WindowState.Minimized;
        SetWebViewMemoryLevel(minimized);
        if (minimized)
            MiamiGraphics.Core.System.MemoryRelease.Trim("окно свёрнуто");
    }

    private void SetWebViewMemoryLevel(bool low)
    {
        try
        {
            var core = WebView?.CoreWebView2;
            if (core == null) return;
            core.MemoryUsageTargetLevel = low
                ? Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Low
                : Microsoft.Web.WebView2.Core.CoreWebView2MemoryUsageTargetLevel.Normal;
        }
        catch (Exception ex) { Debug.WriteLine($"[wv2] memory level: {ex.Message}"); }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int pref = (int)DwmWindowCornerPreference.Round;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(WndProc);
        }
        catch {  }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            try
            {
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(monitor, ref mi))
                    {
                        var area = _fullscreen ? mi.rcMonitor : mi.rcWork;
                        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                        mmi.ptMaxPosition.X = area.Left - mi.rcMonitor.Left;
                        mmi.ptMaxPosition.Y = area.Top - mi.rcMonitor.Top;
                        mmi.ptMaxSize.X = area.Right - area.Left;
                        mmi.ptMaxSize.Y = area.Bottom - area.Top;
                        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
                        mmi.ptMinTrackSize.X = (int)(MinWidth * scale);
                        mmi.ptMinTrackSize.Y = (int)(MinHeight * scale);
                        Marshal.StructureToPtr(mmi, lParam, false);
                        handled = true;
                    }
                }
            }
            catch { }
            return IntPtr.Zero;
        }

        if (msg != WM_NCHITTEST)
            return IntPtr.Zero;

        var screenPoint = GetScreenPointFromLParam(lParam);
        var point = PointFromScreen(screenPoint);
        if (point.Y >= 0 &&
            point.Y < NativeTitlebarHeight &&
            point.X >= NativeTitlebarLeftSafeWidth &&
            point.X <= ActualWidth - NativeTitlebarRightSafeWidth)
        {
            handled = true;
            return new IntPtr(HTCAPTION);
        }

        return IntPtr.Zero;
    }

    private const int WM_GETMINMAXINFO = 0x0024;
    private const int MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTSTRUCT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINTSTRUCT ptReserved;
        public POINTSTRUCT ptMaxSize;
        public POINTSTRUCT ptMaxPosition;
        public POINTSTRUCT ptMinTrackSize;
        public POINTSTRUCT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECTSTRUCT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECTSTRUCT rcMonitor;
        public RECTSTRUCT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private static Point GetScreenPointFromLParam(IntPtr lParam)
    {
        var value = lParam.ToInt64();
        var x = unchecked((short)(value & 0xFFFF));
        var y = unchecked((short)((value >> 16) & 0xFFFF));
        return new Point(x, y);
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private enum DwmWindowCornerPreference { Default = 0, DoNotRound = 1, Round = 2, RoundSmall = 3 }

    private bool _forceExitAllowed = false;

    public void RequestForceExit()
    {
        _forceExitAllowed = true;
        Dispatcher.Invoke(Close);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceExitAllowed && MiamiGraphics.Shell.Services.CriticalOperationGuard.IsActive)
        {
            e.Cancel = true;

            try
            {

                const string eventJson =
                    "{\"kind\":\"event\",\"name\":\"app:criticalOpExitBlocked\",\"data\":null}";
                WebView?.CoreWebView2?.PostWebMessageAsJson(eventJson);
            }
            catch (Exception postEx)
            {
                Debug.WriteLine($"[OnClosing] postWebMessage failed, falling back to MessageBox: {postEx.Message}");

                MessageBox.Show(
                    "\u0421\u0435\u0439\u0447\u0430\u0441 \u0438\u0434\u0451\u0442 \u0437\u0430\u0433\u0440\u0443\u0437\u043a\u0430 \u0438\u043b\u0438 \u0437\u0430\u043f\u0438\u0441\u044c \u0444\u0430\u0439\u043b\u043e\u0432 GTA.\n\n" +
                    "\u0414\u043e\u0436\u0434\u0438\u0442\u0435\u0441\u044c \u0437\u0430\u0432\u0435\u0440\u0448\u0435\u043d\u0438\u044f \u043e\u043f\u0435\u0440\u0430\u0446\u0438\u0438 \u0438\u043b\u0438 \u043d\u0430\u0436\u043c\u0438\u0442\u0435 \u00ab\u041e\u0441\u0442\u0430\u043d\u043e\u0432\u0438\u0442\u044c\u00bb \u043d\u0430 \u044d\u043a\u0440\u0430\u043d\u0435 \u0431\u044d\u043a\u0430\u043f\u0430.",
                    "Miami Graphics",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            return;
        }

        base.OnClosing(e);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var wa = SystemParameters.WorkArea;
            bool shrunk = false;
            if (Width > wa.Width)  { Width  = Math.Max(MinWidth,  Math.Floor(wa.Width  * 0.97)); shrunk = true; }
            if (Height > wa.Height) { Height = Math.Max(MinHeight, Math.Floor(wa.Height * 0.95)); shrunk = true; }
            if (shrunk)
            {
                Left = wa.Left + Math.Max(0, (wa.Width - Width) / 2);
                Top  = wa.Top  + Math.Max(0, (wa.Height - Height) / 2);
            }
        }
        catch { }

        await MiamiGraphics.Shell.Services.AdditionalsBootstrapper.EnsureAsync();

        var keyLog = new System.Text.StringBuilder();
        try
        {
            var additionalsRoot = MiamiGraphics.Shell.Services.AdditionalsResolver.FindAdditionalsRoot();
            var keysDir = additionalsRoot is null ? null : Path.Combine(additionalsRoot, "keys");
            keyLog.AppendLine($"additionalsRoot={additionalsRoot ?? "<null>"}");
            keyLog.AppendLine($"keysDir={keysDir ?? "<null>"}");
            if (keysDir is not null)
                foreach (var kf in new[] { "gtav_aes_key.dat", "gtav_hash_lut.dat", "gtav_ng_key.dat", "gtav_ng_decrypt_tables.dat", "gtav_ng_encrypt_luts.dat" })
                {
                    var p = Path.Combine(keysDir, kf);
                    keyLog.AppendLine($"  {kf}: exists={File.Exists(p)} size={(File.Exists(p) ? new FileInfo(p).Length : -1)}");
                }

            bool TryLoad(string dir, string tag)
            {
                try
                {
                    RageLib.GTA5.Cryptography.GTA5Constants.LoadFromPath(dir);
                    var keys = RageLib.GTA5.Cryptography.GTA5Constants.PC_NG_KEYS;
                    var ok = keys is { Length: > 0 } && keys[0] is { Length: > 0 };
                    keyLog.AppendLine($"LoadFromPath[{tag}] {dir}: {(ok ? "OK - PC_NG_KEYS populated" : "RETURNED but PC_NG_KEYS still null/empty")}");
                    return ok;
                }
                catch (Exception ex)
                {
                    keyLog.AppendLine($"LoadFromPath[{tag}] {dir}: THREW {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
            }

            bool loaded = keysDir is not null
                          && File.Exists(Path.Combine(keysDir, "gtav_ng_key.dat"))
                          && TryLoad(keysDir, "installed");

            if (!loaded)
            {
                keyLog.AppendLine("installed keys did not load - forcing fresh additionals download…");
                var freshKeysDir = await MiamiGraphics.Shell.Services.AdditionalsBootstrapper.ForceRefreshKeysAsync();
                keyLog.AppendLine($"ForceRefreshKeysAsync → {freshKeysDir ?? "<null/failed>"}");
                if (freshKeysDir is not null)
                    loaded = TryLoad(freshKeysDir, "refreshed");
            }
            keyLog.AppendLine($"FINAL keysLoaded={loaded}");

            var eta = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiamiGraphics", "workdir", "backup", "dlc.rpf");
            keyLog.AppendLine($"эталон test-open: {MiamiGraphics.Shell.Services.TargetDlcEditor.TryDiagnosticOpen(eta)}");
        }
        catch (Exception kex)
        {
            keyLog.AppendLine($"[Keys] outer failure: {kex}");
        }
        finally
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MiamiGraphics", "keydiag.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.WriteAllText(logPath, $"[{DateTime.Now:u}]\n{keyLog}");
                Debug.WriteLine($"[Keys]\n{keyLog}");
            }
            catch { }
        }

        _assetCache = new MiamiGraphics.Shell.Services.AssetCache();
        _assetCache.WarmInMemoryIndex();

        MiamiGraphics.Core.System.MirrorSelector.ProbeHttpClientFactory =
            timeout => MiamiGraphics.Shell.Services.HttpClientFactory.CreateFragmenting(timeout);

        MiamiGraphics.Core.System.MirrorSelector.RuStorageProvider = () =>
            MiamiGraphics.Shell.Services.ServerRegionStore.EffectiveRegion() == MiamiGraphics.Shell.Services.ServerRegion.Ru
            || MiamiGraphics.Shell.Services.DownloadSourceStore.Effective() == MiamiGraphics.Shell.Services.DownloadSource.Ru2;

        MiamiGraphics.Core.System.MirrorSelector.LogSink =
            line => MiamiGraphics.Shell.Services.DownloadLog.Write("probe", line);

        MiamiGraphics.Core.System.MirrorSelector.Warmup();

        try
        {
            await InitializeWebView2Async();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebView2] initial startup failed: {ex}");
            if (await TryRepairWebView2RuntimeAsync(ex))
            {
                try
                {
                    await InitializeWebView2Async();
                    goto WebViewReady;
                }
                catch (Exception retryEx)
                {
                    Debug.WriteLine($"[WebView2] retry after repair failed: {retryEx}");
                    MessageBox.Show(
                        "Не удалось запустить Microsoft Edge WebView2 Runtime.\n\n" +
                        "Запустите Miami Installer от имени администратора или установите Microsoft Edge WebView2 Runtime вручную.\n\n" +
                        retryEx.Message,
                        "Miami Graphics",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Close();
                    return;
                }
            }

            MessageBox.Show(
                "Не удалось запустить Microsoft Edge WebView2 Runtime.\n\n" +
                "Переустановите приложение через Miami Installer.exe или установите WebView2 Runtime вручную.\n\n" +
                ex.Message,
                "Miami Graphics",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Close();
            return;
        }

WebViewReady:
        WebView.CoreWebView2.ProcessFailed += (_, pf) =>
        {
            Services.SessionLog.Error("wv2",
                $"ProcessFailed kind={pf.ProcessFailedKind} reason={pf.Reason} exit={pf.ExitCode}");
            if (pf.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited
                || pf.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                try { Dispatcher.Invoke(() => { try { WebView.CoreWebView2.Reload(); } catch { } }); }
                catch { }
            }
        };
        WebView.CoreWebView2.NavigationCompleted += (_, _) => _webViewNavigated = true;

        WebView.CoreWebView2.NewWindowRequested += (_, args) =>
        {
            args.Handled = true;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = args.Uri,
                    UseShellExecute = true,
                });
            }
            catch
            {

            }
        };

        var supabase = new MiamiGraphics.Shell.Services.SupabaseClient();

        var deviceId = MiamiGraphics.Shell.Services.DeviceIdService.GetOrCreate();
        IAppSettingsRepository settingsRepo = new SupabaseAppSettingsRepository(supabase, deviceId);
        IUserRepository userRepo = new SupabaseUserRepository(supabase);
        IReduxReviewsRepository reviewsRepo = new SupabaseReduxReviewsRepository(supabase);
        IUserBuildReviewsRepository buildReviewsRepo = new SupabaseUserBuildReviewsRepository(supabase);
        IInstallHistoryRepository installsRepo = new SupabaseInstallHistoryRepository(supabase);
        IHntCodesRepository hntCodesRepo = new SupabaseHntCodesRepository(supabase);

        var adminConfig = new MiamiGraphics.Shell.Services.AdminConfigService();
        var initialAdminCfg = await adminConfig.GetAsync();
        var cleanUpdateFolder = string.IsNullOrWhiteSpace(initialAdminCfg.CleanUpdateRpfPath)
            ? string.Empty
            : Path.GetDirectoryName(initialAdminCfg.CleanUpdateRpfPath) ?? string.Empty;

        var versions = new MiamiGraphics.Core.System.GtaVersionsRegistry();

        var localBackupSource = new MiamiGraphics.Core.System.LocalFolderBackupSource(cleanUpdateFolder);
        var backupSource = new MiamiGraphics.Shell.Services.SupabaseBackupSource(supabase, localBackupSource);
        var backupService = new MiamiGraphics.Core.System.BackupService(versions, backupSource);

        var remoteStorage = new MiamiGraphics.Shell.Services.R2DirectStorage(adminConfig);
        var catalog = new MiamiGraphics.Shell.Admin.SupabaseReduxCatalogRepository(supabase, adminConfig);
        var reduxVersionsRepo = new MiamiGraphics.Shell.Admin.SupabaseReduxVersionsRepository(supabase, adminConfig);
        var featuredRepo = new MiamiGraphics.Shell.Admin.SupabaseFeaturedPicksRepository(supabase, adminConfig);
        var gunpackRepo = new MiamiGraphics.Shell.Admin.SupabaseGunpackRepository(supabase);
        var gunpackWhitelistRepo = new MiamiGraphics.Shell.Admin.SupabaseGunpackWhitelistRepository(supabase);
        var gltfpack = new MiamiGraphics.Shell.Services.GltfpackRunner();
#if ADMIN
        MiamiGraphics.Shell.Admin.IAdminGunpackQueueService gunpackQueue =
            new MiamiGraphics.Shell.Admin.AdminGunpackQueueService(
                gunpackRepo, gunpackWhitelistRepo, remoteStorage, gltfpack);
#else
        MiamiGraphics.Shell.Admin.IAdminGunpackQueueService gunpackQueue = null!;
        _ = gltfpack;
#endif

        var packZipCache = new MiamiGraphics.Shell.Services.PackZipCache();
        var hardwareLocator = new MiamiGraphics.Core.System.HardwareLocator();
        Func<Task<string>> resolveGtaPath = async () =>
        {
            var cfg = await adminConfig.GetAsync();
            if (!string.IsNullOrWhiteSpace(cfg.GtaPathOverride) && System.IO.Directory.Exists(cfg.GtaPathOverride))
                return cfg.GtaPathOverride;
            return await Task.Run(() => hardwareLocator.FindGtaPath() ?? string.Empty);
        };
        var selectedGunsInstaller = new MiamiGraphics.Shell.Services.SelectedGunsInstaller(
            gunpackRepo, gunpackWhitelistRepo, adminConfig, packZipCache, resolveGtaPath, supabase);
        var gunpackInstaller = new MiamiGraphics.Shell.Services.GunpackInstaller(
            gunpackRepo, remoteStorage, adminConfig, supabase, selectedGunsInstaller);

#if ADMIN
        MiamiGraphics.Shell.Admin.IAdminQueueService queue =
            new MiamiGraphics.Shell.Admin.AdminQueueService(catalog, remoteStorage, reduxVersionsRepo);
#else
        MiamiGraphics.Shell.Admin.IAdminQueueService queue = null!;
#endif

        var gtaPresetsRepo = new MiamiGraphics.Shell.Admin.SupabaseGtaPresetsRepository(supabase, adminConfig);

        var userBuildsRepo = new MiamiGraphics.Shell.Admin.SupabaseUserBuildsRepository(supabase);

        var bridge = new AppBridge(settingsRepo, userRepo, backupService, adminConfig, remoteStorage, catalog, queue, supabase, reviewsRepo, installsRepo, reduxVersionsRepo, featuredRepo, gunpackRepo, gunpackWhitelistRepo, gunpackQueue, gunpackInstaller, selectedGunsInstaller, hntCodesRepo, gtaPresetsRepo, userBuildsRepo, buildReviewsRepo);
        bridge.AttachAssetCache(_assetCache);
        _appBridge = bridge;
        _bridgeHost = new WebViewBridgeHost(WebView.CoreWebView2, bridge);

        WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        try
        {
            var gunsmithRoot = Path.Combine(AppContext.BaseDirectory, "gunsmith");
            if (Directory.Exists(gunsmithRoot))
            {
                var customGunRepo = new MiamiGraphics.Shell.Admin.SupabaseCustomGunRepository(supabase);
                var gunsmith = new MiamiGraphics.Shell.Services.GunsmithService(
                    gunpackRepo, supabase, customGunRepo, remoteStorage, selectedGunsInstaller, packZipCache,
                    resolveGtaPath);
                _gunsmithApi = new MiamiGraphics.Shell.Services.GunsmithApiInterceptor(WebView.CoreWebView2, gunsmith, gunsmithRoot);
                _gunsmithApi.Register();
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[gunsmith] init failed: {ex.Message}"); }

        _splashShownAt = DateTime.UtcNow;
        _splashFallback = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2),
        };
        _splashFallback.Tick += (_, _) =>
        {
            var elapsedMs = (DateTime.UtcNow - _splashShownAt).TotalMilliseconds;
            if ((_webViewNavigated && elapsedMs >= 3000) || elapsedMs >= 25000)
            {
                _splashFallback?.Stop();
                RequestHideSplashOverlay();
            }
        };
        _splashFallback.Start();

        _ = Task.Run(async () =>
        {
            var status = await bridge.GetServerStatusAsync();
            Debug.WriteLine($"[Supabase] startup status: reachable={status.Reachable} provisioned={status.Provisioned} :: {status.Message}");
        });

        _ = Task.Run(async () =>
        {
            try
            {
                var applied = await remoteStorage.EnsureCorsAsync(System.Threading.CancellationToken.None);
                Debug.WriteLine(applied
                    ? "[r2.cors] startup apply ok"
                    : "[r2.cors] startup apply skipped (no R2 creds)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[r2.cors] startup apply failed: {ex.Message}");
            }
        });

        try
        {
            var bigmap3dDir = MiamiGraphics.Shell.Bridge.AppBridge.BigMap3dDir;
            Directory.CreateDirectory(bigmap3dDir);
            RegisterBigMap3dInterceptor(bigmap3dDir);
        }
        catch (Exception ex) { Debug.WriteLine($"[bigmap3d] interceptor init failed: {ex.Message}"); }

        var devFlag = Environment.GetEnvironmentVariable("HG_DEV");
        if (devFlag == "1")
        {
            Title = "Miami Graphics - DEV";
            WebView.CoreWebView2.Navigate("http://localhost:5173");
            return;
        }

        var indexPath = Path.Combine(AppContext.BaseDirectory, "ui", "index.html");
        if (File.Exists(indexPath))
        {
            var uiRoot = Path.GetDirectoryName(indexPath)!;
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.huntergraphics.local",
                uiRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);
            WebView.CoreWebView2.Navigate("https://app.huntergraphics.local/index.html");
        }
        else
        {
            WebView.CoreWebView2.NavigateToString(GetMissingUiHtml());
        }
    }

    private async Task InitializeWebView2Async()
    {
        var browserExecutableFolder = FindBundledWebView2RuntimeFolder();
        var availableVersion = GetAvailableWebView2Version(browserExecutableFolder);
        if (string.IsNullOrWhiteSpace(availableVersion))
            throw new InvalidOperationException("Microsoft Edge WebView2 Runtime is not installed.");

        Debug.WriteLine($"[WebView2] Runtime version: {availableVersion}; bundled={browserExecutableFolder ?? "<evergreen>"}");

        if (WebView.CoreWebView2 == null)
        {
            var webViewDataDir = GetWebViewUserDataFolder();
            Directory.CreateDirectory(webViewDataDir);
            var devArgs = Environment.GetEnvironmentVariable("HG_DEV") == "1"
                ? Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS") ?? ""
                : "";
            var webViewEnvironment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: browserExecutableFolder,
                userDataFolder: webViewDataDir,
                options: new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = devArgs });
            await WebView.EnsureCoreWebView2Async(webViewEnvironment);
        }
        HardenEmbeddedBrowser();

        try
        {
            await WebView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.DiskCache);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[wv2] ClearBrowsingData(DiskCache) failed: {ex.Message}");
        }

        new Services.WebView2BypassInterceptor(WebView.CoreWebView2, _assetCache).Register();
    }

    private static string? FindBundledWebView2RuntimeFolder()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "MiamiGraphics", "WebView2Runtime"),
            Path.Combine(AppContext.BaseDirectory, "WebView2Runtime"),
            Path.Combine(AppContext.BaseDirectory, "redist", "WebView2Runtime"),
            Path.Combine(AppContext.BaseDirectory, "..", "redist", "WebView2Runtime"),
        };

        foreach (var candidate in candidates)
        {
            string full;
            try { full = Path.GetFullPath(candidate); }
            catch { continue; }

            if (!Directory.Exists(full)) continue;
            if (File.Exists(Path.Combine(full, "msedgewebview2.exe")))
                return full;

            var nested = Directory.EnumerateDirectories(full, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(dir => File.Exists(Path.Combine(dir, "msedgewebview2.exe")));
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private static string? GetAvailableWebView2Version(string? browserExecutableFolder = null)
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString(browserExecutableFolder);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebView2] version probe failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<bool> TryRepairWebView2RuntimeAsync(Exception startupError)
    {
        if (!LooksLikeWebView2RuntimeProblem(startupError))
            return false;

        var installerPath = await ResolveWebView2BootstrapperAsync();
        if (installerPath is not null)
        {
            try
            {
                Debug.WriteLine($"[WebView2] running bootstrapper: {installerPath}");
                var psi = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/silent /install",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(psi);
                if (process is not null)
                {
                    await process.WaitForExitAsync();
                    Debug.WriteLine($"[WebView2] bootstrapper exit code: {process.ExitCode}");

                    await Task.Delay(1200);
                    if (!string.IsNullOrWhiteSpace(GetAvailableWebView2Version(FindBundledWebView2RuntimeFolder())))
                        return true;

                    Debug.WriteLine("[WebView2] bootstrapper completed but no runtime found - trying mirror CAB fallback");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebView2] bootstrapper failed: {ex.Message} - trying mirror CAB fallback");
            }
        }

        return await TryInstallRuntimeFromMirrorZipAsync();
    }

    private static async Task<bool> TryInstallRuntimeFromMirrorZipAsync()
    {
        try
        {
            const string ZipRelPath = "webview2/WebView2Runtime.x64.zip";

            var hosts = new[]
            {
                "cdn.miamigraphicsstorage.uk",
                "miamigraphicsstorage.uk",
                "pub-f3641b214c164277964c1e92c826b19b.r2.dev",
                "ru.miamigraphicsstorage.uk",
            };

            var tmpDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics");
            Directory.CreateDirectory(tmpDir);
            var zipPath = Path.Combine(tmpDir, "WebView2Runtime.x64.zip");
            var tmpPart = zipPath + ".part";

            bool downloaded = false;
            foreach (var h in hosts)
            {
                var url = $"https://{h}/{ZipRelPath}";
                try
                {
                    Debug.WriteLine($"[WebView2.zip] downloading from {url}");
                    using var http = HttpClientFactory.CreateFragmenting(TimeSpan.FromMinutes(30));
                    using var headerCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(90));
                    using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, headerCts.Token);
                    resp.EnsureSuccessStatusCode();
                    await using (var src = await resp.Content.ReadAsStreamAsync())
                    await using (var dst = new FileStream(tmpPart, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
                        await src.CopyToAsync(dst);
                    if (File.Exists(zipPath)) { try { File.Delete(zipPath); } catch { } }
                    File.Move(tmpPart, zipPath);
                    downloaded = true;
                    Debug.WriteLine($"[WebView2.zip] downloaded {new FileInfo(zipPath).Length / (1024 * 1024)} МБ from {h}");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[WebView2.zip] host {h} failed: {ex.GetType().Name}: {ex.Message}");
                    try { if (File.Exists(tmpPart)) File.Delete(tmpPart); } catch { }
                }
            }
            if (!downloaded) return false;

            var runtimeRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiamiGraphics", "WebView2Runtime");
            if (Directory.Exists(runtimeRoot)) { try { Directory.Delete(runtimeRoot, recursive: true); } catch { } }
            Directory.CreateDirectory(runtimeRoot);

            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, runtimeRoot, overwriteFiles: true);
            try { File.Delete(zipPath); } catch { }
            Debug.WriteLine($"[WebView2.zip] extracted to {runtimeRoot}");

            await Task.Delay(300);
            return !string.IsNullOrWhiteSpace(GetAvailableWebView2Version(FindBundledWebView2RuntimeFolder()));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebView2.zip] mirror fallback failed: {ex.Message}");
            return false;
        }
    }

    private static bool LooksLikeWebView2RuntimeProblem(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is WebView2RuntimeNotFoundException)
                return true;

            var msg = current.Message;
            if (msg.Contains("compatible WebView2 Runtime", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("WebView2 Runtime", StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("msedgewebview2", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return string.IsNullOrWhiteSpace(GetAvailableWebView2Version(FindBundledWebView2RuntimeFolder()));
    }

    private static async Task<string?> ResolveWebView2BootstrapperAsync()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "redist", "MicrosoftEdgeWebView2Setup.exe"),
            Path.Combine(AppContext.BaseDirectory, "MicrosoftEdgeWebView2Setup.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "redist", "MicrosoftEdgeWebView2Setup.exe"),
        };

        foreach (var candidate in candidates)
        {
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath))
                return fullPath;
        }

        try
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "MiamiGraphics", "MicrosoftEdgeWebView2Setup.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
            using var http = HttpClientFactory.CreateFragmenting(TimeSpan.FromSeconds(45));
            await using var src = await http.GetStreamAsync(WebView2BootstrapperUrl);
            await using var dst = File.Create(tempPath);
            await src.CopyToAsync(dst);
            return tempPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebView2] bootstrapper download failed: {ex.Message}");
            return null;
        }
    }

    private void RegisterBigMap3dInterceptor(string bigmap3dDir)
    {
        const string host = "bigmap3d.huntergraphics.local";
        var root = Path.GetFullPath(bigmap3dDir);
        WebView.CoreWebView2.AddWebResourceRequestedFilter(
            $"https://{host}/*", CoreWebView2WebResourceContext.All);
        WebView.CoreWebView2.WebResourceRequested += (_, args) =>
        {
            Uri uri;
            try { uri = new Uri(args.Request.Uri); } catch { return; }
            if (!string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)) return;

            var env = WebView.CoreWebView2.Environment;
            const string corsHeader = "Access-Control-Allow-Origin: *";
            try
            {
                var rel = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
                var full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                {
                    args.Response = env.CreateWebResourceResponse(
                        null, 404, "Not Found", corsHeader);
                    return;
                }
                var bytes = File.ReadAllBytes(full);
                args.Response = env.CreateWebResourceResponse(
                    new MemoryStream(bytes, writable: false), 200, "OK",
                    "Content-Type: model/gltf-binary\r\n" +
                    "Cache-Control: max-age=86400\r\n" +
                    corsHeader);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[bigmap3d] serve failed {uri.AbsolutePath}: {ex.Message}");
                try
                {
                    args.Response = env.CreateWebResourceResponse(
                        null, 500, "Internal Server Error", corsHeader);
                }
                catch {}
            }
        };
        Debug.WriteLine("[bigmap3d] interceptor registered");
    }

    private void HardenEmbeddedBrowser()
    {
        if (WebView.CoreWebView2 is null) return;

        var isDev = Environment.GetEnvironmentVariable("HG_DEV") == "1";
        var settings = WebView.CoreWebView2.Settings;
        settings.AreDevToolsEnabled = isDev;
        settings.AreDefaultContextMenusEnabled = isDev;
        settings.AreBrowserAcceleratorKeysEnabled = isDev;

        WebView.CoreWebView2.ContextMenuRequested += (_, args) =>
        {
            args.Handled = true;
        };
    }

    private static void BlockBrowserInspectionKeys(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (key == Key.F12 ||
            (ctrl && shift && (key == Key.I || key == Key.J || key == Key.C)))
        {
            e.Handled = true;
        }
    }
    private static string GetMissingUiHtml() => """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Miami Graphics - UI not built</title>
<style>
html, body { margin:0; padding:0; height:100%; background:#000; color:#fafafa; font-family: 'Segoe UI', system-ui, sans-serif; }
.wrap { display:flex; align-items:center; justify-content:center; height:100%; }
.card { max-width:560px; padding:32px; border:1px solid rgba(255,255,255,0.12); border-radius:16px; }
h1 { margin:0 0 12px; font-size:20px; font-weight:600; letter-spacing:0.04em; }
p { margin:0 0 8px; color:#a8a8a8; line-height:1.5; }
code { background:rgba(255,255,255,0.06); padding:2px 6px; border-radius:4px; }
</style>
</head>
<body>
<div class="wrap"><div class="card">
<h1>UI not built</h1>
<p>Run <code>npm run build</code> in the <code>ui/</code> folder, or set environment variable <code>HG_DEV=1</code> and start <code>npm run dev</code>.</p>
</div></div>
</body>
</html>
""";
}
