#nullable enable
using System;
using System.Text.RegularExpressions;

namespace MiamiGraphics.Core.PcDiag;

public static class PcDiagCpuRating
{
    public static CpuGtaRating Rate(CpuInfo? cpu)
    {
        if (cpu is null || string.IsNullOrWhiteSpace(cpu.Name))
            return Unknown();

        var name = cpu.Name;

        var ryzen = Regex.Match(name, @"Ryzen\s+(?<seg>[3579])\s+(?:PRO\s+)?(?<model>\d{4})(?<sfx>X3D|XT|X|G|GE|HX|HS|H|U|C)?",
            RegexOptions.IgnoreCase);
        if (ryzen.Success)
        {
            int model = int.Parse(ryzen.Groups["model"].Value);
            int series = model / 1000;
            string sfx = ryzen.Groups["sfx"].Value.ToUpperInvariant();
            bool x3d = sfx == "X3D";
            bool laptop = sfx is "H" or "HS" or "HX" or "U" or "C";

            if (x3d)
            {
                var t = series >= 7 ? CpuTier.S : CpuTier.A;
                return new CpuGtaRating(t, $"Zen X3D ({series}000)", IsHybrid: false, IsX3D: true, IsLaptop: false, Parsed: true);
            }

            CpuTier tier = series switch
            {
                >= 9 => CpuTier.A,
                7 or 8 => series == 7 ? CpuTier.A : CpuTier.B,
                5 or 6 => CpuTier.B,
                3 or 4 => CpuTier.C,
                _ => CpuTier.D
            };
            if (laptop) tier = Down(tier, sfx == "U" ? 2 : 1);
            return new CpuGtaRating(tier, $"Ryzen {series}000{(laptop ? ", ноутбук" : "")}",
                IsHybrid: false, IsX3D: false, IsLaptop: laptop, Parsed: true);
        }

        var ultra = Regex.Match(name, @"Core\s*\(?TM\)?\s*Ultra\s+(?:X?)(?<seg>[579])\s+(?<model>\d{3})(?<sfx>[A-Z]{0,2})",
            RegexOptions.IgnoreCase);
        if (ultra.Success)
        {
            int model = int.Parse(ultra.Groups["model"].Value);
            string sfx = ultra.Groups["sfx"].Value.ToUpperInvariant();
            bool laptop = sfx.Contains('H') || sfx.Contains('U') || sfx.Contains('V');
            var tier = (!laptop && model >= 200) ? CpuTier.A : CpuTier.B;
            if (sfx.Contains('U')) tier = Down(tier, 1);
            return new CpuGtaRating(tier, $"Core Ultra {model}{(laptop ? ", ноутбук" : "")}",
                IsHybrid: true, IsX3D: false, IsLaptop: laptop, Parsed: true);
        }

        var core = Regex.Match(name, @"Core\s*\(?TM\)?\s+i(?<seg>[3579])-(?<model>\d{3,5})(?<sfx>G\d|[A-Z]{0,2})",
            RegexOptions.IgnoreCase);
        if (core.Success)
        {
            int seg = int.Parse(core.Groups["seg"].Value);
            string modelStr = core.Groups["model"].Value;
            int gen = modelStr.Length switch
            {
                5 => int.Parse(modelStr[..2]),
                4 when modelStr[0] == '1' => int.Parse(modelStr[..2]),
                4 => modelStr[0] - '0',
                _ => 1
            };
            string sfx = core.Groups["sfx"].Value.ToUpperInvariant();
            bool laptop = sfx.Contains('H') || sfx.Contains('U') || sfx.Contains('P') ||
                          sfx.StartsWith("G", StringComparison.Ordinal) || sfx == "M" || sfx == "Y";
            bool hybrid = gen >= 12;

            CpuTier tier = gen switch
            {
                >= 13 => seg >= 7 ? CpuTier.A : CpuTier.B,
                12 => seg >= 7 ? CpuTier.A : CpuTier.B,
                10 or 11 => seg >= 7 ? CpuTier.B : CpuTier.C,
                8 or 9 => seg >= 7 ? CpuTier.C : CpuTier.D,
                _ => CpuTier.D
            };
            if (laptop) tier = Down(tier, sfx.Contains('U') ? 2 : 1);
            return new CpuGtaRating(tier, $"Intel {gen} поколение{(laptop ? ", ноутбук" : "")}",
                IsHybrid: hybrid, IsX3D: false, IsLaptop: laptop, Parsed: true);
        }

        if (Regex.IsMatch(name, @"\bFX\s*\(tm\)?-?\d{4}|\bPhenom\b|\bAthlon\b|\bPentium\b|\bCeleron\b|\bCore\s*\(?TM\)?\s*2\b|\bA(?:4|6|8|10|12)-\d{4}\b",
                RegexOptions.IgnoreCase))
            return new CpuGtaRating(CpuTier.D, "устаревшее семейство",
                IsHybrid: false, IsX3D: false, IsLaptop: false, Parsed: true);

        return Unknown();

        static CpuGtaRating Unknown() =>
            new(CpuTier.Unknown, "", IsHybrid: false, IsX3D: false, IsLaptop: false, Parsed: false);
    }

    private static CpuTier Down(CpuTier t, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            t = t switch
            {
                CpuTier.S => CpuTier.A,
                CpuTier.A => CpuTier.B,
                CpuTier.B => CpuTier.C,
                _ => CpuTier.D
            };
        }
        return t;
    }
}
