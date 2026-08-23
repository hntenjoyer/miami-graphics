using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public sealed class SupabaseReduxVersionsRepository : IReduxVersionsRepository
{
    private readonly SupabaseClient _sb;
    private readonly IAdminConfigService _adminConfig;

    public SupabaseReduxVersionsRepository(SupabaseClient sb, IAdminConfigService adminConfig)
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

    public async Task<List<ReduxVersion>> ListByReduxAsync(string reduxId)
    {
        var rows = await _sb.SelectAsync<Row>(
            "redux_versions",
            $"redux_id=eq.{Uri.EscapeDataString(reduxId)}&order=slot.asc&select=*");
        return rows.Select(ToVersion).ToList();
    }

    public async Task<ReduxVersion?> FindByPatchSha256Async(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return null;
        var row = await _sb.SelectOneAsync<Row>(
            "redux_versions",
            $"patch_sha256=eq.{Uri.EscapeDataString(sha256)}&select=*");
        return row is null ? null : ToVersion(row);
    }

    public async Task<ReduxVersion?> FindBySourceSha256Async(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return null;
        var row = await _sb.SelectOneAsync<Row>(
            "redux_versions",
            $"source_sha256=eq.{Uri.EscapeDataString(sha256)}&select=*");
        return row is null ? null : ToVersion(row);
    }

    public async Task UpsertAsync(ReduxVersion v)
        => await _sb.UpsertWithServiceRoleAsync("redux_versions", ToRow(v), await ServiceKeyAsync());

    public async Task DeleteAsync(Guid id)
        => await _sb.DeleteWithServiceRoleAsync("redux_versions", $"id=eq.{id}", await ServiceKeyAsync());

    private static ReduxVersion ToVersion(Row r) => new()
    {
        Id               = r.Id ?? Guid.Empty,
        ReduxId          = r.ReduxId,
        Slot             = r.Slot,
        Label            = r.Label,
        PatchUrl         = r.PatchUrl,
        PatchSizeBytes   = r.PatchSizeBytes,
        PatchSha256      = r.PatchSha256,
        SourceSha256     = r.SourceSha256,
        TargetGtaVersion = r.TargetGtaVersion,
        Components       = r.Components    ?? new(),
        ComponentUrls    = r.ComponentUrls ?? new(),
        ManifestUrl      = r.ManifestUrl,
        ComponentMapUrl  = r.ComponentMapUrl,
        ContentInfoUrl   = r.ContentInfoUrl,
        CreatedAt        = r.CreatedAt ?? default,
        UpdatedAt        = r.UpdatedAt ?? default,
    };

    private static Row ToRow(ReduxVersion v) => new()
    {

        Id               = v.Id == Guid.Empty ? Guid.NewGuid() : v.Id,
        ReduxId          = v.ReduxId,
        Slot             = v.Slot,
        Label            = v.Label,
        PatchUrl         = v.PatchUrl,
        PatchSizeBytes   = v.PatchSizeBytes,
        PatchSha256      = v.PatchSha256,
        SourceSha256     = v.SourceSha256,
        TargetGtaVersion = v.TargetGtaVersion,
        Components       = v.Components,
        ComponentUrls    = v.ComponentUrls,
        ManifestUrl      = v.ManifestUrl,
        ComponentMapUrl  = v.ComponentMapUrl,
        ContentInfoUrl   = v.ContentInfoUrl,

        CreatedAt        = null,
        UpdatedAt        = DateTime.UtcNow,
    };

    private sealed class Row
    {
        public Guid?                                    Id               { get; set; }
        public string                                   ReduxId          { get; set; } = string.Empty;
        public int                                      Slot             { get; set; }
        public string                                   Label            { get; set; } = string.Empty;
        public string?                                  PatchUrl         { get; set; }
        public long                                     PatchSizeBytes   { get; set; }
        public string?                                  PatchSha256      { get; set; }
        public string?                                  SourceSha256     { get; set; }
        public string?                                  TargetGtaVersion { get; set; }
        public Dictionary<string, ReduxComponentInfo>?  Components       { get; set; }
        public Dictionary<string, string>?              ComponentUrls    { get; set; }
        public string?                                  ManifestUrl      { get; set; }
        public string?                                  ComponentMapUrl  { get; set; }
        public string?                                  ContentInfoUrl   { get; set; }
        public DateTime?                                CreatedAt        { get; set; }
        public DateTime?                                UpdatedAt        { get; set; }
    }
}
