#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace MiamiGraphics.Core.Services;

public sealed class OptimizationApplyService
{
    public sealed record Selection(string GroupKey, int? OptionIdx);

    public sealed record KeyChange(string Key, string? From, string To, string GroupKey);

    public sealed record Outcome(
        bool Success,
        string? ErrorMessage,
        IReadOnlyList<KeyChange> Changes,
        IReadOnlyList<string> Warnings,
        string TargetPath,
        string? BackupPath,
        bool GameWasRunning,
        bool BaselineCaptured);

    private readonly OptimizationBaseline _baseline;
    private readonly GtaSettingsApplier _applier;

    public OptimizationApplyService(
        OptimizationBaseline? baseline = null,
        GtaSettingsApplier? applier = null)
    {
        _baseline = baseline ?? new OptimizationBaseline();
        _applier  = applier  ?? new GtaSettingsApplier();
    }

    public async Task<Outcome> ApplyAsync(
        IReadOnlyList<Selection> selections,
        OptimizationCatalog catalog,
        string gameGeneration,
        string? userTier,
        CancellationToken ct = default)
    {
        var targetPath = GtaSettingsApplier.GetSettingsPath();
        var warnings = new List<string>();

        var existed = File.Exists(targetPath);
        string rawXml;
        try
        {
            rawXml = existed ? File.ReadAllText(targetPath) : GtaSettingsModel.Defaults().ToXml();
        }
        catch (Exception ex)
        {
            return Fail(targetPath, $"не удалось прочитать settings.xml: {ex.Message}");
        }
        if (!existed)
            warnings.Add("settings.xml не найден - применяем поверх ванильных значений, игра создаст файл сама");

        var model = GtaSettingsModel.FromXml(rawXml);

        var captured = _baseline.EnsureCaptured(model, targetPath);

        var problems = catalog.Validate();
        if (problems.Count > 0)
            return Fail(targetPath, "каталог оптимизаций противоречив: " + string.Join("; ", problems.Take(3)));

        var patch = new Dictionary<string, (string Value, string GroupKey)>(StringComparer.OrdinalIgnoreCase);

        foreach (var sel in selections)
        {
            ct.ThrowIfCancellationRequested();

            var group = catalog.Find(sel.GroupKey);
            if (group is null)
                return Fail(targetPath, $"группа '{sel.GroupKey}' не найдена в каталоге");

            if (!group.Enabled)          { warnings.Add($"группа '{group.Key}' отключена - пропускаем"); continue; }
            if (!group.TouchesSettings)  { continue; }
            if (!group.SupportsGeneration(gameGeneration))
                return Fail(targetPath, $"группа '{group.Key}' не поддерживает {gameGeneration}");
            if (!group.AvailableForTier(userTier))
                return Fail(targetPath, $"группа '{group.Key}' требует подписку");

            var pairs = sel.OptionIdx is { } idx
                ? ResolveOption(group, idx, targetPath, out var optErr) ?? throw new InvalidOperationException(optErr)
                : ResolveReset(group, catalog);

            foreach (var (key, value) in pairs)
            {
                if (patch.TryGetValue(key, out var prev) && !string.Equals(prev.GroupKey, group.Key, StringComparison.OrdinalIgnoreCase))
                    return Fail(targetPath,
                        $"группы '{prev.GroupKey}' и '{group.Key}' обе пишут ключ '{key}' - применение остановлено");

                patch[key] = (value, group.Key);
            }
        }

        if (patch.Count == 0)
            return new Outcome(true, null, Array.Empty<KeyChange>(), warnings, targetPath, null, false, captured);

        var changes = new List<KeyChange>();
        foreach (var (key, (value, groupKey)) in patch.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var before = GtaSettingsKeyMap.Read(model, key);
            if (!GtaSettingsKeyMap.TryWrite(model, key, value, out var err))
                return Fail(targetPath, $"группа '{groupKey}': {err}");

            var after = GtaSettingsKeyMap.Read(model, key) ?? value;
            if (!string.Equals(before, after, StringComparison.Ordinal))
                changes.Add(new KeyChange(key, before, after, groupKey));
        }

        if (changes.Count == 0)
            return new Outcome(true, null, changes, warnings, targetPath, null, false, captured);

        XDocument doc;
        try { doc = XDocument.Parse(rawXml); }
        catch { doc = XDocument.Parse(model.ToXml()); }
        model.ApplyTo(doc);

        var result = await _applier.ApplyAsync(doc.ToString(), ct).ConfigureAwait(false);

        return new Outcome(
            result.Success,
            result.ErrorMessage,
            result.Success ? changes : Array.Empty<KeyChange>(),
            warnings,
            result.TargetPath,
            result.BackupPath,
            result.GameWasRunning,
            captured);
    }

    private static IReadOnlyDictionary<string, string>? ResolveOption(
        OptimizationGroup group, int idx, string targetPath, out string? error)
    {
        var option = group.OptionAt(idx);
        if (option is null)
        {
            error = $"у группы '{group.Key}' нет варианта {idx}";
            return null;
        }
        error = null;
        return option.Settings;
    }

    private IReadOnlyDictionary<string, string> ResolveReset(OptimizationGroup group, OptimizationCatalog catalog)
    {
        var defaults = GtaSettingsModel.Defaults();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in catalog.KeysOwnedBy(group.Key))
        {
            var value = _baseline.ValueOf(key) ?? GtaSettingsKeyMap.Read(defaults, key);
            if (value is not null) result[key] = value;
        }
        return result;
    }

    private static Outcome Fail(string targetPath, string message)
        => new(false, message, Array.Empty<KeyChange>(), Array.Empty<string>(), targetPath, null, false, false);
}
