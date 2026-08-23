namespace MiamiGraphics.Shell.Admin;

public interface IReduxVersionsRepository
{
    Task<List<ReduxVersion>> ListByReduxAsync(string reduxId);

    Task<ReduxVersion?> FindByPatchSha256Async(string sha256);

    Task<ReduxVersion?> FindBySourceSha256Async(string sha256);

    Task UpsertAsync(ReduxVersion version);

    Task DeleteAsync(Guid id);
}
