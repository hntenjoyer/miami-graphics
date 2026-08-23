using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MiamiGraphics.Installer.Services;

namespace MiamiGraphics.Installer;

public partial class MainWindow : Window
{
    private readonly InstallPipeline   _installPipeline   = new();
    private readonly UninstallPipeline _uninstallPipeline = new();

    public MainWindow()
    {
        InitializeComponent();

        _installPipeline.StepStarted     += OnStepStarted;
        _installPipeline.OverallProgress += OnOverall;
        _installPipeline.DetailUpdated   += OnDetail;
        _uninstallPipeline.StepStarted     += OnStepStarted;
        _uninstallPipeline.OverallProgress += OnOverall;
        _uninstallPipeline.DetailUpdated   += OnDetail;

        Loaded += async (_, _) =>
        {
            var app = (App)Application.Current;
            if (app.IsSilentMode && !app.IsUninstallMode)
            {
                await RunSilentInstallAsync();
                return;
            }
            if (app.IsUninstallMode)
            {
                MainPanel.Visibility      = Visibility.Collapsed;
                UninstallPanel.Visibility = Visibility.Visible;
            }
            else
            {
                try { var ru = await GeoCheck.IsBlockedRegionAsync(); SrcRu.IsChecked = ru; SrcEu.IsChecked = !ru; }
                catch { SrcEu.IsChecked = true; }
            }
        };
    }

    private void OnSourceChanged(object sender, RoutedEventArgs e)
    {
        _installPipeline.PreferRu = SrcRu?.IsChecked == true;
    }

    private async Task RunSilentInstallAsync()
    {
        Visibility    = Visibility.Hidden;
        ShowInTaskbar = false;
        try
        {
            await _installPipeline.RunAsync();
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[silent-install] FAIL: {ex}");
            Application.Current.Shutdown(1);
        }
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        InstallBtn.IsEnabled = false;
        MainPanel.Visibility     = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;

        try
        {
            await _installPipeline.RunAsync();
            ProgressPanel.Visibility = Visibility.Collapsed;
            DonePanel.Visibility     = Visibility.Visible;
            DoneBadge.Visibility     = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility    = Visibility.Visible;
            ErrorMessage.Text = ex.Message;
        }
    }

    private void OnRetryClick(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        MainPanel.Visibility  = Visibility.Visible;
        InstallBtn.IsEnabled  = true;
    }

    private void OnLaunchClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var exe = Path.Combine(_installPipeline.InstallRoot, "app", "Miami Graphics.exe");
            if (File.Exists(exe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{exe}\"",
                    UseShellExecute = true,
                });
            }
        }
        finally { Close(); }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var appDir = Path.Combine(_installPipeline.InstallRoot, "app");
            var target = Directory.Exists(appDir) ? appDir : _installPipeline.InstallRoot;
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex) { Debug.WriteLine($"[open folder] {ex.Message}"); }
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Выберите папку для установки Miami Graphics",
                InitialDirectory = Directory.Exists(_installPipeline.InstallRoot)
                    ? _installPipeline.InstallRoot
                    : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Multiselect = false,
            };
            if (dlg.ShowDialog(this) == true)
            {
                var picked = dlg.FolderName;
                if (!string.IsNullOrWhiteSpace(picked))
                {
                    var final = EnsureMiamiFolder(picked);
                    _installPipeline.InstallRoot = final;
                    Debug.WriteLine($"[settings] install root -> {final}  (picked '{picked}')");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[settings] {ex.Message}");
        }
    }

    private static string EnsureMiamiFolder(string path)
    {
        const string folder = "Miami Graphics";
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leaf = Path.GetFileName(trimmed);
        if (string.Equals(leaf, folder, StringComparison.OrdinalIgnoreCase))
            return path;
        return Path.Combine(path, folder);
    }

    private async void OnUninstallConfirm(object sender, RoutedEventArgs e)
    {
        _uninstallPipeline.WipeUserData = WipeUserDataCheckbox.IsChecked == true;
        UninstallPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility  = Visibility.Visible;

        try
        {
            await _uninstallPipeline.RunAsync();
            ProgressPanel.Visibility      = Visibility.Collapsed;
            UninstallDonePanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility    = Visibility.Visible;
            ErrorMessage.Text = ex.Message;
        }
    }

    private void OnStepStarted(string id, string label)
    {
        Dispatcher.Invoke(() =>
        {
            ProgressTitle.Text  = label.ToUpper();
            ProgressDetail.Text = "";
        });
    }

    private void OnOverall(double percent)
    {
        Dispatcher.Invoke(() =>
        {
            var maxW = ((FrameworkElement)ProgressBar.Parent).ActualWidth;
            ProgressBar.BeginAnimation(WidthProperty,
                new DoubleAnimation(Math.Max(0, Math.Min(maxW, maxW * percent / 100.0)),
                                    TimeSpan.FromMilliseconds(300)));
            ProgressPercent.Text = ((int)percent).ToString();
        });
    }

    private void OnDetail(string text)
    {
        Dispatcher.Invoke(() => ProgressDetail.Text = text);
    }
}
