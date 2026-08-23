using MiamiGraphics.Shell.Repositories.Models;

namespace MiamiGraphics.Shell.Repositories;

public interface IUserBuildReviewsRepository
{
    Task<IReadOnlyList<UserBuildReview>> ListAsync(string buildId, CancellationToken ct = default);

    Task<UserBuildReview> SubmitAsync(string buildId, int rating, string body, CancellationToken ct = default);

    Task<bool> DeleteAsync(string reviewId, CancellationToken ct = default);
}
