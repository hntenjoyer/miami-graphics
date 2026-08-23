using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.Services;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Shell.Services;

public static class HunterGunsSelectedRpfBuilder
{
    public const string OutputRpfName = "hunter_guns_selected.rpf";

    public sealed record BuildResult(
        bool   Success,
        string Message,
        long   FinalSize,
        string Sha256,
        int    GunFilesAdded,
        int    OverlayMagsAdded,
        int    SkippedBroken = 0);

    public static BuildResult Build(
        IReadOnlyDictionary<string, byte[]> gunFilesByName,
        string overlayMagsDir,
        string templatePath)
    {
        var (result, _) = BuildToBytes(gunFilesByName, overlayMagsDir, templatePath);
        return result;
    }

    public static (BuildResult result, byte[] bytes) BuildToBytes(
        IReadOnlyDictionary<string, byte[]> gunFilesByName,
        string overlayMagsDir,
        string templatePath)
    {
        Debug.WriteLine($"[guns-selected-builder] START: {gunFilesByName.Count} input files, template={templatePath}, overlaymags={overlayMagsDir}");
        if (!File.Exists(templatePath))
        {
            Debug.WriteLine($"[guns-selected-builder] FAIL: template missing at {templatePath}");
            return (new BuildResult(false, Loc.T("error.templateNotFound", ("file", Path.GetFileName(templatePath))), 0, string.Empty, 0, 0), Array.Empty<byte>());
        }
        if (!Directory.Exists(overlayMagsDir))
        {
            Debug.WriteLine($"[guns-selected-builder] FAIL: overlaymags missing at {overlayMagsDir}");
            return (new BuildResult(false, Loc.T("error.overlaymagsDirNotFound"), 0, string.Empty, 0, 0), Array.Empty<byte>());
        }

        int skippedBroken = 0;
        var merged = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, bytes) in gunFilesByName)
        {
            if (string.IsNullOrEmpty(name) || bytes is null || bytes.Length == 0) continue;
            if (!RpfEntryGate.TryPrepare(name, bytes, out var ready, out var bad, out var note))
            {
                Debug.WriteLine($"[guns-selected-builder] ОТСЕЯНО '{name}' ({bytes.Length:N0} б): {bad}");
                skippedBroken++;
                continue;
            }
            if (note != null) Debug.WriteLine($"[guns-selected-builder] '{name}' {note}");
            merged[name] = ready;
        }
        int gunsAdded = merged.Count;

        int magsAdded = 0;
        foreach (var path in Directory.EnumerateFiles(overlayMagsDir, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(path);
            try
            {
                var bytes = File.ReadAllBytes(path);
                if (!RpfEntryGate.TryPrepare(name, bytes, out var ready, out var bad, out var note))
                {
                    Debug.WriteLine($"[guns-selected-builder] ОТСЕЯН оверлей-магазин '{name}' ({bytes.Length:N0} б): {bad}");
                    skippedBroken++;
                    continue;
                }
                if (note != null) Debug.WriteLine($"[guns-selected-builder] магазин '{name}' {note}");
                merged[name] = ready;
                magsAdded++;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[guns-selected-builder] mag read FAIL {name}: {ex.Message}");
            }
        }
        Debug.WriteLine($"[guns-selected-builder] merged: {merged.Count} files (guns={gunsAdded} pre-mag, mags={magsAdded})");

        if (merged.Count == 0)
            return (new BuildResult(false, Loc.T("error.noFilesForRpfBuild"), 0, string.Empty, 0, 0), Array.Empty<byte>());

        var tempPath = Path.Combine(Path.GetTempPath(),
            "MiamiGraphics_GunsSelected_" + Guid.NewGuid().ToString("N").Substring(0, 12) + ".rpf");
        try
        {
            File.Copy(templatePath, tempPath, overwrite: true);
            Debug.WriteLine($"[guns-selected-builder] template copied to {tempPath} ({new FileInfo(tempPath).Length} bytes)");
            using (var archive = RageArchiveWrapper7.Open(tempPath))
            {
                var root = archive.Root;
                foreach (var (fileName, bytes) in merged)
                {
                    AddFile(root, fileName, bytes);
                }
                archive.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                archive.Flush();
            }

            ArchiveFixer.FixOrThrow(tempPath, TargetDlcEditor.MIAMI_GUNS_SELECTED_RPF_NAME);

            var finalBytes = File.ReadAllBytes(tempPath);
            string sha;
            using (var sha256 = SHA256.Create())
                sha = Convert.ToHexString(sha256.ComputeHash(finalBytes)).ToLowerInvariant();

            Debug.WriteLine($"[guns-selected-builder] OK: {finalBytes.Length:N0} bytes, sha={sha[..8]}, files={merged.Count}, отсеяно={skippedBroken}");
            var ok = new BuildResult(
                Success:          true,
                Message:          Loc.T("install.selectedRpfBuilt", ("files", merged.Count), ("guns", gunsAdded), ("mags", magsAdded)),
                FinalSize:        finalBytes.LongLength,
                Sha256:           sha,
                GunFilesAdded:    gunsAdded,
                OverlayMagsAdded: magsAdded,
                SkippedBroken:    skippedBroken);
            return (ok, finalBytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[guns-selected-builder] CRASH: {ex}");
            return (new BuildResult(false, Loc.T("error.buildCrashed", ("reason", ex.Message)), 0, string.Empty, gunsAdded, magsAdded), Array.Empty<byte>());
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static void AddFile(IArchiveDirectory dir, string fileName, byte[] data)
    {
        if (IsRsc7Resource(data))
        {
            var resFile = dir.CreateResourceFile();
            resFile.Name = fileName;
            using var ms = new MemoryStream(data);
            resFile.Import(ms);
        }
        else
        {
            var binFile = dir.CreateBinaryFile();
            binFile.Name = fileName;
            binFile.IsEncrypted = false;
            binFile.IsCompressed = false;
            binFile.UncompressedSize = data.LongLength;
            using var ms = new MemoryStream(data);
            binFile.Import(ms);
        }
    }

    private static bool IsRsc7Resource(byte[] data) => RpfEntrySanity.IsRsc7(data);
}
