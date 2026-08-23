using MiamiGraphics.Bridge;
using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Repositories.Supabase;

internal sealed class SupabaseAppSettingsRepository : IAppSettingsRepository
{
    private static readonly AppSettingsDto Defaults = new(
        Language:         "ru",
        AccentColor:      "slate",
        Background:       "cubes",
        PolygonsEnabled:  true,
        SidebarCollapsed: false);

    private readonly SupabaseClient _sb;
    private readonly string         _deviceId;

    public SupabaseAppSettingsRepository(SupabaseClient sb, string deviceId)
    {
        _sb       = sb;
        _deviceId = deviceId;
    }

    public async Task<AppSettingsDto> GetAsync()
    {
        Row? row;
        try
        {
            row = await _sb.SelectOneAsync<Row>(
                "app_settings",
                $"device_id=eq.{Uri.EscapeDataString(_deviceId)}&select=*");
        }
        catch
        {

            return Defaults;
        }
        if (row is null) return Defaults;

        var bg = string.IsNullOrEmpty(row.Background) || row.Background == "polygons"
            ? Defaults.Background
            : row.Background;

        return new AppSettingsDto(
            Language:         row.Language,
            AccentColor:      row.AccentColor,
            Background:       bg,
            PolygonsEnabled:  row.PolygonsEnabled,
            SidebarCollapsed: row.SidebarCollapsed);
    }

    public Task SaveAsync(AppSettingsDto settings)
    {
        var row = new Row
        {
            DeviceId         = _deviceId,
            Language         = settings.Language,
            AccentColor      = settings.AccentColor,
            Background       = settings.Background,
            PolygonsEnabled  = settings.PolygonsEnabled,
            SidebarCollapsed = settings.SidebarCollapsed,
            UpdatedAt        = DateTime.UtcNow,
        };
        return _sb.UpsertAsync("app_settings", row);
    }

    private sealed class Row
    {
        public string   DeviceId         { get; set; } = string.Empty;
        public string   Language         { get; set; } = "ru";
        public string   AccentColor      { get; set; } = "slate";
        public string?  Background       { get; set; } = "cubes";
        public bool     PolygonsEnabled  { get; set; } = true;
        public bool     SidebarCollapsed { get; set; }
        public DateTime UpdatedAt        { get; set; }
    }
}
