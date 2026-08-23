#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace MiamiGraphics.Core.Services;

public sealed class GtaSettingsAnalyzer
{

    public const int MaxGainPercent = 43;

    public sealed record Result(
        int GainPercent,
        string CpuBias,
        IReadOnlyList<SettingContribution> Contributions
    );

    public sealed record SettingContribution(
        string Key,
        double GainPercent,
        SettingCategory Category
    );

    public enum SettingCategory
    {
        Cpu,
        GpuShadow,
        GpuOther,
        Display,
    }

    public Result Analyze(GtaSettingsModel model)
        => Analyze(model.ToXml());

    public Result Analyze(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new ArgumentException("XML is empty", nameof(xml));

        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new InvalidOperationException("Settings XML has no root");
        var graphics = root.Element("graphics");
        var video = root.Element("video");

        var contributions = new List<SettingContribution>(WeightTable.Length);
        double total = 0;
        double cpuTotal = 0;
        double gpuTotal = 0;

        foreach (var w in WeightTable)
        {
            var parent = w.Section switch
            {
                "graphics" => graphics,
                "video"    => video,
                _          => root
            };
            if (parent is null) continue;

            var element = parent.Element(w.Key);
            if (element is null) continue;

            var current = w.Kind switch
            {
                ValueKind.Float => ReadFloatAttr(element, w.DefaultValue),
                ValueKind.Int   => (double)ReadIntAttr(element, (int)w.DefaultValue),
                ValueKind.Bool  => ReadBoolAttr(element, w.DefaultValue > 0.5) ? 1.0 : 0.0,
                _ => w.DefaultValue
            };

            var delta = NormalisedDelta(current, w.DefaultValue, w.MinValue, w.Kind);
            if (delta <= 0) continue;

            var gain = w.Points * delta;
            total += gain;

            if (w.Category == SettingCategory.Cpu) cpuTotal += gain;
            else if (w.Category != SettingCategory.Display) gpuTotal += gain;

            contributions.Add(new SettingContribution(w.Key, Math.Round(gain, 2), w.Category));
        }

        var resGain = ResolutionGain(video);
        if (resGain > 0)
        {
            total += resGain;
            contributions.Add(new SettingContribution("Resolution", Math.Round(resGain, 2), SettingCategory.Display));
        }
        var vsyncGain = VsyncGain(video);
        if (vsyncGain > 0)
        {
            total += vsyncGain;
            contributions.Add(new SettingContribution("VSync", Math.Round(vsyncGain, 2), SettingCategory.Display));
        }

        var capped = Math.Min(MaxGainPercent, total);
        var bias = ResolveBias(cpuTotal, gpuTotal);

        return new Result(
            GainPercent: (int)Math.Round(capped, MidpointRounding.AwayFromZero),
            CpuBias: bias,
            Contributions: contributions
        );
    }

    private static string ResolveBias(double cpuGain, double gpuGain)
    {
        var sum = cpuGain + gpuGain;
        if (sum < 1.0) return "balanced";
        var cpuShare = cpuGain / sum;
        if (cpuShare >= 0.60) return "cpu";
        if (cpuShare <= 0.30) return "gpu";
        return "balanced";
    }

    private const double ResolutionMaxWeight = 6.0;
    private const double BaselinePixels = 1920.0 * 1080.0;

    private static double ResolutionGain(XElement? video)
    {
        if (video is null) return 0;
        var w = ReadIntAttr(video.Element("ScreenWidth"),  1920);
        var h = ReadIntAttr(video.Element("ScreenHeight"), 1080);
        if (w <= 0 || h <= 0) return 0;
        var pixels = (double)w * h;
        if (pixels >= BaselinePixels) return 0;
        var ratio = 1.0 - (pixels / BaselinePixels);

        var clipped = Math.Min(0.55, ratio);
        return ResolutionMaxWeight * (clipped / 0.55);
    }

    private static double VsyncGain(XElement? video)
    {
        if (video is null) return 0;
        var vsync = ReadIntAttr(video.Element("VSync"), 1);
        return vsync == 0 ? 3.0 : 0.0;
    }

    private enum ValueKind { Int, Float, Bool }
    private sealed record Weight(
        string Key,
        string Section,
        ValueKind Kind,
        double Points,
        double DefaultValue,
        double MinValue,
        SettingCategory Category
    );

