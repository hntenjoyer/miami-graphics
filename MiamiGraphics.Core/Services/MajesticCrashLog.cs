#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MiamiGraphics.Core.Services;

public static class MajesticCrashLog
{
    public sealed record Crash(
        string? Time,
        string? LastAsset,
        string? TextureInfo,
        string? Position,
        int FailedModelRequests,
        bool Oversized,
        bool StreamingFailure,
        string? OversizedName)
    {
        public string Signature =>
            Oversized || StreamingFailure ? "стриминг: запись не влезла (Oversized/ERR_STR_FAILURE)"
            : FailedModelRequests >= 200   ? "сборка педов на спавне (лавина Failed to request model)"
            : "неизвестная";

        public string Describe()
        {
            var parts = new List<string> { $"краш в {Time ?? "?"}", Signature };
            if (!string.IsNullOrEmpty(OversizedName)) parts.Add($"запись: {OversizedName}");
            if (!string.IsNullOrEmpty(LastAsset)) parts.Add($"последний ассет: {LastAsset}");
            if (!string.IsNullOrEmpty(TextureInfo)) parts.Add($"текстуры: {TextureInfo}");
            if (!string.IsNullOrEmpty(Position)) parts.Add($"позиция: {Position}");
            parts.Add($"Failed to request model: {FailedModelRequests}");
            return string.Join(" | ", parts);
        }
    }

    private static readonly Regex TimeRx = new(@"^\[(\d\d:\d\d:\d\d\.\d+)\]", RegexOptions.Compiled);
    private static readonly Regex OversizedRx = new(@"Oversized file \(>100MB\)\s*(\S+)?", RegexOptions.Compiled);

    public static Crash? Parse(IEnumerable<string> lines)
    {
        string? time = null, lastAsset = null, texInfo = null, pos = null, oversizedName = null;
        int failedModels = 0;
        bool minidump = false, oversized = false, strFailure = false;

        foreach (var raw in lines)
        {
            var line = raw ?? string.Empty;

            if (line.Contains("Failed to request model", StringComparison.Ordinal)) { failedModels++; continue; }

            if (line.Contains("MINIDUMP", StringComparison.Ordinal))
            {
                minidump = true;
                time = TimeRx.Match(line) is { Success: true } m ? m.Groups[1].Value : time;
                continue;
            }

            if (line.Contains("Oversized file (>100MB)", StringComparison.Ordinal))
            {
                oversized = true;
                var m = OversizedRx.Match(line);
                if (m.Success && m.Groups[1].Success) oversizedName ??= m.Groups[1].Value;
                continue;
            }

            if (line.Contains("ERR_STR_FAILURE", StringComparison.Ordinal)) { strFailure = true; continue; }

            lastAsset ??= After(line, "Last loaded asset:");
            texInfo   ??= After(line, "Texture info:");
            pos       ??= After(line, "Last pos ");
        }

        if (!minidump && !oversized && !strFailure) return null;

        return new Crash(time, lastAsset, texInfo, pos, failedModels, oversized, strFailure, oversizedName);
    }

    private static string? After(string line, string marker)
    {
        int i = line.IndexOf(marker, StringComparison.Ordinal);
        return i < 0 ? null : line[(i + marker.Length)..].Trim() is { Length: > 0 } s ? s : null;
    }
}
