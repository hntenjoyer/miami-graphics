#nullable enable
using System.Globalization;
using System.Xml.Linq;

namespace MiamiGraphics.Core.Services;

public sealed class GtaSettingsModel
{

    public int     ScreenWidth     { get; set; } = 1920;
    public int     ScreenHeight    { get; set; } = 1080;
    public int     RefreshRate     { get; set; } = 144;
    public int     AspectRatio     { get; set; } = 0;
    public int     Windowed        { get; set; } = 0;
    public bool    VSync           { get; set; } = false;

    public int     TextureQuality  { get; set; } = 2;
    public int     ShaderQuality   { get; set; } = 2;
    public int     WaterQuality    { get; set; } = 1;
    public int     ParticleQuality { get; set; } = 1;
    public int     PostFx          { get; set; } = 2;
    public int     ShadowQuality   { get; set; } = 3;

    public bool    Fxaa            { get; set; } = false;
    public bool    Txaa            { get; set; } = false;
    public int     Msaa            { get; set; } = 0;
    public int     ReflectionMsaa  { get; set; } = 0;

    public double  CityDensity      { get; set; } = 1.0;
    public double  PedVariety       { get; set; } = 1.0;
    public double  VehicleVariety   { get; set; } = 1.0;
    public double  LodScale         { get; set; } = 1.0;
    public double  VehicleLodBias   { get; set; } = 1.0;
    public double  PedLodBias       { get; set; } = 1.0;
    public int     GrassQuality     { get; set; } = 2;
    public int     ReflectionQuality{ get; set; } = 3;
    public double  ShadowDistance   { get; set; } = 1.0;
    public double  MaxLodScale      { get; set; } = 0.0;

    public int     Tessellation         { get; set; } = 3;
    public int     AnisotropicFiltering { get; set; } = 16;
    public int     Ssao                 { get; set; } = 2;
    public int     ShadowSoftShadows    { get; set; } = 3;
    public double  ShadowSplitZStart    { get; set; } = 0.93;
    public double  ShadowSplitZEnd      { get; set; } = 0.89;
    public bool    UltraShadows         { get; set; } = false;
    public bool    ShadowParticles      { get; set; } = false;
    public bool    ShadowLongShadows    { get; set; } = false;
    public bool    ReflectionMipBlur    { get; set; } = false;
    public int     DxVersion            { get; set; } = 3;
    public bool    Dof                  { get; set; } = false;
    public bool    HdStreaming          { get; set; } = false;
    public double  MotionBlur           { get; set; } = 0.0;
    public bool    FogVolumes           { get; set; } = false;

    public static GtaSettingsModel Defaults() => new();

    public static GtaSettingsModel FromXml(string xml)
    {
        var m = new GtaSettingsModel();
        if (string.IsNullOrWhiteSpace(xml)) return m;

        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return m; }

        var root = doc.Root;
        if (root is null) return m;

        var graphics = root.Element("graphics");
        var video    = root.Element("video");

        if (video is not null)
        {
            m.ScreenWidth   = ReadInt(video, "ScreenWidth",  m.ScreenWidth);
            m.ScreenHeight  = ReadInt(video, "ScreenHeight", m.ScreenHeight);
            m.RefreshRate   = ReadInt(video, "RefreshRate",  m.RefreshRate);
            m.AspectRatio   = ReadInt(video, "AspectRatio",  m.AspectRatio);
            m.Windowed      = ReadInt(video, "Windowed",     m.Windowed);
            m.VSync         = ReadInt(video, "VSync", 0) != 0;
        }

        if (graphics is null) return m;

        m.TextureQuality  = ReadInt   (graphics, "TextureQuality",  m.TextureQuality);
        m.ShaderQuality   = ReadInt   (graphics, "ShaderQuality",   m.ShaderQuality);
        m.WaterQuality    = ReadInt   (graphics, "WaterQuality",    m.WaterQuality);
        m.ParticleQuality = ReadInt   (graphics, "ParticleQuality", m.ParticleQuality);
        m.PostFx          = ReadInt   (graphics, "PostFX",          m.PostFx);
        m.ShadowQuality   = ReadInt   (graphics, "ShadowQuality",   m.ShadowQuality);

