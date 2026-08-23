namespace MiamiGraphics.Shell.Admin;

public sealed class ReduxVersion
{
    public Guid     Id               { get; set; }
    public string   ReduxId          { get; set; } = string.Empty;
    public int      Slot             { get; set; }
    public string   Label            { get; set; } = string.Empty;

    public string?  PatchUrl         { get; set; }
    public long     PatchSizeBytes   { get; set; }
    public string?  PatchSha256      { get; set; }
    public string?  SourceSha256     { get; set; }
    public string?  TargetGtaVersion { get; set; }

    public Dictionary<string, ReduxComponentInfo> Components    { get; set; } = new();
    public Dictionary<string, string>             ComponentUrls { get; set; } = new();

    public string?  ManifestUrl      { get; set; }
    public string?  ComponentMapUrl  { get; set; }
    public string?  ContentInfoUrl   { get; set; }

    public DateTime CreatedAt        { get; set; }
    public DateTime UpdatedAt        { get; set; }
}
