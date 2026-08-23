using System;
using System.IO;
using System.Security.Cryptography;
using MiamiGraphics.Core.I18n;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{
    public enum BackpackFileState
    {
        Missing,
        Vanilla,
        Removed,
        RemovedForeign,
        Foreign,
    }

    public sealed record BackpackStatus(
        BackpackFileState State,
        string FullPath,
        long SizeBytes,
        string Sha256,
        bool BackupAvailable);

    public static class BackpackDlcService
    {
        public const string RelativePath = @"update\x64\dlcpacks\patchday11ng\dlc.rpf";

        public const string VanillaSha256 = "54aab351832d6fa3ffab96601b6b966802ea08d8fb44b4e1c1e7061db304547f";
        public const long   VanillaSize   = 9_955_328;

        public const string RemovedSha256 = "ffa3ec4a2032f658c258f9be0a2b0a0828395c460fe91c63faf424280ea2eff1";
        public const long   RemovedSize   = 5_092_352;

        public const string R2Vanilla = "rukzak/patchday11ng_vanilla.rpf";
        public const string R2Removed = "rukzak/patchday11ng_removed_v2.rpf";

        public static string ResolvePath(string gtaRoot) => Path.Combine(gtaRoot, RelativePath);

        public static BackpackStatus Inspect(string gtaRoot, string? backupPath, Func<string, string>? sha = null)
        {
            sha ??= Sha256File;
            var path = ResolvePath(gtaRoot);
            bool backup = !string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath);

            if (!File.Exists(path))
                return new BackpackStatus(BackpackFileState.Missing, path, 0, "", backup);

            long size = new FileInfo(path).Length;

            if (size != VanillaSize && size != RemovedSize)
                return new BackpackStatus(ClassifyForeign(path), path, size, "", backup);

            var hash = sha(path);
            if (hash.Equals(VanillaSha256, StringComparison.OrdinalIgnoreCase))
                return new BackpackStatus(BackpackFileState.Vanilla, path, size, hash, backup);
            if (hash.Equals(RemovedSha256, StringComparison.OrdinalIgnoreCase))
                return new BackpackStatus(BackpackFileState.Removed, path, size, hash, backup);

            return new BackpackStatus(ClassifyForeign(path), path, size, hash, backup);
        }

        private const long StubMaxBytes = 4096;

        private const int StubMinCount = 50;

        private const int MaxArchiveDepth = 3;

        private static bool IsBackpackModelName(string name)
            => OverlayModDetector.IsBackpackModelName(name);

        private static BackpackFileState ClassifyForeign(string dlcRpfPath)
        {
            try
            {
                using var arc = RageArchiveWrapper7.Open(dlcRpfPath);
                int stubs = 0;
                CountStubs(arc.Root, archiveDepth: 0, ref stubs);
                return stubs >= StubMinCount ? BackpackFileState.RemovedForeign : BackpackFileState.Foreign;
            }
            catch { return BackpackFileState.Foreign; }
        }

        private static void CountStubs(IArchiveDirectory dir, int archiveDepth, ref int stubs)
        {
            if (stubs >= StubMinCount) return;

            foreach (var f in dir.GetFiles())
            {
                if (stubs >= StubMinCount) return;

                if (IsBackpackModelName(f.Name) && IsStub(f)) { stubs++; continue; }

                if (archiveDepth >= MaxArchiveDepth) continue;
                if (!f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)) continue;
                if (f is not IArchiveBinaryFile bin) continue;
                try
                {
                    using var ms = new MemoryStream();
                    bin.Export(ms);
                    ms.Position = 0;
                    using var nested = RageArchiveWrapper7.Open(ms, f.Name, leaveOpen: true);
                    CountStubs(nested.Root, archiveDepth + 1, ref stubs);
                }
                catch {}
            }

            foreach (var d in dir.GetDirectories())
            {
                if (stubs >= StubMinCount) return;
                CountStubs(d, archiveDepth, ref stubs);
            }
        }

        private static bool IsStub(IArchiveFile f) => f switch
        {
            IArchiveBinaryFile b => b.UncompressedSize <= StubMaxBytes,
            RageArchiveResourceFileWrapper7 r => r.Size <= StubMaxBytes,
            _ => false,
        };

        public static void Swap(string gtaRoot, string sourcePath, string? backupPath, BackpackFileState current)
        {
            var target = ResolvePath(gtaRoot);
            var dir = Path.GetDirectoryName(target)
                      ?? throw new InvalidOperationException(Loc.T("error.patchday11ngNotResolved"));
            Directory.CreateDirectory(dir);

            if (!string.IsNullOrWhiteSpace(backupPath)
                && current != BackpackFileState.Removed
                && current != BackpackFileState.Missing
                && !File.Exists(backupPath)
                && File.Exists(target))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                File.Copy(target, backupPath, overwrite: false);
            }

            var tmp = target + ".mgnew";
            try
            {
                File.Copy(sourcePath, tmp, overwrite: true);
                if (File.Exists(target)) File.Delete(target);
                File.Move(tmp, target);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch {}
            }
        }

        public static string Sha256File(string path)
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }
    }
}
