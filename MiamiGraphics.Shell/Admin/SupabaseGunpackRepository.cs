using System.Text.Json.Serialization;
using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public sealed class SupabaseGunpackRepository : IGunpackRepository
{
    private readonly SupabaseClient _sb;

    public SupabaseGunpackRepository(SupabaseClient sb) => _sb = sb;

    public async Task<List<GunpackItem>> ListAsync(GunpackFilter? filter = null)
    {
        var rows = await _sb.SelectAsync<PackRow>(
            "gunpacks",
            "select=*&order=viewer_priority.desc,uploaded_at.desc");

        IEnumerable<PackRow> q = rows;
        if (!string.IsNullOrWhiteSpace(filter?.SearchText))
        {
            var t = filter.SearchText.Trim().ToLowerInvariant();
            q = q.Where(x => x.Name.ToLowerInvariant().Contains(t)
                          || (x.Author ?? string.Empty).ToLowerInvariant().Contains(t));
        }
        if (!string.IsNullOrWhiteSpace(filter?.Status))
            q = q.Where(x => x.Status == filter.Status);

        return q.Select(ToItem).ToList();
    }

    public async Task<GunpackItem?> GetByIdAsync(string id)
    {
        var row = await _sb.SelectOneAsync<PackRow>(
            "gunpacks",
            $"id=eq.{Uri.EscapeDataString(id)}&select=*");
        return row is null ? null : ToItem(row);
    }

    public async Task<GunpackItem?> FindByWeaponsRpfSha256Async(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return null;
        var row = await _sb.SelectOneAsync<PackRow>(
            "gunpacks",
            $"weapons_rpf_sha256=eq.{Uri.EscapeDataString(sha256)}&select=*");
        return row is null ? null : ToItem(row);
    }

    public Task AddAsync(GunpackItem item)
        => _sb.UpsertAsync("gunpacks", ToRow(item));

    public async Task UpdateAsync(string id, Action<GunpackItem> update)
    {
        var item = await GetByIdAsync(id);
        if (item is null) return;
        update(item);
        item.UpdatedAt = DateTime.UtcNow;
        await _sb.UpsertAsync("gunpacks", ToRow(item));
    }

    public Task DeleteAsync(string id)
        => _sb.DeleteAsync("gunpacks", $"id=eq.{Uri.EscapeDataString(id)}");

    public async Task<long> IncrementDownloadsAsync(string id)
    {
        try
        {
            var row = await _sb.RpcSingleAsync<CounterRow>(
                "gunpack_increment_downloads",
                new { p_id = id });
            return row?.NewTotal ?? 0;
        }
        catch { return 0; }
    }

    private sealed class CounterRow
    {
        public long NewTotal { get; set; }
    }

    public async Task<List<GunpackGun>> ListGunsAsync(string gunpackId)
    {
        var rows = await _sb.SelectAsync<GunRow>(
            "gunpack_guns",
            $"gunpack_id=eq.{Uri.EscapeDataString(gunpackId)}&select=*&order=sort_order.asc,base_name.asc");
        return rows.Select(ToGun).ToList();
    }

    public async Task BulkInsertGunsAsync(IEnumerable<GunpackGun> guns)
    {

        var rows = guns.Select(ToGunRow).ToList();
        if (rows.Count == 0) return;
        await _sb.UpsertManyAsync("gunpack_guns", rows);
    }

    public async Task UpdateGunAsync(Guid gunId, Action<GunpackGun> update)
    {
        var row = await _sb.SelectOneAsync<GunRow>(
            "gunpack_guns",
            $"id=eq.{gunId}&select=*");
        if (row is null) return;
        var gun = ToGun(row);
        update(gun);
        await _sb.UpsertAsync("gunpack_guns", ToGunRow(gun));
    }

    public Task DeleteGunAsync(Guid gunId)
        => _sb.DeleteAsync("gunpack_guns", $"id=eq.{gunId}");

    public Task DeleteAllGunsForPackAsync(string gunpackId)
        => _sb.DeleteAsync("gunpack_guns", $"gunpack_id=eq.{Uri.EscapeDataString(gunpackId)}");

    public Task AddWithServiceRoleAsync(GunpackItem item, string serviceRoleKey)
        => _sb.UpsertWithServiceRoleAsync("gunpacks", ToRow(item), serviceRoleKey);

    public Task BulkUpsertGunsWithServiceRoleAsync(IEnumerable<GunpackGun> guns, string serviceRoleKey)
    {
        var rows = guns.Select(ToGunRow).ToList();
        if (rows.Count == 0) return Task.CompletedTask;
        return _sb.UpsertManyWithServiceRoleAsync("gunpack_guns", rows, serviceRoleKey);
    }

    public async Task DeleteAllGunsForPackWithServiceRoleAsync(string gunpackId, string serviceRoleKey)
    {
        await _sb.DeleteWithServiceRoleAsync(
            "gunpack_guns", $"gunpack_id=eq.{Uri.EscapeDataString(gunpackId)}", serviceRoleKey);
    }

    public async Task<List<GunpackVariant>> ListVariantsAsync(string gunpackId)
    {
        var rows = await _sb.SelectAsync<VariantRow>(
            "gunpack_variants",
            $"gunpack_id=eq.{Uri.EscapeDataString(gunpackId)}&select=*&order=sort_order.asc,created_at.asc");
        return rows.Select(ToVariant).ToList();
    }

    public async Task<GunpackVariant?> GetVariantByIdAsync(Guid variantId)
    {
        var row = await _sb.SelectOneAsync<VariantRow>(
            "gunpack_variants",
            $"id=eq.{variantId}&select=*");
        return row is null ? null : ToVariant(row);
    }

    public Task AddVariantAsync(GunpackVariant variant, string serviceRoleKey)
        => _sb.InsertWithServiceRoleAsync("gunpack_variants", ToVariantRow(variant), serviceRoleKey);

    public Task<int> PatchVariantAsync(Guid variantId, Dictionary<string, object?> patch, string serviceRoleKey)
    {
        if (patch.Count == 0) return Task.FromResult(0);
        patch["updated_at"] = DateTime.UtcNow;
        return _sb.UpdateWithServiceRoleAsync(
            "gunpack_variants", $"id=eq.{variantId}", patch, serviceRoleKey);
    }

    public async Task DeleteVariantAsync(Guid variantId, string serviceRoleKey)
    {
        await _sb.DeleteWithServiceRoleAsync(
            "gunpack_variants", $"id=eq.{variantId}", serviceRoleKey);
    }

    private static GunpackVariant ToVariant(VariantRow r) => new()
    {
        Id               = r.Id ?? Guid.Empty,
        GunpackId        = r.GunpackId.ToString(),
        Name             = r.Name,
        WeaponsRpfUrl    = r.WeaponsRpfUrl ?? string.Empty,
        WeaponsRpfSize   = r.WeaponsRpfSize,
        WeaponsRpfSha256 = r.WeaponsRpfSha256 ?? string.Empty,
        PackZipUrl       = r.PackZipUrl,
        PackZipSize      = r.PackZipSize,
        PackZipSha256    = r.PackZipSha256,
        ManifestUrl      = r.ManifestUrl,
        CoverUrl         = r.CoverUrl,
        IsDefault        = r.IsDefault,
        SortOrder        = r.SortOrder,
        CreatedAt        = r.CreatedAt ?? DateTime.MinValue,
        UpdatedAt        = r.UpdatedAt ?? DateTime.MinValue,
        GunPreviews      = r.GunPreviews,
    };

    private static VariantRow ToVariantRow(GunpackVariant v) => new()
    {
        Id               = v.Id == Guid.Empty ? null : v.Id,
        GunpackId        = Guid.Parse(v.GunpackId),
        Name             = v.Name,
        WeaponsRpfUrl    = v.WeaponsRpfUrl,
        WeaponsRpfSize   = v.WeaponsRpfSize,
        WeaponsRpfSha256 = v.WeaponsRpfSha256,
        PackZipUrl       = v.PackZipUrl,
        PackZipSize      = v.PackZipSize,
        PackZipSha256    = v.PackZipSha256,
        ManifestUrl      = v.ManifestUrl,
        CoverUrl         = v.CoverUrl,
        IsDefault        = v.IsDefault,
        SortOrder        = v.SortOrder,
        GunPreviews      = v.GunPreviews,
    };

    private sealed class VariantRow
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Guid?  Id                { get; set; }

        public Guid   GunpackId         { get; set; }
        public string Name              { get; set; } = "Default";

        public string? WeaponsRpfUrl    { get; set; }
        public long    WeaponsRpfSize   { get; set; }
        public string? WeaponsRpfSha256 { get; set; }

        public string? PackZipUrl       { get; set; }
        public long?   PackZipSize      { get; set; }
        public string? PackZipSha256    { get; set; }

        public string? ManifestUrl      { get; set; }
        public string? CoverUrl         { get; set; }

        public bool    IsDefault        { get; set; }
        public int     SortOrder        { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? CreatedAt       { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DateTime? UpdatedAt       { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, VariantGun>? GunPreviews { get; set; }
    }

    private static GunpackItem ToItem(PackRow r) => new()
    {
        Id               = r.Id,
        Name             = r.Name,
        Author           = r.Author       ?? string.Empty,
        AuthorLink       = r.AuthorLink   ?? string.Empty,
        Description      = r.Description  ?? string.Empty,
        WeaponsRpfUrl    = r.WeaponsRpfUrl,
        WeaponsRpfSize   = r.WeaponsRpfSize,
        WeaponsRpfSha256 = r.WeaponsRpfSha256,
        PackZipUrl       = r.PackZipUrl,
        PackZipSize      = r.PackZipSize,
        PackZipSha256    = r.PackZipSha256,
        ManifestUrl      = r.ManifestUrl,
        CoverKind        = r.CoverKind,
        CoverUrl         = r.CoverUrl,
        GalleryUrls      = r.GalleryUrls  ?? new(),
        Status           = r.Status,
        IsVerified       = r.IsVerified,
        ViewerPriority   = r.ViewerPriority,
        DownloadCount    = r.DownloadCount,
        UploadedAt       = r.UploadedAt,
        UploadedBy       = r.UploadedBy   ?? string.Empty,
        UpdatedAt        = r.UpdatedAt,
        Notes            = r.Notes,
    };

    private static PackRow ToRow(GunpackItem i) => new()
    {
        Id               = i.Id,
        Name             = i.Name,
        Author           = string.IsNullOrEmpty(i.Author)     ? null : i.Author,
        AuthorLink       = string.IsNullOrEmpty(i.AuthorLink) ? null : i.AuthorLink,
        Description      = string.IsNullOrEmpty(i.Description)? null : i.Description,
        WeaponsRpfUrl    = i.WeaponsRpfUrl,
        WeaponsRpfSize   = i.WeaponsRpfSize,
        WeaponsRpfSha256 = i.WeaponsRpfSha256,
        PackZipUrl       = i.PackZipUrl,
        PackZipSize      = i.PackZipSize,
        PackZipSha256    = i.PackZipSha256,
        ManifestUrl      = i.ManifestUrl,
        CoverKind        = i.CoverKind,
        CoverUrl         = i.CoverUrl,
        GalleryUrls      = i.GalleryUrls,
        Status           = i.Status,
        IsVerified       = i.IsVerified,
        ViewerPriority   = i.ViewerPriority,
        DownloadCount    = i.DownloadCount,
        UploadedAt       = i.UploadedAt,
        UploadedBy       = string.IsNullOrEmpty(i.UploadedBy) ? null : i.UploadedBy,
        UpdatedAt        = i.UpdatedAt,
        Notes            = i.Notes,
    };

    private static GunpackGun ToGun(GunRow r) => new()
    {
        Id           = r.Id ?? Guid.Empty,
        GunpackId    = r.GunpackId,
        BaseName     = r.BaseName,
        WeaponPrefix = r.WeaponPrefix,
        Category     = r.Category,
        DisplayName  = r.DisplayName,
        GlbUrl       = r.GlbUrl,
        PreviewUrl   = r.PreviewUrl,
        Files        = r.Files ?? new(),
        SizeBytes    = r.SizeBytes,
        IsHidden     = r.IsHidden,
        SortOrder    = r.SortOrder,
        CreatedAt    = r.CreatedAt,
    };

    private static GunRow ToGunRow(GunpackGun g) => new()
    {

        Id           = g.Id == Guid.Empty ? null : g.Id,
        GunpackId    = g.GunpackId,
        BaseName     = g.BaseName,
        WeaponPrefix = g.WeaponPrefix,
        Category     = g.Category,
        DisplayName  = g.DisplayName,
        GlbUrl       = g.GlbUrl,
        PreviewUrl   = g.PreviewUrl,
        Files        = g.Files,
        SizeBytes    = g.SizeBytes,
        IsHidden     = g.IsHidden,
        SortOrder    = g.SortOrder,

    };

    private sealed class PackRow
    {
        public string Id               { get; set; } = string.Empty;
        public string Name             { get; set; } = string.Empty;
        public string? Author          { get; set; }
        public string? AuthorLink      { get; set; }
        public string? Description     { get; set; }
        public string WeaponsRpfUrl    { get; set; } = string.Empty;
        public long   WeaponsRpfSize   { get; set; }
        public string WeaponsRpfSha256 { get; set; } = string.Empty;
        public string? PackZipUrl     { get; set; }
        public long?   PackZipSize    { get; set; }
        public string? PackZipSha256  { get; set; }
        public string? ManifestUrl    { get; set; }
        public string  CoverKind      { get; set; } = "image";
        public string? CoverUrl       { get; set; }
        public List<string>? GalleryUrls { get; set; }
        public string Status          { get; set; } = "published";
        public bool   IsVerified      { get; set; }
        public int    ViewerPriority  { get; set; }
        public long   DownloadCount   { get; set; }
        public DateTime UploadedAt    { get; set; }
        public string? UploadedBy     { get; set; }
        public DateTime UpdatedAt     { get; set; }
        public string? Notes          { get; set; }
    }

    private sealed class GunRow
    {

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Guid?  Id           { get; set; }

        public string GunpackId    { get; set; } = string.Empty;
        public string BaseName     { get; set; } = string.Empty;
        public string WeaponPrefix { get; set; } = string.Empty;
        public string Category     { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? GlbUrl      { get; set; }
        public string? PreviewUrl  { get; set; }
        public List<string>? Files { get; set; }
        public long   SizeBytes    { get; set; }
        public bool   IsHidden     { get; set; }
        public int    SortOrder    { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public DateTime CreatedAt  { get; set; }
    }
}
