namespace MiamiGraphics.Shell.Admin;

public sealed class FeaturedPick
{
    public int      SlotIndex { get; set; }
    public string   ReduxId   { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
