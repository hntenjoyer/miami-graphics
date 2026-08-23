using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.System
{

    internal static class YptHashDiagnostic
    {

        private static readonly string LogFilePath =
            Path.Combine(WorkDirDefaults.ResultBaseDir, "_DebugLogs", "tracer_hash_log.txt");

        public static void DumpPhysicalFile(string label, string physicalPath)
        {
            try
            {
                if (!File.Exists(physicalPath))
                {
                    WriteBoth($"[{label}] FILE NOT FOUND: {physicalPath}");
                    return;
                }

                byte[] bytes = File.ReadAllBytes(physicalPath);
                Dump(label, $"disk: {physicalPath}", bytes);
            }
            catch (Exception ex)
            {
                WriteBoth($"[{label}] ERROR: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void DumpInsideUpdateRpf(string label, string updateRpfPath, string internalPath)
        {
            try
            {
                if (!File.Exists(updateRpfPath))
                {
                    WriteBoth($"[{label}] UPDATE.RPF NOT FOUND: {updateRpfPath}");
                    return;
                }

                string[] parts = internalPath.Replace('\\', '/').TrimStart('/').Split('/');

                using var archive = RageArchiveWrapper7.Open(updateRpfPath);
                byte[] extracted = ExtractByPath(archive.Root, parts, 0);
                if (extracted == null)
                {
                    WriteBoth($"[{label}] FILE NOT FOUND IN RPF: {internalPath}");
                    return;
                }

                Dump(label, $"inside update.rpf: {internalPath}", extracted);
            }
            catch (Exception ex)
            {
                WriteBoth($"[{label}] ERROR: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static byte[] ExtractByPath(IArchiveDirectory dir, string[] parts, int idx)
        {
            if (idx >= parts.Length) return null;
            string current = parts[idx];

            if (idx == parts.Length - 1)
            {
                var file = dir.GetFiles().FirstOrDefault(f =>
                    f.Name.Equals(current, StringComparison.OrdinalIgnoreCase));
                if (file == null) return null;

                using var ms = new MemoryStream();
                file.Export(ms);
                return ms.ToArray();
            }

            var sub = dir.GetDirectories().FirstOrDefault(d =>
                d.Name.Equals(current, StringComparison.OrdinalIgnoreCase));
            if (sub != null) return ExtractByPath(sub, parts, idx + 1);

            var nested = dir.GetFiles().FirstOrDefault(f =>
                f.Name.Equals(current, StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile;
            if (nested != null && current.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                using var s = nested.GetStream();
                using var a = RageArchiveWrapper7.Open(s, nested.Name, true);
                return ExtractByPath(a.Root, parts, idx + 1);
            }

            return null;
        }

        private static void Dump(string label, string source, byte[] bytes)
        {
            string sha = ComputeSha256(bytes);
            string hex16 = BytesToHex(bytes, 16);
            uint magic = bytes.Length >= 4 ? BitConverter.ToUInt32(bytes, 0) : 0;
            string magicAscii = bytes.Length >= 4
                ? ((char)bytes[0]).ToString() + (char)bytes[1] + (char)bytes[2] + $" 0x{bytes[3]:X2}"
                : "<too short>";

            var sb = new global::System.Text.StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"=== [{label}] ===");
            sb.AppendLine($"  Source:    {source}");
            sb.AppendLine($"  Size:      {bytes.LongLength} bytes");
            sb.AppendLine($"  SHA256:    {sha}");
            sb.AppendLine($"  Hex[0-16]: {hex16}");
            sb.AppendLine($"  Magic:     {magicAscii}   (raw uint32: 0x{magic:X8})");
            string all = sb.ToString();

            Console.Write(all);
            AppendLog(all);
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using var sha = SHA256.Create();
            byte[] h = sha.ComputeHash(bytes);
            return BitConverter.ToString(h).Replace("-", "").ToLowerInvariant();
        }

        private static string BytesToHex(byte[] bytes, int n)
        {
            int take = Math.Min(n, bytes.Length);
            var parts = new string[take];
            for (int i = 0; i < take; i++) parts[i] = bytes[i].ToString("X2");
            return string.Join(" ", parts);
        }

        private static void WriteBoth(string line)
        {
            Console.WriteLine(line);
            AppendLog(line + global::System.Environment.NewLine);
        }

        private static void AppendLog(string text)
        {
            try
            {
                string dir = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogFilePath, text);
            }
            catch {  }
        }

        public static void Separator(string title)
        {
            string line = global::System.Environment.NewLine
                + "################################################################" + global::System.Environment.NewLine
                + "# " + title + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + global::System.Environment.NewLine
                + "################################################################"
                + global::System.Environment.NewLine;
            Console.Write(line);
            AppendLog(line);
        }
    }
}
