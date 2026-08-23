namespace MiamiGraphics.Shell.Repositories.Models;

public sealed class Mod
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Version { get; set; }
}
