namespace MiamiGraphics.Shell.Admin;

public sealed class GtaPresetItem
{
    public string Id                  { get; set; } = string.Empty;
    public string Name                { get; set; } = string.Empty;
    public string Description         { get; set; } = string.Empty;
    public string Author              { get; set; } = string.Empty;

    public string XmlUrl              { get; set; } = string.Empty;
    public long   XmlSizeBytes        { get; set; }
    public string XmlSha256           { get; set; } = string.Empty;

    public int?   ExpectedFpsLow      { get; set; }
    public int?   ExpectedFpsHigh     { get; set; }
    public string? BaselineHwLabel    { get; set; }

    public int    ComputedGainPercent { get; set; }
    public string CpuBias             { get; set; } = "balanced";

    public bool   IsTournament        { get; set; }
    public string Status              { get; set; } = "published";
    public int    ViewerPriority      { get; set; }
    public long   DownloadCount       { get; set; }

    public string UploadedBy          { get; set; } = string.Empty;
    public DateTime UploadedAt        { get; set; }
    public DateTime UpdatedAt         { get; set; }
}

public sealed class GtaPresetFilter
{
    public string? SearchText { get; set; }
    public string? Status     { get; set; }
}

public interface IGtaPresetsRepository
{
    Task<List<GtaPresetItem>> ListAsync(GtaPresetFilter? filter = null);
    Task<GtaPresetItem?>      GetByIdAsync(string id);
    Task<GtaPresetItem?>      FindByXmlSha256Async(string sha256);
    Task                      AddAsync(GtaPresetItem item);
    Task                      UpdateAsync(string id, Action<GtaPresetItem> update);
    Task                      DeleteAsync(string id);
    Task<long>                IncrementDownloadsAsync(string id);
}
