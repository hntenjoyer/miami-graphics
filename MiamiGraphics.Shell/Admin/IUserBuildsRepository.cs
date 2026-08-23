namespace MiamiGraphics.Shell.Admin;

public interface IUserBuildsRepository
{
    Task<List<UserBuildItem>> ListAsync(UserBuildFilter? filter = null);

    Task<UserBuildItem?> GetByIdAsync(string id);

    Task<UserBuildItem?> GetByHntCodeAsync(string hntCode);

    Task<UserBuildItem> AddAsync(UserBuildItem item);

    Task DeleteAsync(string id);

    Task<long> IncrementDownloadsAsync(string id);

    Task<long> IncrementViewsAsync(string id);

    Task<UserBuildItem> SubmitAsync(UserBuildItem item);

    Task<UserBuildItem> UpdateAsync(string id, IReadOnlyDictionary<string, object?> patch);

    Task<List<UserBuildItem>> ListPendingAsync();

    Task<List<UserBuildItem>> ListMyPendingAsync(string authorUserId);

    Task<UserBuildItem> ApproveAsync(string id, string reviewerUserId, int? tier);

    Task<UserBuildItem> RejectAsync(string id, string reviewerUserId, string reason);

    Task<UserBuildItem> ResubmitAsync(string id);
}
