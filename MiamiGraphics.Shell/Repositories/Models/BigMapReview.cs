namespace MiamiGraphics.Shell.Repositories.Models;

public sealed class BigMapReview
{
    public string   Id        { get; set; } = string.Empty;
    public string   MapId     { get; set; } = string.Empty;
    public string   UserId    { get; set; } = string.Empty;
    public string   Username  { get; set; } = string.Empty;
    public string   Role      { get; set; } = "User";
    public string?  AvatarUrl { get; set; }
    public int      Rating    { get; set; }
    public string   Body      { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
