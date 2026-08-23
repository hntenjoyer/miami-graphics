using MiamiGraphics.Shell.Repositories.Models;
using MiamiGraphics.Shell.Services;
using System.Diagnostics;

namespace MiamiGraphics.Shell.Repositories.Supabase;

internal sealed class SupabaseInstallHistoryRepository : IInstallHistoryRepository
{
    private readonly SupabaseClient _sb;

    public SupabaseInstallHistoryRepository(SupabaseClient sb) { _sb = sb; }

    public async Task<IReadOnlyList<InstallHistoryEntry>> ListAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Array.Empty<InstallHistoryEntry>();

        var rows = await _sb.SelectAsync<Row>(
            "redux_installs",
            $"select=*&user_id=eq.{Uri.EscapeDataString(userId)}&order=installed_at.desc",
            ct);

        var output = new List<InstallHistoryEntry>(rows.Count);
        foreach (var r in rows) output.Add(ToDomain(r));
        return output;
    }

    public async Task<InstallHistoryEntry> RecordAsync(
        string userId, string reduxId, string name, string author, string? previewUrl,
        CancellationToken ct = default)
    {
        var row = await _sb.RpcSingleAsync<Row>(
            "install_record",
            new
            {
                p_user_id     = userId,
                p_redux_id    = reduxId,
                p_name        = name      ?? string.Empty,
                p_author      = author    ?? string.Empty,
                p_preview_url = previewUrl ?? string.Empty,
            },
            ct);

        if (row is null)
            throw new SupabaseException(SupabaseErrorKind.Server, "install_record returned no row.");

        Debug.WriteLine($"[install] record ok: user={userId} redux={reduxId}");
        return ToDomain(row);
    }

    private static InstallHistoryEntry ToDomain(Row r) => new()
    {
        UserId      = r.UserId      ?? string.Empty,
        ReduxId     = r.ReduxId     ?? string.Empty,
        Name        = r.Name        ?? string.Empty,
        Author      = r.Author      ?? string.Empty,
        PreviewUrl  = r.PreviewUrl,
        InstalledAt = r.InstalledAt,
    };

    private sealed class Row
    {
        public string?  UserId      { get; set; }
        public string?  ReduxId     { get; set; }
        public string?  Name        { get; set; }
        public string?  Author      { get; set; }
        public string?  PreviewUrl  { get; set; }
        public DateTime InstalledAt { get; set; }
    }
}
