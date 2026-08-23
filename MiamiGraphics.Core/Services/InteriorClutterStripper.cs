#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace MiamiGraphics.Core.Services;

public static class InteriorClutterStripper
{
    public sealed record Result(
        byte[]? Data,
        int Before,
        int After,
        IReadOnlyList<string> RemovedNames,
        string? ErrorMessage = null);

    public static IReadOnlyList<uint> HashesOf(byte[] ytypData)
    {
        try
        {
            var y = new YtypFile();
            y.Load(ytypData);
            return (y.AllArchetypes ?? Array.Empty<Archetype>()).OfType<MloArchetype>()
                .SelectMany(m => m.entities ?? Array.Empty<MCEntityDef>())
                .Select(e => (uint)e._Data.archetypeName)
                .Distinct()
                .ToList();
        }
        catch { return Array.Empty<uint>(); }
    }

    public static Result Strip(byte[] vanillaYtyp, IReadOnlyDictionary<uint, string> names)
    {
        YtypFile ytyp;
        try
        {
            ytyp = new YtypFile();
            ytyp.Load(vanillaYtyp);
        }
        catch (Exception ex)
        {
            return new Result(null, 0, 0, Array.Empty<string>(),
                              $"файл интерьера не разобрался: {ex.GetType().Name}");
        }

        var mlo = (ytyp.AllArchetypes ?? Array.Empty<Archetype>()).OfType<MloArchetype>().ToList();
        if (mlo.Count == 0)
            return new Result(null, 0, 0, Array.Empty<string>(), "в файле нет интерьеров");

        var removed = new List<string>();
        var before = 0;
        var after = 0;

        foreach (var m in mlo)
        {
            var ents = m.entities ?? Array.Empty<MCEntityDef>();
            before += ents.Length;

            var map = new int[ents.Length];
            var kept = new List<MCEntityDef>(ents.Length);
            for (int i = 0; i < ents.Length; i++)
            {
                names.TryGetValue((uint)ents[i]._Data.archetypeName, out var name);
                if (InteriorClutterRules.IsClutter(name)) { removed.Add(name!); map[i] = -1; }
                else { map[i] = kept.Count; kept.Add(ents[i]); }
            }

            m.entities = kept.ToArray();
            for (int i = 0; i < m.entities.Length; i++) m.entities[i].Index = i;
            after += kept.Count;

            foreach (var r in m.rooms ?? Array.Empty<MCMloRoomDef>())
                r.AttachedObjects = RemapAttachments(r.AttachedObjects, map);
            foreach (var pt in m.portals ?? Array.Empty<MCMloPortalDef>())
                pt.AttachedObjects = RemapAttachments(pt.AttachedObjects, map);
        }

        if (removed.Count == 0)
            return new Result(null, before, after, removed, "убирать нечего");

        byte[] data;
        try
        {
            data = ytyp.Save();
        }
        catch (Exception ex)
        {
            return new Result(null, before, after, Array.Empty<string>(),
                              $"файл не пересобрался: {ex.GetType().Name}: {ex.Message}");
        }

        var broken = FindDanglingAttachments(data);
        if (broken != null)
            return new Result(null, before, after, Array.Empty<string>(), broken);

        return new Result(data, before, after,
                          removed.Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList());
    }

    internal static uint[] RemapAttachments(uint[]? attached, int[] map)
    {
        if (attached == null || attached.Length == 0) return Array.Empty<uint>();

        var next = new List<uint>(attached.Length);
        foreach (var old in attached)
        {
            if (old >= map.Length) continue;
            var mapped = map[old];
            if (mapped >= 0) next.Add((uint)mapped);
        }
        return next.ToArray();
    }

    private static string? FindDanglingAttachments(byte[] ytypData)
    {
        try
        {
            var y = new YtypFile();
            y.Load(ytypData);
            foreach (var m in (y.AllArchetypes ?? Array.Empty<Archetype>()).OfType<MloArchetype>())
            {
                int count = m.entities?.Length ?? 0;
                int bad = (m.rooms ?? Array.Empty<MCMloRoomDef>())
                              .SelectMany(r => r.AttachedObjects ?? Array.Empty<uint>())
                              .Count(i => i >= count)
                        + (m.portals ?? Array.Empty<MCMloPortalDef>())
                              .SelectMany(pt => pt.AttachedObjects ?? Array.Empty<uint>())
                              .Count(i => i >= count);
                if (bad > 0)
                    return $"интерьер {m.Name}: {bad} привязок ссылаются за конец списка " +
                           $"из {count} объектов - такой файл роняет игру, не отдаём";
            }
            return null;
        }
        catch (Exception ex)
        {
            return $"собранный файл не перечитался: {ex.GetType().Name}: {ex.Message}";
        }
    }
}
