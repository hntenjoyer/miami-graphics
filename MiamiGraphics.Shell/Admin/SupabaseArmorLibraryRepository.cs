using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public sealed class SupabaseArmorLibraryRepository
{
    private readonly SupabaseClient _sb;
    private readonly IAdminConfigService _adminConfig;
    public SupabaseArmorLibraryRepository(SupabaseClient sb, IAdminConfigService adminConfig)
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

    public async Task<List<ArmorLibraryItem>> ListAsync(string? statusFilter = "published")
    {
        var queryParts = new List<string>
        {
            "select=*",
            "order=viewer_priority.desc,uploaded_at.desc",
        };
        if (!string.IsNullOrWhiteSpace(statusFilter))
            queryParts.Add($"status=eq.{Uri.EscapeDataString(statusFilter)}");

        var rows = await _sb.SelectAsync<Row>("armor_library", string.Join("&", queryParts));
        return rows.Select(ToItem).ToList();
    }

    public async Task<ArmorLibraryItem?> GetByIdAsync(string id)
    {
        var row = await _sb.SelectOneAsync<Row>(
            "armor_library",
            $"id=eq.{Uri.EscapeDataString(id)}&select=*");
        return row is null ? null : ToItem(row);
    }

    public async Task UpsertAsync(ArmorLibraryItem item)
        => await _sb.UpsertWithServiceRoleAsync("armor_library", ToRow(item), await ServiceKeyAsync());

    public async Task DeleteAsync(string id)
        => await _sb.DeleteWithServiceRoleAsync("armor_library", $"id=eq.{Uri.EscapeDataString(id)}", await ServiceKeyAsync());

    public async Task<long> IncrementDownloadsAsync(string id)
    {
        try
        {
            var row = await _sb.RpcSingleAsync<CounterRow>(
                "armor_library_increment_downloads",
                new { p_id = id });
            return row?.NewTotal ?? 0;
        }
        catch { return 0; }
    }

    private sealed class CounterRow
    {
        public long NewTotal { get; set; }
    }

    private static ArmorLibraryItem ToItem(Row r) => new()
    {
        Id                   = r.Id,
        Name                 = r.Name,
        Author               = r.Author      ?? string.Empty,
        AuthorLink           = r.AuthorLink  ?? string.Empty,
        Description          = r.Description ?? string.Empty,
        GlbUrl               = r.GlbUrl      ?? string.Empty,
        ArmorRpfUrl          = r.ArmorRpfUrl ?? string.Empty,
        InternalPath         = r.InternalPath ?? string.Empty,
        UploadedBy           = r.UploadedBy ?? string.Empty,
        UploadedAt           = r.UploadedAt,
        Status               = r.Status     ?? "published",
        IsVerified           = r.IsVerified,
        DownloadCount        = r.DownloadCount,
        ViewerPriority       = r.ViewerPriority,
        SupportedServers     = r.SupportedServers ?? new List<string> { "majestic" },
        ArmorRpfUrlMajestic  = r.ArmorRpfUrlMajestic,
        InternalPathMajestic = r.InternalPathMajestic,
        ArmorRpfUrlGta5Rp    = r.ArmorRpfUrlGta5Rp,
        InternalPathGta5Rp   = r.InternalPathGta5Rp,
        ArmorRpfSha256          = r.ArmorRpfSha256,
        ArmorRpfSha256Majestic  = r.ArmorRpfSha256Majestic,
        ArmorRpfSha256Gta5Rp    = r.ArmorRpfSha256Gta5Rp,
        PreviewUrl           = r.PreviewUrl,
        PreviewVariants      = r.PreviewVariants,
        HasMale              = r.HasMale,
        HasFemale            = r.HasFemale,
    };

    private static Row ToRow(ArmorLibraryItem i) => new()
    {
        Id                   = i.Id,
        Name                 = i.Name,
        Author               = i.Author      ?? string.Empty,
        AuthorLink           = i.AuthorLink  ?? string.Empty,
        Description          = i.Description ?? string.Empty,
        GlbUrl               = i.GlbUrl      ?? string.Empty,
        ArmorRpfUrl          = i.ArmorRpfUrl ?? string.Empty,
        InternalPath         = i.InternalPath ?? string.Empty,
        UploadedBy           = string.IsNullOrEmpty(i.UploadedBy) ? null : i.UploadedBy,
        UploadedAt           = i.UploadedAt == default ? DateTime.UtcNow : i.UploadedAt,
        Status               = i.Status,
        IsVerified           = i.IsVerified,
        DownloadCount        = i.DownloadCount,
        ViewerPriority       = i.ViewerPriority,
        SupportedServers     = i.SupportedServers ?? new List<string> { "majestic" },
        ArmorRpfUrlMajestic  = i.ArmorRpfUrlMajestic,
        InternalPathMajestic = i.InternalPathMajestic,
        ArmorRpfUrlGta5Rp    = i.ArmorRpfUrlGta5Rp,
        InternalPathGta5Rp   = i.InternalPathGta5Rp,
        ArmorRpfSha256          = string.IsNullOrWhiteSpace(i.ArmorRpfSha256)         ? null : i.ArmorRpfSha256,
        ArmorRpfSha256Majestic  = string.IsNullOrWhiteSpace(i.ArmorRpfSha256Majestic) ? null : i.ArmorRpfSha256Majestic,
        ArmorRpfSha256Gta5Rp    = string.IsNullOrWhiteSpace(i.ArmorRpfSha256Gta5Rp)   ? null : i.ArmorRpfSha256Gta5Rp,
        PreviewUrl           = i.PreviewUrl,
        PreviewVariants      = i.PreviewVariants,
        HasMale              = i.HasMale,
        HasFemale            = i.HasFemale,
    };

    private sealed class Row
    {
        public string Id                    { get; set; } = string.Empty;
        public string Name                  { get; set; } = string.Empty;
        public string? Author               { get; set; }
        public string? AuthorLink           { get; set; }
        public string? Description          { get; set; }
        public string? GlbUrl               { get; set; }
        public string? ArmorRpfUrl          { get; set; }
        public string? InternalPath         { get; set; }
        public string? UploadedBy           { get; set; }
        public DateTime UploadedAt          { get; set; }
        public string? Status               { get; set; }
        public bool   IsVerified            { get; set; }
        public long   DownloadCount         { get; set; }
        public int    ViewerPriority        { get; set; }
        public List<string>? SupportedServers     { get; set; }
        public string? ArmorRpfUrlMajestic  { get; set; }
        public string? InternalPathMajestic { get; set; }
        public string? ArmorRpfUrlGta5Rp    { get; set; }
        public string? InternalPathGta5Rp   { get; set; }
        public string? ArmorRpfSha256          { get; set; }
        public string? ArmorRpfSha256Majestic  { get; set; }
        public string? ArmorRpfSha256Gta5Rp    { get; set; }
        public string? PreviewUrl           { get; set; }
        public List<string>? PreviewVariants { get; set; }
        public bool HasMale   { get; set; } = true;
        public bool HasFemale { get; set; } = true;
    }
}

