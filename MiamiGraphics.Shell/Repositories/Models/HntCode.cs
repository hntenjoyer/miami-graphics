using System.Text.Json;

namespace MiamiGraphics.Shell.Repositories.Models;

public sealed class HntCode
{
    public string       Code             { get; set; } = string.Empty;
    public JsonElement  Payload          { get; set; }
    public string       CreatedBy        { get; set; } = string.Empty;
    public DateTime     CreatedAt        { get; set; }
    public DateTime     LastDownloadedAt { get; set; }
    public int          DownloadsCount   { get; set; }
}
