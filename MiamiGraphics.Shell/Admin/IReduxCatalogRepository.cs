namespace MiamiGraphics.Shell.Admin;

public interface IReduxCatalogRepository
{
    Task<List<ReduxItem>> ListAsync(ReduxFilter? filter = null);
    Task<ReduxItem?> GetByIdAsync(string id);
    Task<ReduxItem?> FindByPatchSha256Async(string sha256);
    Task AddAsync(ReduxItem item);
    Task UpdateAsync(string id, Action<ReduxItem> update);
    Task DeleteAsync(string id);
}
