#nullable disable
using System.Text;
using CodeWalker.GameFiles;

namespace MiamiGraphics.Gunpack.Services;

public static class DrawableDump
{
    public static string Dump(string ydrPath)
    {
        var sb = new StringBuilder();
        void W(string s) => sb.AppendLine(s);

        var ydr = new YdrFile();
        ydr.Load(File.ReadAllBytes(ydrPath));
        var d = ydr.Drawable;
        W($"=== {Path.GetFileName(ydrPath)} ===");
        W($"Name: {d.Name}");
        W($"BSCenter: {d.BoundingCenter}  BSRadius: {d.BoundingSphereRadius}");
        W($"BBox: {d.BoundingBoxMin} .. {d.BoundingBoxMax}");
        W($"LodDist H/M/L/VL: {d.LodDistHigh}/{d.LodDistMed}/{d.LodDistLow}/{d.LodDistVlow}");
        W($"Flags H/M/L/VL: {d.FlagsHigh}/{d.FlagsMed}/{d.FlagsLow}/{d.FlagsVlow}");

        var bones = d.Skeleton?.Bones?.Items;
        W($"\n-- SKELETON: {bones?.Length ?? 0} bones");
        if (bones != null)
            for (int i = 0; i < bones.Length; i++)
                W($"  [{i,2}] {bones[i].Name,-20} parent={bones[i].ParentIndex} tag={bones[i].Tag}");

        void DumpModels(string lod, DrawableModel[] models)
        {
            W($"\n-- LOD {lod}: {models?.Length ?? 0} models");
            if (models == null) return;
            for (int mi = 0; mi < models.Length; mi++)
            {
                var m = models[mi];
                W($"  [Model {mi}] VFT={m.VFT} Unk4={m.Unknown_4h} HasSkin={m.HasSkin} BoneIndex={m.BoneIndex} " +
                  $"RenderMaskFlags=0x{m.RenderMaskFlags:X4} geoms={m.Geometries?.Length ?? 0} " +
                  $"cnt1/2/3={m.GeometriesCount1}/{m.GeometriesCount2}/{m.GeometriesCount3} " +
                  $"boundsData={m.BoundsData?.Length ?? 0} shaderMap=[{string.Join(",", m.ShaderMapping ?? Array.Empty<ushort>())}]");
                if (m.Geometries == null) continue;
                for (int gi = 0; gi < m.Geometries.Length; gi++)
                {
                    var g = m.Geometries[gi];
                    var decl = g.VertexData?.Info;
                    W($"    [Geom {gi}] verts={g.VerticesCount} idx={g.IndicesCount} tris={g.TrianglesCount} " +
                      $"stride={g.VertexStride} Unk62={g.Unknown_62h} boneIds={(g.BoneIds == null ? "null" : g.BoneIds.Length.ToString())} " +
                      $"VFT={g.VFT} declFlags=0x{decl?.Flags:X} declTypes={decl?.Types} declStride={decl?.Stride} declCount={decl?.Count}");
                }
            }
        }
        DumpModels("High", d.DrawableModels?.High);
        DumpModels("Med", d.DrawableModels?.Med);

        var shaders = d.ShaderGroup?.Shaders?.data_items;
        W($"\n-- SHADERS: {shaders?.Length ?? 0}  (grpVFT={d.ShaderGroup?.VFT} grpUnk4={d.ShaderGroup?.Unknown_4h} cnt1={d.ShaderGroup?.ShadersCount1} cnt2={d.ShaderGroup?.ShadersCount2})");
        if (shaders != null)
            for (int i = 0; i < shaders.Length; i++)
            {
                var s = shaders[i];
                W($"  [Sh {i}] Name={s.Name} File={s.FileName} bucket={s.RenderBucket} bucketMask=0x{s.RenderBucketMask:X} " +
                  $"pSize={s.ParameterSize} pDataSize={s.ParameterDataSize} pCount={s.ParameterCount} texCount={s.TextureParametersCount} " +
                  $"UnkC={s.Unknown_Ch} Unk12={s.Unknown_12h}");
                var pl = s.ParametersList;
                if (pl?.Parameters != null)
                    for (int p = 0; p < pl.Parameters.Length; p++)
                    {
                        var par = pl.Parameters[p];
                        string val = par.Data is TextureBase tb
                            ? $"TEX '{tb.Name}' hash={tb.NameHash} Unk4={tb.Unknown_4h} Unk30={tb.Unknown_30h} Unk32={tb.Unknown_32h} (type={tb.GetType().Name})"
                            : par.Data?.ToString() ?? "null";
                        W($"      [{p}] hash={pl.Hashes[p]} dt={par.DataType} Unk1={par.Unknown_1h} → {val}");
                    }
            }

        var texs = d.ShaderGroup?.TextureDictionary?.Textures?.data_items;
        var hashes = d.ShaderGroup?.TextureDictionary?.TextureNameHashes?.data_items;
        W($"\n-- TEXTURE DICT: {texs?.Length ?? 0} entries");
        W($"   nameHashes: [{string.Join(",", hashes ?? Array.Empty<uint>())}]");
        if (texs != null)
            foreach (var t in texs)
            {
                W($"  TEX '{t.Name}' hash={t.NameHash}");
                W($"      {t.Width}x{t.Height} depth={t.Depth} levels={t.Levels} fmt={t.Format} stride={t.Stride} dataLen={t.Data?.FullData?.Length}");
                W($"      VFT={t.VFT} Unk4={t.Unknown_4h} Unk30={t.Unknown_30h} Unk32={t.Unknown_32h} Usage={t.Usage} UsageData={t.UsageData}");
                W($"      Unk38={t.Unknown_38h} Unk3C={t.Unknown_3Ch} ExtraFlags={t.ExtraFlags} Unk4C={t.Unknown_4Ch} Unk5C={t.Unknown_5Ch} Unk5E={t.Unknown_5Eh}");
                W($"      Unk60={t.Unknown_60h} Unk64={t.Unknown_64h} Unk68={t.Unknown_68h} Unk6C={t.Unknown_6Ch} Unk78={t.Unknown_78h} Unk7C={t.Unknown_7Ch}");
                W($"      Unk80={t.Unknown_80h} Unk84={t.Unknown_84h} Unk88={t.Unknown_88h} Unk8C={t.Unknown_8Ch}");
            }

        return sb.ToString();
    }
}
