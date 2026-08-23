namespace MiamiGraphics.Shell.Repositories.Models;

public sealed class InstallHistoryEntry
{
    public string   UserId      { get; set; } = string.Empty;
    public string   ReduxId     { get; set; } = string.Empty;
    public string   Name        { get; set; } = string.Empty;
    public string   Author      { get; set; } = string.Empty;
    public string?  PreviewUrl  { get; set; }
    public DateTime InstalledAt { get; set; }
}
