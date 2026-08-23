using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.Services;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Shell.Services;

internal static class RpfFileMutator
{
    public static byte[] Apply(
        byte[] sourceRpfBytes,
        IEnumerable<string> filenamesToRemove,
        IReadOnlyDictionary<string, byte[]> filesToAdd,
        IReadOnlyDictionary<string, byte[]>? filesToAddIfMissing = null)
    {
        if (sourceRpfBytes is null || sourceRpfBytes.Length == 0)
            throw new ArgumentException("source rpf bytes empty", nameof(sourceRpfBytes));

        var removeSet = new HashSet<string>(
            filenamesToRemove.Select(NormalizeName),
            StringComparer.OrdinalIgnoreCase);

        var addByName = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in filesToAdd)
        {
            var n = NormalizeName(k);
            if (string.IsNullOrEmpty(n) || v is null || v.Length == 0) continue;
            addByName[n] = v;
        }

        var skipSet = new HashSet<string>(removeSet, StringComparer.OrdinalIgnoreCase);
        foreach (var n in addByName.Keys) skipSet.Add(n);

        var presentNames = new HashSet<string>(addByName.Keys, StringComparer.OrdinalIgnoreCase);

        var (src, srcDir) = OpenSourceTolerant(sourceRpfBytes);
        var dstPath = MakeTemp("rpfmut_dst");
        try
        {
            using (src)
            {
                var dst = RageArchiveWrapper7.Create(dstPath);
                try
                {
                    dst.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;

                    int copied = 0, skippedRemove = 0, skippedOverride = 0;
                    foreach (var entry in src.Root.GetFiles())
                    {
                        var key = NormalizeName(entry.Name);
                        if (skipSet.Contains(key))
                        {
                            if (addByName.ContainsKey(key)) skippedOverride++;
                            else                            skippedRemove++;
                            continue;
                        }
                        CopyFile(entry, dst.Root);
                        presentNames.Add(key);
                        copied++;
                    }

                    int added = 0;
                    foreach (var (name, bytes) in addByName)
                    {
                        AddRawFile(dst.Root, name, bytes);
                        added++;
                    }

                    int stubbed = 0;
                    if (filesToAddIfMissing is { Count: > 0 })
                    {
                        foreach (var (name, bytes) in filesToAddIfMissing)
                        {
                            var n = NormalizeName(name);
                            if (string.IsNullOrEmpty(n) || bytes is null || bytes.Length == 0) continue;
                            if (presentNames.Contains(n)) continue;
                            AddRawFile(dst.Root, n, bytes);
                            stubbed++;
                        }
                    }
                    Debug.WriteLine($"[rpfmut.apply] copied={copied} removed={skippedRemove} overridden={skippedOverride} added={added} stubbed={stubbed}");

                    dst.FileName = Path.GetFileName(dstPath);
                    dst.Flush();
                }
                finally { dst.Dispose(); }
            }
            ArchiveFixer.FixOrThrow(dstPath, TargetDlcEditor.MIAMI_WEAPON_RPF_NAME);
            return File.ReadAllBytes(dstPath);
        }
        finally
        {
            try { Directory.Delete(srcDir, true); } catch { }
            TryDelete(dstPath);
        }
    }

    public static byte[] NormalizeAndFix(
        byte[] weaponRpfBytes,
        IReadOnlyDictionary<string, byte[]>? filesToAddIfMissing = null)
        => Apply(weaponRpfBytes, Array.Empty<string>(), new Dictionary<string, byte[]>(), filesToAddIfMissing);

    public static byte[] RemoveFiles(byte[] sourceRpfBytes, IEnumerable<string> filenamesToRemove)
    {
        if (sourceRpfBytes is null || sourceRpfBytes.Length == 0)
            throw new ArgumentException("source rpf bytes empty", nameof(sourceRpfBytes));

        var removeSet = new HashSet<string>(
            filenamesToRemove.Select(NormalizeName),
            StringComparer.OrdinalIgnoreCase);

        var (src, srcDir) = OpenSourceTolerant(sourceRpfBytes);
        var dstPath = MakeTemp("rpfmut_dst");
        try
        {
            using (src)
            {
                var dst = RageArchiveWrapper7.Create(dstPath);
                try
                {

                    dst.archive_.Encryption = src.archive_.Encryption;

                    int copied = 0;
                    int skipped = 0;
                    foreach (var entry in src.Root.GetFiles())
                    {
                        var key = NormalizeName(entry.Name);
                        if (removeSet.Contains(key))
                        {
                            skipped++;
                            continue;
                        }
                        CopyFile(entry, dst.Root);
                        copied++;
                    }
                    Debug.WriteLine($"[rpfmut.remove] copied={copied} removed={skipped} (asked-to-remove={removeSet.Count})");

                    dst.FileName = Path.GetFileName(dstPath);
                    dst.Flush();
                }
                finally { dst.Dispose(); }
            }
            return File.ReadAllBytes(dstPath);
        }
        finally
        {
            try { Directory.Delete(srcDir, true); } catch { }
            TryDelete(dstPath);
        }
    }

    public static byte[] AddFiles(byte[] sourceRpfBytes, IReadOnlyDictionary<string, byte[]> filesToAdd)
    {
        if (sourceRpfBytes is null || sourceRpfBytes.Length == 0)
            throw new ArgumentException("source rpf bytes empty", nameof(sourceRpfBytes));

        var addByName = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in filesToAdd)
        {
            var n = NormalizeName(k);
            if (string.IsNullOrEmpty(n) || v is null || v.Length == 0) continue;
            addByName[n] = v;
        }
        if (addByName.Count == 0) return sourceRpfBytes;

        var (src, srcDir) = OpenSourceTolerant(sourceRpfBytes);
        var dstPath = MakeTemp("rpfmut_dst");
        try
        {
            using (src)
            {
                var dst = RageArchiveWrapper7.Create(dstPath);
                try
                {
                    dst.archive_.Encryption = src.archive_.Encryption;

                    int copied = 0;
                    int overridden = 0;
                    foreach (var entry in src.Root.GetFiles())
                    {
                        var key = NormalizeName(entry.Name);
                        if (addByName.ContainsKey(key))
                        {
                            overridden++;
                            continue;
                        }
                        CopyFile(entry, dst.Root);
                        copied++;
                    }

                    foreach (var (name, bytes) in addByName)
                    {
                        AddRawFile(dst.Root, name, bytes);
                    }
                    Debug.WriteLine($"[rpfmut.add] copied={copied} overridden={overridden} added={addByName.Count}");

                    dst.FileName = Path.GetFileName(dstPath);
                    dst.Flush();
                }
                finally { dst.Dispose(); }
            }
            return File.ReadAllBytes(dstPath);
        }
        finally
        {
            try { Directory.Delete(srcDir, true); } catch { }
            TryDelete(dstPath);
        }
    }

    public static string Sha256(byte[] data)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
    }

    private static void CopyFile(IArchiveFile src, IArchiveDirectory destDir)
    {
        if (src is IArchiveBinaryFile bin)
        {
            using var ms = new MemoryStream();
            bin.Export(ms);

            var bad = RpfEntrySanity.RejectReason(bin.Name, ms.ToArray());
            if (bad != null)
            {
                Debug.WriteLine($"[rpf-mutator] ОТСЕЯНА при копировании '{bin.Name}' ({ms.Length:N0} б): {bad}");
                return;
            }

            var newF = destDir.CreateBinaryFile();
            newF.Name             = bin.Name;
            newF.IsEncrypted      = bin.IsEncrypted;
            newF.IsCompressed     = bin.IsCompressed;
            newF.UncompressedSize = bin.UncompressedSize;
            ms.Position = 0;
            newF.Import(ms);
        }
        else if (src is IArchiveResourceFile rsc)
        {
            var newF = destDir.CreateResourceFile();
            newF.Name = rsc.Name;
            using var ms = new MemoryStream();
            rsc.Export(ms);
            ms.Position = 0;
            newF.Import(ms);
        }

    }

    private static void AddRawFile(IArchiveDirectory dir, string fileName, byte[] data)
    {
        if (!RpfEntryGate.TryPrepare(fileName, data, out var ready, out var bad, out var note))
        {
            Debug.WriteLine($"[rpf-mutator] ОТСЕЯНО '{fileName}' ({data.Length:N0} б): {bad}");
            return;
        }
        if (note != null) Debug.WriteLine($"[rpf-mutator] '{fileName}' {note}");
        data = ready;

        if (IsRsc7Resource(data))
        {
            var newF = dir.CreateResourceFile();
            newF.Name = fileName;
            using var ms = new MemoryStream(data);
            newF.Import(ms);
            return;
        }

        var binF = dir.CreateBinaryFile();
        binF.Name             = fileName;
        binF.IsEncrypted      = false;
        binF.IsCompressed     = false;
        binF.UncompressedSize = data.LongLength;
        using var bms = new MemoryStream(data);
        binF.Import(bms);
    }

    private static bool IsRsc7Resource(byte[] data) => RpfEntrySanity.IsRsc7(data);

    private static string NormalizeName(string name)
    {

        if (string.IsNullOrEmpty(name)) return string.Empty;
        var idx = name.LastIndexOfAny(new[] { '/', '\\' });
        return idx >= 0 ? name.Substring(idx + 1) : name;
    }

    private static string WriteTemp(byte[] bytes, string tag)
    {
        var p = MakeTemp(tag);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private static readonly string[] SourceNameCandidates = { "miami_weapon.rpf", "weapon.rpf", "weapons.rpf" };

    private static (RageArchiveWrapper7 arc, string dir) OpenSourceTolerant(byte[] bytes)
    {
        var dir = Path.Combine(Path.GetTempPath(), "rpfmut_src_" + System.Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(dir);
        System.Exception? last = null;
        foreach (var name in SourceNameCandidates)
        {
            var p = Path.Combine(dir, name);
            try
            {
                File.WriteAllBytes(p, bytes);
                var arc = RageArchiveWrapper7.Open(p);
                if (arc.Root.GetFiles().Length == 0) { arc.Dispose(); TryDelete(p); continue; }
                return (arc, dir);
            }
            catch (System.Exception ex) { last = ex; TryDelete(p); }
        }
        try { Directory.Delete(dir, true); } catch { }
        throw last ?? new System.Exception("RPF7 has no directory entries - file is malformed or uses an unsupported format.");
    }

    private static string MakeTemp(string tag) =>
        Path.Combine(Path.GetTempPath(),
            $"{tag}_{Guid.NewGuid().ToString("N")[..12]}.rpf");

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
