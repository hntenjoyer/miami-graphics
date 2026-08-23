using MiamiGraphics.Shell.Repositories.Models;

namespace MiamiGraphics.Shell.Repositories;

public readonly record struct ReduxRatingAggregate(double Avg, int Count);

public interface IReduxReviewsRepository
{
    Task<IReadOnlyList<ReduxReview>> ListAsync(string reduxId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, ReduxRatingAggregate>> AggregateAllAsync(CancellationToken ct = default);

    Task<ReduxReview> SubmitAsync(
        string  reduxId,
        string  userId,
        string  username,
        string  role,
        string? avatarUrl,
        int     rating,
        string  body,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(string reviewId, CancellationToken ct = default);
}
