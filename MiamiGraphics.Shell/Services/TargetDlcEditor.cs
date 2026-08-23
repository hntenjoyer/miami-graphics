using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.Services;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Shell.Services;

public static class TargetDlcEditor
{
    private static readonly string[] WeaponsRpfParentParts = { "x64", "levels", "gta5" };
    private const string ContentXmlDevicePrefix         = "dlc_PATCHDAY18NG";
    private const string ContentXmlChangeSet            = "CCS_PATCHDAY18_NG_STREAMING";
    private const string ContentXmlFilesToEnableDevice  = "dlc_PATCHDAY18ng";

    public const string MIAMI_WEAPON_RPF_NAME        = "miami_weapon.rpf";

    public const string MIAMI_GUNS_SELECTED_RPF_NAME = "miami_guns_selected.rpf";

    public const string MIAMI_BILLBOARDS_RPF_NAME = "miami_billboards.rpf";

    public static bool RpfExistsInsideTarget(string targetDlcPath, string rpfName)
        => TryRpfExistsInsideTarget(targetDlcPath, rpfName) == true;

    public static bool? TryRpfExistsInsideTarget(string targetDlcPath, string rpfName)
    {
        if (!File.Exists(targetDlcPath)) return false;
        try
        {
            using var archive = RageArchiveWrapper7.Open(targetDlcPath);
            var dir = NavigateTo(archive.Root, WeaponsRpfParentParts);
            if (dir is null) return false;
            return dir.GetFiles().Any(f => f.Name.Equals(rpfName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[dlc-editor] TryRpfExistsInsideTarget INDETERMINATE ({rpfName}): {ex.Message}");
            return null;
        }
    }

    public static byte[]? ExtractRpfBytesFromTarget(string targetDlcPath, string rpfName)
    {
        if (!File.Exists(targetDlcPath)) return null;
        try
        {
            using var archive = RageArchiveWrapper7.Open(targetDlcPath);
            var dir = NavigateTo(archive.Root, WeaponsRpfParentParts);
            if (dir is null) return null;
            var file = dir.GetFiles()
                .OfType<IArchiveBinaryFile>()
                .FirstOrDefault(f => f.Name.Equals(rpfName, StringComparison.OrdinalIgnoreCase));
            if (file is null) return null;
            using var ms = new MemoryStream();
            file.Export(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[dlc-editor] ExtractRpfBytes FAIL ({rpfName}): {ex.Message}");
            return null;
        }
    }

    public static string? ComputeEmbeddedRpfSha256(string targetDlcPath, string rpfName)
    {
        var bytes = ExtractRpfBytesFromTarget(targetDlcPath, rpfName);
        if (bytes is null) return null;
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
    }

    public static void InjectRpfIntoTarget(string targetDlcPath, string rpfName, byte[] bytes, string? logicalDlcName = null)
    {
        if (!File.Exists(targetDlcPath))
            throw new FileNotFoundException("Target DLC RPF not found.", targetDlcPath);
        if (bytes is null || bytes.Length == 0)
            throw new ArgumentException("bytes is null/empty", nameof(bytes));

        var sizeBefore = new FileInfo(targetDlcPath).Length;
        Debug.WriteLine($"[dlc-editor.inject] {rpfName} START: targetDlc={targetDlcPath} size={sizeBefore:N0} bytes; injecting {bytes.Length:N0} bytes; logicalName={logicalDlcName ?? "<actual>"}");

        using (var archive = OpenWithLogicalName(targetDlcPath, logicalDlcName))
        {
            var dir = NavigateTo(archive.Root, WeaponsRpfParentParts);
            if (dir is null)
                throw new InvalidOperationException(
                    $"Target DLC missing path /{string.Join("/", WeaponsRpfParentParts)} - DLC structure unexpected.");

            var existing = dir.GetFiles()
                .FirstOrDefault(f => f.Name.Equals(rpfName, StringComparison.OrdinalIgnoreCase));
            Debug.WriteLine($"[dlc-editor.inject] {rpfName}: existing={(existing is null ? "<none>" : existing.GetType().Name)}; dir has {dir.GetFiles().Length} files");

            if (existing is IArchiveBinaryFile existingBin)
            {
                using var ms = new MemoryStream(bytes);
                existingBin.Import(ms);
                Debug.WriteLine($"[dlc-editor.inject] {rpfName}: replaced existing ({bytes.Length:N0} bytes)");
            }
            else
            {
                if (existing is not null)
                    throw new InvalidOperationException(
                        $"Target DLC has '{rpfName}' as a non-binary entry - RageLib " +
                        "can't replace it in place. Rebuild from template via " +
                        "RebuildFromTemplate() instead.");

                var fresh = dir.CreateBinaryFile();
                fresh.Name = rpfName;
                fresh.IsEncrypted = false;
                fresh.IsCompressed = false;
                fresh.UncompressedSize = bytes.LongLength;
                using var ms = new MemoryStream(bytes);
                fresh.Import(ms);
                Debug.WriteLine($"[dlc-editor.inject] {rpfName}: created fresh ({bytes.Length:N0} bytes)");
            }
            archive.Flush();
        }

        var sizeAfter = new FileInfo(targetDlcPath).Length;
        Debug.WriteLine($"[dlc-editor.inject] {rpfName} DONE: targetDlc went {sizeBefore:N0} → {sizeAfter:N0} bytes (Δ {sizeAfter - sizeBefore:+#,##0;-#,##0;0})");
    }

    private static void FixTargetDlcArchive(string dlcPath, string? logicalDlcName)
    {
        using (var archive = OpenWithLogicalName(dlcPath, logicalDlcName))
        {
            archive.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
            archive.Flush();
        }
        ArchiveFixer.FixOrThrow(dlcPath);
    }

    public static void RebuildFromTemplate(
        string templatePath,
        string targetDlcPath,
        byte[]? hunterWeaponBytes,
        byte[]? hunterGunsSelectedBytes)
        => RebuildFromTemplate(templatePath, targetDlcPath, new[]
        {
            new DlcChild(MIAMI_WEAPON_RPF_NAME,        hunterWeaponBytes),
            new DlcChild(MIAMI_GUNS_SELECTED_RPF_NAME, hunterGunsSelectedBytes),
        });

    public sealed record DlcChild(string RpfName, byte[]? Bytes);

    public static void RebuildFromTemplate(
        string templatePath,
        string targetDlcPath,
        IReadOnlyList<DlcChild> children)
    {
        if (!File.Exists(templatePath))
            throw new FileNotFoundException(Loc.T("error.cleanDlcTemplateNotFound"), templatePath);

        var targetDir = Path.GetDirectoryName(targetDlcPath);
        if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

        var tempPath = targetDlcPath + ".building";
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }

        var payload = (children ?? Array.Empty<DlcChild>())
            .Where(c => c is { Bytes.Length: > 0 } && !string.IsNullOrWhiteSpace(c.RpfName))
            .ToList();
        Debug.WriteLine($"[dlc-editor] REBUILD start: target={targetDlcPath} children=" +
                        string.Join(", ", payload.Select(c => $"{c.RpfName}={c.Bytes!.Length:N0}b")));

        try
        {
            File.Copy(templatePath, tempPath, overwrite: true);
            var sizeAfterCopy = new FileInfo(tempPath).Length;
            Debug.WriteLine($"[dlc-editor] REBUILD step 1 - template copied: {tempPath} = {sizeAfterCopy:N0} bytes (template at {templatePath} = {new FileInfo(templatePath).Length:N0} bytes)");

            var canonicalName = Path.GetFileName(targetDlcPath);

            foreach (var child in payload)
            {
                InjectRpfIntoTarget(tempPath, child.RpfName, child.Bytes!, canonicalName);
                Debug.WriteLine($"[dlc-editor] REBUILD inject {child.RpfName} ({child.Bytes!.Length:N0} b): temp = {new FileInfo(tempPath).Length:N0} bytes");
                EnsureContentXmlEntry(tempPath, child.RpfName, canonicalName);
                Debug.WriteLine($"[dlc-editor] REBUILD content.xml {child.RpfName}: temp = {new FileInfo(tempPath).Length:N0} bytes");
            }

            var finalTempSize = new FileInfo(tempPath).Length;

            string? rollbackPath = null;
            if (File.Exists(targetDlcPath))
            {
                rollbackPath = targetDlcPath + ".rollback";
                try
                {
                    if (File.Exists(rollbackPath)) File.Delete(rollbackPath);
                    File.Move(targetDlcPath, rollbackPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[dlc-editor] REBUILD rollback snapshot failed ({ex.Message}) - proceeding without net");
                    rollbackPath = null;
                }
            }

            File.Move(tempPath, targetDlcPath, overwrite: true);

            try
            {
                FixTargetDlcArchive(targetDlcPath, canonicalName);
            }
            catch
            {
                if (rollbackPath is not null && File.Exists(rollbackPath))
                {
                    try
                    {
                        File.Move(rollbackPath, targetDlcPath, overwrite: true);
                        Debug.WriteLine("[dlc-editor] REBUILD fix FAILED - previous dlc.rpf restored from .rollback");
                    }
                    catch (Exception rex)
                    {
                        Debug.WriteLine($"[dlc-editor] REBUILD rollback restore FAILED: {rex.Message}");
                    }
                }
                throw;
            }
            finally
            {
                try { if (rollbackPath is not null && File.Exists(rollbackPath)) File.Delete(rollbackPath); } catch { }
            }

            var finalTargetSize = new FileInfo(targetDlcPath).Length;
            Debug.WriteLine($"[dlc-editor] REBUILD DONE - temp ({finalTempSize:N0} bytes) → target {targetDlcPath} ({finalTargetSize:N0} bytes)");
        }
        catch
        {

            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    public static bool DeleteTargetDlc(string targetDlcPath)
    {
        if (!File.Exists(targetDlcPath))
        {
            Debug.WriteLine($"[dlc-editor] DELETE: {targetDlcPath} not present, no-op");
            return false;
        }
        try
        {
            File.Delete(targetDlcPath);
            Debug.WriteLine($"[dlc-editor] DELETE: removed {targetDlcPath}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[dlc-editor] DELETE FAIL {targetDlcPath}: {ex.Message}");
            throw;
        }
    }

    public static bool EnsureContentXmlEntry(string targetDlcPath, string rpfName, string? logicalDlcName = null)
    {
        if (!File.Exists(targetDlcPath)) return false;
        try
        {
            using var archive = OpenWithLogicalName(targetDlcPath, logicalDlcName);
            var contentFile = archive.Root.GetFiles()
                .FirstOrDefault(f => f.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase))
                as IArchiveBinaryFile;
            if (contentFile is null)
            {
                Debug.WriteLine("[dlc-editor] content.xml not found in target DLC root");
                return false;
            }

            bool wasCompressed = contentFile.IsCompressed;
            byte[] xmlBytes = ReadRealBytes(contentFile);
            if (xmlBytes is null || xmlBytes.Length == 0) return false;

            string xmlText = DecodeXmlText(xmlBytes);
            if (string.IsNullOrWhiteSpace(xmlText)) return false;

            XDocument doc;
            try { doc = XDocument.Parse(xmlText); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[dlc-editor] content.xml parse FAIL: {ex.Message}");
                return false;
            }

            string filenameValue       = $"{ContentXmlDevicePrefix}:/%PLATFORM%/levels/gta5/{rpfName}";
            string filesToEnableValue  = $"{ContentXmlFilesToEnableDevice}:/%PLATFORM%/levels/gta5/{rpfName}";

            bool modified = false;

            var dataFilesRoot = doc.Descendants("dataFiles").FirstOrDefault();
            if (dataFilesRoot is null)
            {
                Debug.WriteLine("[dlc-editor] content.xml missing <dataFiles>");
                return false;
            }
            bool alreadyInDataFiles = dataFilesRoot.Elements("Item").Any(item =>
            {
                var fn = item.Element("filename")?.Value;
                return fn != null && fn.Equals(filenameValue, StringComparison.OrdinalIgnoreCase);
            });
            if (!alreadyInDataFiles)
            {
                dataFilesRoot.Add(new XElement("Item",
                    new XElement("filename", filenameValue),
                    new XElement("fileType", "RPF_FILE"),
                    new XElement("locked",     new XAttribute("value", "true")),
                    new XElement("disabled",   new XAttribute("value", "true")),
                    new XElement("persistent", new XAttribute("value", "true")),
                    new XElement("overlay",    new XAttribute("value", "true"))));
                modified = true;
            }

            var changeSet = doc.Descendants("Item").FirstOrDefault(item =>
            {
                var name = item.Element("changeSetName")?.Value;
                return name != null && name.Equals(ContentXmlChangeSet, StringComparison.OrdinalIgnoreCase);
            });
            if (changeSet is null)
            {
                Debug.WriteLine($"[dlc-editor] content.xml missing changeSet {ContentXmlChangeSet}");
                return false;
            }
            var filesToEnable = changeSet.Element("filesToEnable") ?? new XElement("filesToEnable");
            if (filesToEnable.Parent is null) changeSet.Add(filesToEnable);

            bool alreadyInFilesToEnable = filesToEnable.Elements("Item")
                .Any(it => string.Equals(it.Value, filesToEnableValue, StringComparison.OrdinalIgnoreCase));
            if (!alreadyInFilesToEnable)
            {
                filesToEnable.Add(new XElement("Item", filesToEnableValue));
                modified = true;
            }

            if (!modified) return false;

            string newXml;
            using (var sw = new StringWriter()) { doc.Save(sw, SaveOptions.None); newXml = sw.ToString(); }
            byte[] newRawBytes = Encoding.UTF8.GetBytes(newXml);

            contentFile.IsEncrypted = false;
            byte[] toImport;
            if (wasCompressed)
            {
                using var msDef = new MemoryStream();
                using (var def = new DeflateStream(msDef, CompressionMode.Compress, true))
                    def.Write(newRawBytes, 0, newRawBytes.Length);
                toImport = msDef.ToArray();
                contentFile.IsCompressed = true;
                contentFile.UncompressedSize = newRawBytes.LongLength;
            }
            else
            {
                toImport = newRawBytes;
                contentFile.IsCompressed = false;
                contentFile.UncompressedSize = newRawBytes.LongLength;
            }
            using (var ms = new MemoryStream(toImport)) contentFile.Import(ms);
            archive.Flush();
            Debug.WriteLine($"[dlc-editor] content.xml updated for {rpfName}");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[dlc-editor] EnsureContentXmlEntry CRASH ({rpfName}): {ex}");
            return false;
        }
    }

    public sealed record AnimLooseFile(
        string RelPath,
        byte[]? Bytes,
        string ContentXmlFileType,
        string? Contents = null);

    public static void InstallAnimLooseFiles(
        string targetDlcPath, IReadOnlyList<AnimLooseFile> files, string? logicalDlcName = null)
    {
        if (!File.Exists(targetDlcPath))
            throw new FileNotFoundException("Target DLC RPF not found.", targetDlcPath);
        if (files is null || files.Count == 0) return;

        using (var archive = OpenWithLogicalName(targetDlcPath, logicalDlcName))
        {
            foreach (var f in files)
            {
                if (f?.Bytes is not { Length: > 0 } || string.IsNullOrWhiteSpace(f.RelPath)) continue;
                PlaceLooseFile(archive.Root, f.RelPath, f.Bytes);
            }

            var contentFile = archive.Root.GetFiles()
                .FirstOrDefault(x => x.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase))
                as IArchiveBinaryFile
                ?? throw new InvalidOperationException(Loc.T("error.contentXmlNotFoundInDlc"));

            bool wasCompressed = contentFile.IsCompressed;
            string xmlText = DecodeXmlText(ReadRealBytes(contentFile));
            var doc = XDocument.Parse(xmlText);

            var dataFiles = doc.Descendants("dataFiles").FirstOrDefault()
                ?? throw new InvalidOperationException(Loc.T("error.contentXmlNoDataFiles"));
            var changeSet = doc.Descendants("Item").FirstOrDefault(it =>
                string.Equals(it.Element("changeSetName")?.Value, ContentXmlChangeSet, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(Loc.T("error.contentXmlNoChangeSet", ("changeSet", ContentXmlChangeSet)));
            var filesToEnable = changeSet.Element("filesToEnable");
            if (filesToEnable is null) { filesToEnable = new XElement("filesToEnable"); changeSet.Add(filesToEnable); }

            foreach (var f in files)
            {
                if (f is null || string.IsNullOrWhiteSpace(f.RelPath)) continue;
                string rel = f.RelPath.Replace('\\', '/').TrimStart('/');
                string dfName = $"{ContentXmlDevicePrefix}:/{rel}";
                string feName = $"{ContentXmlFilesToEnableDevice}:/{rel}";

                bool inData = dataFiles.Elements("Item").Any(it =>
                    string.Equals(it.Element("filename")?.Value, dfName, StringComparison.OrdinalIgnoreCase));
                if (!inData)
                {
                    var item = new XElement("Item",
                        new XElement("filename", dfName),
                        new XElement("fileType", f.ContentXmlFileType),
                        new XElement("locked",     new XAttribute("value", "true")),
                        new XElement("disabled",   new XAttribute("value", "true")),
                        new XElement("persistent", new XAttribute("value", "true")),
                        new XElement("overlay",    new XAttribute("value", "true")));
                    if (!string.IsNullOrEmpty(f.Contents)) item.Add(new XElement("contents", f.Contents));
                    dataFiles.Add(item);
                }
                bool inEnable = filesToEnable.Elements("Item").Any(it =>
                    string.Equals(it.Value, feName, StringComparison.OrdinalIgnoreCase));
                if (!inEnable) filesToEnable.Add(new XElement("Item", feName));
            }

            string newXml;
            using (var sw = new StringWriter()) { doc.Save(sw, SaveOptions.None); newXml = sw.ToString(); }
            byte[] raw = Encoding.UTF8.GetBytes(newXml);
            contentFile.IsEncrypted = false;
            byte[] toImport;
            if (wasCompressed)
            {
                using var msDef = new MemoryStream();
                using (var def = new DeflateStream(msDef, CompressionMode.Compress, true)) def.Write(raw, 0, raw.Length);
                toImport = msDef.ToArray();
                contentFile.IsCompressed = true; contentFile.UncompressedSize = raw.LongLength;
            }
            else { toImport = raw; contentFile.IsCompressed = false; contentFile.UncompressedSize = raw.LongLength; }
            using (var ms = new MemoryStream(toImport)) contentFile.Import(ms);

            archive.Flush();
        }

        FixTargetDlcArchive(targetDlcPath, logicalDlcName);
        Debug.WriteLine($"[dlc-editor] InstallAnimLooseFiles DONE: {files.Count} loose files into {targetDlcPath}");
    }

    public static void PatchUpdateRpfDlcPatch(string updateRpfPath, string dlcName, string relPath, byte[] bytes)
    {
        if (!File.Exists(updateRpfPath))
            throw new FileNotFoundException(Loc.T("error.updateRpfNotFound"), updateRpfPath);
        if (bytes is not { Length: > 0 })
            throw new ArgumentException("bytes пустые", nameof(bytes));

        var backup = updateRpfPath + ".mg_backup";
        if (!File.Exists(backup))
        {
            Debug.WriteLine($"[dlc-editor] update.rpf backup → {backup}");
            File.Copy(updateRpfPath, backup, overwrite: false);
        }

        Debug.WriteLine($"[dlc-editor] PatchUpdateRpf: dlc_patch/{dlcName}/{relPath} ({bytes.Length:N0} bytes) → {updateRpfPath}");
        using (var archive = RageArchiveWrapper7.Open(updateRpfPath))
        {
            PlaceLooseFile(archive.Root, $"dlc_patch/{dlcName}/{relPath}", bytes);
            archive.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
            archive.Flush();
        }
        ArchiveFixer.FixOrThrow(updateRpfPath);
        Debug.WriteLine($"[dlc-editor] PatchUpdateRpf DONE ({new FileInfo(updateRpfPath).Length:N0} bytes)");
    }

    private static void PlaceLooseFile(IArchiveDirectory root, string relPath, byte[] bytes)
    {
        var parts = relPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        var dir = root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var name = parts[i];
            var next = dir.GetDirectories().FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (next is null) { next = dir.CreateDirectory(); next.Name = name; }
            dir = next;
        }
        var fileName = parts[^1];

        if (!RpfEntryGate.TryPrepare(fileName, bytes, out var ready, out var bad, out var note))
        {
            Debug.WriteLine($"[target-dlc] ОТСЕЯНО '{relPath}' ({bytes.Length:N0} б): {bad}");
            return;
        }
        if (note != null) Debug.WriteLine($"[target-dlc] '{relPath}' {note}");
        bytes = ready;

        bool isRsc = RpfEntrySanity.IsRsc7(bytes);
        var existing = dir.GetFiles().FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (existing is IArchiveBinaryFile eb && !isRsc)
        {
            eb.IsEncrypted = false; eb.IsCompressed = false; eb.UncompressedSize = bytes.LongLength;
            using var ms = new MemoryStream(bytes); eb.Import(ms);
            return;
        }
        if (isRsc)
        {
            var rf = dir.CreateResourceFile(); rf.Name = fileName;
            using var ms = new MemoryStream(bytes); rf.Import(ms);
        }
        else
        {
            var bf = dir.CreateBinaryFile(); bf.Name = fileName;
            bf.IsEncrypted = false; bf.IsCompressed = false; bf.UncompressedSize = bytes.LongLength;
            using var ms = new MemoryStream(bytes); bf.Import(ms);
        }
    }

    private static RageArchiveWrapper7 OpenWithLogicalName(string path, string? logicalName)
    {
        if (string.IsNullOrEmpty(logicalName))
            return RageArchiveWrapper7.Open(path);

        var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        try
        {
            return RageArchiveWrapper7.Open(fs, logicalName, leaveOpen: false);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    public static string TryDiagnosticOpen(string path)
    {
        try
        {
            if (!File.Exists(path)) return $"missing ({path})";
            var size = new FileInfo(path).Length;
            using var archive = OpenWithLogicalName(path, "dlc.rpf");
            var rootFiles = archive.Root?.GetFiles()?.Length ?? -1;
            return $"OK size={size:N0} rootFiles={rootFiles}";
        }
        catch (Exception ex)
        {
            return $"FAIL {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static IArchiveDirectory? NavigateTo(IArchiveDirectory root, string[] parts)
    {
        IArchiveDirectory? cur = root;
        foreach (var p in parts)
        {
            cur = cur?.GetDirectories().FirstOrDefault(d =>
                d.Name.Equals(p, StringComparison.OrdinalIgnoreCase));
            if (cur is null) return null;
        }
        return cur;
    }

    private static byte[] ReadRealBytes(IArchiveBinaryFile binFile)
    {
        using var ms = new MemoryStream();
        binFile.Export(ms);
        byte[] buf = ms.ToArray();

        if (binFile.IsEncrypted)
        {
            var hash   = GTA5Hash.CalculateHash(binFile.Name);
            var keyIdx = (hash + (uint)binFile.UncompressedSize + (101 - 40)) % 0x65;
            var key    = GTA5Constants.PC_NG_KEYS[keyIdx];
            if (key is { Length: > 0 })
                buf = GTA5Crypto.Decrypt(buf, key);
        }
        if (binFile.IsCompressed)
        {
            using var def = new DeflateStream(new MemoryStream(buf), CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            def.CopyTo(outMs);
            return outMs.ToArray();
        }
        return buf;
    }

    private static string DecodeXmlText(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return string.Empty;
        using var ms = new MemoryStream(bytes);
        using var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        if (LooksLikeXml(text)) return text;

        var utf8   = Encoding.UTF8.GetString(bytes);          if (LooksLikeXml(utf8))   return utf8;
        var utf16  = Encoding.Unicode.GetString(bytes);       if (LooksLikeXml(utf16))  return utf16;
        var utf16b = Encoding.BigEndianUnicode.GetString(bytes); if (LooksLikeXml(utf16b)) return utf16b;
        return string.Empty;
    }

    private static bool LooksLikeXml(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.TrimStart('﻿', '\0', ' ', '\t', '\r', '\n');
        return t.StartsWith("<") &&
               (t.Contains("<content") || t.Contains("<filesToEnable") || t.Contains("<?xml"));
    }
}
