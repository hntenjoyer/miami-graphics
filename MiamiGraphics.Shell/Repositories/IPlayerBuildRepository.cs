using MiamiGraphics.Shell.Repositories.Models;

namespace MiamiGraphics.Shell.Repositories;

public interface IPlayerBuildRepository
{
    Task<IReadOnlyList<PlayerBuild>> GetAllAsync();
    Task<PlayerBuild?> GetByIdAsync(string id);
    Task UpsertAsync(PlayerBuild build);
    Task DeleteAsync(string id);
}
