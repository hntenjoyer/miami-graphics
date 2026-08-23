namespace MiamiGraphics.Shell.Repositories.Models;

public sealed class PlayerBuild
{
    public string Id { get; set; } = string.Empty;
    public string? OwnerUserId { get; set; }
    public string? Name { get; set; }
    public string? CreatedAt { get; set; }
}
