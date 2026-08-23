#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MiamiGraphics.Core.Services;

public sealed record OptimizationCatalog(
    IReadOnlyList<OptimizationGroup> Groups,
    IReadOnlyDictionary<string, string> KeyOwners,
    IReadOnlyDictionary<string, string>? FileKeyOwners = null)
{
    public static string FileKey(string targetPath, string key) => targetPath + "|" + key;

    public OptimizationGroup? Find(string groupKey)
        => Groups.FirstOrDefault(g => string.Equals(g.Key, groupKey, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> KeysOwnedBy(string groupKey)
        => KeyOwners.Where(p => string.Equals(p.Value, groupKey, StringComparison.OrdinalIgnoreCase))
                    .Select(p => p.Key)
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();

        foreach (var (key, owner) in KeyOwners)
        {
            if (!GtaSettingsKeyMap.IsKnown(key))
                problems.Add($"ключ '{key}' зарегистрирован за группой '{owner}', но лаунчер его не знает");
            if (Find(owner) is null)
                problems.Add($"ключ '{key}' закреплён за несуществующей группой '{owner}'");
        }

        foreach (var g in Groups)
        {
            if (g.Options.Count == 0)
            {
                problems.Add($"группа '{g.Key}' без вариантов");
                continue;
            }
            if (g.Options.All(o => o.Idx != g.ResetIndex))
                problems.Add($"у группы '{g.Key}' reset_index={g.ResetIndex}, а варианта с таким idx нет");

            foreach (var o in g.Options)
            foreach (var key in o.Settings.Keys)
            {
                if (!KeyOwners.TryGetValue(key, out var owner))
                    problems.Add($"группа '{g.Key}' пишет незарегистрированный ключ '{key}'");
                else if (!string.Equals(owner, g.Key, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"группа '{g.Key}' пишет чужой ключ '{key}' (владелец '{owner}')");
            }

            var fileOwners = FileKeyOwners;
            foreach (var o in g.Options)
            foreach (var e in o.FileEdits)
            {
                var addr = FileKey(e.TargetPath, e.Key);
                if (fileOwners is null || !fileOwners.TryGetValue(addr, out var owner))
                    problems.Add($"группа '{g.Key}' правит незарегистрированный ключ '{addr}'");
                else if (!string.Equals(owner, g.Key, StringComparison.OrdinalIgnoreCase))
                    problems.Add($"группа '{g.Key}' правит чужой ключ '{addr}' (владелец '{owner}')");
            }
        }

        return problems;
    }
}

public sealed record OptimizationGroup(
    string Key,
    string Style,
    IReadOnlyList<string> Methods,
    int ResetIndex,
    IReadOnlyList<string> GenCompatibility,
    IReadOnlyList<string> Tiers,
    int Position,
    bool Beta,
    bool Enabled,
    string IconUrl,
    IReadOnlyDictionary<string, string> Title,
    IReadOnlyDictionary<string, string> Description,
    IReadOnlyList<OptimizationOption> Options)
{
    public bool TouchesSettings => Methods.Contains("setting", StringComparer.OrdinalIgnoreCase);

    public bool SupportsGeneration(string generation)
        => GenCompatibility.Count == 0
        || GenCompatibility.Contains(generation, StringComparer.OrdinalIgnoreCase);

    public bool AvailableForTier(string? tier)
        => Tiers.Count == 0
        || (tier is not null && Tiers.Contains(tier, StringComparer.OrdinalIgnoreCase));

    public OptimizationOption? OptionAt(int idx)
        => Options.FirstOrDefault(o => o.Idx == idx);
}

public sealed record OptimizationOption(
    int Idx,
    IReadOnlyDictionary<string, string> Name,
    IReadOnlyDictionary<string, string> ProgressMessage,
    string PreviewUrl,
    string FpsLabel,
    IReadOnlyDictionary<string, string> Settings,
    IReadOnlyList<OptimizationFileEdit> FileEdits);

public sealed record OptimizationFileEdit(
    string Archive,
    string TargetPath,
    string Key,
    string Value)
{
    public const string ReplaceWholeFile = "@replace";

    public const string RestoreWholeFile = "@restore";

    public const string BuildInterior = "@interior";

    public bool IsWholeFile
        => string.Equals(Key, ReplaceWholeFile, StringComparison.Ordinal)
        || string.Equals(Key, RestoreWholeFile, StringComparison.Ordinal)
        || string.Equals(Key, BuildInterior, StringComparison.Ordinal);

    public bool IsInterior => string.Equals(Key, BuildInterior, StringComparison.Ordinal);
}
