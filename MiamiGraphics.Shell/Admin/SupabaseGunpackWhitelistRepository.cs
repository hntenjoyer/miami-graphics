using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public sealed class SupabaseGunpackWhitelistRepository : IGunpackWhitelistRepository
{
    private readonly SupabaseClient _sb;

    public SupabaseGunpackWhitelistRepository(SupabaseClient sb) => _sb = sb;

    public async Task<List<GunpackWhitelistEntry>> ListAsync()
    {
        var rows = await _sb.SelectAsync<Row>(
            "gunpack_whitelist",
            "select=*&order=sort_order.asc");
        return rows.Select(r => new GunpackWhitelistEntry
        {
            InternalName  = r.InternalName,
            DisplayName   = r.DisplayName,
            Category      = r.Category,
            WeaponPrefix  = r.WeaponPrefix,
            IsSmgOverride = r.IsSmgOverride,
            SortOrder     = r.SortOrder,
            PreviewUrl    = r.PreviewUrl,
        }).ToList();
    }

    private sealed class Row
    {
        public string InternalName  { get; set; } = string.Empty;
        public string DisplayName   { get; set; } = string.Empty;
        public string Category      { get; set; } = string.Empty;
        public string WeaponPrefix  { get; set; } = string.Empty;
        public bool   IsSmgOverride { get; set; }
        public int    SortOrder     { get; set; }
        public string? PreviewUrl   { get; set; }
    }
}
