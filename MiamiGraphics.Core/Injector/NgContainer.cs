using System;
using System.IO;
using MiamiGraphics.Core.I18n;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Injector
{
    public static class NgContainer
    {
        private const uint RPF7 = 0x52504637u;
        private const uint OPEN = 0x4E45504Fu;
        private const int DIR_MARKER = 0x7FFFFF00;

        public static bool IsObfuscated(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var head = new byte[16];
                if (fs.Read(head, 0, 16) < 16) return false;
                if (BitConverter.ToUInt32(head, 0) != RPF7) return false;
                return (BitConverter.ToUInt32(head, 8) & 0x80000000u) != 0;
            }
            catch { return false; }
        }

        public static string? MakeReadableCopy(string inputPath) =>
            IsObfuscated(inputPath) ? DeobfuscateToTemp(inputPath) : null;

        public static void DropCopy(string? tempPath)
        {
            if (tempPath == null) return;
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }

        public static string DeobfuscateToTemp(string inputPath, string? fileName = null)
        {
            using var fs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var head = new byte[16];
            if (fs.Read(head, 0, 16) < 16 || BitConverter.ToUInt32(head, 0) != RPF7)
                throw new InvalidOperationException(Loc.T("error.notRpf7"));

            uint entriesCount = BitConverter.ToUInt32(head, 4);
            uint namesLen = BitConverter.ToUInt32(head, 8) & 0x7FFFFFFF;
            long entLen = 16L * entriesCount;
            if (entLen > fs.Length || namesLen > fs.Length)
                throw new InvalidOperationException(Loc.T("error.rpf7HeaderCorrupt"));

            var entRaw = new byte[entLen];
            ReadExact(fs, entRaw);
            var namesRaw = new byte[namesLen];
            ReadExact(fs, namesRaw);

            var (entDec, namesDec) = DecryptToc(entRaw, namesRaw);

            Array.Copy(BitConverter.GetBytes(namesLen), 0, head, 8, 4);
            Array.Copy(BitConverter.GetBytes(OPEN), 0, head, 12, 4);

            string dir = Path.Combine(Path.GetTempPath(), "MiamiGraphics.NgContainer", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string tmp = Path.Combine(dir, fileName ?? Path.GetFileName(inputPath));

            using (var outFs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                outFs.Write(head, 0, 16);
                outFs.Write(entDec, 0, entDec.Length);
                outFs.Write(namesDec, 0, namesDec.Length);
                fs.CopyTo(outFs, 1 << 20);
            }
            return tmp;
        }

        private static (byte[] ent, byte[] names) DecryptToc(byte[] entRaw, byte[] namesRaw)
        {
            if (HasDirectory(entRaw)) return (entRaw, namesRaw);

            int keyCount = GTA5Constants.PC_NG_KEYS?.Length ?? 0;
            for (int i = 0; i < keyCount; i++)
            {
                var key = GTA5Constants.PC_NG_KEYS![i];
                if (key == null || key.Length == 0) continue;
                byte[] t;
                try { t = GTA5Crypto.Decrypt(entRaw, key); } catch { continue; }
                if (HasDirectory(t)) return (t, GTA5Crypto.Decrypt(namesRaw, key));
            }
            throw new InvalidOperationException(Loc.T("error.ngTocNotDecrypted"));
        }

        private static bool HasDirectory(byte[] e)
        {
            int n = e.Length / 16;
            for (int i = 0; i < n; i++)
            {
                int off = i * 16 + 4;
                if (off + 4 > e.Length) break;
                if (BitConverter.ToInt32(e, off) == DIR_MARKER) return true;
            }
            return false;
        }

        private static void ReadExact(Stream s, byte[] buf)
        {
            int off = 0;
            while (off < buf.Length)
            {
                int n = s.Read(buf, off, buf.Length - off);
                if (n <= 0) throw new InvalidOperationException(Loc.T("error.fileShorterThanToc"));
                off += n;
            }
        }
    }
}
