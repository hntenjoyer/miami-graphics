using System.Text.Json.Serialization;

namespace MiamiGraphics.Shell.Admin;

public sealed class GunpackQueueItem
{
    public string TempId { get; set; } = string.Empty;

    public GunpackItem Metadata { get; set; } = new();

    public string SourceDlcRpfPath { get; set; } = string.Empty;
    public string TempWorkDir      { get; set; } = string.Empty;

    public bool   UploadToR2 { get; set; } = true;

    public string  Status        { get; set; } = "pending";
    public int?    Percent       { get; set; }
    public string? CurrentPhase  { get; set; }

    public string? ErrorMessage  { get; set; }
    public DateTime AddedAt      { get; set; }

    public List<string>? Warnings { get; set; }

    public VariantUploadContext? Variant { get; set; }

    [JsonIgnore]
    public string ServiceRoleKey { get; set; } = string.Empty;
}

public sealed class VariantUploadContext
{
    public string PackId { get; set; } = string.Empty;

    public Guid   VariantId { get; set; }

    public string Name { get; set; } = "Default";

    public string? CoverImagePath { get; set; }

    public string ServiceRoleKey { get; set; } = string.Empty;
}

public interface IAdminGunpackQueueService
{
    Task<List<GunpackQueueItem>> ListAsync();

    Task<GunpackQueueItem> EnqueueAndStartAsync(
        GunpackQueueItem item,
        Action<GunpackQueueItem>? emit,
        CancellationToken ct);

    Task RemoveAsync(string tempId);

    void Cancel();

    Task<int> ReconcileOrphansAsync();
}
