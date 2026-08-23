using System.Text.Json;
using MiamiGraphics.Shell.Repositories.Models;

namespace MiamiGraphics.Shell.Repositories;

public interface IHntCodesRepository
{
    Task<HntCode> ExportAsync(string userId, JsonElement payload, CancellationToken ct = default);

    Task<HntCode> ImportAsync(string code, CancellationToken ct = default);

    Task<IReadOnlyList<HntCode>> ListMyAsync(string userId, CancellationToken ct = default);

    Task<HntCode> DeleteAsync(string code, string userId, CancellationToken ct = default);
}
