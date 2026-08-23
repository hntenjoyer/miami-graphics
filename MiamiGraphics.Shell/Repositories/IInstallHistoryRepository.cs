using MiamiGraphics.Shell.Repositories.Models;

namespace MiamiGraphics.Shell.Repositories;

public interface IInstallHistoryRepository
{
    Task<IReadOnlyList<InstallHistoryEntry>> ListAsync(string userId, CancellationToken ct = default);

    Task<InstallHistoryEntry> RecordAsync(
        string userId,
        string reduxId,
        string name,
        string author,
        string? previewUrl,
        CancellationToken ct = default);
}
