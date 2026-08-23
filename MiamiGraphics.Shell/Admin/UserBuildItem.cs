namespace MiamiGraphics.Shell.Admin;

public sealed class UserBuildItem
{
    public string Id { get; set; } = string.Empty;

    public string HntCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? AuthorUserId { get; set; }
    public string AuthorUsername { get; set; } = string.Empty;

    public string ReduxId   { get; set; } = string.Empty;
    public string GunpackId { get; set; } = string.Empty;

    public string ReduxNameSnapshot   { get; set; } = string.Empty;
    public string GunpackNameSnapshot { get; set; } = string.Empty;

    public string GunSlotsJson { get; set; } = "{}";

    public string? ArmorJson { get; set; }

    public string? ArenaJson { get; set; }

    public string? MinimapJson { get; set; }

    public string? ReticleJson { get; set; }

    public string? SoundsJson { get; set; }

    public long      DownloadCount { get; set; }
    public long      ViewCount     { get; set; }
    public DateTime  CreatedAt     { get; set; }
    public DateTime  UpdatedAt     { get; set; }

    public string? DevicesJson { get; set; }

    public decimal? Sensitivity { get; set; }

    public int? Dpi { get; set; }

    public string? Resolution { get; set; }

    public string? VideoUrl { get; set; }

    public string? SettingsXmlUrl { get; set; }

    public string Description { get; set; } = string.Empty;

    public int? Tier { get; set; }

    public string? CoverUrl { get; set; }

    public string Status { get; set; } = "approved";

    public bool SubmittedForReview { get; set; }

    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? RejectReason { get; set; }

    public string? Family { get; set; }
    public string? CategoryLabel { get; set; }
    public int? FpsAvg { get; set; }
    public int? MonitorHz { get; set; }
    public string? AdminNotes { get; set; }
}

public sealed class UserBuildFilter
{
    public string? SearchText { get; set; }
    public string? AuthorUserId { get; set; }
}
