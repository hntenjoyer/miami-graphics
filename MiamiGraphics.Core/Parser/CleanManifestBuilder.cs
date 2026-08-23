using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Parser
{
    public class CleanManifestBuildResult
    {
        public CleanUpdateManifest Manifest { get; set; }
        public DateTime GeneratedAt { get; set; }
        public string ManifestPath { get; set; }
        public string ManifestSha256 { get; set; }
        public int UniqueBlobs { get; set; }
        public long BlobBytes { get; set; }
        public int DuplicateLeaves { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    public class CleanManifestBuilder
    {
        public CleanManifestBuildResult Build(
            string rpfPath, string exeVersion, string outDir,
            bool writeBlobs = true, string logicalName = "update.rpf", Action<string> log = null)
        {
            log ??= _ => { };
            var sw = Stopwatch.StartNew();

            Directory.CreateDirectory(outDir);
            string blobDir = Path.Combine(outDir, "blobs");
            if (writeBlobs)
            {
                if (Directory.Exists(blobDir) && Directory.EnumerateFileSystemEntries(blobDir).Any())
                    throw new InvalidOperationException(
                        $"blobs/ не пуст: {blobDir}. Генератор пишет только в чистую папку - удали её или укажи другой outDir.");
                Directory.CreateDirectory(blobDir);
            }

            var manifest = new CleanUpdateManifest
            {
                ExeVersion = exeVersion,
                StatsOnly = !writeBlobs,
                SourceFileSize = new FileInfo(rpfPath).Length,
            };

            log("SHA-256 исходного файла…");
            manifest.SourceFileSha256 = Sha256File(rpfPath);

            var blobIndex = new Dictionary<string, (long Size, string Encoding)>(StringComparer.Ordinal);
            int dupLeaves = 0;
            long processedReal = 0;

            using (var fs = new FileStream(rpfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var arc = RageArchiveWrapper7.Open(fs, logicalName, leaveOpen: false))
            {
                WalkDir(arc.Root, "");
            }

            manifest.BlobBaseUrl = null;

            string manifestPath = Path.Combine(outDir, writeBlobs ? "update_manifest.json" : "update_manifest.noblobs.json");
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(manifest, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            });
            File.WriteAllBytes(manifestPath, json);

            long blobBytes = 0;
            foreach (var v in blobIndex.Values) blobBytes += v.Size;

            return new CleanManifestBuildResult
            {
                Manifest = manifest,
                GeneratedAt = DateTime.UtcNow,
                ManifestPath = manifestPath,
                ManifestSha256 = Sha256Hex(json),
                UniqueBlobs = blobIndex.Count,
                BlobBytes = blobBytes,
                DuplicateLeaves = dupLeaves,
                Elapsed = sw.Elapsed,
            };

            void WalkDir(IArchiveDirectory dir, string prefix)
            {
                foreach (var file in dir.GetFiles())
                {
                    string path = prefix + file.Name.ToLowerInvariant();
                    if (file is IArchiveBinaryFile bin && file.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                        AddNestedRpf(path, bin);
                    else
                        AddLeaf(path, file);
                }
                foreach (var sub in dir.GetDirectories())
                    WalkDir(sub, prefix + sub.Name.ToLowerInvariant() + "/");
            }

            void AddLeaf(string path, IArchiveFile file)
            {
                byte[] real = ReadRealStrict(path, file);
                string sha = Sha256Hex(real);

                var entry = new CleanManifestEntry
                {
                    Path = path,
                    Name = file.Name,
                    Sha256 = sha,
                    RealSize = real.Length,
                    StoredSize = StoredSize(file),
                    IsResource = file is IArchiveResourceFile,
                };

                if (!blobIndex.TryGetValue(sha, out var blob))
                {
                    blob = writeBlobs ? WriteBlob(sha, real) : (0, null);
                    blobIndex[sha] = blob;
                }
                else dupLeaves++;

                entry.BlobSize = blob.Size;
                entry.BlobEncoding = blob.Encoding;

                manifest.Entries.Add(entry);
                manifest.LeafCount++;
                manifest.TotalRealBytes += entry.RealSize;
                manifest.TotalStoredBytes += entry.StoredSize;

                processedReal += entry.RealSize;
                if (manifest.LeafCount % 2000 == 0)
                    log($"{manifest.LeafCount:N0} листов, {processedReal / (1024 * 1024):N0} МБ real, {sw.Elapsed.TotalSeconds:F0}с…");
            }

            void AddNestedRpf(string path, IArchiveBinaryFile bin)
            {
                byte[] container = ReadRealStrict(path, bin);
                manifest.Entries.Add(new CleanManifestEntry
                {
                    Path = path,
                    Name = bin.Name,
                    Sha256 = Sha256Hex(container),
                    RealSize = container.Length,
                    StoredSize = StoredSize(bin),
                    IsNestedRpf = true,
                });
                manifest.NestedRpfCount++;

                using var ms = new MemoryStream(container, writable: false);
                RageArchiveWrapper7 nested;
                try { nested = RageArchiveWrapper7.Open(ms, bin.Name, leaveOpen: true); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Не открылся вложенный rpf чистого апдейта: {path}", ex);
                }
                using (nested)
                    WalkDir(nested.Root, path + "/");
            }

            (long Size, string Encoding) WriteBlob(string sha, byte[] real)
            {
                byte[] payload;
                string encoding;
                using (var comp = new MemoryStream())
                {
                    using (var def = new DeflateStream(comp, CompressionLevel.Optimal, leaveOpen: true))
                        def.Write(real, 0, real.Length);
                    if (comp.Length < real.Length)
                    {
                        payload = comp.ToArray();
                        encoding = "deflate";
                    }
                    else
                    {
                        payload = real;
                        encoding = "raw";
                    }
                }
                string blobPath = Path.Combine(blobDir, sha);
                string tmpPath = blobPath + ".tmp";
                File.WriteAllBytes(tmpPath, payload);
                File.Move(tmpPath, blobPath, overwrite: true);
                return (payload.LongLength, encoding);
            }

            byte[] ReadRealStrict(string path, IArchiveFile file)
            {
                try { return RpfRealBytes.Get(file); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Нечитаемая запись чистого апдейта: {path}", ex);
                }
            }
        }

        private static long StoredSize(IArchiveFile file) => file.Size;

        private static string Sha256Hex(byte[] data)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
        }

        private static string Sha256File(string path)
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }
    }
}
