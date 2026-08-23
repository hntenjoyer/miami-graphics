using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public sealed class SupabaseFeaturedPicksRepository : IFeaturedPicksRepository
{
    private readonly SupabaseClient _sb;
    private readonly IAdminConfigService _adminConfig;

    public SupabaseFeaturedPicksRepository(SupabaseClient sb, IAdminConfigService adminConfig)
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

    public async Task<List<FeaturedPick>> ListAsync()
    {
        var rows = await _sb.SelectAsync<Row>(
            "featured_picks",
            "select=*&order=slot_index.asc");
        return rows.Select(ToPick).ToList();
    }

    public async Task UpsertAsync(int slotIndex, string reduxId)
        => await _sb.UpsertWithServiceRoleAsync("featured_picks", new Row
        {
            SlotIndex = slotIndex,
            ReduxId   = reduxId,

            UpdatedAt = null,
        }, await ServiceKeyAsync());

    public async Task DeleteAsync(int slotIndex)
        => await _sb.DeleteWithServiceRoleAsync("featured_picks", $"slot_index=eq.{slotIndex}", await ServiceKeyAsync());

    private static FeaturedPick ToPick(Row r) => new()
    {
        SlotIndex = r.SlotIndex,
        ReduxId   = r.ReduxId,
        UpdatedAt = r.UpdatedAt ?? default,
    };

    private sealed class Row
    {
        public int       SlotIndex { get; set; }
        public string    ReduxId   { get; set; } = string.Empty;
        public DateTime? UpdatedAt { get; set; }
    }
}
