#nullable enable
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace MiamiGraphics.Core.Services;

public static class RpfEntryGate
{
    private static readonly ConcurrentDictionary<string, byte[]> _fitted = new();

    private const int FittedCap = 4;

    private static string HashOf(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    public static bool TryPrepare(string? name, byte[]? bytes,
        out byte[] prepared, out string? reason, out string? note)
    {
        prepared = bytes ?? Array.Empty<byte>();
        note = null;
        reason = RpfEntrySanity.RejectReason(name, bytes);
        if (reason == null) return true;

        if (bytes == null || !RpfResourceBudget.IsOverBudget(bytes)) return false;

        var key = HashOf(bytes);
        if (_fitted.TryGetValue(key, out var cached))
        {
            prepared = cached;
            note = "ужат под стриминг (повтор того же файла)";
            reason = null;
            return true;
        }

        var fitted = GunResourceFitter.Fit(name!, bytes, out var rep);
        if (!rep.Fits) return false;

        if (_fitted.Count >= FittedCap) _fitted.Clear();
        _fitted[key] = fitted;

        prepared = fitted;
        note = $"ужат под стриминг: {rep.Describe()}";
        reason = null;
        return true;
    }
}
