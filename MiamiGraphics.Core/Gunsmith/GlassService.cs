#nullable disable
using System.Text.Json.Serialization;
using CodeWalker.GameFiles;
using SharpDX;

namespace MiamiGraphics.Core.Gunsmith;

public static class GlassService
{
    public static readonly uint AlphaSpsFile   = JenkHash.GenHash("normal_spec_alpha.sps");
    public static readonly uint AlphaSpsName   = JenkHash.GenHash("normal_spec");
    public static readonly uint NormalSpecFile = JenkHash.GenHash("normal_spec.sps");

    public static byte[] TransformYdr(byte[] bytes, GlassState glass)
    {
        if (glass == null || !glass.Any) return null;
        var ydr = new YdrFile();
        ydr.Load(bytes);
        var d = ydr.Drawable;
        bool changed = false;

        Texture flatN = null, flatS = null;
        var shaders = d?.ShaderGroup?.Shaders?.data_items;
        if (shaders != null)
            foreach (var sh in shaders)
            {
                if (sh == null || sh.RenderBucket != 0) continue;
                string diff = DiffuseName(sh);
                if (diff == null || !glass.Textures.ContainsKey(diff)) continue;

                if ((uint)sh.FileName == NormalSpecFile)
                {
                }
                else
                {
                    EnsureFlatHelpers(d, ref flatN, ref flatS);
                    RebuildAlphaParams(sh, flatN, flatS);
                }
                sh.FileName = AlphaSpsFile;
                sh.Name = AlphaSpsName;
                sh.RenderBucket = 1;
                sh.RenderBucketMask = (1u << 1) | 0xFF00;
                changed = true;
            }

        if (BakeDict(d?.ShaderGroup?.TextureDictionary, glass)) changed = true;
        return changed ? ydr.Save() : null;
    }

    public static byte[] TransformYtd(byte[] bytes, GlassState glass)
    {
        if (glass == null || !glass.Any) return null;
        var ytd = new YtdFile();
        ytd.Load(bytes);
        return BakeDict(ytd.TextureDict, glass) ? ytd.Save() : null;
    }

    private static string DiffuseName(ShaderFX sh)
    {
        var pl = sh.ParametersList;
        if (pl?.Parameters == null || pl.Hashes == null) return null;
        string first = null;
        for (int i = 0; i < pl.Parameters.Length; i++)
        {
            if (pl.Parameters[i]?.Data is not TextureBase tb || string.IsNullOrEmpty(tb.Name)) continue;
            string h = pl.Hashes[i].ToString().ToLowerInvariant();
            if (h.Contains("diffuse")) return tb.Name;
            first ??= tb.Name;
        }
        return first;
    }

    private static void RebuildAlphaParams(ShaderFX sh, TextureBase flatNormal, TextureBase flatSpec)
    {
        TextureBase diffuse = null, bump = null, spec = null;
        var src = sh.ParametersList;
        if (src?.Parameters != null && src.Hashes != null)
            for (int i = 0; i < src.Parameters.Length; i++)
            {
                if (src.Parameters[i]?.Data is not TextureBase tb) continue;
                string h = src.Hashes[i].ToString().ToLowerInvariant();
                if (h.Contains("diffuse")) diffuse ??= tb;
                else if (h.Contains("bump") || h.Contains("normal")) bump ??= tb;
                else if (h.Contains("spec")) spec ??= tb;
                else diffuse ??= tb;
            }
        if (diffuse == null) return;
        bump ??= flatNormal ?? diffuse;
        spec ??= flatSpec ?? diffuse;

        var names = new List<ShaderParamNames>();
        var vals = new List<ShaderParameter>();
        void Add(ShaderParamNames name, object val)
        {
            var sp = new ShaderParameter { Data = val };
            if (val is TextureBase) { sp.DataType = 0; sp.Unknown_1h = (byte)((names.Count > 0) ? names.Count + 1 : 0); }
            else sp.DataType = 1;
            names.Add(name); vals.Add(sp);
        }

        Add(ShaderParamNames.DiffuseSampler, diffuse);
        Add(ShaderParamNames.BumpSampler, bump);
        Add(ShaderParamNames.SpecSampler, spec);
        Add(ShaderParamNames.HardAlphaBlend, new Vector4(1, 0, 0, 0));
        Add(ShaderParamNames.useTessellation, new Vector4(0, 0, 0, 0));
        Add(ShaderParamNames.wetnessMultiplier, new Vector4(1, 0, 0, 0));
        Add(ShaderParamNames.bumpiness, new Vector4(1, 0, 0, 0));
        Add(ShaderParamNames.specMapIntMask, new Vector4(1, 0, 0, 0));
        Add(ShaderParamNames.specularIntensityMult, new Vector4(1, 0, 0, 0));
        Add(ShaderParamNames.specularFalloffMult, new Vector4(100, 0, 0, 0));
        Add(ShaderParamNames.specularFresnel, new Vector4(0.75f, 0, 0, 0));

        for (int i = 0; i < vals.Count; i++)
            if (vals[i].DataType == 1)
                vals[i].Unknown_1h = (byte)(160 + ((vals.Count - 1) - i));

        var block = new ShaderParametersBlock
        {
            Hashes = names.Select(x => (MetaName)x).ToArray(),
            Parameters = vals.ToArray(),
            Count = vals.Count,
        };
        sh.ParametersList = block;
        sh.ParameterSize = block.ParametersSize;
        sh.ParameterDataSize = (ushort)(block.BlockLength + 36);
        sh.ParameterCount = (byte)vals.Count;
        sh.TextureParametersCount = block.TextureParamsCount;
    }

