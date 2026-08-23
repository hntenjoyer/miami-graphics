using MiamiGraphics.Shell.Repositories.Models;
using MiamiGraphics.Shell.Services;
using System.Diagnostics;

namespace MiamiGraphics.Shell.Repositories.Supabase;

internal sealed class SupabaseBigMapReviewsRepository : IBigMapReviewsRepository
{
    private readonly SupabaseClient _sb;

    public SupabaseBigMapReviewsRepository(SupabaseClient sb) { _sb = sb; }

    public async Task<IReadOnlyList<BigMapReview>> ListAsync(string mapId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(mapId)) return Array.Empty<BigMapReview>();

        var rows = await _sb.SelectAsync<Row>(
            "big_map_reviews",
            $"select=*&map_id=eq.{Uri.EscapeDataString(mapId)}&order=created_at.desc",
            ct);

        var output = new List<BigMapReview>(rows.Count);
        foreach (var r in rows) output.Add(ToDomain(r));
        return output;
    }

    public async Task<IReadOnlyDictionary<string, BigMapRatingAggregate>> AggregateAllAsync(CancellationToken ct = default)
    {
        var rows = await _sb.SelectAllPagedAsync<AggRow>(
            "big_map_reviews", "select=map_id,rating", ct: ct);

        var sums   = new Dictionary<string, long>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (string.IsNullOrEmpty(r.MapId)) continue;
            sums.TryGetValue(r.MapId, out var s);
            counts.TryGetValue(r.MapId, out var c);
            sums[r.MapId]   = s + r.Rating;
            counts[r.MapId] = c + 1;
        }

        var output = new Dictionary<string, BigMapRatingAggregate>(counts.Count, StringComparer.Ordinal);
        foreach (var (mapId, count) in counts)
        {
            var avg = count == 0 ? 0 : Math.Round((double)sums[mapId] / count, 2);
            output[mapId] = new BigMapRatingAggregate(avg, count);
        }
        return output;
    }

    public async Task<BigMapReview> SubmitAsync(
        string mapId, string userId, string username, string role, string? avatarUrl, int rating, string body,
        CancellationToken ct = default)
    {
        _ = (userId, username, role, avatarUrl);
        var row = await _sb.RpcSingleAsync<Row>(
            "big_map_review_submit_secure",
            new
            {
                p_token  = SupabaseClient.UserSessionToken,
                p_map_id = mapId,
                p_rating = rating,
                p_body   = body,
            },
            ct);

        if (row is null)
            throw new SupabaseException(SupabaseErrorKind.Server, "big_map_review_submit returned no row.");

        Debug.WriteLine($"[bigmap.reviews] submit ok: map={mapId} user={userId} rating={rating}");
        return ToDomain(row);
    }

    public async Task<bool> DeleteAsync(string reviewId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reviewId)) return false;

        await _sb.RpcVoidAsync(
            "big_map_review_delete_secure",
            new
            {
                p_token     = SupabaseClient.UserSessionToken,
                p_review_id = reviewId,
            },
            ct);
        Debug.WriteLine($"[bigmap.reviews] delete ok: review={reviewId}");
        return true;
    }

    private static BigMapReview ToDomain(Row r) => new()
    {
        Id        = r.Id        ?? string.Empty,
        MapId     = r.MapId     ?? string.Empty,
        UserId    = r.UserId    ?? string.Empty,
        Username  = r.Username  ?? string.Empty,
        Role      = r.Role      ?? "User",
        AvatarUrl = r.AvatarUrl,
        Rating    = r.Rating,
        Body      = r.Body      ?? string.Empty,
        CreatedAt = r.CreatedAt,
    };

    private sealed class AggRow
    {
        public string? MapId  { get; set; }
        public int     Rating { get; set; }
    }

    private sealed class Row
    {
        public string?  Id        { get; set; }
        public string?  MapId     { get; set; }
        public string?  UserId    { get; set; }
        public string?  Username  { get; set; }
        public string?  Role      { get; set; }
        public string?  AvatarUrl { get; set; }
        public int      Rating    { get; set; }
        public string?  Body      { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
