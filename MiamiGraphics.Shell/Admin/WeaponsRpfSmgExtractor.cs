using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Shell.Admin;

public static class WeaponsRpfSmgExtractor
{
    public sealed record SmgArtifact(
        string  BaseName,
        string  WeaponPrefix,
        string  InternalName,
        List<string> FileNames,
        string  PerGunZipPath,
        string  BaseYdrPath
    );

    public static List<SmgArtifact> Extract(
        string weaponsRpfPath,
        IEnumerable<GunpackWhitelistEntry> whitelist,
        string outputDir,
        string? logicalName = null)
    {
        var results = new List<SmgArtifact>();

        var smgEntries = whitelist.Where(w => w.IsSmgOverride).ToList();
        if (smgEntries.Count == 0) return results;

        if (!File.Exists(weaponsRpfPath))
        {
            Debug.WriteLine($"[smg-extract] weapons.rpf missing: {weaponsRpfPath}");
            return results;
        }

        Directory.CreateDirectory(outputDir);

        var allFiles = new List<(string FullName, byte[] Bytes)>();
        try
        {
            using var fs = new FileStream(weaponsRpfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = RageArchiveWrapper7.Open(
                fs, logicalName ?? Path.GetFileName(weaponsRpfPath), true);
            CollectAllFiles(archive.Root, "", allFiles);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[smg-extract] failed to open weapons.rpf: {ex.Message}");
            return results;
        }

        Debug.WriteLine($"[smg-extract] weapons.rpf has {allFiles.Count} files; checking {smgEntries.Count} SMG whitelist entries");

        foreach (var entry in smgEntries)
        {

            var internalName = entry.InternalName.ToLowerInvariant();
            var prefix = entry.WeaponPrefix.ToLowerInvariant();
            if (!internalName.StartsWith(prefix, StringComparison.Ordinal))
            {
                Debug.WriteLine($"[smg-extract] whitelist row '{entry.InternalName}' doesn't start with prefix '{entry.WeaponPrefix}' - skip");
                continue;
            }
            var baseName = internalName.Substring(prefix.Length);

            var dotPrefix = internalName + ".";
            var underPrefix = internalName + "_";
            var matched = new List<(string FullName, byte[] Bytes)>();
            foreach (var f in allFiles)
            {
                var fileLower = Path.GetFileName(f.FullName).ToLowerInvariant();
                if (fileLower.StartsWith(dotPrefix, StringComparison.Ordinal)
                 || fileLower.StartsWith(underPrefix, StringComparison.Ordinal))
                {
                    matched.Add(f);
                }
            }

            var baseFileLower = internalName + ".ydr";
            var hiFileLower   = internalName + "_hi.ydr";
            var baseEntry = matched.FirstOrDefault(m =>
                Path.GetFileName(m.FullName).Equals(baseFileLower, StringComparison.OrdinalIgnoreCase));
            if (baseEntry.Bytes is null || baseEntry.Bytes.Length == 0)
            {
                baseEntry = matched.FirstOrDefault(m =>
                    Path.GetFileName(m.FullName).Equals(hiFileLower, StringComparison.OrdinalIgnoreCase));
                if (baseEntry.Bytes is null || baseEntry.Bytes.Length == 0)
                {
                    Debug.WriteLine($"[smg-extract] '{entry.InternalName}': neither base .ydr nor _hi.ydr found in weapons.rpf - skipping");
                    continue;
                }
                Debug.WriteLine($"[smg-extract] '{entry.InternalName}': base .ydr missing, using _hi.ydr fallback");
            }

            var zipPath = Path.Combine(outputDir, $"{baseName}.zip");
            try
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                using var zipStream = File.Create(zipPath);
                using var zip = new ZipArchive(zipStream, ZipArchiveMode.Create);
                foreach (var (fullName, bytes) in matched.OrderBy(x => x.FullName, StringComparer.OrdinalIgnoreCase))
                {
                    var name = Path.GetFileName(fullName);
                    var zipEntry = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using var s = zipEntry.Open();
                    s.Write(bytes, 0, bytes.Length);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[smg-extract] '{entry.InternalName}': zip write failed: {ex.Message} - skipping");
                continue;
            }

            var ydrPath = Path.Combine(outputDir, $"{baseName}.ydr");
            try { File.WriteAllBytes(ydrPath, baseEntry.Bytes); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[smg-extract] '{entry.InternalName}': ydr write failed: {ex.Message} - skipping");
                continue;
            }

            results.Add(new SmgArtifact(
                BaseName:       baseName,
                WeaponPrefix:   entry.WeaponPrefix,
                InternalName:   entry.InternalName,
                FileNames:      matched.Select(m => Path.GetFileName(m.FullName))
                                       .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                       .ToList(),
                PerGunZipPath:  zipPath,
                BaseYdrPath:    ydrPath));

            Debug.WriteLine($"[smg-extract] +{entry.InternalName} ({matched.Count} files)");
        }

        return results;
    }

    private static void CollectAllFiles(IArchiveDirectory dir, string currentPath, List<(string, byte[])> acc)
    {
        foreach (var f in dir.GetFiles())
        {
            var fullName = string.IsNullOrEmpty(currentPath) ? f.Name : currentPath + "/" + f.Name;
            try
            {
                using var ms = new MemoryStream();
                f.Export(ms);
                acc.Add((fullName, ms.ToArray()));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[smg-extract] couldn't read {fullName}: {ex.Message}");
            }
        }
        foreach (var sub in dir.GetDirectories())
        {
            var subPath = string.IsNullOrEmpty(currentPath) ? sub.Name : currentPath + "/" + sub.Name;
            CollectAllFiles(sub, subPath, acc);
        }
    }
}
