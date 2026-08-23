namespace MiamiGraphics.Shell.Repositories.Models;

public sealed class User
{
    public string   Id           { get; set; } = string.Empty;
    public string   Username     { get; set; } = string.Empty;
    public string   Email        { get; set; } = string.Empty;
    public string   PasswordHash { get; set; } = string.Empty;
    public string   Role         { get; set; } = "User";
    public DateTime CreatedAt    { get; set; }
    public string?  AvatarUrl    { get; set; }
    public bool     TesterAccess { get; set; }
}
