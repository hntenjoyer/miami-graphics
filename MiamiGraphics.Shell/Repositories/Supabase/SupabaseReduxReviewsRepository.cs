using MiamiGraphics.Shell.Repositories.Models;
using MiamiGraphics.Shell.Services;
using System.Diagnostics;

namespace MiamiGraphics.Shell.Repositories.Supabase;

internal sealed class SupabaseReduxReviewsRepository : IReduxReviewsRepository
{
    private readonly SupabaseClient _sb;

    public SupabaseReduxReviewsRepository(SupabaseClient sb) { _sb = sb; }

    public async Task<IReadOnlyList<ReduxReview>> ListAsync(string reduxId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reduxId)) return Array.Empty<ReduxReview>();

        var rows = await _sb.SelectAsync<Row>(
            "redux_reviews",
            $"select=*&redux_id=eq.{Uri.EscapeDataString(reduxId)}&order=created_at.desc",
            ct);

        var output = new List<ReduxReview>(rows.Count);
        foreach (var r in rows) output.Add(ToDomain(r));
        return output;
    }

    public async Task<IReadOnlyDictionary<string, ReduxRatingAggregate>> AggregateAllAsync(CancellationToken ct = default)
    {
        var rows = await _sb.SelectAllPagedAsync<AggRow>(
            "redux_reviews", "select=redux_id,rating", ct: ct);

        var sums   = new Dictionary<string, long>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            if (string.IsNullOrEmpty(r.ReduxId)) continue;
            sums.TryGetValue(r.ReduxId, out var s);
            counts.TryGetValue(r.ReduxId, out var c);
            sums[r.ReduxId]   = s + r.Rating;
            counts[r.ReduxId] = c + 1;
        }

        var output = new Dictionary<string, ReduxRatingAggregate>(counts.Count, StringComparer.Ordinal);
        foreach (var (reduxId, count) in counts)
        {
            var avg = count == 0 ? 0 : Math.Round((double)sums[reduxId] / count, 2);
            output[reduxId] = new ReduxRatingAggregate(avg, count);
        }
        return output;
    }

    public async Task<ReduxReview> SubmitAsync(
        string reduxId, string userId, string username, string role, string? avatarUrl, int rating, string body,
        CancellationToken ct = default)
    {

        _ = (userId, username, role, avatarUrl);
        var row = await _sb.RpcSingleAsync<Row>(
            "redux_review_submit_secure",
            new
            {
                p_token    = SupabaseClient.UserSessionToken,
                p_redux_id = reduxId,
                p_rating   = rating,
                p_body     = body,
            },
            ct);

        if (row is null)
        {

            throw new SupabaseException(SupabaseErrorKind.Server, "redux_review_submit returned no row.");
        }
        Debug.WriteLine($"[reviews] submit ok: redux={reduxId} user={userId} rating={rating}");
        return ToDomain(row);
    }

    public async Task<bool> DeleteAsync(string reviewId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reviewId)) return false;

        await _sb.RpcVoidAsync(
            "redux_review_delete_secure",
            new
            {
                p_token     = SupabaseClient.UserSessionToken,
                p_review_id = reviewId,
            },
            ct);
        Debug.WriteLine($"[reviews] delete ok: review={reviewId}");
        return true;
    }

    private static ReduxReview ToDomain(Row r) => new()
    {
        Id        = r.Id        ?? string.Empty,
        ReduxId   = r.ReduxId   ?? string.Empty,
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
        public string? ReduxId { get; set; }
        public int     Rating  { get; set; }
    }

    private sealed class Row
    {
        public string?  Id        { get; set; }
        public string?  ReduxId   { get; set; }
        public string?  UserId    { get; set; }
        public string?  Username  { get; set; }
        public string?  Role      { get; set; }
        public string?  AvatarUrl { get; set; }
        public int      Rating    { get; set; }
        public string?  Body      { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
