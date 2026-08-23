using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public sealed class SupabaseBigMapRepository
{
    private readonly SupabaseClient _sb;
    private readonly IAdminConfigService _adminConfig;
    public SupabaseBigMapRepository(SupabaseClient sb, IAdminConfigService adminConfig)
    {
        _sb = sb;
        _adminConfig = adminConfig;
    }

    private async Task<string> ServiceKeyAsync()
    {
        var key = (await _adminConfig.GetAsync()).SupabaseServiceKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "Не задан Supabase Service Role Key в Admin → Настройки (запись каталога заблокирована для anon).");
        return key;
    }

    public async Task<List<BigMapItem>> ListAsync(string? statusFilter = "published")
    {
        var queryParts = new List<string>
        {
            "select=*",
            "order=viewer_priority.desc,uploaded_at.desc",
        };
        if (!string.IsNullOrWhiteSpace(statusFilter))
            queryParts.Add($"status=eq.{Uri.EscapeDataString(statusFilter)}");

        var rows = await _sb.SelectAsync<Row>("big_maps", string.Join("&", queryParts));
        return rows.Select(ToItem).ToList();
    }

    public async Task<BigMapItem?> GetByIdAsync(string id)
    {
        var row = await _sb.SelectOneAsync<Row>(
            "big_maps",
            $"id=eq.{Uri.EscapeDataString(id)}&select=*");
        return row is null ? null : ToItem(row);
    }

    public async Task UpsertAsync(BigMapItem item)
        => await _sb.UpsertWithServiceRoleAsync("big_maps", ToRow(item), await ServiceKeyAsync());

    public async Task DeleteAsync(string id)
        => await _sb.DeleteWithServiceRoleAsync("big_maps", $"id=eq.{Uri.EscapeDataString(id)}", await ServiceKeyAsync());

    public async Task<long> IncrementDownloadsAsync(string id)
    {
        try
        {
            var row = await _sb.RpcSingleAsync<CounterRow>(
                "big_map_increment_downloads",
                new { p_id = id });
            return row?.NewTotal ?? 0;
        }
        catch { return 0; }
    }

    private sealed class CounterRow
    {
        public long NewTotal { get; set; }
    }

    private static BigMapItem ToItem(Row r) => new()
    {
        Id               = r.Id,
        Name             = r.Name,
        Author           = r.Author       ?? string.Empty,
        AuthorLink       = r.AuthorLink   ?? string.Empty,
        Description      = r.Description  ?? string.Empty,
        PreviewUrl       = r.PreviewUrl   ?? string.Empty,
        GalleryUrls      = r.GalleryUrls  ?? new List<string>(),
        VideoUrl         = r.VideoUrl     ?? string.Empty,
        SupportedServers = r.SupportedServers ?? new List<string> { "majestic" },
        PackageUrl       = r.PackageUrl   ?? string.Empty,
        PackageSha256    = r.PackageSha256 ?? string.Empty,
        SizeBytes        = r.SizeBytes,
        PackFormat       = r.PackFormat   ?? "A",
        UploadedBy       = r.UploadedBy   ?? string.Empty,
        UploadedAt       = r.UploadedAt,
        Status           = r.Status       ?? "published",
        IsVerified       = r.IsVerified,
        DownloadCount    = r.DownloadCount,
        ViewerPriority   = r.ViewerPriority,
    };

    private static Row ToRow(BigMapItem i) => new()
    {
        Id               = i.Id,
        Name             = i.Name,
        Author           = i.Author,
        AuthorLink       = i.AuthorLink,
        Description      = i.Description,
        PreviewUrl       = i.PreviewUrl,
        GalleryUrls      = i.GalleryUrls,
        VideoUrl         = i.VideoUrl,
        SupportedServers = i.SupportedServers,
        PackageUrl       = i.PackageUrl,
        PackageSha256    = i.PackageSha256,
        SizeBytes        = i.SizeBytes,
        PackFormat       = i.PackFormat,
        UploadedBy       = string.IsNullOrEmpty(i.UploadedBy) ? null : i.UploadedBy,
        UploadedAt       = i.UploadedAt == default ? DateTime.UtcNow : i.UploadedAt,
        Status           = i.Status,
        IsVerified       = i.IsVerified,
        DownloadCount    = i.DownloadCount,
        ViewerPriority   = i.ViewerPriority,
    };

    private sealed class Row
    {
        public string Id                    { get; set; } = string.Empty;
        public string Name                  { get; set; } = string.Empty;
        public string? Author               { get; set; }
        public string? AuthorLink           { get; set; }
        public string? Description          { get; set; }
        public string? PreviewUrl           { get; set; }
        public List<string>? GalleryUrls    { get; set; }
        public string? VideoUrl             { get; set; }
        public List<string>? SupportedServers { get; set; }
        public string? PackageUrl           { get; set; }
        public string? PackageSha256        { get; set; }
        public long   SizeBytes             { get; set; }
        public string? PackFormat           { get; set; }
        public string? UploadedBy           { get; set; }
        public DateTime UploadedAt          { get; set; }
        public string? Status               { get; set; }
        public bool   IsVerified            { get; set; }
        public long   DownloadCount         { get; set; }
        public int    ViewerPriority        { get; set; }
    }
}

public sealed class BigMapItem
{
    public string Id                     { get; set; } = string.Empty;
    public string Name                   { get; set; } = string.Empty;
    public string Author                 { get; set; } = string.Empty;
    public string AuthorLink             { get; set; } = string.Empty;
    public string Description            { get; set; } = string.Empty;
    public string PreviewUrl             { get; set; } = string.Empty;
    public List<string> GalleryUrls      { get; set; } = new();
    public string VideoUrl               { get; set; } = string.Empty;
    public List<string> SupportedServers { get; set; } = new() { "majestic" };
    public string PackageUrl             { get; set; } = string.Empty;
    public string PackageSha256          { get; set; } = string.Empty;
    public long SizeBytes                { get; set; }
    public string PackFormat             { get; set; } = "A";
    public string UploadedBy             { get; set; } = string.Empty;
    public DateTime UploadedAt           { get; set; }
    public string Status                 { get; set; } = "published";
    public bool IsVerified               { get; set; }
    public long DownloadCount            { get; set; }
    public int ViewerPriority            { get; set; }
}
