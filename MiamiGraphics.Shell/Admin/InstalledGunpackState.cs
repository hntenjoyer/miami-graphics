namespace MiamiGraphics.Shell.Admin;

public sealed class InstalledGunpackState
{
    public int SchemaVersion { get; set; } = 1;

    public string? ActiveGunpackId { get; set; }

    public string? ActiveGunpackName { get; set; }

    public string? WeaponsRpfSha256 { get; set; }

    public DateTime? InstalledAt { get; set; }
}
