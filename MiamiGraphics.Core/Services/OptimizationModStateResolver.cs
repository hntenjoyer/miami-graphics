#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services;

public static class OptimizationModStateResolver
{
    public static IReadOnlyDictionary<string, int?> Resolve(
        string gtaRoot, OptimizationCatalog catalog,
        IReadOnlyDictionary<string, string>? cleanSourceFor = null)
    {
        var result = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);

        var modGroups = catalog.Groups
            .Where(g => g.Methods.Contains("mod", StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (modGroups.Count == 0) return result;

        var wanted = modGroups
            .SelectMany(g => g.Options)
            .SelectMany(o => o.FileEdits)
            .GroupBy(e => e.Archive, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key,
                          g => g.Select(e => e.TargetPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                          StringComparer.OrdinalIgnoreCase);

        var current = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (archive, paths) in wanted)
        {
            var abs = Path.Combine(gtaRoot, archive);
            if (!File.Exists(abs)) continue;
            try
            {
                using var arc = RageArchiveWrapper7.Open(abs);
                foreach (var p in paths)
                {
                    var text = ModTextPatchBuilder.TryReadText(arc.Root, p);
                    if (text is null) continue;
                    raw[archive + "|" + p] = text;
                    current[archive + "|" + p] = ModTextPatchBuilder.ParseKeyValues(text);
                }
            }
            catch
            {
            }
        }

        foreach (var g in modGroups)
        {
            int? match = null;
            foreach (var o in g.Options.Where(x => x.FileEdits.Count > 0))
            {
                var all = true;
                foreach (var e in o.FileEdits)
                {
                    var addr = e.Archive + "|" + e.TargetPath;

                    if (e.IsInterior) continue;

                    if (e.IsWholeFile)
                    {
                        if (!raw.TryGetValue(addr, out var text)) { all = false; break; }

                        var expected = e.Key == OptimizationFileEdit.ReplaceWholeFile
                            ? e.Value
                            : ReadClean(cleanSourceFor, e.Archive, e.TargetPath);

                        if (expected is null || !SameContent(text, expected)) { all = false; break; }
                        continue;
                    }

                    if (!current.TryGetValue(addr, out var vals) ||
                        !vals.TryGetValue(e.Key, out var actual) ||
                        !SameValue(actual, e.Value))
                    { all = false; break; }
                }
                if (all) { match = o.Idx; break; }
            }
            result[g.Key] = match;
        }

        return result;
    }

    private static string? ReadClean(
        IReadOnlyDictionary<string, string>? cleanSourceFor, string archive, string targetPath)
    {
        if (cleanSourceFor is null ||
            !cleanSourceFor.TryGetValue(archive, out var clean) || !File.Exists(clean)) return null;
        try
        {
            using var arc = RageArchiveWrapper7.Open(clean);
            return ModTextPatchBuilder.TryReadText(arc.Root, targetPath);
        }
        catch { return null; }
    }

    private static bool SameContent(string a, string b)
    {
        static string Squeeze(string s)
        {
            var sb = new global::System.Text.StringBuilder(s.Length);
            foreach (var c in s)
                if (!char.IsWhiteSpace(c) && c != '﻿') sb.Append(c);
            return sb.ToString();
        }
        return string.Equals(Squeeze(a), Squeeze(b), StringComparison.Ordinal);
    }

    private static bool SameValue(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        return double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var da)
            && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var db)
            && Math.Abs(da - db) < 1e-6;
    }
}
