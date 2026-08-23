#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MiamiGraphics.Core.Injector;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services;

public sealed class OptimizationModApplyService
{
    public sealed record Outcome(
        bool Success,
        string? ErrorMessage,
        IReadOnlyList<string> Changed,
        IReadOnlyList<string> Skipped,
        int ArchivesTouched);

    private readonly string _gtaRoot;
    private readonly Func<string, IRpfPatchInjector> _injectorFactory;
    private readonly Func<string, string, string?> _cleanText;

    public OptimizationModApplyService(
        string gtaRoot,
        Func<string, IRpfPatchInjector>? injectorFactory = null,
        Func<string, string, string?>? cleanTextReader = null)
    {
        _gtaRoot = gtaRoot;
        _injectorFactory = injectorFactory ?? (root => new RpfPatchInjector(root));
        _cleanText = cleanTextReader ?? ReadFromArchive;
    }

    private async Task<Outcome?> ApplyInteriorsAsync(
        IReadOnlyList<(OptimizationFileEdit Edit, string Group)> edits,
        List<string> changed, List<string> skipped, CancellationToken ct)
    {
        var byYtyp = edits.GroupBy(x => x.Edit.TargetPath, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var g in byYtyp)
        {
            var owners = g.Select(x => x.Group).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (owners.Count > 1)
                return new Outcome(false,
                    $"{g.Key}: группы '{string.Join("' и '", owners)}' обе чистят один интерьер - " +
                    "применение остановлено", changed, skipped, 0);
        }

        var work = Path.Combine(Path.GetTempPath(), "mg-interiors-" + Guid.NewGuid().ToString("N"));
        try
        {
            var names = byYtyp.Select(g => g.Key).ToList();
            var built = await Task.Run(
                () => InteriorPatchBuilder.Build(_gtaRoot, work, names, InteriorNamesCachePath), ct)
                .ConfigureAwait(false);

            changed.AddRange(built.Changed);
            skipped.AddRange(built.Skipped);

            if (!built.Success)
                return new Outcome(false, built.ErrorMessage ?? "интерьеры не собрались",
                                   changed, skipped, 0);
            if (built.PatchDirectory is null) return null;

            var injector = _injectorFactory(_gtaRoot);
            var ok = await Task.Run(() => injector.InjectPatch(built.PatchDirectory), ct)
                               .ConfigureAwait(false);
            if (!ok)
                return new Outcome(false, injector.LastError ?? "инжект интерьеров не удался",
                                   changed, skipped, 0);
            return null;
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
        }
    }

    private static string InteriorNamesCachePath
        => Path.Combine(MiamiGraphics.Core.System.AppDataRoot.DefaultCacheRoot, "interior-names.txt");

    private static string? ReadFromArchive(string archivePath, string targetPath)
    {
        using var archive = RageArchiveWrapper7.Open(archivePath);
        return ModTextPatchBuilder.TryReadText(archive.Root, targetPath);
    }

