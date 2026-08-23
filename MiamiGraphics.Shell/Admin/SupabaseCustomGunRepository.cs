using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public sealed class SupabaseCustomGunRepository
{
    private readonly SupabaseClient _sb;
    public SupabaseCustomGunRepository(SupabaseClient sb) => _sb = sb;

    private static string? Token => SupabaseClient.UserSessionToken;

    public async Task<List<CustomGunItem>> ListPublishedAsync(string? search, string? sort)
    {
        var rows = await _sb.SelectAllPagedAsync<Row>(
            "custom_guns", "select=*&status=eq.published");

        IEnumerable<Row> q = rows;
        var t = (search ?? string.Empty).Trim().ToLowerInvariant();
        if (t.Length > 0)
            q = q.Where(r =>
                (r.DisplayName ?? string.Empty).ToLowerInvariant().Contains(t)
             || (r.OwnerName  ?? string.Empty).ToLowerInvariant().Contains(t));

        q = (sort is "new" or "recent")
            ? q.OrderByDescending(r => r.CreatedAt)
            : q.OrderByDescending(r => r.DownloadCount).ThenByDescending(r => r.CreatedAt);

        return q.Select(ToItem).ToList();
    }

    public async Task<CustomGunItem?> GetPublishedByIdAsync(string id)
    {
        var row = await _sb.SelectOneAsync<Row>(
            "custom_guns", $"id=eq.{Uri.EscapeDataString(id)}&status=eq.published&select=*");
        return row is null ? null : ToItem(row);
    }

    public async Task<CustomGunItem?> GetOwnOrPublishedByIdAsync(string id)
    {
        if (!string.IsNullOrEmpty(Token))
        {
            var own = (await MineAsync()).FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));
            if (own is not null) return own;
        }
        return await GetPublishedByIdAsync(id);
    }

    public async Task<List<CustomGunItem>> MineAsync()
    {
        if (string.IsNullOrEmpty(Token)) return new List<CustomGunItem>();
        var rows = await _sb.RpcManyAsync<Row>("custom_guns_mine_secure", new { p_token = Token });
        return rows.Select(ToItem).ToList();
    }

    public async Task<int> SlotUsedAsync()
    {
        var mine = await MineAsync();
        return mine.Count(m => m.Status is "saved" or "pending" or "published");
    }

    public async Task<CustomGunItem?> SaveDraftAsync(
        string? id, string baseName, string weaponPrefix, string internalName,
        string displayName, string description, string category,
        string? glbUrl, string? filesUrl, string? filesSha256, string? previewUrl = null)
    {
        var row = await _sb.RpcSingleAsync<Row>("custom_gun_save_secure", new
        {
            p_token         = Token,
            p_id            = id,
            p_base_name     = baseName,
            p_weapon_prefix = weaponPrefix ?? string.Empty,
            p_internal_name = internalName,
            p_display_name  = displayName,
            p_description   = description ?? string.Empty,
            p_preview_url   = previewUrl,
            p_category      = string.IsNullOrWhiteSpace(category) ? "assault" : category,
            p_glb_url       = glbUrl,
            p_files_url     = filesUrl,
            p_files_sha256  = filesSha256,
        });
        return row is null ? null : ToItem(row);
    }

    public async Task<CustomGunItem?> PublishSecureAsync(string id)
    {
        var row = await _sb.RpcSingleAsync<Row>(
            "custom_gun_publish_secure", new { p_token = Token, p_id = id });
        return row is null ? null : ToItem(row);
    }

    public async Task<CustomGunItem?> PatchAsync(string id, string? displayName, string? description, string? category)
    {
        var row = await _sb.RpcSingleAsync<Row>("custom_gun_patch_secure", new
        {
            p_token        = Token,
            p_id           = id,
            p_display_name = displayName,
            p_description  = description,
            p_category     = category,
        });
        return row is null ? null : ToItem(row);
    }

    public Task DeleteAsync(string id)
        => _sb.RpcVoidAsync("custom_gun_delete_secure", new { p_token = Token, p_id = id });

    public async Task<List<CustomGunItem>> ListPendingAsync()
    {
        if (string.IsNullOrEmpty(Token)) return new List<CustomGunItem>();
        var rows = await _sb.RpcManyAsync<Row>("custom_gun_list_pending_secure", new { p_token = Token });
        return rows.Select(ToItem).ToList();
    }

    public async Task<CustomGunItem?> ApproveAsync(string id, string? previewUrl)
    {
        var row = await _sb.RpcSingleAsync<Row>("custom_gun_approve_secure", new
        {
            p_token       = Token,
            p_id          = id,
            p_preview_url = previewUrl ?? string.Empty,
        });
        return row is null ? null : ToItem(row);
    }

    public async Task<CustomGunItem?> RejectAsync(string id, string reason)
    {
        var row = await _sb.RpcSingleAsync<Row>("custom_gun_reject_secure", new
        {
            p_token  = Token,
            p_id     = id,
            p_reason = reason ?? string.Empty,
        });
        return row is null ? null : ToItem(row);
    }

    public async Task<List<CustomGunItem>> AdminListAsync(string? status = null, string? search = null)
    {
        if (string.IsNullOrEmpty(Token)) return new List<CustomGunItem>();
        var rows = await _sb.RpcManyAsync<Row>("custom_gun_admin_list_secure", new
        {
            p_token  = Token,
            p_status = status,
            p_search = search,
        });
        return rows.Select(ToItem).ToList();
    }

    public async Task<CustomGunItem?> AdminPatchAsync(string id, string? displayName, string? description, string? category)
    {
        var row = await _sb.RpcSingleAsync<Row>("custom_gun_admin_patch_secure", new
        {
            p_token        = Token,
            p_id           = id,
            p_display_name = displayName,
            p_description  = description,
            p_category     = category,
        });
        return row is null ? null : ToItem(row);
    }

    public async Task<CustomGunItem?> AdminDeleteAsync(string id, string? reason = null, bool hard = false)
    {
        var row = await _sb.RpcSingleAsync<Row>("custom_gun_admin_delete_secure", new
        {
            p_token  = Token,
            p_id     = id,
            p_reason = reason,
            p_hard   = hard,
        });
        return row is null ? null : ToItem(row);
    }

    public sealed class WorkshopAttemptRow
    {
        public string Flow    { get; set; } = "";
        public string GunKey  { get; set; } = "";
        public int    Used    { get; set; }
    }

    public async Task<List<WorkshopAttemptRow>> WorkshopLimitsAsync()
    {
        if (string.IsNullOrEmpty(Token)) return new List<WorkshopAttemptRow>();
        return await _sb.RpcManyAsync<WorkshopAttemptRow>(
            "workshop_limits_secure", new { p_token = Token });
    }

    private sealed class ConsumeRow { public int Used { get; set; } public int MaxCount { get; set; } }

    public async Task<(int used, int max)> WorkshopConsumeAsync(string flow, string gunKey)
    {
        var row = await _sb.RpcSingleAsync<ConsumeRow>("workshop_attempt_consume_secure",
            new { p_token = Token, p_flow = flow, p_gun_key = gunKey ?? "" })
            ?? throw new InvalidOperationException("consume вернул пустой ответ");
        return (row.Used, row.MaxCount);
    }

    public sealed class UserGunpackMineRow
    {
        public string   Id        { get; set; } = "";
        public string   Name      { get; set; } = "";
        public long     GunCount  { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public async Task<List<UserGunpackMineRow>> UserGunpackMineAsync()
    {
        if (string.IsNullOrEmpty(Token)) return new List<UserGunpackMineRow>();
        return await _sb.RpcManyAsync<UserGunpackMineRow>(
            "user_gunpack_mine_secure", new { p_token = Token });
    }

    public sealed record UserGunpackSaveResult(string PackId, string PackName, string GunId, long GunCount);

    public async Task<UserGunpackSaveResult> UserGunpackSaveGunAsync(
        string? packId, string? packName, string baseName, string weaponPrefix,
        string internalName, string displayName, string category,
        string? glbUrl, string? previewUrl, string? filesUrl, string? filesSha256)
    {
        var node = await _sb.RpcJsonAsync("user_gunpack_save_gun_secure", new
        {
            p_token         = Token,
            p_pack_id       = packId,
            p_pack_name     = packName,
            p_base_name     = baseName,
            p_weapon_prefix = weaponPrefix ?? string.Empty,
            p_internal_name = internalName,
            p_display_name  = displayName,
            p_category      = string.IsNullOrWhiteSpace(category) ? "assault" : category,
            p_glb_url       = glbUrl,
            p_preview_url   = previewUrl,
            p_files_url     = filesUrl,
            p_files_sha256  = filesSha256,
        }) ?? throw new InvalidOperationException("user_gunpack_save_gun_secure вернул пустой ответ");
        return new UserGunpackSaveResult(
            node["pack_id"]?.GetValue<string>() ?? "",
            node["pack_name"]?.GetValue<string>() ?? "",
            node["gun_id"]?.GetValue<string>() ?? "",
            node["gun_count"]?.GetValue<long>() ?? 0);
    }

    public sealed class UserGunpackRow
    {
        public string   Id            { get; set; } = "";
        public string   OwnerId       { get; set; } = "";
        public string   OwnerName     { get; set; } = "";
        public string   Name          { get; set; } = "";
        public long     DownloadCount { get; set; }
        public DateTime CreatedAt     { get; set; }
    }

    public Task<List<UserGunpackRow>> UserGunpacksListAsync()
        => _sb.SelectAllPagedAsync<UserGunpackRow>(
            "user_gunpacks", "select=*&status=eq.active", "created_at.desc,id.asc");

    public async Task<List<CustomGunItem>> UserGunpackGunsAsync()
    {
        var rows = await _sb.SelectAllPagedAsync<Row>(
            "custom_guns", "select=*&user_gunpack_id=not.is.null&status=eq.published",
            "created_at.asc,id.asc");
        return rows.Select(ToItem).ToList();
    }

    public Task UserGunpackDeleteAsync(string id)
        => _sb.RpcVoidAsync("user_gunpack_delete_secure", new { p_token = Token, p_id = id });

    public async Task UserGunpackIncrementDownloadsAsync(string id)
    {
        try { await _sb.RpcVoidAsync("user_gunpack_increment_downloads", new { p_id = id }); }
        catch {}
    }

    public async Task<long> IncrementDownloadsAsync(string id)
    {
        try
        {
            var row = await _sb.RpcSingleAsync<CounterRow>(
                "custom_gun_increment_downloads", new { p_id = id });
            return row?.NewTotal ?? 0;
        }
        catch { return 0; }
    }

    private static CustomGunItem ToItem(Row r) => new()
    {
        Id            = r.Id ?? string.Empty,
        OwnerId       = r.OwnerId ?? string.Empty,
        OwnerName     = r.OwnerName ?? string.Empty,
        BaseName      = r.BaseName ?? string.Empty,
        WeaponPrefix  = r.WeaponPrefix ?? string.Empty,
        InternalName  = r.InternalName ?? string.Empty,
        DisplayName   = r.DisplayName ?? string.Empty,
        Description   = r.Description ?? string.Empty,
        Category      = r.Category ?? "assault",
        GlbUrl        = r.GlbUrl,
        PreviewUrl    = r.PreviewUrl,
        FilesUrl      = r.FilesUrl,
        FilesSha256   = r.FilesSha256,
        Status        = string.IsNullOrEmpty(r.Status) ? "saved" : r.Status,
        SubmittedForReview = r.SubmittedForReview,
        ReviewedBy    = r.ReviewedBy,
        ReviewedAt    = r.ReviewedAt,
        RejectReason  = r.RejectReason,
        DownloadCount = r.DownloadCount,
        CreatedAt     = r.CreatedAt,
        UpdatedAt     = r.UpdatedAt,
        UserGunpackId = r.UserGunpackId,
    };

    private sealed class CounterRow { public long NewTotal { get; set; } }

    private sealed class Row
    {
        public string?  Id            { get; set; }
        public string?  OwnerId       { get; set; }
        public string?  OwnerName     { get; set; }
        public string?  BaseName      { get; set; }
        public string?  WeaponPrefix  { get; set; }
        public string?  InternalName  { get; set; }
        public string?  DisplayName   { get; set; }
        public string?  Description   { get; set; }
        public string?  Category      { get; set; }
        public string?  GlbUrl        { get; set; }
        public string?  PreviewUrl    { get; set; }
        public string?  FilesUrl      { get; set; }
        public string?  FilesSha256   { get; set; }
        public string?  Status        { get; set; }
        public bool     SubmittedForReview { get; set; }
        public string?  ReviewedBy    { get; set; }
        public DateTime? ReviewedAt   { get; set; }
        public string?  RejectReason  { get; set; }
        public long     DownloadCount { get; set; }
        public DateTime CreatedAt     { get; set; }
        public DateTime UpdatedAt     { get; set; }
        public string?  UserGunpackId { get; set; }
    }
}

public static class WorkshopFlowLimits
{
    public const int StandardPerGun = 2;
    public const int PackBaseTotal = 4;
    public const int OwnPackTotal = 2;
    public const int OwnPackGunCap = 3;
}

public sealed class CustomGunItem
{
    public string Id            { get; set; } = string.Empty;
    public string OwnerId       { get; set; } = string.Empty;
    public string OwnerName     { get; set; } = string.Empty;
    public string BaseName      { get; set; } = string.Empty;
    public string WeaponPrefix  { get; set; } = string.Empty;
    public string InternalName  { get; set; } = string.Empty;
    public string DisplayName   { get; set; } = string.Empty;
    public string Description   { get; set; } = string.Empty;
    public string Category      { get; set; } = "assault";
    public string? GlbUrl       { get; set; }
    public string? PreviewUrl   { get; set; }
    public string? FilesUrl     { get; set; }
    public string? FilesSha256  { get; set; }
    public string Status        { get; set; } = "saved";
    public bool   SubmittedForReview { get; set; }
    public string? ReviewedBy   { get; set; }
    public DateTime? ReviewedAt  { get; set; }
    public string? RejectReason  { get; set; }
    public long   DownloadCount { get; set; }
    public DateTime CreatedAt   { get; set; }
    public DateTime UpdatedAt   { get; set; }
    public string? UserGunpackId { get; set; }
}
