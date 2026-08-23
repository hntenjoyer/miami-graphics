using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiamiGraphics.Shell.Services;

public enum ServerRegion
{
    NotSelected = 0,

    Eu = 1,

    Ru = 2,
}

public static class ServerRegionConfig
{
    public const string EuUrl = "https://eu.miamigraphicsstorage.uk";
    public const string RuUrl = "https://ru.miamigraphicsstorage.uk";

    public const string RuFallbackUrl = EuUrl;

    public const string EuFallbackUrl = RuUrl;

    public static string PrimaryUrl(ServerRegion region) => region switch
    {
        ServerRegion.Ru => RuUrl,
        ServerRegion.Eu => EuUrl,
        _               => EuUrl,
    };

    public static string[] AllUrls(ServerRegion region) => region switch
    {
        ServerRegion.Ru => [RuUrl, RuFallbackUrl],
        ServerRegion.Eu => [EuUrl, EuFallbackUrl],
        _               => [EuUrl, EuFallbackUrl],
    };
}

public static class ServerRegionStore
{
    private const string FileName = "region.json";
    private const string SubDir   = "config";
    private const string AppDir   = "MiamiGraphics";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented        = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ConfigFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDir, SubDir, FileName);

    private sealed record RegionFile(
        [property: JsonPropertyName("region")] string Region,
        [property: JsonPropertyName("setAt")]  string SetAt);

    public static bool IsConfigured()
    {
        if (!File.Exists(ConfigFilePath)) return false;
        try
        {
            var json = File.ReadAllText(ConfigFilePath);
            var file = JsonSerializer.Deserialize<RegionFile>(json, Json);
            return file is not null && ParseRegion(file.Region) != ServerRegion.NotSelected;
        }
        catch
        {

            return false;
        }
    }

    public static bool ZapretForUs { get; private set; }

    public static void SetZapretForUs(bool on) => ZapretForUs = on;

    public static ServerRegion EffectiveRegion()
        => ZapretForUs ? ServerRegion.Eu : Load();

    public static ServerRegion Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath)) return ServerRegion.Eu;
            var json = File.ReadAllText(ConfigFilePath);
            var file = JsonSerializer.Deserialize<RegionFile>(json, Json);
            var parsed = ParseRegion(file?.Region ?? "");
            return parsed == ServerRegion.NotSelected ? ServerRegion.Eu : parsed;
        }
        catch
        {
            return ServerRegion.Eu;
        }
    }

    public static void Save(ServerRegion region)
    {
        if (region == ServerRegion.NotSelected)
            throw new ArgumentException("Cannot persist NotSelected", nameof(region));

        var dir = Path.GetDirectoryName(ConfigFilePath)!;
        Directory.CreateDirectory(dir);

        var file = new RegionFile(
            Region: RegionToString(region),
            SetAt:  DateTimeOffset.UtcNow.ToString("O"));

        var tmp = ConfigFilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, Json));

        File.Move(tmp, ConfigFilePath, overwrite: true);
    }

    private static ServerRegion ParseRegion(string s) => s?.ToLowerInvariant() switch
    {
        "eu" => ServerRegion.Eu,
        "ru" => ServerRegion.Ru,
        _    => ServerRegion.NotSelected,
    };

    private static string RegionToString(ServerRegion r) => r switch
    {
        ServerRegion.Eu => "eu",
        ServerRegion.Ru => "ru",
        _               => "",
    };
}