public sealed class ArmorLibraryItem
{
    public string Id                  { get; set; } = string.Empty;
    public string Name                { get; set; } = string.Empty;
    public string Author              { get; set; } = string.Empty;
    public string AuthorLink          { get; set; } = string.Empty;
    public string Description         { get; set; } = string.Empty;
    public string GlbUrl              { get; set; } = string.Empty;
    public string ArmorRpfUrl         { get; set; } = string.Empty;
    public string InternalPath        { get; set; } = string.Empty;
    public string UploadedBy          { get; set; } = string.Empty;
    public DateTime UploadedAt        { get; set; } = DateTime.UtcNow;
    public string Status              { get; set; } = "published";
    public bool   IsVerified          { get; set; }
    public long   DownloadCount       { get; set; }
    public int    ViewerPriority      { get; set; }
    public List<string>? SupportedServers     { get; set; }
    public string? ArmorRpfUrlMajestic  { get; set; }
    public string? InternalPathMajestic { get; set; }
    public string? ArmorRpfUrlGta5Rp    { get; set; }
    public string? InternalPathGta5Rp   { get; set; }

    public string? ArmorRpfSha256          { get; set; }
    public string? ArmorRpfSha256Majestic  { get; set; }
    public string? ArmorRpfSha256Gta5Rp    { get; set; }

    public string? PreviewUrl           { get; set; }

    public List<string>? PreviewVariants { get; set; }

    public bool HasMale   { get; set; } = true;
    public bool HasFemale { get; set; } = true;
}
