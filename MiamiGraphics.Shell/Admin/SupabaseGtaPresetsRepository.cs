using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public sealed class SupabaseGtaPresetsRepository : IGtaPresetsRepository
{
    private readonly SupabaseClient _sb;
    private readonly IAdminConfigService _adminConfig;

    public SupabaseGtaPresetsRepository(SupabaseClient sb, IAdminConfigService adminConfig)
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

    public async Task<List<GtaPresetItem>> ListAsync(GtaPresetFilter? filter = null)
    {

        var queryParts = new List<string>
        {
            "select=*",
            "order=viewer_priority.desc,uploaded_at.desc",
        };
        if (!string.IsNullOrWhiteSpace(filter?.Status))
            queryParts.Add($"status=eq.{Uri.EscapeDataString(filter.Status)}");

        var rows = await _sb.SelectAsync<Row>("gta_presets", string.Join("&", queryParts));

        IEnumerable<Row> q = rows;
        if (!string.IsNullOrWhiteSpace(filter?.SearchText))
        {
            var t = filter.SearchText.Trim().ToLowerInvariant();
            q = q.Where(x => x.Name.ToLowerInvariant().Contains(t)
                          || (x.Author ?? string.Empty).ToLowerInvariant().Contains(t));
        }

        return q.Select(ToItem).ToList();
    }

    public async Task<GtaPresetItem?> GetByIdAsync(string id)
    {
        var row = await _sb.SelectOneAsync<Row>(
            "gta_presets",
            $"id=eq.{Uri.EscapeDataString(id)}&select=*");
        return row is null ? null : ToItem(row);
    }

    public async Task<GtaPresetItem?> FindByXmlSha256Async(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256)) return null;
        var row = await _sb.SelectOneAsync<Row>(
            "gta_presets",
            $"xml_sha256=eq.{Uri.EscapeDataString(sha256)}&select=*");
        return row is null ? null : ToItem(row);
    }

    public async Task AddAsync(GtaPresetItem item)
        => await _sb.UpsertWithServiceRoleAsync("gta_presets", ToRow(item), await ServiceKeyAsync());

    public async Task UpdateAsync(string id, Action<GtaPresetItem> update)
    {
        var item = await GetByIdAsync(id);
        if (item is null) return;
        update(item);
        item.UpdatedAt = DateTime.UtcNow;
        await _sb.UpsertWithServiceRoleAsync("gta_presets", ToRow(item), await ServiceKeyAsync());
    }

    public async Task DeleteAsync(string id)
        => await _sb.DeleteWithServiceRoleAsync("gta_presets", $"id=eq.{Uri.EscapeDataString(id)}", await ServiceKeyAsync());

    public async Task<long> IncrementDownloadsAsync(string id)
    {

        var row = await _sb.RpcSingleAsync<CounterRow>(
            "gta_preset_increment_downloads",
            new { p_id = id });
        return row?.NewTotal ?? 0;
    }

    private sealed class CounterRow
    {
        public long NewTotal { get; set; }
    }

    private static GtaPresetItem ToItem(Row r) => new()
    {
        Id                  = r.Id,
        Name                = r.Name,
        Description         = r.Description ?? string.Empty,
        Author              = r.Author      ?? string.Empty,
        XmlUrl              = r.XmlUrl,
        XmlSizeBytes        = r.XmlSizeBytes,
        XmlSha256           = r.XmlSha256   ?? string.Empty,
        ExpectedFpsLow      = r.ExpectedFpsLow,
        ExpectedFpsHigh     = r.ExpectedFpsHigh,
        BaselineHwLabel     = r.BaselineHwLabel,
        ComputedGainPercent = r.ComputedGainPercent,
        CpuBias             = r.CpuBias  ?? "balanced",
        IsTournament        = r.IsTournament,
        Status              = r.Status   ?? "published",
        ViewerPriority      = r.ViewerPriority,
        DownloadCount       = r.DownloadCount,
        UploadedBy          = r.UploadedBy ?? string.Empty,
        UploadedAt          = r.UploadedAt,
        UpdatedAt           = r.UpdatedAt,
    };

    private static Row ToRow(GtaPresetItem i) => new()
    {
        Id                  = i.Id,
        Name                = i.Name,
        Description         = string.IsNullOrEmpty(i.Description) ? null : i.Description,
        Author              = string.IsNullOrEmpty(i.Author)      ? null : i.Author,
        XmlUrl              = i.XmlUrl,
        XmlSizeBytes        = i.XmlSizeBytes,
        XmlSha256           = i.XmlSha256,
        ExpectedFpsLow      = i.ExpectedFpsLow,
        ExpectedFpsHigh     = i.ExpectedFpsHigh,
        BaselineHwLabel     = i.BaselineHwLabel,
        ComputedGainPercent = i.ComputedGainPercent,
        CpuBias             = i.CpuBias,
        IsTournament        = i.IsTournament,
        Status              = i.Status,
        ViewerPriority      = i.ViewerPriority,
        DownloadCount       = i.DownloadCount,
        UploadedBy          = string.IsNullOrEmpty(i.UploadedBy) ? null : i.UploadedBy,
        UploadedAt          = i.UploadedAt,
        UpdatedAt           = i.UpdatedAt,
    };

    private sealed class Row
    {
        public string Id                  { get; set; } = string.Empty;
        public string Name                { get; set; } = string.Empty;
        public string? Description        { get; set; }
        public string? Author             { get; set; }
        public string XmlUrl              { get; set; } = string.Empty;
        public long   XmlSizeBytes        { get; set; }
        public string? XmlSha256          { get; set; }
        public int?   ExpectedFpsLow      { get; set; }
        public int?   ExpectedFpsHigh     { get; set; }
        public string? BaselineHwLabel    { get; set; }
        public int    ComputedGainPercent { get; set; }
        public string? CpuBias            { get; set; }
        public bool   IsTournament        { get; set; }
        public string? Status             { get; set; }
        public int    ViewerPriority      { get; set; }
        public long   DownloadCount       { get; set; }
        public string? UploadedBy         { get; set; }
        public DateTime UploadedAt        { get; set; }
        public DateTime UpdatedAt         { get; set; }
    }
}
