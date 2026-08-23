#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MiamiGraphics.Core.Services;

public static class GtaSettingsKeyMap
{
    private sealed record Accessor(
        Func<GtaSettingsModel, string> Read,
        Func<GtaSettingsModel, string, bool> TryWrite);

    private static Accessor Int(Func<GtaSettingsModel, int> get, Action<GtaSettingsModel, int> set) =>
        new(m => get(m).ToString(CultureInfo.InvariantCulture),
            (m, raw) =>
            {
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return false;
                set(m, v);
                return true;
            });

    private static Accessor Flt(Func<GtaSettingsModel, double> get, Action<GtaSettingsModel, double> set) =>
        new(m => get(m).ToString("0.000000", CultureInfo.InvariantCulture),
            (m, raw) =>
            {
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
                set(m, v);
                return true;
            });

    private static Accessor Bln(Func<GtaSettingsModel, bool> get, Action<GtaSettingsModel, bool> set) =>
        new(m => get(m) ? "true" : "false",
            (m, raw) =>
            {
                if (bool.TryParse(raw, out var b)) { set(m, b); return true; }
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) { set(m, n != 0); return true; }
                return false;
            });

    private static readonly Dictionary<string, Accessor> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TextureQuality"]           = Int(m => m.TextureQuality,       (m, v) => m.TextureQuality = v),
        ["ShaderQuality"]            = Int(m => m.ShaderQuality,        (m, v) => m.ShaderQuality = v),
        ["WaterQuality"]             = Int(m => m.WaterQuality,         (m, v) => m.WaterQuality = v),
        ["ParticleQuality"]          = Int(m => m.ParticleQuality,      (m, v) => m.ParticleQuality = v),
        ["PostFX"]                   = Int(m => m.PostFx,               (m, v) => m.PostFx = v),
        ["ShadowQuality"]            = Int(m => m.ShadowQuality,        (m, v) => m.ShadowQuality = v),

        ["FXAA_Enabled"]             = Bln(m => m.Fxaa,                 (m, v) => m.Fxaa = v),
        ["TXAA_Enabled"]             = Bln(m => m.Txaa,                 (m, v) => m.Txaa = v),
        ["MSAA"]                     = Int(m => m.Msaa,                 (m, v) => m.Msaa = v),
        ["ReflectionMSAA"]           = Int(m => m.ReflectionMsaa,       (m, v) => m.ReflectionMsaa = v),

        ["CityDensity"]              = Flt(m => m.CityDensity,          (m, v) => m.CityDensity = v),
        ["PedVarietyMultiplier"]     = Flt(m => m.PedVariety,           (m, v) => m.PedVariety = v),
        ["VehicleVarietyMultiplier"] = Flt(m => m.VehicleVariety,       (m, v) => m.VehicleVariety = v),
        ["LodScale"]                 = Flt(m => m.LodScale,             (m, v) => m.LodScale = v),
        ["MaxLodScale"]              = Flt(m => m.MaxLodScale,          (m, v) => m.MaxLodScale = v),
        ["VehicleLodBias"]           = Flt(m => m.VehicleLodBias,       (m, v) => m.VehicleLodBias = v),
        ["PedLodBias"]               = Flt(m => m.PedLodBias,           (m, v) => m.PedLodBias = v),
        ["GrassQuality"]             = Int(m => m.GrassQuality,         (m, v) => m.GrassQuality = v),
        ["ReflectionQuality"]        = Int(m => m.ReflectionQuality,    (m, v) => m.ReflectionQuality = v),

        ["Shadow_Distance"]          = Flt(m => m.ShadowDistance,       (m, v) => m.ShadowDistance = v),
        ["Shadow_SoftShadows"]       = Int(m => m.ShadowSoftShadows,    (m, v) => m.ShadowSoftShadows = v),
        ["Shadow_SplitZStart"]       = Flt(m => m.ShadowSplitZStart,    (m, v) => m.ShadowSplitZStart = v),
        ["Shadow_SplitZEnd"]         = Flt(m => m.ShadowSplitZEnd,      (m, v) => m.ShadowSplitZEnd = v),
        ["Shadow_ParticleShadows"]   = Bln(m => m.ShadowParticles,      (m, v) => m.ShadowParticles = v),
        ["Shadow_LongShadows"]       = Bln(m => m.ShadowLongShadows,    (m, v) => m.ShadowLongShadows = v),
        ["UltraShadows_Enabled"]     = Bln(m => m.UltraShadows,         (m, v) => m.UltraShadows = v),

        ["Tessellation"]             = Int(m => m.Tessellation,         (m, v) => m.Tessellation = v),
        ["AnisotropicFiltering"]     = Int(m => m.AnisotropicFiltering, (m, v) => m.AnisotropicFiltering = v),
        ["SSAO"]                     = Int(m => m.Ssao,                 (m, v) => m.Ssao = v),
        ["Reflection_MipBlur"]       = Bln(m => m.ReflectionMipBlur,    (m, v) => m.ReflectionMipBlur = v),
        ["DX_Version"]               = Int(m => m.DxVersion,            (m, v) => m.DxVersion = v),
        ["DoF"]                      = Bln(m => m.Dof,                  (m, v) => m.Dof = v),
        ["HdStreamingInFlight"]      = Bln(m => m.HdStreaming,          (m, v) => m.HdStreaming = v),
        ["MotionBlurStrength"]       = Flt(m => m.MotionBlur,           (m, v) => m.MotionBlur = v),
        ["Lighting_FogVolumes"]      = Bln(m => m.FogVolumes,           (m, v) => m.FogVolumes = v),
    };

    public static IReadOnlyCollection<string> KnownKeys { get; } =
        Map.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsKnown(string key) => Map.ContainsKey(key);

    public static string? Read(GtaSettingsModel model, string key)
        => Map.TryGetValue(key, out var a) ? a.Read(model) : null;

    public static bool TryWrite(GtaSettingsModel model, string key, string value, out string? error)
    {
        if (!Map.TryGetValue(key, out var a))
        {
            error = $"ключ '{key}' не поддерживается лаунчером";
            return false;
        }
        if (!a.TryWrite(model, value))
        {
            error = $"значение '{value}' не подходит ключу '{key}'";
            return false;
        }
        error = null;
        return true;
    }
}
