using MiamiGraphics.Core.Services;
using MiamiGraphics.Shell.Services;

namespace MiamiGraphics.Shell.Admin;

public interface IOptimizationCatalogRepository
{
    Task<OptimizationCatalog> GetAsync(CancellationToken ct = default);
}

public sealed class SupabaseOptimizationCatalogRepository : IOptimizationCatalogRepository
{
    private readonly SupabaseClient _sb;

    public SupabaseOptimizationCatalogRepository(SupabaseClient sb) => _sb = sb;

    private static OptimizationCatalog? _cache;
    private static DateTime _cacheAt;
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public static void InvalidateCache() { _cache = null; }

    public async Task<OptimizationCatalog> GetAsync(CancellationToken ct = default)
    {
        if (_cache != null && DateTime.UtcNow - _cacheAt < CacheFor) return _cache;

        await _gate.WaitAsync(ct);
        try
        {
            if (_cache != null && DateTime.UtcNow - _cacheAt < CacheFor) return _cache;
            var built = await FetchAsync(ct);
            _cache = built; _cacheAt = DateTime.UtcNow;
            return built;
        }
        finally { _gate.Release(); }
    }

    private async Task<OptimizationCatalog> FetchAsync(CancellationToken ct)
    {
        var groupsT   = _sb.SelectAsync<GroupRow>("optimization_groups", "select=*&enabled=eq.true&order=position.asc", ct);
        var optionsT  = _sb.SelectAsync<OptionRow>("optimization_options", "select=*&order=idx.asc", ct);
        var settingsT = _sb.SelectAsync<OptionSettingRow>("optimization_option_settings", "select=*", ct);
        var ownersT   = _sb.SelectAsync<KeyOwnerRow>("optimization_key_owner", "select=*", ct);
        var editsT    = _sb.SelectAsync<OptionFileEditRow>("optimization_option_file_edits", "select=*", ct);
        var fOwnersT  = _sb.SelectAsync<FileKeyOwnerRow>("optimization_file_key_owner", "select=*", ct);

        await Task.WhenAll(groupsT, optionsT, settingsT, ownersT, editsT, fOwnersT);

        var groupRows     = groupsT.Result;
        var optionRows    = optionsT.Result;
        var settingRows   = settingsT.Result;
        var ownerRows     = ownersT.Result;
        var fileEditRows  = editsT.Result;
        var fileOwnerRows = fOwnersT.Result;

        var settingsByOption = settingRows
            .GroupBy(s => s.OptionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, string>)g
                        .GroupBy(x => x.SettingKey, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(x => x.Key, x => x.First().SettingValue, StringComparer.OrdinalIgnoreCase));

        var optionsByGroup = optionRows
            .GroupBy(o => o.GroupId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Idx).ToList());

        var editsByOption = fileEditRows
            .GroupBy(e => e.OptionId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<OptimizationFileEdit>)g
                        .Select(e => new OptimizationFileEdit(
                            string.IsNullOrWhiteSpace(e.Archive) ? @"update\update.rpf" : e.Archive,
                            e.TargetPath, e.EditKey, e.EditValue))
                        .ToList());

        var groupIdToKey = groupRows.ToDictionary(g => g.Id, g => g.Key);

        var groups = groupRows.Select(g => new OptimizationGroup(
            Key:               g.Key,
            Style:             g.Style ?? "toggle",
            Methods:           g.Methods ?? new List<string> { "setting" },
            ResetIndex:        g.ResetIndex,
            GenCompatibility:  g.GenCompatibility ?? new List<string>(),
            Tiers:             g.Tiers ?? new List<string>(),
            Position:          g.Position,
            Beta:              g.Beta,
            Enabled:           g.Enabled,
            IconUrl:           g.IconUrl ?? string.Empty,
            Title:             g.Title ?? new Dictionary<string, string>(),
            Description:       g.Description ?? new Dictionary<string, string>(),
            Options:           (optionsByGroup.TryGetValue(g.Id, out var opts) ? opts : new List<OptionRow>())
                                   .Select(o => new OptimizationOption(
                                       Idx:             o.Idx,
                                       Name:            o.Name ?? new Dictionary<string, string>(),
                                       ProgressMessage: o.ProgressMessage ?? new Dictionary<string, string>(),
                                       PreviewUrl:      o.PreviewUrl ?? string.Empty,
                                       FpsLabel:        o.FpsLabel ?? string.Empty,
                                       Settings:        settingsByOption.TryGetValue(o.Id, out var s)
                                                            ? s
                                                            : new Dictionary<string, string>(),
                                       FileEdits:       editsByOption.TryGetValue(o.Id, out var fe)
                                                            ? fe
                                                            : Array.Empty<OptimizationFileEdit>()))
                                   .ToList()))
            .ToList();

        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in ownerRows)
            if (groupIdToKey.TryGetValue(o.GroupId, out var key))
                owners[o.SettingKey] = key;

        var fileOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in fileOwnerRows)
            if (groupIdToKey.TryGetValue(o.GroupId, out var key))
                fileOwners[OptimizationCatalog.FileKey(o.TargetPath, o.EditKey)] = key;

        return new OptimizationCatalog(groups, owners, fileOwners);
    }

    private sealed class GroupRow
    {
        public string Id { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string? Style { get; set; }
        public List<string>? Methods { get; set; }
        public int ResetIndex { get; set; }
        public List<string>? GenCompatibility { get; set; }
        public List<string>? Tiers { get; set; }
        public int Position { get; set; }
        public bool Beta { get; set; }
        public bool Enabled { get; set; } = true;
        public string? IconUrl { get; set; }
        public Dictionary<string, string>? Title { get; set; }
        public Dictionary<string, string>? Description { get; set; }
    }

    private sealed class OptionRow
    {
        public string Id { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public int Idx { get; set; }
        public Dictionary<string, string>? Name { get; set; }
        public Dictionary<string, string>? ProgressMessage { get; set; }
        public string? PreviewUrl { get; set; }
        public string? FpsLabel { get; set; }
    }

    private sealed class OptionSettingRow
    {
        public string OptionId { get; set; } = string.Empty;
        public string SettingKey { get; set; } = string.Empty;
        public string SettingValue { get; set; } = string.Empty;
    }

    private sealed class KeyOwnerRow
    {
        public string SettingKey { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    private sealed class OptionFileEditRow
    {
        public string OptionId { get; set; } = string.Empty;
        public string? Archive { get; set; }
        public string TargetPath { get; set; } = string.Empty;
        public string EditKey { get; set; } = string.Empty;
        public string EditValue { get; set; } = string.Empty;
    }

    private sealed class FileKeyOwnerRow
    {
        public string TargetPath { get; set; } = string.Empty;
        public string EditKey { get; set; } = string.Empty;
        public string GroupId { get; set; } = string.Empty;
        public string? Note { get; set; }
    }
}
