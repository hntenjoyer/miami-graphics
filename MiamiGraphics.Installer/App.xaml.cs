using System.Windows;

namespace MiamiGraphics.Installer;

public partial class App : Application
{
    public bool IsUninstallMode { get; private set; }
    public bool IsSilentMode    { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        foreach (var arg in e.Args)
        {
            switch (arg.ToLowerInvariant())
            {
                case "--uninstall": IsUninstallMode = true; break;
                case "--silent":
                case "-silent":
                case "/silent":
                case "/verysilent": IsSilentMode = true; break;
                case "/suppressmsgboxes":
                case "/norestart": break;
            }
        }
        base.OnStartup(e);
    }
}