    private static void EnsureFlatHelpers(Drawable d, ref Texture flatN, ref Texture flatS)
    {
        if (flatN != null && flatS != null) return;
        var td = d.ShaderGroup.TextureDictionary;
        if (td == null) { td = new TextureDictionary(); d.ShaderGroup.TextureDictionary = td; }
        var list = (td.Textures?.data_items ?? Array.Empty<Texture>()).ToList();
        var donor = list.FirstOrDefault();

        Texture Make(string name, byte b, byte g, byte r)
        {
            const int n = 4;
            var data = new byte[n * n * 4];
            for (int i = 0; i < data.Length; i += 4) { data[i] = b; data[i + 1] = g; data[i + 2] = r; data[i + 3] = 255; }
            return new Texture
            {
                Name = name,
                NameHash = JenkHash.GenHash(name.ToLowerInvariant()),
                Width = n, Height = n, Depth = 1, Levels = 1,
                Format = TextureFormat.D3DFMT_A8R8G8B8,
                Stride = n * 4,
                Data = new TextureData { FullData = data },
                VFT = donor?.VFT ?? 2483783232,
                Unknown_4h = donor?.Unknown_4h ?? 32760,
                Unknown_30h = donor?.Unknown_30h ?? 1,
                Unknown_32h = donor?.Unknown_32h ?? 128,
                Usage = donor?.Usage ?? TextureUsage.UNKNOWN,
                UsageData = donor?.UsageData ?? 538269056,
            };
        }

        flatN = list.FirstOrDefault(t => t.Name == "mg_glass_flatn") ?? Make("mg_glass_flatn", 255, 128, 128);
        flatS = list.FirstOrDefault(t => t.Name == "mg_glass_flats") ?? Make("mg_glass_flats", 0, 0, 0);
        if (!list.Contains(flatN)) list.Add(flatN);
        if (!list.Contains(flatS)) list.Add(flatS);
        td.BuildFromTextureList(list);
    }

    private static bool BakeDict(TextureDictionary td, GlassState glass)
    {
        var items = td?.Textures?.data_items;
        if (items == null) return false;
        bool any = false;
        foreach (var t in items)
            if (t?.Name != null && glass.Textures.TryGetValue(t.Name, out var g)) { BakeAlpha(t, g); any = true; }
        return any;
    }

    private static void BakeAlpha(Texture tex, GlassTex g)
    {
        var png = TextureCodec.ToPng(tex);
        var (bgra, w, h) = TextureCodec.PngToBgra(png);
        byte a = (byte)Math.Clamp((int)Math.Round(g.Opacity * 255f), 8, 255);
        for (int i = 3; i < bgra.Length; i += 4) bgra[i] = a;

        MiamiGraphics.Core.Services.GameTextureWriter.Apply(tex, bgra, w, h);
    }
}

public sealed class GlassState
{
    [JsonPropertyName("textures")]
    public Dictionary<string, GlassTex> Textures { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore] public bool Any => Textures != null && Textures.Count > 0;
}

public sealed class GlassTex
{
    [JsonPropertyName("opacity")] public float Opacity { get; set; } = 0.4f;
    [JsonPropertyName("color")] public string Color { get; set; } = "#7fdfff";
}
