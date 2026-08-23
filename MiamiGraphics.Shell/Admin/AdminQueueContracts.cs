using MiamiGraphics.Core.System;

namespace MiamiGraphics.Shell.Admin;

public sealed class QueueItem
{
    public string TempId { get; set; } = string.Empty;
    public ReduxItem Metadata { get; set; } = new();
    public string SourceUpdateRpfPath { get; set; } = string.Empty;
    public string TempWorkDir { get; set; } = string.Empty;
    public bool UploadToR2 { get; set; }
    public string Status { get; set; } = "pending";
    public int? Percent { get; set; }
    public string? CurrentPhase { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime AddedAt { get; set; }

    public List<VersionSpec>? Versions { get; set; }

    public string? AppendToReduxId { get; set; }
}

public sealed class VersionSpec
{
    public int    Slot                { get; set; }
    public string Label               { get; set; } = string.Empty;
    public string SourceUpdateRpfPath { get; set; } = string.Empty;
    public string TempWorkDir         { get; set; } = string.Empty;
    public long   SizeBytes           { get; set; }
    public string TargetGtaVersion    { get; set; } = string.Empty;
    public Dictionary<string, ReduxComponentInfo> Components { get; set; } = new();
    public string SourceSha256        { get; set; } = string.Empty;
}

public interface IAdminQueueService
{
    Task<List<QueueItem>> ListAsync();
    Task<QueueItem> AddAsync(QueueItem item);
    Task RemoveAsync(string tempId);
    Task RunAsync(IProgress<QueueItem>? progress, CancellationToken ct);
    void Cancel();
    Task<int> ReconcileOrphansAsync();

    Task<int> RebuildComponentsIndexAsync();

    Task<int> RecalculatePatchSizesAsync(CancellationToken ct);
}
