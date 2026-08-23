using System.IO;
using Microsoft.Win32;

namespace MiamiGraphics.Installer.Services;

public sealed record SystemSummary(
    bool HasOldInstall,
    string? OldInstallPath,
    string HumanText);

public static class SystemDetector
{
    public static SystemSummary Detect()
    {
        var hasOld = false;
        string? oldPath = null;
        var details = new List<string>();

        foreach (var pf in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            foreach (var name in new[] { "Miami Graphics", "MiamiGraphics" })
            {
                var p = Path.Combine(pf, name);
                if (Directory.Exists(p))
                {
                    hasOld = true;
                    oldPath ??= p;
                    details.Add($"Найдена старая установка: {p}");
                }
            }
        }

        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var name in new[] { "MiamiGraphics", "Miami Graphics", "MiamiGraphics" })
        {
            var p = Path.Combine(lad, name);
            if (Directory.Exists(p))
            {
                hasOld = true;
                long sizeMb = 0;
                try { sizeMb = DirectorySize(p) / 1024 / 1024; } catch { }
                details.Add($"Найден кэш: {p}" + (sizeMb > 0 ? $" (~{sizeMb} МБ)" : ""));
            }
        }

        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B5C65F97-79B5-41F2-8F3C-9891146D0632}_is1");
            if (key is not null)
            {
                hasOld = true;
                details.Add("Найден реестровый ключ Inno-установки (Add/Remove Programs).");
            }
        }
        catch {}

        var gta = FindGta();
        if (gta is not null) details.Add($"Найдена GTA V: {gta}");
        else                 details.Add("GTA V не найдена - её можно указать позже в настройках лаунчера.");

        var text = hasOld
            ? "Будет снесено: " + string.Join("; ", details)
            : "Чистая система - будем устанавливать с нуля.\n" + string.Join("\n", details);

        return new SystemSummary(hasOld, oldPath, text);
    }

    private static string? FindGta()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"Software\WOW6432Node\Rockstar Games\Grand Theft Auto V");
            if (key?.GetValue("InstallFolder") is string s && Directory.Exists(s)) return s;
        }
        catch {}
        return null;
    }

    private static long DirectorySize(string path)
    {
        long total = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return total;
    }
}
