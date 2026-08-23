using MiamiGraphics.Shell.Repositories.Models;

namespace MiamiGraphics.Shell.Repositories;

public interface IModRepository
{
    Task<IReadOnlyList<Mod>> GetAllAsync();
    Task<Mod?> GetByIdAsync(string id);
    Task UpsertAsync(Mod mod);
    Task DeleteAsync(string id);
}
