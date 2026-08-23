using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiamiGraphics.Shell.Services;

public enum DownloadSource
{
    Eu = 0,

    Ru2 = 1,
}

public static class DownloadSourceStore
{
    public const string RuVpsHost = "ru.miamigraphicsstorage.uk";

    private const string FileName = "download_source.json";
    private const string SubDir   = "config";
    private const string AppDir   = "MiamiGraphics";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented          = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string ConfigFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDir, SubDir, FileName);

    private sealed record SourceFile(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("setAt")]  string SetAt);

    public static DownloadSource Effective()
    {
        try
        {
            if (!File.Exists(ConfigFilePath)) return DownloadSource.Eu;
            var file = JsonSerializer.Deserialize<SourceFile>(File.ReadAllText(ConfigFilePath), Json);
            return Parse(file?.Source ?? "");
        }
        catch { return DownloadSource.Eu; }
    }

    public static bool QueueEnabled => Effective() == DownloadSource.Ru2;

    public static void Save(DownloadSource source)
    {
        var dir = Path.GetDirectoryName(ConfigFilePath)!;
        Directory.CreateDirectory(dir);
        var file = new SourceFile(ToStr(source), DateTimeOffset.UtcNow.ToString("O"));
        var tmp = ConfigFilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, Json));
        File.Move(tmp, ConfigFilePath, overwrite: true);
    }

    public static DownloadSource Parse(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "ru2" or "ru" => DownloadSource.Ru2,
        _             => DownloadSource.Eu,
    };

    public static string ToStr(DownloadSource s) => s switch
    {
        DownloadSource.Ru2 => "ru2",
        _                  => "eu",
    };
}
