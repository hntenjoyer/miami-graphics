using System.Diagnostics;
using System.IO;

namespace MiamiGraphics.Shell.Services;

public static class OrphanBackupRecovery
{
    public static void Run()
    {
        try
        {
            var locator = new MiamiGraphics.Core.System.HardwareLocator();
            var gtaPath = locator.FindGtaPath();
            if (string.IsNullOrWhiteSpace(gtaPath) || !Directory.Exists(gtaPath))
            {
                Debug.WriteLine("[orphan-bak] skip: GTA path not found");
                return;
            }

            var probeDirs = new[]
            {
                Path.Combine(gtaPath, "update"),
                Path.Combine(gtaPath, "update", "x64", "dlcpacks", "miami_weapon", "dlc.rpf"),
                Path.Combine(gtaPath, "update", "x64", "dlcpacks"),
                Path.Combine(gtaPath, "x64", "audio", "sfx"),
                Path.Combine(gtaPath, "mods", "update"),
                Path.Combine(gtaPath, "mods", "update", "x64", "dlcpacks"),
                Path.Combine(gtaPath, "mods", "x64", "audio", "sfx"),
            };

            int restored = 0, removed = 0;
            foreach (var dir in probeDirs.Where(Directory.Exists))
            {
                try
                {
                    foreach (var bakPath in Directory.EnumerateFiles(dir, "*.bak", SearchOption.TopDirectoryOnly))
                    {
                        var origPath = bakPath.Substring(0, bakPath.Length - 4);
                        try
                        {
                            if (!File.Exists(origPath))
                            {

                                File.Move(bakPath, origPath);
                                restored++;
                                Debug.WriteLine($"[orphan-bak] RESTORED {origPath} from .bak");
                            }
                            else
                            {

                                File.Delete(bakPath);
                                removed++;
                                Debug.WriteLine($"[orphan-bak] removed residual {bakPath}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[orphan-bak] {bakPath}: {ex.Message}");
                        }
                    }

                    foreach (var tmpPath in Directory.EnumerateFiles(dir, "*.hnt_temp", SearchOption.TopDirectoryOnly))
                    {
                        try { File.Delete(tmpPath); removed++; Debug.WriteLine($"[orphan-bak] removed temp {tmpPath}"); }
                        catch (Exception ex) { Debug.WriteLine($"[orphan-bak] del temp {tmpPath}: {ex.Message}"); }
                    }
                }
                catch (Exception ex) { Debug.WriteLine($"[orphan-bak] scan {dir}: {ex.Message}"); }
            }

            if (restored > 0 || removed > 0)
                Debug.WriteLine($"[orphan-bak] done: restored={restored}, removed={removed}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[orphan-bak] OUTER: {ex.Message}");
        }
    }
}
