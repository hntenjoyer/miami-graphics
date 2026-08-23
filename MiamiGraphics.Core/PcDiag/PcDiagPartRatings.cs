#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace MiamiGraphics.Core.PcDiag;

public enum PartTier { Unknown, S, A, B, C, D }

public sealed record PartRating(PartTier Tier, string Note);

public static class PcDiagDiskRating
{
    public static PartRating Rate(PcSnapshot snap)
    {
        var disks = snap.Disks ?? (IReadOnlyList<DiskInfo>)Array.Empty<DiskInfo>();
        var gameMedia = snap.Game?.Media ?? DiskMedia.Unknown;

        if (gameMedia == DiskMedia.Hdd)
            return new PartRating(PartTier.D, "игра на жёстком диске - самая дорогая потеря времени при заходе");

        var sameMedia = disks.Where(d => d.Media == gameMedia).ToList();
        var bus = sameMedia.Count == 1 ? sameMedia[0].Bus
                : sameMedia.Count > 1 && sameMedia.All(d => d.Bus == sameMedia[0].Bus) ? sameMedia[0].Bus
                : DiskBus.Unknown;

        if (gameMedia == DiskMedia.Ssd || gameMedia == DiskMedia.Scm)
        {
            return bus switch
            {
                DiskBus.Nvme => new PartRating(PartTier.S, "игра на NVMe - быстрее для GTA уже некуда"),
                DiskBus.Sata => new PartRating(PartTier.A, "игра на SATA SSD - разница с NVMe в загрузке почти не видна"),
                DiskBus.Usb  => new PartRating(PartTier.B, "игра на внешнем диске: упирается в USB, а не в сам SSD"),
                _            => new PartRating(PartTier.A, "игра на SSD"),
            };
        }

        if (disks.Count == 0) return new PartRating(PartTier.Unknown, "");
        bool anyHdd = disks.Any(d => d.Media == DiskMedia.Hdd);
        bool anyNvme = disks.Any(d => d.Bus == DiskBus.Nvme);
        return new PartRating(PartTier.Unknown,
            anyNvme && !anyHdd ? "где стоит игра - не определили, в системе только SSD" : "где стоит игра - не определили");
    }
}

public static class PcDiagRamRating
{
    public static PartRating Rate(PcSnapshot snap)
    {
        var sticks = snap.RamSticks ?? (IReadOnlyList<RamStickInfo>)Array.Empty<RamStickInfo>();
        if (sticks.Count == 0) return new PartRating(PartTier.Unknown, "");

        int gb = (int)Math.Round(snap.TotalRamBytes / (1024.0 * 1024 * 1024));
        if (gb <= 0) gb = (int)Math.Round(sticks.Sum(s => s.CapacityBytes) / (1024.0 * 1024 * 1024));

        int channels = ChannelCount(sticks);
        bool profileOn = sticks.All(s => s.RatedMt <= 0 || s.ConfiguredMt <= 0 || s.ConfiguredMt >= s.RatedMt - 132);
        string speed = sticks[0].ConfiguredMt > 0 ? $"{sticks[0].ConfiguredMt} МТ/с" : "";

        if (gb < 12)
            return new PartRating(PartTier.D, $"{gb} ГБ - главный лимитер, апгрейд даст больше любых твиков");

        if (channels < 2)
            return new PartRating(PartTier.C,
                "одна планка: одноканал стоит кадров больше, чем любая частота - вторая планка того же объёма всё чинит");

        var note = "двухканал" + (speed.Length > 0 ? ", " + speed : "");

        if (!profileOn)
            return new PartRating(gb >= 24 ? PartTier.A : PartTier.B, note + ", профиль XMP/EXPO не включён");

        return new PartRating(gb >= 24 ? PartTier.S : PartTier.A, note + ", профиль включён");
    }

    private static int ChannelCount(IReadOnlyList<RamStickInfo> sticks)
    {
        var letters = new HashSet<char>();
        foreach (var s in sticks)
        {
            char letter = '\0';
            if (s.SlotName.Length > 0 && char.IsLetter(s.SlotName[0])) letter = char.ToUpperInvariant(s.SlotName[0]);
            else
            {
                var m = global::System.Text.RegularExpressions.Regex.Match(
                    s.BankLabel ?? "", @"CHANNEL\s*([A-Z])",
                    global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success) letter = char.ToUpperInvariant(m.Groups[1].Value[0]);
            }
            if (letter != '\0') letters.Add(letter);
        }
        return letters.Count > 0 ? letters.Count : sticks.Count;
    }
}
