using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public sealed class SupabaseReduxCatalogRepository : IReduxCatalogRepository
{
    private readonly SupabaseClient _sb;
    private readonly IAdminConfigService _adminConfig;

    public SupabaseReduxCatalogRepository(SupabaseClient sb, IAdminConfigService adminConfig)
    {
        _sb = sb;
        _adminConfig = adminConfig;
    }

    private async Task<string> ServiceKeyAsync()
    {
        var key = (await _adminConfig.GetAsync()).SupabaseServiceKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "Не задан Supabase Service Role Key в Admin → Настройки (запись каталога заблокирована RLS для anon).");
        return key;
    }

    public async Task<List<ReduxItem>> ListAsync(ReduxFilter? filter = null)
    {

        var rows = await _sb.SelectAsync<Row>("redux_items", "select=*&order=uploaded_at.desc");
        IEnumerable<Row> q = rows;

        if (!string.IsNullOrWhiteSpace(filter?.SearchText))
        {
            var t = filter.SearchText.Trim().ToLowerInvariant();
            q = q.Where(x => x.Name.ToLowerInvariant().Contains(t)
                          || x.Author.ToLowerInvariant().Contains(t));
        }
        if (!string.IsNullOrWhiteSpace(filter?.Server))
            q = q.Where(x => x.SupportedServers != null && x.SupportedServers.Contains(filter.Server));
        if (!string.IsNullOrWhiteSpace(filter?.Status))
            q = q.Where(x => x.Status == filter.Status);

        return q.Select(ToItem).ToList();
    }

    public async Task<ReduxItem?> GetByIdAsync(string id)
    {
        var row = await _sb.SelectOneAsync<Row>(
            "redux_items",
            $"id=eq.{Uri.EscapeDataString(id)}&select=*");
        return row is null ? null : ToItem(row);
    }

    public async Task<ReduxItem?> FindByPatchSha256Async(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return null;
        var row = await _sb.SelectOneAsync<Row>(
            "redux_items",
            $"patch_sha256=eq.{Uri.EscapeDataString(sha256)}&select=*");
        return row is null ? null : ToItem(row);
    }

    public async Task AddAsync(ReduxItem item)
        => await _sb.UpsertWithServiceRoleAsync("redux_items", ToRow(item), await ServiceKeyAsync());

    public async Task UpdateAsync(string id, Action<ReduxItem> update)
    {
        var item = await GetByIdAsync(id);
        if (item is null) return;
        update(item);
        await _sb.UpsertWithServiceRoleAsync("redux_items", ToRow(item), await ServiceKeyAsync());
    }

    public async Task DeleteAsync(string id)
        => await _sb.DeleteWithServiceRoleAsync("redux_items", $"id=eq.{Uri.EscapeDataString(id)}", await ServiceKeyAsync());

    private static ReduxItem ToItem(Row r) => new()
    {
        Id               = r.Id,
        Name             = r.Name,
        Author           = r.Author,
        AuthorLink       = r.AuthorLink,
        Description      = r.Description,
        VideoUrl         = r.VideoUrl,
        PreviewUrl       = r.PreviewUrl,
        GalleryUrls      = r.GalleryUrls      ?? new(),
        R2Urls           = r.R2Urls,
        PatchSizeBytes   = r.PatchSizeBytes,
        PatchSha256      = r.PatchSha256      ?? string.Empty,
        TargetGtaVersion = r.TargetGtaVersion,
        SupportedServers = r.SupportedServers ?? new(),
        IsVerified       = r.IsVerified,
        Components       = r.Components       ?? new(),
        ComponentScreenshots = r.ComponentScreenshots ?? new(),
        UploadedAt       = r.UploadedAt,
        UploadedBy       = r.UploadedBy,
        Status           = r.Status,
        ViewerPriority   = r.ViewerPriority,
        DownloadCount    = r.DownloadCount,
        TagNew           = r.TagNew,
        TagBest          = r.TagBest,
        ArmorStandaloneInstallHidden = r.ArmorStandaloneInstallHidden,
    };

    private static Row ToRow(ReduxItem i) => new()
    {
        Id               = i.Id,
        Name             = i.Name,
        Author           = i.Author,
        AuthorLink       = i.AuthorLink,
        Description      = i.Description,
        VideoUrl         = i.VideoUrl,
        PreviewUrl       = i.PreviewUrl,
        GalleryUrls      = i.GalleryUrls,
        R2Urls           = i.R2Urls,
        PatchSizeBytes   = i.PatchSizeBytes,
        PatchSha256      = i.PatchSha256,
        TargetGtaVersion = i.TargetGtaVersion,
        SupportedServers = i.SupportedServers,
        IsVerified       = i.IsVerified,
        Components       = i.Components,
        ComponentScreenshots = i.ComponentScreenshots,
        UploadedAt       = i.UploadedAt,
        UploadedBy       = i.UploadedBy,
        Status           = i.Status,
        ViewerPriority   = i.ViewerPriority,
        DownloadCount    = i.DownloadCount,
        TagNew           = i.TagNew,
        TagBest          = i.TagBest,
        ArmorStandaloneInstallHidden = i.ArmorStandaloneInstallHidden,
    };

    private sealed class Row
    {
        public string                                   Id               { get; set; } = string.Empty;
        public string                                   Name             { get; set; } = string.Empty;
        public string                                   Author           { get; set; } = string.Empty;
        public string                                   AuthorLink       { get; set; } = string.Empty;
        public string                                   Description      { get; set; } = string.Empty;
        public string                                   VideoUrl         { get; set; } = string.Empty;
        public string                                   PreviewUrl       { get; set; } = string.Empty;
        public List<string>?                            GalleryUrls      { get; set; }
        public R2UrlsLocal?                             R2Urls           { get; set; }
        public long                                     PatchSizeBytes   { get; set; }
        public string?                                  PatchSha256      { get; set; }
        public string                                   TargetGtaVersion { get; set; } = string.Empty;
        public List<string>?                            SupportedServers { get; set; }
        public bool                                     IsVerified       { get; set; }
        public Dictionary<string, ReduxComponentInfo>?  Components       { get; set; }
        public Dictionary<string, string>?              ComponentScreenshots { get; set; }
        public DateTime                                 UploadedAt       { get; set; }
        public string                                   UploadedBy       { get; set; } = string.Empty;
        public string                                   Status           { get; set; } = "published";
        public int                                      ViewerPriority   { get; set; }
        public long                                     DownloadCount    { get; set; }
        public bool                                     TagNew           { get; set; }
        public bool                                     TagBest          { get; set; }
        public bool                                     ArmorStandaloneInstallHidden { get; set; }
    }
}
