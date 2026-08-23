namespace MiamiGraphics.Shell.Admin;

public sealed record CustomSkinEntry(string InternalName, string DisplayName, string PackId);

public sealed class CustomSkinsState
{
    public int SchemaVersion { get; set; } = 1;

    public List<CustomSkinEntry> Customs { get; set; } = new();

    public List<AnimDetailEntry> AnimDetails { get; set; } = new();
}

public sealed record AnimLooseDesc(string RelPath, string ContentXmlFileType, string? Contents, string? BytesFile);

public sealed record AnimDetailEntry(
    string InternalName,
    string DisplayName,
    string PackId,
    List<AnimLooseDesc> Loose,
    string? UpdateDlcName,
    string? UpdateRelPath,
    string? UpdateBytesFile);
