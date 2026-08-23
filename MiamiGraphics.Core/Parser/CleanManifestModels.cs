using System;
using System.Collections.Generic;

namespace MiamiGraphics.Core.Parser
{
    public class CleanUpdateManifest
    {
        public int FormatVersion { get; set; } = 1;
        public string ExeVersion { get; set; }

        public bool StatsOnly { get; set; }

        public string SourceFileSha256 { get; set; }
        public long SourceFileSize { get; set; }

        public int LeafCount { get; set; }
        public int NestedRpfCount { get; set; }
        public long TotalRealBytes { get; set; }
        public long TotalStoredBytes { get; set; }

        public string BlobBaseUrl { get; set; }

        public List<CleanManifestEntry> Entries { get; set; } = new List<CleanManifestEntry>();
    }

    public class CleanManifestEntry
    {
        public string Path { get; set; }

        public string Name { get; set; }

        public string Sha256 { get; set; }

        public long RealSize { get; set; }

        public long StoredSize { get; set; }

        public long BlobSize { get; set; }

        public string BlobEncoding { get; set; }

        public bool IsResource { get; set; }

        public bool IsNestedRpf { get; set; }
    }
}
