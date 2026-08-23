#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MiamiGraphics.Core.Services;

public static class OptimizationPresetResolver
{
    public sealed record Resolution(
        IReadOnlyDictionary<string, int?> Selections,
        IReadOnlyList<string> UnmappedKeys,
        IReadOnlyList<string> CustomGroups);

    public static Resolution Resolve(string presetXml, OptimizationCatalog catalog)
        => Resolve(GtaSettingsModel.FromXml(presetXml), catalog);

    public static Resolution Resolve(GtaSettingsModel preset, OptimizationCatalog catalog)
    {
        var selections = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        var custom = new List<string>();
        var explained = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in catalog.Groups.Where(g => g.TouchesSettings))
        {
            var owned = catalog.KeysOwnedBy(group.Key);
            foreach (var k in owned) explained.Add(k);

            var match = BestMatch(group, preset);
            selections[group.Key] = match;
            if (match is null) custom.Add(group.Key);
        }

        var defaults = GtaSettingsModel.Defaults();
        var unmapped = GtaSettingsKeyMap.KnownKeys
            .Where(k => !explained.Contains(k))
            .Where(k => !string.Equals(
                GtaSettingsKeyMap.Read(preset, k),
                GtaSettingsKeyMap.Read(defaults, k),
                StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new Resolution(selections, unmapped, custom);
    }

    private static int? BestMatch(OptimizationGroup group, GtaSettingsModel preset)
    {
        foreach (var option in Meaningful(group))
        {
            var all = true;
            foreach (var (key, expected) in option.Settings)
            {
                var actual = GtaSettingsKeyMap.Read(preset, key);
                if (actual is null || !SameValue(actual, expected)) { all = false; break; }
            }
            if (all) return option.Idx;
        }

        return ClampMatch(group, preset);
    }

    private static IEnumerable<OptimizationOption> Meaningful(OptimizationGroup group)
        => group.Options.Where(o => o.Settings.Count > 0);

    private static int? ClampMatch(OptimizationGroup group, GtaSettingsModel preset)
    {
        var options = Meaningful(group).ToList();
        if (options.Count == 0) return null;

        var bounds = ScaleBounds(options);

        foreach (var option in options)
        {
            var all = true;
            foreach (var (key, expected) in option.Settings)
            {
                var actual = GtaSettingsKeyMap.Read(preset, key);
                if (actual is null) { all = false; break; }
                if (SameValue(actual, expected)) continue;

                if (!bounds.TryGetValue(key, out var scale)
                    || !TryNumber(actual, out var a)
                    || !TryNumber(expected, out var e)) { all = false; break; }

                bool below = a < scale.Min - Eps && Math.Abs(e - scale.Min) < Eps;
                bool above = a > scale.Max + Eps && Math.Abs(e - scale.Max) < Eps;
                if (!below && !above) { all = false; break; }
            }
            if (all) return option.Idx;
        }

        return null;
    }

    private static Dictionary<string, (double Min, double Max)> ScaleBounds(
        IReadOnlyList<OptimizationOption> options)
    {
        var bounds = new Dictionary<string, (double Min, double Max)>(StringComparer.OrdinalIgnoreCase);
        var broken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var o in options)
            foreach (var (key, raw) in o.Settings)
            {
                if (!TryNumber(raw, out var v)) { broken.Add(key); continue; }
                bounds[key] = bounds.TryGetValue(key, out var b)
                    ? (Math.Min(b.Min, v), Math.Max(b.Max, v))
                    : (v, v);
            }

        foreach (var key in broken) bounds.Remove(key);
        return bounds;
    }

    private const double Eps = 1e-6;

    private static bool TryNumber(string raw, out double value)
    {
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return true;
        if (bool.TryParse(raw, out var b)) { value = b ? 1 : 0; return true; }
        value = 0;
        return false;
    }

    private static bool SameValue(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var da) &&
            double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
            return Math.Abs(da - db) < 1e-6;

        static bool? AsBool(string s) =>
            bool.TryParse(s, out var v) ? v
            : int.TryParse(s, out var n) ? n != 0
            : null;

        var ba = AsBool(a);
        var bb = AsBool(b);
        return ba is not null && bb is not null && ba == bb;
    }
}
