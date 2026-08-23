#nullable enable
using System;
using System.IO;
using RageLib.Cryptography;
using RageLib.GTA5.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Injector
{
    public enum RpfTocMode { Unknown, Plain, Aes, Ng }

    public sealed record RpfCheck(
        bool IsRpf7,
        RpfTocMode Declared,
        RpfTocMode Actual,
        uint EntriesCount,
        bool GameCanRead,
        string Detail);

    public static class RpfEncryptionCheck
    {
        private const uint Rpf7Ident = 0x52504637u;
        private const uint MarkerOpen = 0x4E45504Fu;
        private const uint MarkerAes = 0x0FFFFFF9u;

        public static RpfCheck Inspect(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                using var fs = File.OpenRead(path);

                var head = new byte[16];
                if (fs.Read(head, 0, 16) != 16)
                    return new RpfCheck(false, RpfTocMode.Unknown, RpfTocMode.Unknown, 0, false, "файл короче заголовка");

                if (BitConverter.ToUInt32(head, 0) != Rpf7Ident)
                    return new RpfCheck(false, RpfTocMode.Unknown, RpfTocMode.Unknown, 0, false, "не RPF7");

                uint entriesCount = BitConverter.ToUInt32(head, 4);
                uint marker = BitConverter.ToUInt32(head, 12);
                var declared = marker == MarkerOpen ? RpfTocMode.Plain
                             : marker == MarkerAes ? RpfTocMode.Aes
                             : RpfTocMode.Ng;

                long tocLen = 16L * entriesCount;
                if (tocLen <= 0 || tocLen > fi.Length)
                    return new RpfCheck(true, declared, RpfTocMode.Unknown, entriesCount, false,
                                        $"в заголовке {entriesCount} записей - не бьётся с размером файла");

                var toc = new byte[tocLen];
                fs.Position = 16;
                fs.ReadExactly(toc, 0, (int)tocLen);

                var actual = DetectMode(toc, fi.Name, fi.Length);
                bool ok = actual != RpfTocMode.Unknown && actual == declared;

                string detail = ok
                    ? ""
                    : actual == RpfTocMode.Unknown
                        ? $"оглавление не разбирается ни одним способом (заявлено {declared})"
                        : $"заявлено {declared}, а лежит {actual}";

                return new RpfCheck(true, declared, actual, entriesCount, ok, detail);
            }
            catch (Exception ex)
            {
                return new RpfCheck(false, RpfTocMode.Unknown, RpfTocMode.Unknown, 0, false,
                                    $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        public static bool WillSurviveRename(string path, string futureName)
        {
            try
            {
                var fi = new FileInfo(path);
                using var fs = File.OpenRead(path);
                var head = new byte[16];
                if (fs.Read(head, 0, 16) != 16) return false;
                if (BitConverter.ToUInt32(head, 0) != Rpf7Ident) return false;

                uint entriesCount = BitConverter.ToUInt32(head, 4);
                uint marker = BitConverter.ToUInt32(head, 12);
                if (marker == MarkerOpen || marker == MarkerAes) return true;

                long tocLen = 16L * entriesCount;
                if (tocLen <= 0 || tocLen > fi.Length) return false;
                var toc = new byte[tocLen];
                fs.Position = 16;
                fs.ReadExactly(toc, 0, (int)tocLen);

                return TryNg(toc, futureName, fi.Length);
            }
            catch { return false; }
        }

        public static bool TryConvertToOpen(string path, out string error)
        {
            error = "";
            try
            {
                using (var arc = RageArchiveWrapper7.Open(path))
                {
                    if (arc.archive_.Encryption == RageArchiveEncryption7.None) return true;
                    arc.archive_.Encryption = RageArchiveEncryption7.None;
                    arc.Flush();
                }

                var after = Inspect(path);
                if (!after.GameCanRead) { error = after.Detail; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        private static RpfTocMode DetectMode(byte[] toc, string fileName, long fileLength)
        {
            if (LooksLikeToc(toc)) return RpfTocMode.Plain;

            try
            {
                if (GTA5Constants.PC_AES_KEY != null
                    && LooksLikeToc(AesEncryption.DecryptData((byte[])toc.Clone(), GTA5Constants.PC_AES_KEY)))
                    return RpfTocMode.Aes;
            }
            catch { }

            return TryNg(toc, fileName, fileLength) ? RpfTocMode.Ng : RpfTocMode.Unknown;
        }

        private static bool TryNg(byte[] toc, string fileName, long fileLength)
        {
            try
            {
                if (GTA5Constants.PC_NG_KEYS is null || GTA5Constants.PC_LUT is null) return false;
                uint idx = (GTA5Hash.CalculateHash(fileName) + (uint)fileLength + (101 - 40)) % 0x65;
                return LooksLikeToc(GTA5Crypto.Decrypt((byte[])toc.Clone(), GTA5Constants.PC_NG_KEYS[idx]));
            }
            catch { return false; }
        }

        private static bool LooksLikeToc(byte[] toc)
        {
            for (int i = 0; i + 16 <= toc.Length; i += 16)
                if (BitConverter.ToUInt32(toc, i + 4) == 0x7FFFFF00u) return true;
            return false;
        }
    }
}
