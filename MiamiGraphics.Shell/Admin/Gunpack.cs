namespace MiamiGraphics.Shell.Admin;

public sealed class GunpackItem
{
    public string Id           { get; set; } = string.Empty;
    public string Name         { get; set; } = string.Empty;
    public string Author       { get; set; } = string.Empty;
    public string AuthorLink   { get; set; } = string.Empty;
    public string Description  { get; set; } = string.Empty;

    public string WeaponsRpfUrl    { get; set; } = string.Empty;
    public long   WeaponsRpfSize   { get; set; }
    public string WeaponsRpfSha256 { get; set; } = string.Empty;

    public string? PackZipUrl    { get; set; }
    public long?   PackZipSize   { get; set; }
    public string? PackZipSha256 { get; set; }

    public string? ManifestUrl { get; set; }

    public string       CoverKind   { get; set; } = "image";
    public string?      CoverUrl    { get; set; }
    public List<string> GalleryUrls { get; set; } = new();

    public string Status         { get; set; } = "published";
    public bool   IsVerified     { get; set; }
    public int    ViewerPriority { get; set; }
    public long   DownloadCount  { get; set; }

    public DateTime UploadedAt { get; set; }
    public string   UploadedBy { get; set; } = string.Empty;
    public DateTime UpdatedAt  { get; set; }
    public string?  Notes      { get; set; }
}

public sealed class GunpackGun
{
    public Guid   Id            { get; set; }
    public string GunpackId     { get; set; } = string.Empty;
    public string BaseName      { get; set; } = string.Empty;
    public string WeaponPrefix  { get; set; } = string.Empty;
    public string Category      { get; set; } = string.Empty;
    public string? DisplayName  { get; set; }
    public string? GlbUrl       { get; set; }
    public string? PreviewUrl   { get; set; }
    public List<string> Files   { get; set; } = new();
    public long   SizeBytes     { get; set; }
    public bool   IsHidden      { get; set; }
    public int    SortOrder     { get; set; }
    public DateTime CreatedAt   { get; set; }
}

public sealed class GunpackVariant
{
    public Guid     Id                { get; set; }
    public string   GunpackId         { get; set; } = string.Empty;
    public string   Name              { get; set; } = "Default";

    public string   WeaponsRpfUrl     { get; set; } = string.Empty;
    public long     WeaponsRpfSize    { get; set; }
    public string   WeaponsRpfSha256  { get; set; } = string.Empty;

    public string?  PackZipUrl        { get; set; }
    public long?    PackZipSize       { get; set; }
    public string?  PackZipSha256     { get; set; }

    public string?  ManifestUrl       { get; set; }
    public string?  CoverUrl          { get; set; }

    public bool     IsDefault         { get; set; }
    public int      SortOrder         { get; set; }
    public DateTime CreatedAt         { get; set; }
    public DateTime UpdatedAt         { get; set; }

    public Dictionary<string, VariantGun>? GunPreviews { get; set; }
}

public sealed class VariantGun
{
    public string? Glb  { get; set; }
    public string? Webp { get; set; }

    public string? DisplayName  { get; set; }
    public string? Category     { get; set; }
    public string? WeaponPrefix { get; set; }
    public List<string>? Files  { get; set; }
    public long?   SizeBytes    { get; set; }
    public int?    SortOrder    { get; set; }
}

public sealed class GunpackWhitelistEntry
{
    public string InternalName   { get; set; } = string.Empty;
    public string DisplayName    { get; set; } = string.Empty;
    public string Category       { get; set; } = string.Empty;
    public string WeaponPrefix   { get; set; } = string.Empty;
    public bool   IsSmgOverride  { get; set; }
    public int    SortOrder      { get; set; }
    public string? PreviewUrl    { get; set; }
}

public sealed class GunpackFilter
{
    public string? SearchText { get; set; }
    public string? Status     { get; set; }
    public string? Category   { get; set; }
}
