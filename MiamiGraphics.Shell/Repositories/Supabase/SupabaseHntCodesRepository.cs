using System.Diagnostics;
using System.Text.Json;
using MiamiGraphics.Shell.Repositories.Models;
using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Repositories.Supabase;

internal sealed class SupabaseHntCodesRepository : IHntCodesRepository
{
    private readonly SupabaseClient _sb;

    public SupabaseHntCodesRepository(SupabaseClient sb) { _sb = sb; }

    public async Task<HntCode> ExportAsync(string userId, JsonElement payload, CancellationToken ct = default)
    {
        var row = await _sb.RpcSingleAsync<Row>(
            "hnt_export",
            new
            {
                p_user_id = userId,
                p_payload = payload,
            },
            ct);

        if (row is null)
            throw new SupabaseException(SupabaseErrorKind.Server, "hnt_export returned no row.");

        Debug.WriteLine($"[hnt.export] code={row.Code} user={userId}");
        return ToDomain(row);
    }

    public async Task<HntCode> ImportAsync(string code, CancellationToken ct = default)
    {

        var row = await _sb.RpcSingleAsync<Row>(
            "hnt_import",
            new { p_code = code },
            ct);

        if (row is null)
            throw new SupabaseException(SupabaseErrorKind.Server, "hnt_import returned no row.");

        Debug.WriteLine($"[hnt.import] code={row.Code} downloads={row.DownloadsCount}");
        return ToDomain(row);
    }

    public async Task<HntCode> DeleteAsync(string code, string userId, CancellationToken ct = default)
    {
        var row = await _sb.RpcSingleAsync<Row>(
            "hnt_delete",
            new { p_code = code, p_user_id = userId },
            ct);

        if (row is null)
            throw new SupabaseException(SupabaseErrorKind.Server, "hnt_delete returned no row.");

        Debug.WriteLine($"[hnt.delete] code={row.Code} user={userId}");
        return ToDomain(row);
    }

    public async Task<IReadOnlyList<HntCode>> ListMyAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return Array.Empty<HntCode>();

        var rows = await _sb.RpcManyAsync<Row>(
            "hnt_my_codes",
            new { p_user_id = userId },
            ct);

        var output = new List<HntCode>(rows.Count);
        foreach (var r in rows) output.Add(ToDomain(r));
        return output;
    }

    private static HntCode ToDomain(Row r) => new()
    {
        Code             = r.Code             ?? string.Empty,
        Payload          = r.Payload,
        CreatedBy        = r.CreatedBy        ?? string.Empty,
        CreatedAt        = r.CreatedAt,
        LastDownloadedAt = r.LastDownloadedAt,
        DownloadsCount   = r.DownloadsCount,
    };

    private sealed class Row
    {
        public string?     Code             { get; set; }
        public JsonElement Payload          { get; set; }
        public string?     CreatedBy        { get; set; }
        public DateTime    CreatedAt        { get; set; }
        public DateTime    LastDownloadedAt { get; set; }
        public int         DownloadsCount   { get; set; }
    }
}
