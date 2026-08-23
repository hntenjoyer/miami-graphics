using MiamiGraphics.Shell.Repositories.Models;
using MiamiGraphics.Shell.Services;
using System.Diagnostics;

namespace MiamiGraphics.Shell.Repositories.Supabase;

internal sealed class SupabaseUserBuildReviewsRepository : IUserBuildReviewsRepository
{
    private readonly SupabaseClient _sb;

    public SupabaseUserBuildReviewsRepository(SupabaseClient sb) { _sb = sb; }

    public async Task<IReadOnlyList<UserBuildReview>> ListAsync(string buildId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(buildId)) return Array.Empty<UserBuildReview>();

        var rows = await _sb.SelectAsync<Row>(
            "user_build_reviews",
            $"select=*&user_build_id=eq.{Uri.EscapeDataString(buildId)}&order=created_at.desc",
            ct);

        var output = new List<UserBuildReview>(rows.Count);
        foreach (var r in rows) output.Add(ToDomain(r));
        return output;
    }

    public async Task<UserBuildReview> SubmitAsync(string buildId, int rating, string body, CancellationToken ct = default)
    {
        var row = await _sb.RpcSingleAsync<Row>(
            "user_build_review_submit_secure",
            new
            {
                p_token    = SupabaseClient.UserSessionToken,
                p_build_id = buildId,
                p_rating   = rating,
                p_body     = body,
            },
            ct);

        if (row is null)
            throw new SupabaseException(SupabaseErrorKind.Server, "user_build_review_submit returned no row.");

        Debug.WriteLine($"[buildReviews] submit ok: build={buildId} rating={rating}");
        return ToDomain(row);
    }

    public async Task<bool> DeleteAsync(string reviewId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reviewId)) return false;

        await _sb.RpcVoidAsync(
            "user_build_review_delete_secure",
            new
            {
                p_token     = SupabaseClient.UserSessionToken,
                p_review_id = reviewId,
            },
            ct);
        Debug.WriteLine($"[buildReviews] delete ok: review={reviewId}");
        return true;
    }

    private static UserBuildReview ToDomain(Row r) => new()
    {
        Id          = r.Id          ?? string.Empty,
        UserBuildId = r.UserBuildId ?? string.Empty,
        UserId      = r.UserId      ?? string.Empty,
        Username    = r.Username    ?? string.Empty,
        Role        = r.Role        ?? "User",
        AvatarUrl   = r.AvatarUrl,
        Rating      = r.Rating,
        Body        = r.Body        ?? string.Empty,
        CreatedAt   = r.CreatedAt,
    };

    private sealed class Row
    {
        public string?  Id          { get; set; }
        public string?  UserBuildId { get; set; }
        public string?  UserId      { get; set; }
        public string?  Username    { get; set; }
        public string?  Role        { get; set; }
        public string?  AvatarUrl   { get; set; }
        public int      Rating      { get; set; }
        public string?  Body        { get; set; }
        public DateTime CreatedAt   { get; set; }
    }
}
