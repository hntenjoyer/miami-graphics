namespace MiamiGraphics.Shell.Admin;

public sealed class ReduxItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string AuthorLink { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string VideoUrl { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public List<string> GalleryUrls { get; set; } = new();
    public R2UrlsLocal? R2Urls { get; set; }
    public long PatchSizeBytes { get; set; }
    public string PatchSha256 { get; set; } = string.Empty;
    public string TargetGtaVersion { get; set; } = string.Empty;
    public List<string> SupportedServers { get; set; } = new();
    public bool IsVerified { get; set; }
    public bool TagNew  { get; set; }
    public bool TagBest { get; set; }
    public bool ArmorStandaloneInstallHidden { get; set; }
    public Dictionary<string, ReduxComponentInfo> Components { get; set; } = new();
    public Dictionary<string, string> ComponentScreenshots { get; set; } = new();
    public DateTime UploadedAt { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public string Status { get; set; } = "published";
    public int ViewerPriority { get; set; }
    public long DownloadCount { get; set; }
}

public sealed class R2UrlsLocal
{
    public string? Patch { get; set; }
    public Dictionary<string, string> Components { get; set; } = new();
    public string? Manifest { get; set; }
    public string? ComponentMap { get; set; }
    public string? ContentInfo { get; set; }
}

public sealed class ReduxComponentInfo
{
    public bool IsFound { get; set; }
    public string SourceRpf { get; set; } = string.Empty;
    public List<string> InternalPaths { get; set; } = new();
    public List<string> Flags { get; set; } = new();
}

public sealed class ReduxFilter
{
    public string? SearchText { get; set; }
    public string? Server { get; set; }
    public string? Status { get; set; }
}
