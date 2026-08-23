using System;
using System.IO;
using System.Linq;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{
    public static class PlayerSwitchService
    {
        public const string FileName     = "playerswitch.meta";
        public const string InternalPath = "common/data/playerswitch.meta";

        public static void Apply(string updateRpfPath, byte[] metaBytes)
        {
            if (metaBytes is null || metaBytes.Length == 0)
                throw new ArgumentException("playerswitch.meta bytes empty", nameof(metaBytes));
            Mutate(updateRpfPath, enable: true, metaBytes);
        }

        public static void Remove(string updateRpfPath)
            => Mutate(updateRpfPath, enable: false, metaBytes: null);

        public static bool IsApplied(string updateRpfPath)
        {
            try
            {
                using var arc = RageArchiveWrapper7.Open(updateRpfPath);
                var dataDir = FindDataDir(arc.Root, createMissing: false);
                return dataDir?.GetFiles()
                    .Any(f => f.Name.Equals(FileName, StringComparison.OrdinalIgnoreCase)) ?? false;
            }
            catch { return false; }
        }

        private static void Mutate(string updateRpfPath, bool enable, byte[]? metaBytes)
        {
            if (!File.Exists(updateRpfPath))
                throw new FileNotFoundException("update.rpf not found", updateRpfPath);

            using var arc = RageArchiveWrapper7.Open(updateRpfPath);
            var dataDir = FindDataDir(arc.Root, createMissing: enable);
            if (dataDir is null)
            {
                if (enable) throw new InvalidOperationException("common/data directory not found in update.rpf.");
                return;
            }

            var existing = dataDir.GetFiles()
                .FirstOrDefault(f => f.Name.Equals(FileName, StringComparison.OrdinalIgnoreCase));

            if (enable)
            {
                if (existing is IArchiveBinaryFile eb)
                {
                    eb.Import(new MemoryStream(metaBytes!));
                    eb.IsCompressed = false;
                    eb.IsEncrypted = false;
                    eb.UncompressedSize = (uint)metaBytes!.Length;
                }
                else
                {
                    var nf = dataDir.CreateBinaryFile();
                    nf.Name = FileName;
                    nf.Import(new MemoryStream(metaBytes!));
                    nf.IsCompressed = false;
                    nf.IsEncrypted = false;
                    nf.UncompressedSize = (uint)metaBytes!.Length;
                }
            }
            else if (existing is not null)
            {
                try { dataDir.DeleteFile(existing); }
                catch {}
            }

            arc.Flush();
        }

        private static IArchiveDirectory? FindDataDir(IArchiveDirectory root, bool createMissing)
            => Descend(Descend(root, "common", createMissing), "data", createMissing);

        private static IArchiveDirectory? Descend(IArchiveDirectory? dir, string name, bool createMissing)
        {
            if (dir is null) return null;
            var sub = dir.GetDirectories()
                .FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (sub is not null) return sub;
            if (!createMissing) return null;
            var nd = dir.CreateDirectory();
            nd.Name = name;
            return nd;
        }
    }
}