        m.Fxaa            = ReadBool  (graphics, "FXAA_Enabled",    m.Fxaa);
        m.Txaa            = ReadBool  (graphics, "TXAA_Enabled",    m.Txaa);
        m.Msaa            = ReadInt   (graphics, "MSAA",            m.Msaa);
        m.ReflectionMsaa  = ReadInt   (graphics, "ReflectionMSAA",  m.ReflectionMsaa);

        m.CityDensity      = ReadFloat(graphics, "CityDensity",                m.CityDensity);
        m.PedVariety       = ReadFloat(graphics, "PedVarietyMultiplier",       m.PedVariety);
        m.VehicleVariety   = ReadFloat(graphics, "VehicleVarietyMultiplier",   m.VehicleVariety);
        m.LodScale         = ReadFloat(graphics, "LodScale",                   m.LodScale);
        m.VehicleLodBias   = ReadFloat(graphics, "VehicleLodBias",             m.VehicleLodBias);
        m.PedLodBias       = ReadFloat(graphics, "PedLodBias",                 m.PedLodBias);
        m.GrassQuality     = ReadInt  (graphics, "GrassQuality",               m.GrassQuality);
        m.ReflectionQuality= ReadInt  (graphics, "ReflectionQuality",          m.ReflectionQuality);
        m.ShadowDistance   = ReadFloat(graphics, "Shadow_Distance",            m.ShadowDistance);
        m.MaxLodScale      = ReadFloat(graphics, "MaxLodScale",                m.MaxLodScale);

        m.Tessellation         = ReadInt  (graphics, "Tessellation",            m.Tessellation);
        m.AnisotropicFiltering = ReadInt  (graphics, "AnisotropicFiltering",    m.AnisotropicFiltering);
        m.Ssao                 = ReadInt  (graphics, "SSAO",                    m.Ssao);
        m.ShadowSoftShadows    = ReadInt  (graphics, "Shadow_SoftShadows",      m.ShadowSoftShadows);
        m.ShadowSplitZStart    = ReadFloat(graphics, "Shadow_SplitZStart",      m.ShadowSplitZStart);
        m.ShadowSplitZEnd      = ReadFloat(graphics, "Shadow_SplitZEnd",        m.ShadowSplitZEnd);
        m.UltraShadows         = ReadBool (graphics, "UltraShadows_Enabled",    m.UltraShadows);
        m.ShadowParticles      = ReadBool (graphics, "Shadow_ParticleShadows",  m.ShadowParticles);
        m.ShadowLongShadows    = ReadBool (graphics, "Shadow_LongShadows",      m.ShadowLongShadows);
        m.ReflectionMipBlur    = ReadBool (graphics, "Reflection_MipBlur",      m.ReflectionMipBlur);
        m.DxVersion            = ReadInt  (graphics, "DX_Version",              m.DxVersion);
        m.Dof                  = ReadBool (graphics, "DoF",                     m.Dof);
        m.HdStreaming          = ReadBool (graphics, "HdStreamingInFlight",     m.HdStreaming);
        m.MotionBlur           = ReadFloat(graphics, "MotionBlurStrength",      m.MotionBlur);
        m.FogVolumes           = ReadBool (graphics, "Lighting_FogVolumes",     m.FogVolumes);

