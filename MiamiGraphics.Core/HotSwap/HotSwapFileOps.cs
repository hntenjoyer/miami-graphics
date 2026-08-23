using System;
using System.IO;
using System.Threading;

namespace MiamiGraphics.Core.HotSwap
{
    internal static class HotSwapFileOps
    {
        public const string TempSuffix = ".mgswap.tmp";

        public static string TempFor(string dest) => dest + TempSuffix;

        public static void DeleteQuiet(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        public static void ReplaceWithRetry(string source, string dest, string backup, int attempts = 12)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
            for (int i = 0; ; i++)
            {
                try { File.Replace(source, dest, backup, ignoreMetadataErrors: true); return; }
                catch (IOException) when (i < attempts - 1) { Thread.Sleep(250); }
            }
        }

        public static void MoveOverwriteWithRetry(string source, string dest, int attempts = 12)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            for (int i = 0; ; i++)
            {
                try { File.Move(source, dest, overwrite: true); return; }
                catch (IOException) when (i < attempts - 1) { Thread.Sleep(250); }
                catch (UnauthorizedAccessException) when (i < attempts - 1) { Thread.Sleep(250); }
            }
        }

        private static void CopyWithRetry(string source, string dest, int attempts)
        {
            for (int i = 0; ; i++)
            {
                try { File.Copy(source, dest, overwrite: true); return; }
                catch (IOException) when (i < attempts - 1) { Thread.Sleep(250); }
                catch (UnauthorizedAccessException) when (i < attempts - 1) { Thread.Sleep(250); }
            }
        }

        public static void CopyThroughTemp(string source, string dest, int attempts = 12)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var tmp = TempFor(dest);
            DeleteQuiet(tmp);
            try { CopyWithRetry(source, tmp, attempts); }
            catch { DeleteQuiet(tmp); throw; }
            MoveOverwriteWithRetry(tmp, dest, attempts);
        }

        public static string Stamp(string path)
        {
            var fi = new FileInfo(path);
            return fi.Exists ? $"{fi.Length}-{fi.LastWriteTimeUtc.Ticks}" : "";
        }

        public static bool StampEq(string a, string b)
        {
            var sa = Stamp(a);
            return sa.Length > 0 && string.Equals(sa, Stamp(b), StringComparison.Ordinal);
        }

        public static string ContentSig(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length == 0) return "";
                const int Chunk = 1024 * 1024;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 0, FileOptions.SequentialScan);
                using var sha = global::System.Security.Cryptography.SHA256.Create();
                var buf = new byte[Chunk];
                long[] offsets = { 0, Math.Max(0, fi.Length / 2 - Chunk / 2), Math.Max(0, fi.Length - Chunk) };
                foreach (var off in offsets)
                {
                    fs.Position = off;
                    int want = (int)Math.Min(Chunk, fi.Length - off), got = 0;
                    while (got < want)
                    {
                        int n = fs.Read(buf, got, want - got);
                        if (n <= 0) break;
                        got += n;
                    }
                    if (got > 0) sha.TransformBlock(buf, 0, got, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return fi.Length + "-" + Convert.ToHexString(sha.Hash!, 0, 8);
            }
            catch { return ""; }
        }

        public static void SanitizeTemp(string dest, int minAgeMinutes = 2)
        {
            try
            {
                var tmp = TempFor(dest);
                var fi = new FileInfo(tmp);
                if (!fi.Exists) return;
                if ((DateTime.UtcNow - fi.LastWriteTimeUtc).TotalMinutes < minAgeMinutes) return;
                fi.Delete();
            }
            catch { }
        }
    }
}
