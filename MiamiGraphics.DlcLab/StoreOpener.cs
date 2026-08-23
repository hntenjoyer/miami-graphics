using System.Text;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.DlcLab;

public static class StoreOpener
{
    public static void Open(string path)
    {
        byte[] all = File.ReadAllBytes(path);
        uint ident = BitConverter.ToUInt32(all, 0);
        uint entriesCount = BitConverter.ToUInt32(all, 4);
        uint namesLenRaw = BitConverter.ToUInt32(all, 8);
        uint enc = BitConverter.ToUInt32(all, 12);
        uint namesLen = namesLenRaw & 0x7FFFFFFF;

        Console.WriteLine($"[store] ident=0x{ident:X8} (RPF7=0x52504637)  entries={entriesCount}");
        Console.WriteLine($"[store] namesLength raw=0x{namesLenRaw:X8} -> masked={namesLen} (÷16={(namesLen % 16 == 0 ? "yes" : "NO")})");
        Console.WriteLine($"[store] encryption=0x{enc:X8} (NG=0x0FEFFFFF, AES=0x0FFFFFF9, OPEN=0x4E45504F)\n");

        int entLen = 16 * (int)entriesCount;
        var entRaw = new byte[entLen];
        Array.Copy(all, 16, entRaw, 0, entLen);
        var namesRaw = new byte[namesLen];
        Array.Copy(all, 16 + entLen, namesRaw, 0, (int)namesLen);

        byte[]? entDec = null, namesDec = null;
        string how = "";

        if (HasDirectory(entRaw)) { entDec = entRaw; namesDec = namesRaw; how = "OPEN"; }

        if (entDec == null && GTA5Constants.PC_NG_KEYS != null)
        {
            for (int i = 0; i < GTA5Constants.PC_NG_KEYS.Length; i++)
            {
                var key = GTA5Constants.PC_NG_KEYS[i];
                if (key == null || key.Length == 0) continue;
                byte[] tryEnt;
                try { tryEnt = GTA5Crypto.Decrypt(entRaw, key); } catch { continue; }
                if (HasDirectory(tryEnt))
                {
                    entDec = tryEnt;
                    namesDec = GTA5Crypto.Decrypt(namesRaw, key);
                    how = $"NG key index {i}";
                    break;
                }
            }
        }

        if (entDec == null)
        {
            Console.WriteLine("[store] FAILED to decrypt TOC (no key produced a valid directory). " +
                              "Header/name scheme is more custom than just the high-bit trick.");
            return;
        }

        Console.WriteLine($"[store] ✓ decrypted via {how}\n");
        var entries = ParseEntries(entDec!, (int)entriesCount, namesDec!);
        int files = 0, dirs = 0;
        WalkPrint(entries, 0, "", ref files, ref dirs, 0);
        Console.WriteLine($"\n[store] {dirs} dirs, {files} files");
    }

    private static bool HasDirectory(byte[] entriesDec)
    {
        int n = entriesDec.Length / 16;
        for (int i = 0; i < n; i++)
        {
            int off = i * 16 + 4;
            if (off + 4 > entriesDec.Length) break;
            if (BitConverter.ToInt32(entriesDec, off) == 0x7FFFFF00) return true;
        }
        return false;
    }

    private sealed record Ent(bool IsDir, string Name, long Size, uint EntriesIndex, uint EntriesCount, bool Encrypted);

    private static List<Ent> ParseEntries(byte[] e, int count, byte[] names)
    {
        string ReadName(int nameOffset)
        {
            if (nameOffset < 0 || nameOffset >= names.Length) return "?";
            int end = nameOffset;
            while (end < names.Length && names[end] != 0) end++;
            return Encoding.ASCII.GetString(names, nameOffset, end - nameOffset);
        }

        var list = new List<Ent>(count);
        for (int i = 0; i < count; i++)
        {
            int b = i * 16;
            int marker = BitConverter.ToInt32(e, b + 4);
            if (marker == 0x7FFFFF00)
            {
                uint nameOff = BitConverter.ToUInt32(e, b + 0);
                uint idx = BitConverter.ToUInt32(e, b + 8);
                uint cnt = BitConverter.ToUInt32(e, b + 12);
                list.Add(new Ent(true, ReadName((int)nameOff), 0, idx, cnt, false));
            }
            else
            {
                ushort nameOff = BitConverter.ToUInt16(e, b + 0);
                long size = e[b + 2] | (e[b + 3] << 8) | (e[b + 4] << 16);
                bool isResource = (marker & 0x80000000u) != 0;
                bool encrypted = false;
                if (!isResource)
                {
                    long uncompressed = BitConverter.ToUInt32(e, b + 8);
                    if (size == 0) size = uncompressed;
                    encrypted = BitConverter.ToUInt32(e, b + 12) != 0;
                }
                list.Add(new Ent(false, ReadName(nameOff), size, 0, 0, encrypted));
            }
        }
        return list;
    }

    private static void WalkPrint(List<Ent> entries, int dirIndex, string prefix, ref int files, ref int dirs, int depth)
    {
        if (dirIndex < 0 || dirIndex >= entries.Count) return;
        var dir = entries[dirIndex];
        for (uint k = dir.EntriesIndex; k < dir.EntriesIndex + dir.EntriesCount && k < entries.Count; k++)
        {
            var ch = entries[(int)k];
            if (ch.IsDir)
            {
                dirs++;
                Console.WriteLine($"  {prefix}{ch.Name}/");
                WalkPrint(entries, (int)k, prefix + ch.Name + "/", ref files, ref dirs, depth + 1);
            }
            else
            {
                files++;
                Console.WriteLine($"  {prefix}{ch.Name}   ({ch.Size:N0} b{(ch.Encrypted ? ", enc" : "")})");
            }
        }
    }
}
