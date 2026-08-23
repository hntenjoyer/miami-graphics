using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiamiGraphics.Core.Update
{
    public sealed class AppManifestFile
    {
        [JsonPropertyName("path")]   public string Path   { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("size")]   public long   Size   { get; set; }
    }

    public sealed class AppManifest
    {
        [JsonPropertyName("version")]     public string Version     { get; set; } = "";
        [JsonPropertyName("blobBaseUrl")] public string BlobBaseUrl { get; set; } = "";
        [JsonPropertyName("files")]       public List<AppManifestFile> Files { get; set; } = new();

        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public string ToJson() => JsonSerializer.Serialize(this, JsonOpts);

        public static AppManifest? FromJson(string json)
        {
            try { return JsonSerializer.Deserialize<AppManifest>(json, JsonOpts); }
            catch { return null; }
        }

        public static AppManifest? Load(string path)
        {
            try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : null; }
            catch { return null; }
        }

        public void Save(string path)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson());
        }

        public string BlobUrl(AppManifestFile f) => $"{BlobBaseUrl.TrimEnd('/')}/{f.Sha256.ToLowerInvariant()}";

        public static readonly string[] UntrackedPrefixes =
        {
            "installed_manifest.json",
            "payload.tmp.zip",
        };

        public static readonly string[] UntrackedExtensions = { ".log", ".tmp" };

        private static bool IsUntracked(string relPath)
        {
            foreach (var p in UntrackedPrefixes)
                if (relPath.Equals(p, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var e in UntrackedExtensions)
                if (relPath.EndsWith(e, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string NormalizePath(string path) =>
            path.Replace('\\', '/').TrimStart('/');

        public static AppManifest ComputeFromDirectory(string appDir, string version = "", string blobBaseUrl = "")
        {
            var m = new AppManifest { Version = version, BlobBaseUrl = blobBaseUrl };
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories); }
            catch { return m; }

            foreach (var full in files)
            {
                string rel;
                try { rel = NormalizePath(Path.GetRelativePath(appDir, full)); }
                catch { continue; }
                if (IsUntracked(rel)) continue;
                try
                {
                    var fi = new FileInfo(full);
                    m.Files.Add(new AppManifestFile { Path = rel, Sha256 = Sha256File(full), Size = fi.Length });
                }
                catch {}
            }
            m.Files.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
            return m;
        }

        public static string Sha256File(string path)
        {
            using var sha = SHA256.Create();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }

        public static ManifestDiff Diff(AppManifest? installed, AppManifest target)
        {
            var have = new Dictionary<string, AppManifestFile>(StringComparer.OrdinalIgnoreCase);
            if (installed != null)
                foreach (var f in installed.Files) have[NormalizePath(f.Path)] = f;

            var want = new Dictionary<string, AppManifestFile>(StringComparer.OrdinalIgnoreCase);
            var toDownload = new List<AppManifestFile>();
            foreach (var f in target.Files)
            {
                var key = NormalizePath(f.Path);
                want[key] = f;
                if (!have.TryGetValue(key, out var cur) ||
                    !string.Equals(cur.Sha256, f.Sha256, StringComparison.OrdinalIgnoreCase))
                    toDownload.Add(f);
            }

            var toDelete = have.Keys.Where(k => !want.ContainsKey(k))
                                    .Select(NormalizePath)
                                    .ToList();

            return new ManifestDiff(toDownload, toDelete);
        }
    }

    public sealed class ManifestDiff
    {
        public IReadOnlyList<AppManifestFile> ToDownload { get; }
        public IReadOnlyList<string>          ToDelete   { get; }
        public bool IsEmpty => ToDownload.Count == 0 && ToDelete.Count == 0;
        public long DownloadBytes => ToDownload.Sum(f => f.Size);

        public ManifestDiff(IReadOnlyList<AppManifestFile> toDownload, IReadOnlyList<string> toDelete)
        {
            ToDownload = toDownload;
            ToDelete   = toDelete;
        }
    }
}