        return m;
    }

    public void ApplyTo(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidOperationException("settings.xml has no root");
        var graphics = root.Element("graphics") ?? AddChild(root, "graphics");
        var video    = root.Element("video")    ?? AddChild(root, "video");

        WriteInt  (video, "ScreenWidth",  ScreenWidth);
        WriteInt  (video, "ScreenHeight", ScreenHeight);
        WriteInt  (video, "RefreshRate",  RefreshRate);
        WriteInt  (video, "AspectRatio",  AspectRatio);
        WriteInt  (video, "Windowed",     Windowed);
        WriteInt  (video, "VSync",        VSync ? 1 : 0);

        WriteInt  (graphics, "TextureQuality",  TextureQuality);
        WriteInt  (graphics, "ShaderQuality",   ShaderQuality);
        WriteInt  (graphics, "WaterQuality",    WaterQuality);
        WriteInt  (graphics, "ParticleQuality", ParticleQuality);
        WriteInt  (graphics, "PostFX",          PostFx);
        WriteInt  (graphics, "ShadowQuality",   ShadowQuality);

        WriteBool (graphics, "FXAA_Enabled",    Fxaa);
        WriteBool (graphics, "TXAA_Enabled",    Txaa);
        WriteInt  (graphics, "MSAA",            Msaa);
        WriteInt  (graphics, "ReflectionMSAA",  ReflectionMsaa);

        WriteFloat(graphics, "CityDensity",                CityDensity);
        WriteFloat(graphics, "PedVarietyMultiplier",       PedVariety);
        WriteFloat(graphics, "VehicleVarietyMultiplier",   VehicleVariety);
        WriteFloat(graphics, "LodScale",                   LodScale);
        WriteFloat(graphics, "VehicleLodBias",             VehicleLodBias);
        WriteFloat(graphics, "PedLodBias",                 PedLodBias);
        WriteInt  (graphics, "GrassQuality",               GrassQuality);
        WriteInt  (graphics, "ReflectionQuality",          ReflectionQuality);
        WriteFloat(graphics, "Shadow_Distance",            ShadowDistance);
        WriteFloat(graphics, "MaxLodScale",                MaxLodScale);

        WriteInt  (graphics, "Tessellation",            Tessellation);
        WriteInt  (graphics, "AnisotropicFiltering",    AnisotropicFiltering);
        WriteInt  (graphics, "SSAO",                    Ssao);
        WriteInt  (graphics, "Shadow_SoftShadows",      ShadowSoftShadows);
        WriteFloat(graphics, "Shadow_SplitZStart",      ShadowSplitZStart);
        WriteFloat(graphics, "Shadow_SplitZEnd",        ShadowSplitZEnd);
        WriteBool (graphics, "UltraShadows_Enabled",    UltraShadows);
        WriteBool (graphics, "Shadow_ParticleShadows",  ShadowParticles);
        WriteBool (graphics, "Shadow_LongShadows",      ShadowLongShadows);
        WriteBool (graphics, "Reflection_MipBlur",      ReflectionMipBlur);
        WriteInt  (graphics, "DX_Version",              DxVersion);
        WriteBool (graphics, "DoF",                     Dof);
        WriteBool (graphics, "HdStreamingInFlight",     HdStreaming);
        WriteFloat(graphics, "MotionBlurStrength",      MotionBlur);
        WriteBool (graphics, "Lighting_FogVolumes",     FogVolumes);
    }

    public string ToXml()
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("Settings",
                new XElement("graphics"),
                new XElement("video")));
        ApplyTo(doc);
        return doc.ToString();
    }

    private static int ReadInt(XElement parent, string name, int fallback)
    {
        var raw = parent.Element(name)?.Attribute("value")?.Value;
        if (raw is null) return fallback;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static double ReadFloat(XElement parent, string name, double fallback)
    {
        var raw = parent.Element(name)?.Attribute("value")?.Value;
        if (raw is null) return fallback;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static bool ReadBool(XElement parent, string name, bool fallback)
    {
        var raw = parent.Element(name)?.Attribute("value")?.Value;
        if (raw is null) return fallback;
        if (bool.TryParse(raw, out var v)) return v;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return n != 0;
        return fallback;
    }

    private static void WriteInt(XElement parent, string name, int value)
        => SetValueAttr(parent, name, value.ToString(CultureInfo.InvariantCulture));

    private static void WriteFloat(XElement parent, string name, double value)

        => SetValueAttr(parent, name, value.ToString("0.000000", CultureInfo.InvariantCulture));

    private static void WriteBool(XElement parent, string name, bool value)
        => SetValueAttr(parent, name, value ? "true" : "false");

    private static void SetValueAttr(XElement parent, string name, string value)
    {
        var el = parent.Element(name);
        if (el is null)
        {
            el = new XElement(name);
            parent.Add(el);
        }
        var attr = el.Attribute("value");
        if (attr is null) el.SetAttributeValue("value", value);
        else attr.Value = value;
    }

    private static XElement AddChild(XElement parent, string name)
    {
        var el = new XElement(name);
        parent.Add(el);
        return el;
    }
}
