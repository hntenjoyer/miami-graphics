using MiamiGraphics.Shell.Repositories.Models;

namespace MiamiGraphics.Shell.Repositories;

public readonly record struct BigMapRatingAggregate(double Avg, int Count);

public interface IBigMapReviewsRepository
{
    Task<IReadOnlyList<BigMapReview>> ListAsync(string mapId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<string, BigMapRatingAggregate>> AggregateAllAsync(CancellationToken ct = default);

    Task<BigMapReview> SubmitAsync(
        string  mapId,
        string  userId,
        string  username,
        string  role,
        string? avatarUrl,
        int     rating,
        string  body,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(string reviewId, CancellationToken ct = default);
}
