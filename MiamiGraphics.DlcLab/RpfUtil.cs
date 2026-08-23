using System.IO.Compression;
using System.Text;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.DlcLab;

public static class RpfUtil
{
    public static RageArchiveWrapper7 OpenRpf(string path, string? logicalName = null, bool write = false)
    {
        var fs = new FileStream(path, FileMode.Open, write ? FileAccess.ReadWrite : FileAccess.Read);
        try
        {
            string name = logicalName ?? Path.GetFileName(path);
            return RageArchiveWrapper7.Open(fs, name, leaveOpen: false);
        }
        catch { fs.Dispose(); throw; }
    }

    public static IArchiveDirectory? NavigateDir(IArchiveDirectory root, string dirPath)
    {
        IArchiveDirectory? cur = root;
        foreach (var part in Split(dirPath))
        {
            cur = cur?.GetDirectories()
                .FirstOrDefault(d => d.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (cur is null) return null;
        }
        return cur;
    }

    public static IArchiveFile? FindFile(IArchiveDirectory root, string internalPath)
    {
        var parts = Split(internalPath);
        if (parts.Length == 0) return null;

        IArchiveDirectory? dir = root;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            dir = dir?.GetDirectories()
                .FirstOrDefault(d => d.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
            if (dir is null) return null;
        }

        string fileName = parts[^1];
        return dir?.GetFiles()
            .FirstOrDefault(f => f.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    public static byte[] ReadRealBytes(IArchiveFile file)
    {
        if (file is IArchiveBinaryFile bin)
        {
            using var ms = new MemoryStream();
            bin.Export(ms);
            byte[] buf = ms.ToArray();

            if (bin.IsEncrypted)
            {
                var hash = GTA5Hash.CalculateHash(bin.Name);
                var keyIdx = (hash + (uint)bin.UncompressedSize + (101 - 40)) % 0x65;
                var key = GTA5Constants.PC_NG_KEYS is { } keys && keys.Length > keyIdx ? keys[keyIdx] : null;
                if (key is { Length: > 0 })
                    buf = GTA5Crypto.Decrypt(buf, key);
                else
                    Console.WriteLine($"[RpfUtil] WARN: no NG key for {bin.Name} (idx={keyIdx})");
            }

            if (bin.IsCompressed)
            {
                using var def = new DeflateStream(new MemoryStream(buf), CompressionMode.Decompress);
                using var outMs = new MemoryStream();
                def.CopyTo(outMs);
                return outMs.ToArray();
            }
            return buf;
        }

        using (var ms = new MemoryStream())
        {
            file.Export(ms);
            return ms.ToArray();
        }
    }

    public static string DecodeXml(byte[] bytes)
    {
        if (bytes.Length == 0) return "";
        using (var ms = new MemoryStream(bytes))
        using (var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            var text = reader.ReadToEnd();
            if (LooksLikeXml(text)) return text;
        }
        foreach (var enc in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
        {
            var t = enc.GetString(bytes);
            if (LooksLikeXml(t)) return t;
        }
        return "";
    }

    private static bool LooksLikeXml(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.TrimStart('﻿', '\0', ' ', '\t', '\r', '\n');
        return t.StartsWith("<");
    }

    public static void PrintDir(IArchiveDirectory dir, string label)
    {
        Console.WriteLine($"== {label} ==");
        foreach (var d in dir.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            Console.WriteLine($"  [dir]  {d.Name}/");
        foreach (var f in dir.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            string kind = f is IArchiveBinaryFile b
                ? $"bin  size={b.Size,-10} comp={b.IsCompressed,-5} enc={b.IsEncrypted}"
                : "res";
            Console.WriteLine($"  [file] {f.Name,-40} {kind}");
        }
    }

    private static string[] Split(string path) =>
        path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
}