    private static readonly Weight[] WeightTable = new Weight[]
    {

        new("CityDensity",                "graphics", ValueKind.Float, 12.0, 1.0,  0.0,  SettingCategory.Cpu),
        new("LodScale",                   "graphics", ValueKind.Float,  7.0, 1.0,  0.0,  SettingCategory.Cpu),
        new("PedVarietyMultiplier",       "graphics", ValueKind.Float,  6.0, 1.0,  0.0,  SettingCategory.Cpu),
        new("VehicleVarietyMultiplier",   "graphics", ValueKind.Float,  5.0, 1.0,  0.0,  SettingCategory.Cpu),
        new("VehicleLodBias",             "graphics", ValueKind.Float,  4.0, 1.0, -0.5,  SettingCategory.Cpu),
        new("PedLodBias",                 "graphics", ValueKind.Float,  3.0, 1.0,  0.0,  SettingCategory.Cpu),
        new("Shadow_Distance",            "graphics", ValueKind.Float,  2.0, 1.0,  0.0,  SettingCategory.Cpu),

        new("ShadowQuality",              "graphics", ValueKind.Int,    6.0, 4,    0,    SettingCategory.GpuShadow),
        new("Shadow_SoftShadows",         "graphics", ValueKind.Int,    1.5, 4,    0,    SettingCategory.GpuShadow),
        new("Shadow_SplitZStart",         "graphics", ValueKind.Float,  0.5, 0.93, 0.0,  SettingCategory.GpuShadow),
        new("Shadow_SplitZEnd",           "graphics", ValueKind.Float,  0.5, 0.89, 0.0,  SettingCategory.GpuShadow),
        new("UltraShadows_Enabled",       "graphics", ValueKind.Bool,   1.0, 1,    0,    SettingCategory.GpuShadow),
        new("Shadow_ParticleShadows",     "graphics", ValueKind.Bool,   0.5, 1,    0,    SettingCategory.GpuShadow),
        new("Shadow_LongShadows",         "graphics", ValueKind.Bool,   0.3, 1,    0,    SettingCategory.GpuShadow),

        new("MSAA",                       "graphics", ValueKind.Int,    3.0, 8,    0,    SettingCategory.GpuOther),
        new("ReflectionQuality",          "graphics", ValueKind.Int,    3.0, 4,    0,    SettingCategory.GpuOther),
        new("SSAO",                       "graphics", ValueKind.Int,    2.0, 2,    0,    SettingCategory.GpuOther),
        new("Tessellation",               "graphics", ValueKind.Int,    1.5, 3,    0,    SettingCategory.GpuOther),
        new("GrassQuality",               "graphics", ValueKind.Int,    1.5, 2,    0,    SettingCategory.GpuOther),
        new("PostFX",                     "graphics", ValueKind.Int,    1.5, 3,    0,    SettingCategory.GpuOther),
        new("ParticleQuality",            "graphics", ValueKind.Int,    1.0, 2,   -1,    SettingCategory.GpuOther),
        new("ShaderQuality",              "graphics", ValueKind.Int,    1.0, 2,    0,    SettingCategory.GpuOther),
        new("ReflectionMSAA",             "graphics", ValueKind.Int,    1.0, 8,    0,    SettingCategory.GpuOther),
        new("WaterQuality",               "graphics", ValueKind.Int,    0.5, 2,   -1,    SettingCategory.GpuOther),
        new("TextureQuality",             "graphics", ValueKind.Int,    0.5, 2,    0,    SettingCategory.GpuOther),
        new("AnisotropicFiltering",       "graphics", ValueKind.Int,    0.5, 16,   0,    SettingCategory.GpuOther),
        new("FXAA_Enabled",               "graphics", ValueKind.Bool,   0.3, 1,    0,    SettingCategory.GpuOther),
        new("TXAA_Enabled",               "graphics", ValueKind.Bool,   0.5, 1,    0,    SettingCategory.GpuOther),
        new("MotionBlurStrength",         "graphics", ValueKind.Float,  0.5, 1.0,  0.0,  SettingCategory.GpuOther),
        new("DoF",                        "graphics", ValueKind.Bool,   0.3, 1,    0,    SettingCategory.GpuOther),
        new("Lighting_FogVolumes",        "graphics", ValueKind.Bool,   0.3, 1,    0,    SettingCategory.GpuOther),
        new("Reflection_MipBlur",         "graphics", ValueKind.Bool,   0.3, 1,    0,    SettingCategory.GpuOther),
        new("HdStreamingInFlight",        "graphics", ValueKind.Bool,   0.5, 1,    0,    SettingCategory.GpuOther),
        new("DX_Version",                 "graphics", ValueKind.Int,    0.5, 3,    2,    SettingCategory.Display),
    };

    private static double ReadFloatAttr(XElement? el, double fallback)
    {
        if (el is null) return fallback;
        var raw = el.Attribute("value")?.Value;
        if (raw is null) return fallback;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
    private static int ReadIntAttr(XElement? el, int fallback)
    {
        if (el is null) return fallback;
        var raw = el.Attribute("value")?.Value;
        if (raw is null) return fallback;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
    private static bool ReadBoolAttr(XElement? el, bool fallback)
    {
        if (el is null) return fallback;
        var raw = el.Attribute("value")?.Value;
        if (raw is null) return fallback;
        if (bool.TryParse(raw, out var v)) return v;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n != 0;
        return fallback;
    }

    private static double NormalisedDelta(double current, double def, double min, ValueKind kind)
    {
        if (kind == ValueKind.Bool)
            return Math.Abs(current - def) > 0.5 ? 1.0 : 0.0;

        if (Math.Abs(def - min) < 1e-9) return 0;
        var raw = (def - current) / (def - min);
        if (raw <= 0) return 0;
        return Math.Min(1.0, raw);
    }
}
