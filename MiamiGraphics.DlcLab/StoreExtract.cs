using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.DlcLab;

public static class StoreExtract
{
    public static void Extract(string containerPath, string outDir)
    {
        byte[] open = Deobfuscate(File.ReadAllBytes(containerPath));
        string tmp = Path.Combine(Path.GetTempPath(), "mg_store_open_" + Guid.NewGuid().ToString("N") + ".rpf");
        File.WriteAllBytes(tmp, open);
        Console.WriteLine($"[extract] de-obfuscated OPEN copy -> {tmp}");

        outDir = Path.GetFullPath(outDir);
        int files = 0, nested = 0;
        try
        {
            using var arc = RageArchiveWrapper7.Open(tmp);
            Directory.CreateDirectory(Ext(outDir));
            ExtractDir(arc.Root, outDir, "", ref files, ref nested);
        }
        finally { try { File.Delete(tmp); } catch { } }

        Console.WriteLine($"\n[extract] DONE: {files} files written, {nested} nested rpf expanded -> {outDir}");
    }

    private static void ExtractDir(IArchiveDirectory dir, string diskDir, string rel, ref int files, ref int nested)
    {
        Directory.CreateDirectory(Ext(diskDir));
        foreach (var f in dir.GetFiles())
        {
            string name = f.Name;
            bool isRpf = name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);
            byte[] bytes;
            try { bytes = RpfUtil.ReadRealBytes(f); }
            catch (Exception ex) { Console.WriteLine($"  ! {rel}{name}: read failed ({ex.GetType().Name})"); continue; }

            if (isRpf)
            {
                string sub = Path.Combine(diskDir, Sanitize(name));
                try
                {
                    byte[] openBytes = LooksRpf(bytes) ? Deobfuscate(bytes) : bytes;
                    using var ms = new MemoryStream(openBytes);
                    using var narc = RageArchiveWrapper7.Open(ms, name, true);
                    Console.WriteLine($"  [rpf] {rel}{name}/  (expanding)");
                    ExtractDir(narc.Root, sub, rel + name + "/", ref files, ref nested);
                    nested++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [rpf] {rel}{name}: can't expand ({ex.GetType().Name}) — writing raw blob as {name}.raw");
                    File.WriteAllBytes(Ext(Path.Combine(diskDir, Sanitize(name) + ".raw")), bytes);
                    files++;
                }
            }
            else
            {
                File.WriteAllBytes(Ext(Path.Combine(diskDir, Sanitize(name))), bytes);
                files++;
            }
        }
        foreach (var sd in dir.GetDirectories())
            ExtractDir(sd, Path.Combine(diskDir, Sanitize(sd.Name)), rel + sd.Name + "/", ref files, ref nested);
    }

    private static string Ext(string p) => p.StartsWith(@"\\?\") ? p : @"\\?\" + p;

    private static bool LooksRpf(byte[] b) => b.Length >= 4 && BitConverter.ToUInt32(b, 0) == 0x52504637u;

    private static byte[] Deobfuscate(byte[] src)
    {
        uint entriesCount = BitConverter.ToUInt32(src, 4);
        uint namesLen = BitConverter.ToUInt32(src, 8) & 0x7FFFFFFF;
        int entLen = 16 * (int)entriesCount;

        var entRaw = new byte[entLen];
        Array.Copy(src, 16, entRaw, 0, entLen);
        var namesRaw = new byte[namesLen];
        Array.Copy(src, 16 + entLen, namesRaw, 0, (int)namesLen);

        byte[] entDec = entRaw, namesDec = namesRaw;
        string how = "OPEN";
        if (!HasDirectory(entRaw))
        {
            bool found = false;
            for (int i = 0; i < (GTA5Constants.PC_NG_KEYS?.Length ?? 0); i++)
            {
                var key = GTA5Constants.PC_NG_KEYS![i];
                if (key == null || key.Length == 0) continue;
                byte[] t;
                try { t = GTA5Crypto.Decrypt(entRaw, key); } catch { continue; }
                if (HasDirectory(t)) { entDec = t; namesDec = GTA5Crypto.Decrypt(namesRaw, key); how = $"NG key {i}"; found = true; break; }
            }
            if (!found) throw new InvalidOperationException("Could not decrypt TOC with any NG key.");
        }
        Console.WriteLine($"[extract] TOC decrypted via {how}");

        var outBytes = (byte[])src.Clone();
        Array.Copy(BitConverter.GetBytes(namesLen), 0, outBytes, 8, 4);
        Array.Copy(BitConverter.GetBytes(0x4E45504Fu), 0, outBytes, 12, 4);
        Array.Copy(entDec, 0, outBytes, 16, entLen);
        Array.Copy(namesDec, 0, outBytes, 16 + entLen, (int)namesLen);
        return outBytes;
    }

    private static bool HasDirectory(byte[] e)
    {
        int n = e.Length / 16;
        for (int i = 0; i < n; i++)
        {
            int off = i * 16 + 4;
            if (off + 4 > e.Length) break;
            if (BitConverter.ToInt32(e, off) == 0x7FFFFF00) return true;
        }
        return false;
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "_" : name;
    }
}