    public async Task<Outcome> ApplyAsync(
        IReadOnlyList<OptimizationApplyService.Selection> selections,
        OptimizationCatalog catalog,
        IReadOnlyDictionary<string, string>? cleanSourceFor = null,
        CancellationToken ct = default)
    {
        var changed = new List<string>();
        var skipped = new List<string>();

        var byArchive = new Dictionary<string, Dictionary<string, Dictionary<string, (string Value, string Group)>>>(
            StringComparer.OrdinalIgnoreCase);

        string? AddEdit(OptimizationFileEdit e, string groupKey)
        {
            var files = byArchive.TryGetValue(e.Archive, out var f)
                ? f
                : byArchive[e.Archive] = new(StringComparer.OrdinalIgnoreCase);
            var keys = files.TryGetValue(e.TargetPath, out var k)
                ? k
                : files[e.TargetPath] = new(StringComparer.OrdinalIgnoreCase);

            if (keys.TryGetValue(e.Key, out var prev) &&
                !string.Equals(prev.Group, groupKey, StringComparison.OrdinalIgnoreCase))
                return $"группы '{prev.Group}' и '{groupKey}' обе правят " +
                       $"{e.TargetPath}:{e.Key} - применение остановлено";

            keys[e.Key] = (e.Value, groupKey);
            return null;
        }

        var resets = new List<OptimizationGroup>();
        var interiors = new List<(OptimizationFileEdit Edit, string Group)>();

        foreach (var sel in selections)
        {
            ct.ThrowIfCancellationRequested();

            var group = catalog.Find(sel.GroupKey);
            if (group is null) return Fail($"группа '{sel.GroupKey}' не найдена в каталоге");
            if (!group.Methods.Contains("mod", StringComparer.OrdinalIgnoreCase)) continue;
            if (!group.Enabled) { skipped.Add($"группа '{group.Key}' отключена"); continue; }

            if (sel.OptionIdx is not { } idx) { resets.Add(group); continue; }

            var option = group.OptionAt(idx);
            if (option is null) return Fail($"у группы '{group.Key}' нет варианта {idx}");

            foreach (var e in option.FileEdits)
            {
                if (e.IsInterior) { interiors.Add((e, group.Key)); continue; }
                if (AddEdit(e, group.Key) is { } err) return Fail(err);
            }
        }

        if (resets.Count > 0)
        {
            var restored = ResolveResets(resets, cleanSourceFor, out var resetError);
            if (restored is null) return Fail(resetError!);
            foreach (var (edit, groupKey) in restored)
                if (AddEdit(edit, groupKey) is { } err) return Fail(err);
        }

        var touchedByInteriors = 0;
        if (interiors.Count > 0)
        {
            var res = await ApplyInteriorsAsync(interiors, changed, skipped, ct).ConfigureAwait(false);
            if (res is not null) return res;
            touchedByInteriors = 1;
        }

        if (byArchive.Count == 0)
            return new Outcome(true, null, changed, skipped, touchedByInteriors);

        var touched = touchedByInteriors;
        foreach (var (archive, files) in byArchive)
        {
            ct.ThrowIfCancellationRequested();

            var work = Path.Combine(Path.GetTempPath(), "mg-optmod-" + Guid.NewGuid().ToString("N"));
            try
            {
                string? readFrom = null;
                if (cleanSourceFor is not null && cleanSourceFor.TryGetValue(archive, out var clean) && File.Exists(clean))
                    readFrom = clean;
                else
                    skipped.Add($"{archive}: чистой копии нет, правки строятся от текущего архива");

                var plans = new List<ModTextPatchBuilder.FilePlan>();
                foreach (var (target, keys) in files)
                {
                    var whole = keys.Where(kv => kv.Key is OptimizationFileEdit.ReplaceWholeFile
                                                        or OptimizationFileEdit.RestoreWholeFile).ToList();
                    if (whole.Count == 0)
                    {
                        plans.Add(new ModTextPatchBuilder.FilePlan(
                            target,
                            keys.Select(kv => new ModTextPatchBuilder.Edit(kv.Key, kv.Value.Value)).ToList()));
                        continue;
                    }

                    if (keys.Count > whole.Count)
                        return Fail($"{target}: группа '{whole[0].Value.Group}' заменяет файл целиком, " +
                                    "а другая правит в нём строки - применение остановлено");
                    if (whole.Count > 1)
                        return Fail($"{target}: файл целиком заменяют сразу две правки - применение остановлено");

                    string text;
                    if (whole[0].Key == OptimizationFileEdit.ReplaceWholeFile)
                    {
                        text = whole[0].Value.Value;
                    }
                    else
                    {
                        if (readFrom is null)
                            return Fail($"{target}: вернуть файл не от чего - нет чистой копии {archive}");
                        var original = _cleanText(readFrom, target);
                        if (original is null)
                            return Fail($"{target}: в чистой копии {archive} такого файла нет");
                        text = original;
                    }

                    plans.Add(new ModTextPatchBuilder.FilePlan(
                        target, Array.Empty<ModTextPatchBuilder.Edit>(), text));
                }

                var built = new ModTextPatchBuilder(_gtaRoot).Build(archive, plans, work, readFrom);
                changed.AddRange(built.Changed);
                skipped.AddRange(built.Skipped);

                if (!built.Success) return Fail(built.ErrorMessage ?? "сборка патча не удалась");
                if (built.PatchDirectory is null) continue;

                var injector = _injectorFactory(_gtaRoot);
                var ok = await Task.Run(() => injector.InjectPatch(built.PatchDirectory), ct).ConfigureAwait(false);
                if (!ok) return Fail(injector.LastError ?? $"инжект в {archive} не удался");

                touched++;
            }
            finally
            {
                try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch { }
            }
        }

        return new Outcome(true, null, changed, skipped, touched);

        Outcome Fail(string message) => new(false, message, changed, skipped, 0);
    }

    internal List<(OptimizationFileEdit Edit, string GroupKey)>? ResolveResets(
        IReadOnlyList<OptimizationGroup> groups,
        IReadOnlyDictionary<string, string>? cleanSourceFor,
        out string? error)
    {
        var result = new List<(OptimizationFileEdit, string)>();
        var cache = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        var wanted = groups
            .SelectMany(g => g.Options.SelectMany(o => o.FileEdits).Select(e => (Group: g.Key, Edit: e)))
            .GroupBy(x => x.Edit.Archive, StringComparer.OrdinalIgnoreCase);

        foreach (var arc in wanted)
        {
            if (cleanSourceFor is null ||
                !cleanSourceFor.TryGetValue(arc.Key, out var clean) || !File.Exists(clean))
            {
                error = $"сброс невозможен: нет чистой копии {arc.Key}, " +
                        "вернуть исходные значения не от чего";
                return null;
            }

            foreach (var target in arc.Select(x => x.Edit.TargetPath).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var addr = arc.Key + "|" + target;
                if (cache.ContainsKey(addr)) continue;
                try
                {
                    var raw = _cleanText(clean, target);
                    var values = raw is null ? null : ModTextPatchBuilder.ParseKeyValues(raw);
                    if (values is null)
                    {
                        error = $"в чистой копии {arc.Key} нет {target} - сброс отменён";
                        return null;
                    }
                    cache[addr] = values;
                }
                catch (Exception ex)
                {
                    error = $"чистая копия {arc.Key} не читается: {ex.GetType().Name}: {ex.Message}";
                    return null;
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (groupKey, e) in arc)
            {
                if (!seen.Add(groupKey + "|" + e.TargetPath + "|" + (e.IsWholeFile ? "@whole" : e.Key))) continue;

                if (e.IsWholeFile)
                {
                    result.Add((e with { Key = OptimizationFileEdit.RestoreWholeFile, Value = "" }, groupKey));
                    continue;
                }

                if (!cache[e.Archive + "|" + e.TargetPath].TryGetValue(e.Key, out var value))
                {
                    error = $"в чистой копии нет ключа {e.TargetPath}:{e.Key} - сброс отменён";
                    return null;
                }
                result.Add((new OptimizationFileEdit(e.Archive, e.TargetPath, e.Key, value), groupKey));
            }
        }

        error = null;
        return result;
    }
}
