using System.Diagnostics;
using System.IO;

namespace MiamiGraphics.Shell.Services;

internal static class MiamiPathMigration
{
    private const string LegacyName = "MiamiGraphics";
    private const string ModernName = "MiamiGraphics";

    public static void EnsureMiamiLayout()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var tempDir = Path.GetTempPath();

        TryRenameOne(localAppData);
        TryRenameOne(tempDir);
    }

    private static void TryRenameOne(string parent)
    {
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent)) return;

        var legacy = Path.Combine(parent, LegacyName);
        var modern = Path.Combine(parent, ModernName);

        try
        {
            bool legacyExists = Directory.Exists(legacy);
            bool legacyIsJunction = legacyExists && IsReparsePoint(legacy);
            bool modernExists = Directory.Exists(modern);

            if (legacyIsJunction)
            {
                try
                {
                    Directory.Delete(legacy, recursive: false);
                    Debug.WriteLine($"[miami.migrate] removed legacy junction {legacy}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[miami.migrate] failed to remove junction {legacy}: {ex.Message}");
                }
                return;
            }

            if (legacyExists && !modernExists)
            {
                Directory.Move(legacy, modern);
                Debug.WriteLine($"[miami.migrate] renamed {legacy} → {modern}");
                return;
            }

            if (legacyExists && modernExists)
            {
                Debug.WriteLine(
                    $"[miami.migrate] CONFLICT: both {legacy} and {modern} exist as real dirs - " +
                    "leaving as-is, app will use modern. Manual merge required if legacy has user data.");
                return;
            }

        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[miami.migrate] failed for {parent}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch { return false; }
    }
}
