using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public sealed class SupabaseUserBuildsRepository : IUserBuildsRepository
{
    private readonly SupabaseClient _sb;
    public SupabaseUserBuildsRepository(SupabaseClient sb) => _sb = sb;

    public async Task<List<UserBuildItem>> ListAsync(UserBuildFilter? filter = null)
    {
        var baseParts = new List<string>
        {
            "select=*",

            "status=eq.approved",
            "order=created_at.desc",
        };
        if (!string.IsNullOrWhiteSpace(filter?.AuthorUserId))
            baseParts.Add($"author_user_id=eq.{Uri.EscapeDataString(filter.AuthorUserId)}");

        const int pageSize = 1000;
        const int maxPages = 100;
        var rows = new List<Row>();
        for (var page = 0; page < maxPages; page++)
        {
            var parts = new List<string>(baseParts)
            {
                $"limit={pageSize}",
                $"offset={page * pageSize}",
            };
            var chunk = await _sb.SelectAsync<Row>("user_builds", string.Join("&", parts));
            rows.AddRange(chunk);
            if (chunk.Count < pageSize) break;
        }

        IEnumerable<Row> q = rows;
        if (!string.IsNullOrWhiteSpace(filter?.SearchText))
        {

            var t = filter!.SearchText!.Trim().ToLowerInvariant();
            q = q.Where(r =>
                r.Name.ToLowerInvariant().Contains(t)
             || (r.AuthorUsername ?? string.Empty).ToLowerInvariant().Contains(t)
             || r.HntCode.ToLowerInvariant().Contains(t));
        }
        return q.Select(ToItem).ToList();
    }

    public async Task<UserBuildItem?> GetByIdAsync(string id)
    {
        var row = await _sb.SelectOneAsync<Row>(
            "user_builds",
            $"id=eq.{Uri.EscapeDataString(id)}&select=*");
        return row is null ? null : ToItem(row);
    }

    public async Task<UserBuildItem?> GetByHntCodeAsync(string hntCode)
    {
        if (string.IsNullOrWhiteSpace(hntCode)) return null;
        var row = await _sb.SelectOneAsync<Row>(
            "user_builds",
            $"hnt_code=eq.{Uri.EscapeDataString(hntCode)}&select=*");
        return row is null ? null : ToItem(row);
    }

    public async Task<UserBuildItem> AddAsync(UserBuildItem item)
    {

        var rowToWrite = ToRow(item);
        await _sb.UpsertAsync("user_builds", rowToWrite);

        var saved = await GetByHntCodeAsync(item.HntCode);
        return saved ?? item;
    }

    public Task DeleteAsync(string id)
        => _sb.DeleteAsync("user_builds", $"id=eq.{Uri.EscapeDataString(id)}");

    public async Task<long> IncrementDownloadsAsync(string id)
    {
        var row = await _sb.RpcSingleAsync<CounterRow>(
            "user_build_increment_downloads",
            new { p_id = id });
        return row?.NewTotal ?? 0;
    }

    public async Task<long> IncrementViewsAsync(string id)
    {
        var row = await _sb.RpcSingleAsync<CounterRow>(
            "user_build_increment_views",
            new { p_id = id });
        return row?.NewTotal ?? 0;
    }

    public async Task<UserBuildItem> SubmitAsync(UserBuildItem item)
    {

        item.Status              = "pending";
        item.SubmittedForReview  = true;
        item.ReviewedBy          = null;
        item.ReviewedAt          = null;
        item.RejectReason        = null;
        var rowToWrite = ToRow(item);
        await _sb.UpsertAsync("user_builds", rowToWrite);
        var saved = await GetByHntCodeAsync(item.HntCode);
        return saved ?? item;
    }

    public async Task<UserBuildItem> UpdateAsync(string id, IReadOnlyDictionary<string, object?> patch)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("id required", nameof(id));
        if (patch.Count == 0)
        {

            return await GetByIdAsync(id) ?? throw new InvalidOperationException("build not found");
        }

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "author_username",
            "redux_id", "gunpack_id",
            "redux_name_snapshot", "gunpack_name_snapshot",
            "gun_slots", "armor", "arena", "minimap",
            "devices", "sensitivity", "dpi", "resolution",
            "video_url", "settings_xml_url", "description",
            "tier",
            "cover_url",

            "family", "category_label", "fps_avg", "monitor_hz", "admin_notes",
            "reticle", "sounds",
        };
        var safe = new Dictionary<string, object?>();
        foreach (var (k, v) in patch)
        {
            if (allowed.Contains(k)) safe[k] = v;
        }

        foreach (var col in new[] { "gun_slots", "devices" })
        {
            if (safe.TryGetValue(col, out var v) && IsJsonNullish(v))
                safe[col] = ParseEmptyObject();
        }

        safe["updated_at"] = DateTime.UtcNow;

        await _sb.UpdateAsync("user_builds",
            $"id=eq.{Uri.EscapeDataString(id)}", safe);
        var saved = await GetByIdAsync(id);
        return saved ?? throw new InvalidOperationException("build not found after update");
    }

    public async Task<List<UserBuildItem>> ListPendingAsync()
    {

        var rows = await _sb.SelectAsync<Row>(
            "user_builds",
            "select=*&status=eq.pending&submitted_for_review=eq.true&order=created_at.desc");
        return rows.Select(ToItem).ToList();
    }

    public async Task<List<UserBuildItem>> ListMyPendingAsync(string authorUserId)
    {
        if (string.IsNullOrWhiteSpace(authorUserId)) return new List<UserBuildItem>();

        var rows = await _sb.SelectAsync<Row>(
            "user_builds",
            $"select=*&author_user_id=eq.{Uri.EscapeDataString(authorUserId)}"
          + "&status=in.(pending,rejected)&order=created_at.desc");
        return rows.Select(ToItem).ToList();
    }

    public async Task<UserBuildItem> ApproveAsync(string id, string reviewerUserId, int? tier)
    {

        try
        {
            var row = await _sb.RpcSingleAsync<Row>(
                "user_build_approve",
                new { p_id = id, p_reviewer = reviewerUserId, p_tier = tier });
            if (row is not null) return ToItem(row);
        }
        catch (Exception ex) when (IsAuthOrMissing(ex))
        {

            System.Diagnostics.Debug.WriteLine($"[user-builds] RPC approve failed ({ex.GetType().Name}), fallback to UPDATE");
        }

        var patch = tier.HasValue
            ? (object)new { status = "approved", reviewed_by = reviewerUserId, reviewed_at = DateTime.UtcNow, reject_reason = (string?)null, tier = tier.Value, updated_at = DateTime.UtcNow }
            : new { status = "approved", reviewed_by = reviewerUserId, reviewed_at = DateTime.UtcNow, reject_reason = (string?)null, updated_at = DateTime.UtcNow };
        await _sb.UpdateAsync("user_builds", $"id=eq.{Uri.EscapeDataString(id)}&status=eq.pending", patch);
        var updated = await _sb.SelectOneAsync<Row>("user_builds", $"select=*&id=eq.{Uri.EscapeDataString(id)}&limit=1");
        if (updated is null) throw new InvalidOperationException("approve fallback failed (row not found after update)");
        return ToItem(updated);
    }

    public async Task<UserBuildItem> RejectAsync(string id, string reviewerUserId, string reason)
    {
        try
        {
            var row = await _sb.RpcSingleAsync<Row>(
                "user_build_reject",
                new { p_id = id, p_reviewer = reviewerUserId, p_reason = reason });
            if (row is not null) return ToItem(row);
        }
        catch (Exception ex) when (IsAuthOrMissing(ex))
        {
            System.Diagnostics.Debug.WriteLine($"[user-builds] RPC reject failed ({ex.GetType().Name}), fallback to UPDATE");
        }

        var patch = new
        {
            status = "rejected",
            reviewed_by = reviewerUserId,
            reviewed_at = DateTime.UtcNow,
            reject_reason = reason,
            updated_at = DateTime.UtcNow,
        };
        await _sb.UpdateAsync("user_builds", $"id=eq.{Uri.EscapeDataString(id)}&status=eq.pending", patch);
        var updated = await _sb.SelectOneAsync<Row>("user_builds", $"select=*&id=eq.{Uri.EscapeDataString(id)}&limit=1");
        if (updated is null) throw new InvalidOperationException("reject fallback failed (row not found after update)");
        return ToItem(updated);
    }

    private static bool IsAuthOrMissing(Exception ex)
    {
        if (ex is SupabaseException sex)
        {
            return sex.Kind == SupabaseErrorKind.Unauthorized
                || sex.Kind == SupabaseErrorKind.NotProvisioned;
        }
        return false;
    }

    public async Task<UserBuildItem> ResubmitAsync(string id)
    {
        var row = await _sb.RpcSingleAsync<Row>(
            "user_build_resubmit",
            new { p_id = id });
        if (row is null)
            throw new InvalidOperationException("resubmit failed (row not rejected or not found)");
        return ToItem(row);
    }

    private static UserBuildItem ToItem(Row r) => new()
    {
        Id                  = r.Id ?? string.Empty,
        HntCode             = r.HntCode,
        Name                = r.Name,
        AuthorUserId        = r.AuthorUserId,
        AuthorUsername      = r.AuthorUsername ?? string.Empty,
        ReduxId             = r.ReduxId,
        GunpackId           = r.GunpackId,
        ReduxNameSnapshot   = r.ReduxNameSnapshot   ?? string.Empty,
        GunpackNameSnapshot = r.GunpackNameSnapshot ?? string.Empty,

        GunSlotsJson        = JsonElementToString(r.GunSlots) ?? "{}",
        ArmorJson           = JsonElementToString(r.Armor),
        ArenaJson           = JsonElementToString(r.Arena),
        MinimapJson         = JsonElementToString(r.Minimap),
        ReticleJson         = JsonElementToString(r.Reticle),
        SoundsJson          = JsonElementToString(r.Sounds),
        DownloadCount       = r.DownloadCount,
        ViewCount           = r.ViewCount,
        CreatedAt           = r.CreatedAt,
        UpdatedAt           = r.UpdatedAt,

        DevicesJson         = JsonElementToString(r.Devices),
        Sensitivity         = r.Sensitivity,
        Dpi                 = r.Dpi,
        Resolution          = r.Resolution,
        VideoUrl            = r.VideoUrl,
        SettingsXmlUrl      = r.SettingsXmlUrl,
        Description         = r.Description ?? string.Empty,
        Tier                = r.Tier,
        CoverUrl            = r.CoverUrl,

        Status              = string.IsNullOrEmpty(r.Status) ? "approved" : r.Status,
        SubmittedForReview  = r.SubmittedForReview,
        ReviewedBy          = r.ReviewedBy,
        ReviewedAt          = r.ReviewedAt,
        RejectReason        = r.RejectReason,

        Family                   = r.Family,
        CategoryLabel            = r.CategoryLabel,
        FpsAvg                   = r.FpsAvg,
        MonitorHz                = r.MonitorHz,
        AdminNotes               = r.AdminNotes,
    };

    private static Row ToRow(UserBuildItem i) => new()
    {

        Id                  = string.IsNullOrEmpty(i.Id) ? null : i.Id,
        HntCode             = i.HntCode,
        Name                = i.Name,
        AuthorUserId        = string.IsNullOrEmpty(i.AuthorUserId) ? null : i.AuthorUserId,
        AuthorUsername      = i.AuthorUsername,
        ReduxId             = i.ReduxId,
        GunpackId           = i.GunpackId,
        ReduxNameSnapshot   = i.ReduxNameSnapshot,
        GunpackNameSnapshot = i.GunpackNameSnapshot,
        GunSlots            = StringToJsonElement(i.GunSlotsJson) ?? ParseEmptyObject(),
        Armor               = StringToJsonElement(i.ArmorJson),
        Arena               = StringToJsonElement(i.ArenaJson),
        Minimap             = StringToJsonElement(i.MinimapJson),
        Reticle             = StringToJsonElement(i.ReticleJson),
        Sounds              = StringToJsonElement(i.SoundsJson),
        DownloadCount       = i.DownloadCount,
        CreatedAt           = i.CreatedAt == default ? DateTime.UtcNow : i.CreatedAt,
        UpdatedAt           = DateTime.UtcNow,

        Devices             = StringToJsonElement(i.DevicesJson) ?? ParseEmptyObject(),
        Sensitivity         = i.Sensitivity,
        Dpi                 = i.Dpi,
        Resolution          = string.IsNullOrEmpty(i.Resolution) ? null : i.Resolution,
        VideoUrl            = string.IsNullOrEmpty(i.VideoUrl) ? null : i.VideoUrl,
        SettingsXmlUrl      = string.IsNullOrEmpty(i.SettingsXmlUrl) ? null : i.SettingsXmlUrl,
        Description         = i.Description ?? string.Empty,
        Tier                = i.Tier,
        CoverUrl            = string.IsNullOrEmpty(i.CoverUrl) ? null : i.CoverUrl,

        Status              = string.IsNullOrEmpty(i.Status) ? "approved" : i.Status,
        SubmittedForReview  = i.SubmittedForReview,
        ReviewedBy          = string.IsNullOrEmpty(i.ReviewedBy) ? null : i.ReviewedBy,
        ReviewedAt          = i.ReviewedAt,
        RejectReason        = string.IsNullOrEmpty(i.RejectReason) ? null : i.RejectReason,

        Family                = string.IsNullOrEmpty(i.Family)            ? null : i.Family,
        CategoryLabel         = string.IsNullOrEmpty(i.CategoryLabel)     ? null : i.CategoryLabel,
        FpsAvg                = i.FpsAvg,
        MonitorHz             = i.MonitorHz,
        AdminNotes            = string.IsNullOrEmpty(i.AdminNotes)        ? null : i.AdminNotes,
    };

    private static string? JsonElementToString(JsonElement? je)
    {
        if (je is null) return null;
        if (je.Value.ValueKind == JsonValueKind.Null || je.Value.ValueKind == JsonValueKind.Undefined)
            return null;
        return je.Value.GetRawText();
    }

    private static JsonElement? StringToJsonElement(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement ParseEmptyObject()
    {
        using var doc = JsonDocument.Parse("{}");
        return doc.RootElement.Clone();
    }

    private static bool IsJsonNullish(object? v)
    {
        if (v is null) return true;
        if (v is JsonElement je)
            return je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
        return false;
    }

    private sealed class CounterRow
    {
        public long NewTotal { get; set; }
    }

    private sealed class Row
    {

        public string? Id                  { get; set; }
        public string  HntCode             { get; set; } = string.Empty;
        public string  Name                { get; set; } = string.Empty;
        public string? AuthorUserId        { get; set; }
        public string? AuthorUsername      { get; set; }
        public string  ReduxId             { get; set; } = string.Empty;
        public string  GunpackId           { get; set; } = string.Empty;
        public string? ReduxNameSnapshot   { get; set; }
        public string? GunpackNameSnapshot { get; set; }
        public JsonElement GunSlots        { get; set; }
        public JsonElement? Armor          { get; set; }
        public JsonElement? Arena          { get; set; }
        public JsonElement? Minimap        { get; set; }

        public JsonElement? Reticle        { get; set; }

        public JsonElement? Sounds         { get; set; }
        public long  DownloadCount         { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public long  ViewCount             { get; set; }
        public DateTime CreatedAt          { get; set; }
        public DateTime UpdatedAt          { get; set; }

        public JsonElement Devices         { get; set; }
        public decimal?    Sensitivity     { get; set; }
        public int?        Dpi             { get; set; }
        public string?     Resolution      { get; set; }
        public string?     VideoUrl        { get; set; }
        public string?     SettingsXmlUrl  { get; set; }
        public string?     Description     { get; set; }
        public int?        Tier            { get; set; }

        public string?     CoverUrl        { get; set; }
        public string      Status          { get; set; } = "approved";
        public bool        SubmittedForReview { get; set; }
        public string?     ReviewedBy      { get; set; }
        public DateTime?   ReviewedAt      { get; set; }
        public string?     RejectReason    { get; set; }

        [JsonPropertyName("family")]
        public string?     Family                 { get; set; }
        [JsonPropertyName("category_label")]
        public string?     CategoryLabel          { get; set; }
        [JsonPropertyName("fps_avg")]
        public int?        FpsAvg                 { get; set; }

        [JsonPropertyName("monitor_hz")]
        public int?        MonitorHz              { get; set; }
        [JsonPropertyName("admin_notes")]
        public string?     AdminNotes             { get; set; }
    }
}
