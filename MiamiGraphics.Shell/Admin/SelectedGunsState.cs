namespace MiamiGraphics.Shell.Admin;

public sealed class SelectedGun
{
    public string GunpackId       { get; set; } = string.Empty;
    public string GunpackName     { get; set; } = string.Empty;
    public string GunId           { get; set; } = string.Empty;
    public string InternalName    { get; set; } = string.Empty;
    public string DisplayName     { get; set; } = string.Empty;
    public string BaseName        { get; set; } = string.Empty;
    public string WeaponPrefix    { get; set; } = string.Empty;
    public List<string> Files     { get; set; } = new();
    public string PackZipUrl      { get; set; } = string.Empty;
    public string PackZipSha256   { get; set; } = string.Empty;
    public DateTime SelectedAt    { get; set; }

    public string? ExtractedFromActivePackId { get; set; }
}

public sealed class SelectedGunsState
{
    public int SchemaVersion { get; set; } = 1;

    public List<SelectedGun> Guns { get; set; } = new();

    public DateTime? LastBuiltAt { get; set; }

    public long LastBuiltSize { get; set; }

    public string? LastBuiltSha256 { get; set; }

    public string? LastInjectedSha256 { get; set; }

    public string? VanillaSlotsPackId { get; set; }

    public List<string> VanillaSlotInternalNames { get; set; } = new();
}
