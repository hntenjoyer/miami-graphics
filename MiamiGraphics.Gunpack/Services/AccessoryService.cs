#nullable disable
using SharpDX;
using CodeWalker.GameFiles;

namespace MiamiGraphics.Gunpack.Services;

public static class AccessoryService
{

    public static List<string> AttachQuad(
        string gunDir, byte[] pngBytes,
        float px, float py, float pz, float nx, float ny, float nz,
        float size, string texName, bool glbSpace = true)
    {
        var (bgra, tw, th) = TextureCodec.PngToBgra(pngBytes);
        var (pos, nrm) = ToDrawableSpace(px, py, pz, nx, ny, nz, glbSpace);

        float w = size, h = size * th / (float)tw;
        Vector3 up = Math.Abs(nrm.Z) < 0.9f ? new Vector3(0, 0, 1) : new Vector3(1, 0, 0);
        Vector3 t = Vector3.Normalize(Vector3.Cross(up, nrm));
        Vector3 b = Vector3.Cross(nrm, t);
        Vector3 p = pos + nrm * 0.004f;

        var verts = new List<MeshVert>
        {
            new(p - t*(w/2) + b*(h/2), nrm, 0, 0),
            new(p + t*(w/2) + b*(h/2), nrm, 1, 0),
            new(p + t*(w/2) - b*(h/2), nrm, 1, 1),
            new(p - t*(w/2) - b*(h/2), nrm, 0, 1),
        };
        var idx = new List<ushort> { 0, 1, 2, 0, 2, 3, 2, 1, 0, 3, 2, 0 };
        return PatchGunFiles(gunDir, bgra, tw, th, texName, verts, idx);
    }

    public static List<string> AttachPendant(
        string gunDir, byte[] pngBytes,
        float px, float py, float pz, float nx, float ny, float nz,
        float size, float depthFrac, string texName, bool glbSpace = true)
    {
        var (bgra, tw, th) = TextureCodec.PngToBgra(pngBytes);
        var (pos, nrm) = ToDrawableSpace(px, py, pz, nx, ny, nz, glbSpace);

        var mesh = ExtrudeService.Build(bgra, tw, th, size, size * Math.Clamp(depthFrac, 0.03f, 0.6f));
        if (mesh == null) throw new InvalidOperationException("не удалось построить контур из PNG (слишком мало непрозрачных пикселей?)");

        Vector3 f = new Vector3(nrm.X, nrm.Y, 0);
        if (f.LengthSquared() < 1e-4f) f = new Vector3(0, -1, 0);
        f.Normalize();
        Vector3 upW = new Vector3(0, 0, 1);
        Vector3 r = Vector3.Normalize(Vector3.Cross(upW, f));
        Vector3 anchor = pos + nrm * 0.004f;

        var verts = new List<MeshVert>(mesh.Positions.Count);
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            var lp = mesh.Positions[i];
            var ln = mesh.Normals[i];
            verts.Add(new(
                anchor + r * lp.X + upW * lp.Y + f * lp.Z,
                r * ln.X + upW * ln.Y + f * ln.Z,
                mesh.UVs[i].X, mesh.UVs[i].Y));
        }
        return PatchGunFiles(gunDir, bgra, tw, th, texName, verts, mesh.Indices);
    }

    public readonly record struct MeshVert(Vector3 P, Vector3 N, float U, float V);

    public static List<string> AttachRawMesh(
        string gunDir, byte[] pngBytes,
        float[] pos, float[] nrm, float[] uv, int[] idx, string texName)
    {
        int vc = pos.Length / 3;
        if (vc == 0) throw new InvalidOperationException("пустая геометрия");
        if (vc > 65000) throw new InvalidOperationException($"модель слишком детальная ({vc} вершин > 65000) — возьми проще (low-poly)");
        if (idx.Length % 3 != 0) throw new InvalidOperationException("индексы не кратны 3");

        var (bgra, tw, th) = TextureCodec.PngToBgra(pngBytes);
        var verts = new List<MeshVert>(vc);
        for (int i = 0; i < vc; i++)
        {
            var n = new Vector3(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]);
            if (n.LengthSquared() < 1e-8f) n = new Vector3(0, 0, 1); else n.Normalize();
            verts.Add(new MeshVert(
                new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]), n,
                i * 2 + 1 < uv.Length ? uv[i * 2] : 0f,
                i * 2 + 1 < uv.Length ? uv[i * 2 + 1] : 0f));
        }
        var indices = idx.Select(x => (ushort)x).ToList();
        return PatchGunFiles(gunDir, bgra, tw, th, texName, verts, indices);
    }

    public static List<string> ListAccessories(string gunDir)
    {
        foreach (var file in Directory.GetFiles(gunDir, "*.ydr"))
        {
            string n = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            if (n.Contains("_mag") || n.Contains("_sight")) continue;
            var ydr = new YdrFile();
            ydr.Load(File.ReadAllBytes(file));
            var d = ydr.Drawable;
            var model = d?.DrawableModels?.High?.FirstOrDefault();
            var shaders = d?.ShaderGroup?.Shaders?.data_items;
            if (model?.ShaderMapping == null || shaders == null) continue;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var si in model.ShaderMapping)
            {
                if (si >= shaders.Length) continue;
                var t = FirstTexture(shaders[si]);
                if (t?.Name != null && t.Name.StartsWith("brelok", StringComparison.OrdinalIgnoreCase))
                    names.Add(t.Name);
            }
            return names.OrderBy(x => x).ToList();
        }
        return new List<string>();
    }

    public static List<string> RemoveAccessory(string gunDir, string texName)
    {
        var patched = new List<string>();
        foreach (var file in Directory.GetFiles(gunDir, "*.ydr"))
        {
            string n = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            if (n.Contains("_mag") || n.Contains("_sight")) continue;
            var ydr = new YdrFile();
            ydr.Load(File.ReadAllBytes(file));
            if (RemoveFromDrawable(ydr.Drawable, texName))
            {
                File.WriteAllBytes(file, ydr.Save());
                patched.Add(Path.GetFileName(file));
            }
        }
        return patched;
    }

    private static Texture FirstTexture(ShaderFX sh)
    {
        var ps = sh?.ParametersList?.Parameters;
        if (ps == null) return null;
        foreach (var p in ps) if (p?.Data is Texture t) return t;
        return null;
    }

    private static bool RemoveFromDrawable(Drawable d, string texName)
    {
        var model = d?.DrawableModels?.High?.FirstOrDefault();
        var shaders = d?.ShaderGroup?.Shaders?.data_items;
        if (model?.Geometries == null || model.ShaderMapping == null || shaders == null) return false;

        var removeShaders = new HashSet<int>();
        for (int i = 0; i < shaders.Length; i++)
        {
            var t = FirstTexture(shaders[i]);
            if (t?.Name != null && t.Name.Equals(texName, StringComparison.OrdinalIgnoreCase))
                removeShaders.Add(i);
        }
        if (removeShaders.Count == 0) return false;

        var keptGeoms = new List<DrawableGeometry>();
        var keptMappingOld = new List<ushort>();
        for (int gi = 0; gi < model.Geometries.Length; gi++)
        {
            ushort sIdx = gi < model.ShaderMapping.Length ? model.ShaderMapping[gi] : (ushort)0;
            if (removeShaders.Contains(sIdx)) continue;
            keptGeoms.Add(model.Geometries[gi]);
            keptMappingOld.Add(sIdx);
        }

        var remap = new Dictionary<int, int>();
        var newShaders = new List<ShaderFX>();
        for (int i = 0; i < shaders.Length; i++)
        {
            if (removeShaders.Contains(i)) continue;
            remap[i] = newShaders.Count;
            newShaders.Add(shaders[i]);
        }

        d.ShaderGroup.Shaders.data_items = newShaders.ToArray();
        d.ShaderGroup.ShadersCount1 = (ushort)newShaders.Count;
        d.ShaderGroup.ShadersCount2 = (ushort)newShaders.Count;

        model.Geometries = keptGeoms.ToArray();
        model.GeometriesCount1 = (ushort)keptGeoms.Count;
        model.GeometriesCount2 = (ushort)keptGeoms.Count;
        model.GeometriesCount3 = (ushort)keptGeoms.Count;
        model.ShaderMapping = keptMappingOld.Select(o => (ushort)remap[o]).ToArray();

        RebuildBoundsAll(model);

        var td = d.ShaderGroup.TextureDictionary;
        if (td?.Textures?.data_items != null)
        {
            var texList = td.Textures.data_items
                .Where(t => !t.Name.Equals(texName, StringComparison.OrdinalIgnoreCase)).ToList();
            td.BuildFromTextureList(texList);
        }
        d.BuildRenderMasks();
        return true;
    }

    private static void RebuildBoundsAll(DrawableModel model)
    {
        var geoms = model.Geometries;
        if (geoms == null || geoms.Length == 0) { model.BoundsData = null; return; }
        var per = geoms.Select(ComputeAabb).ToList();
        if (per.Count == 1) { model.BoundsData = per.ToArray(); return; }
        var outer = new AABB_s { Min = new Vector4(float.MaxValue), Max = new Vector4(float.MinValue) };
        foreach (var a in per) { outer.Min = Vector4.Min(outer.Min, a.Min); outer.Max = Vector4.Max(outer.Max, a.Max); }
        var res = new List<AABB_s> { outer }; res.AddRange(per);
        model.BoundsData = res.ToArray();
    }

    private static (Vector3 pos, Vector3 nrm) ToDrawableSpace(
        float px, float py, float pz, float nx, float ny, float nz, bool glbSpace)
    {
        Vector3 pos = glbSpace ? new Vector3(px, -pz, py) : new Vector3(px, py, pz);
        Vector3 nrm = glbSpace ? new Vector3(nx, -nz, ny) : new Vector3(nx, ny, nz);
        if (nrm.LengthSquared() < 1e-6f) nrm = new Vector3(0, 0, 1);
        nrm.Normalize();
        return (pos, nrm);
    }

    private static List<string> PatchGunFiles(
        string gunDir, byte[] bgra, int tw, int th, string texName,
        List<MeshVert> verts, List<ushort> indices)
    {
        var patched = new List<string>();
        foreach (var file in Directory.GetFiles(gunDir, "*.ydr"))
        {
            string n = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            if (n.Contains("_mag") || n.Contains("_sight")) continue;

            var ydr = new YdrFile();
            ydr.Load(File.ReadAllBytes(file));
            var d = ydr.Drawable;
            if (d?.ShaderGroup?.TextureDictionary == null || d.DrawableModels?.High == null || d.DrawableModels.High.Length == 0)
                continue;

            InjectMesh(d, bgra, tw, th, texName, verts, indices);
            File.WriteAllBytes(file, ydr.Save());
            patched.Add(Path.GetFileName(file));
        }
        return patched;
    }

    private static void InjectMesh(Drawable d, byte[] bgra, int tw, int th,
        string texName, List<MeshVert> verts, List<ushort> indices)
    {
        var td = d.ShaderGroup.TextureDictionary;
        var texList = (td.Textures?.data_items ?? Array.Empty<Texture>()).ToList();
        var donorTex = texList.FirstOrDefault();

        var tex = new Texture
        {
            Name = texName,
            NameHash = JenkHash.GenHash(texName.ToLowerInvariant()),
            Width = (ushort)tw,
            Height = (ushort)th,
            Depth = 1,
            Levels = 1,
            Format = TextureFormat.D3DFMT_A8R8G8B8,
            Stride = (ushort)(tw * 4),
            Data = new TextureData { FullData = bgra },
            VFT = donorTex?.VFT ?? 2483783232,
            Unknown_4h = donorTex?.Unknown_4h ?? 32760,
            Unknown_30h = donorTex?.Unknown_30h ?? 1,
            Unknown_32h = donorTex?.Unknown_32h ?? 128,
            Usage = donorTex?.Usage ?? TextureUsage.UNKNOWN,
            UsageData = donorTex?.UsageData ?? 538269056,
        };
        texList.RemoveAll(t => t.NameHash == tex.NameHash);
        texList.Add(tex);
        td.BuildFromTextureList(texList);

        var model = d.DrawableModels.High[0];
        bool skinned = model.HasSkin != 0;
        var donorGeom = model.Geometries?.FirstOrDefault();
        ushort boneLocal = FindMainBoneLocalIndex(d, donorGeom);

        var donorShader = donorGeom?.Shader
            ?? (d.ShaderGroup.Shaders.data_items.Length > 0 ? d.ShaderGroup.Shaders.data_items[0] : null);
        var shader = CloneShaderWithDiffuse(donorShader, tex) ?? BuildDefaultShader(tex);
        var slist = d.ShaderGroup.Shaders.data_items.ToList();
        int shaderIdx = slist.Count;
        slist.Add(shader);
        d.ShaderGroup.Shaders.data_items = slist.ToArray();
        d.ShaderGroup.ShadersCount1 = (ushort)slist.Count;
        d.ShaderGroup.ShadersCount2 = (ushort)slist.Count;

        var geom = BuildGeometry(verts, indices, skinned, boneLocal, shader, donorGeom);

        var geoms = (model.Geometries ?? Array.Empty<DrawableGeometry>()).ToList();
        geoms.Add(geom);
        model.Geometries = geoms.ToArray();
        model.GeometriesCount1 = (ushort)geoms.Count;
        model.GeometriesCount2 = (ushort)geoms.Count;
        model.GeometriesCount3 = (ushort)geoms.Count;

        var smap = (model.ShaderMapping ?? Array.Empty<ushort>()).ToList();
        smap.Add((ushort)shaderIdx);
        model.ShaderMapping = smap.ToArray();

        RebuildBoundsData(model, geom);

        foreach (var v in verts)
        {
            d.BoundingBoxMin = Vector3.Min(d.BoundingBoxMin, v.P);
            d.BoundingBoxMax = Vector3.Max(d.BoundingBoxMax, v.P);
            float rr = (v.P - d.BoundingCenter).Length();
            if (rr > d.BoundingSphereRadius) d.BoundingSphereRadius = rr;
        }

        d.BuildRenderMasks();
    }

    private static byte FindMainBoneLocalIndex(Drawable d, DrawableGeometry donorGeom)
    {
        var bones = d.Skeleton?.Bones?.Items;
        if (bones == null || bones.Length == 0) return 0;

        int main = -1;
        foreach (var pref in new[] { "Gun_Main_Bone", "Gun_Root" })
        {
            for (int i = 0; i < bones.Length; i++)
                if (string.Equals(bones[i].Name, pref, StringComparison.OrdinalIgnoreCase)) { main = i; break; }
            if (main >= 0) break;
        }
        if (main < 0)
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i].ParentIndex < 0) { main = i; break; }
        }
        if (main < 0) { Console.WriteLine("[Accessory] WARN: кость крепления не найдена, бинжу к 0"); return 0; }

        var ids = donorGeom?.BoneIds;
        if (ids != null)
            for (int i = 0; i < ids.Length; i++)
                if (ids[i] == main) return (byte)Math.Min(i, 255);

        return (byte)Math.Min(main, 255);
    }

    private static ShaderFX CloneShaderWithDiffuse(ShaderFX donor, Texture tex)
    {
        var src = donor?.ParametersList;
        if (src?.Parameters == null || src.Hashes == null) return null;

        var sh = new ShaderFX
        {
            Name = donor.Name, FileName = donor.FileName,
            RenderBucket = donor.RenderBucket, RenderBucketMask = donor.RenderBucketMask,
            Unknown_Ch = donor.Unknown_Ch, Unknown_12h = donor.Unknown_12h,
            Unknown_1Ch = donor.Unknown_1Ch, Unknown_24h = donor.Unknown_24h,
            Unknown_26h = donor.Unknown_26h, Unknown_28h = donor.Unknown_28h,
            ParameterSize = donor.ParameterSize, ParameterDataSize = donor.ParameterDataSize,
            ParameterCount = donor.ParameterCount, TextureParametersCount = donor.TextureParametersCount,
            ParametersList = new ShaderParametersBlock(),
        };

        bool diffuseSwapped = false;
        var pars = new ShaderParameter[src.Parameters.Length];
        for (int i = 0; i < src.Parameters.Length; i++)
        {
            var sp = src.Parameters[i];
            object data = sp.Data;
            if (!diffuseSwapped && sp.Data is TextureBase) { data = tex; diffuseSwapped = true; }
            pars[i] = new ShaderParameter { DataType = sp.DataType, Unknown_1h = sp.Unknown_1h, Data = data };
        }
        if (!diffuseSwapped) return null;

        var block = sh.ParametersList;
        block.Hashes = (MetaName[])src.Hashes.Clone();
        block.Parameters = pars;
        block.Count = src.Count;
        return sh;
    }

    private static DrawableGeometry BuildGeometry(
        List<MeshVert> verts, List<ushort> indices,
        bool skinned, ushort boneLocal, ShaderFX shader, DrawableGeometry donorGeom)
    {
        var decl = new VertexDeclaration
        {
            Types = VertexDeclarationTypes.GTAV1,
            Unknown_6h = 0,
            Flags = skinned ? (uint)0x5F : 89,
            Stride = (ushort)(skinned ? 44 : 36),
            Count = (byte)(skinned ? 6 : 4),
        };

        ushort stride = decl.Stride;
        var vBytes = new byte[verts.Count * stride];
        using (var ms = new MemoryStream(vBytes))
        using (var bw = new BinaryWriter(ms))
        {
            foreach (var v in verts)
            {
                bw.Write(v.P.X); bw.Write(v.P.Y); bw.Write(v.P.Z);
                if (skinned)
                {
                    bw.Write((byte)255); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
                    bw.Write((byte)boneLocal); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
                }
                bw.Write(v.N.X); bw.Write(v.N.Y); bw.Write(v.N.Z);
                bw.Write((byte)255); bw.Write((byte)255); bw.Write((byte)255); bw.Write((byte)255);
                bw.Write(v.U); bw.Write(v.V);
            }
        }

        var vData = new VertexData
        {
            Info = decl,
            VertexType = (VertexType)decl.Flags,
            VertexStride = stride,
            VertexCount = verts.Count,
            VertexBytes = vBytes,
        };
        var vBuff = new VertexBuffer
        {
            Data1 = vData, Data2 = vData, Info = decl,
            VertexCount = (uint)verts.Count, VertexStride = stride,
            VFT = donorGeom?.VertexBuffer?.VFT ?? 1080153064,
            Unknown_4h = 1,
        };
        var iBuff = new IndexBuffer
        {
            IndicesCount = (uint)indices.Count, Indices = indices.ToArray(),
            VFT = donorGeom?.IndexBuffer?.VFT ?? 1080111576,
            Unknown_4h = 1,
        };
        return new DrawableGeometry
        {
            Shader = shader,
            VertexData = vData,
            VertexBuffer = vBuff,
            IndexBuffer = iBuff,
            VFT = donorGeom?.VFT ?? 1080133736,
            Unknown_4h = donorGeom?.Unknown_4h ?? 1,
            IndicesCount = (uint)indices.Count,
            TrianglesCount = (uint)indices.Count / 3,
            VerticesCount = (ushort)verts.Count,
            Unknown_62h = 3,
            VertexStride = stride,
            BoneIds = donorGeom?.BoneIds?.ToArray(),
            BoneIdsCount = (ushort)(donorGeom?.BoneIds?.Length ?? 0),
        };
    }

    private static void RebuildBoundsData(DrawableModel model, DrawableGeometry newGeom)
    {
        var geoms = model.Geometries;
        var old = model.BoundsData ?? Array.Empty<AABB_s>();

        var per = new List<AABB_s>();
        int oldGeomCount = geoms.Length - 1;
        if (oldGeomCount >= 1)
        {
            if (oldGeomCount == 1 && old.Length >= 1) per.Add(old[old.Length - 1]);
            else if (old.Length == oldGeomCount + 1) per.AddRange(old.Skip(1));
            else if (old.Length == oldGeomCount) per.AddRange(old);
        }
        while (per.Count < oldGeomCount) per.Add(old.LastOrDefault());

        per.Add(ComputeAabb(newGeom));

        if (per.Count == 1) { model.BoundsData = per.ToArray(); return; }
        var outer = new AABB_s { Min = new Vector4(float.MaxValue), Max = new Vector4(float.MinValue) };
        foreach (var a in per)
        {
            outer.Min = Vector4.Min(outer.Min, a.Min);
            outer.Max = Vector4.Max(outer.Max, a.Max);
        }
        var res = new List<AABB_s> { outer };
        res.AddRange(per);
        model.BoundsData = res.ToArray();
    }

    private static AABB_s ComputeAabb(DrawableGeometry g)
    {
        var aabb = new AABB_s { Min = new Vector4(float.MaxValue), Max = new Vector4(float.MinValue) };
        var vb = g.VertexData.VertexBytes;
        int stride = g.VertexData.VertexStride, count = g.VertexData.VertexCount;
        for (int i = 0; i < count; i++)
        {
            float x = BitConverter.ToSingle(vb, i * stride);
            float y = BitConverter.ToSingle(vb, i * stride + 4);
            float z = BitConverter.ToSingle(vb, i * stride + 8);
            var v4 = new Vector4(x, y, z, x);
            aabb.Min = Vector4.Min(aabb.Min, v4);
            aabb.Max = Vector4.Max(aabb.Max, v4);
        }
        return aabb;
    }

    private static ShaderFX BuildDefaultShader(Texture tex)
    {
        var shader = new ShaderFX
        {
            Name = JenkHash.GenHash("default"),
            FileName = JenkHash.GenHash("default.sps"),
            Unknown_Ch = 0,
            RenderBucket = 0,
            Unknown_12h = 32768,
            Unknown_1Ch = 0,
            Unknown_24h = 0,
            Unknown_26h = 0,
            Unknown_28h = 0,
            ParametersList = new ShaderParametersBlock(),
        };

        var pNames = new List<ShaderParamNames>();
        var pVals = new List<ShaderParameter>();
        void Add(ShaderParamNames name, object val)
        {
            var sp = new ShaderParameter { Data = val };
            if (val is TextureBase) { sp.DataType = 0; sp.Unknown_1h = (byte)((pNames.Count > 0) ? pNames.Count + 1 : 0); }
            else sp.DataType = 1;
            pNames.Add(name); pVals.Add(sp);
        }

        Add(ShaderParamNames.DiffuseSampler, tex);
        Add(ShaderParamNames.matMaterialColorScale, new Vector4(1, 0, 0, 1));
        Add(ShaderParamNames.HardAlphaBlend, new Vector4(0, 0, 0, 0));
        Add(ShaderParamNames.useTessellation, new Vector4(0, 0, 0, 0));
        Add(ShaderParamNames.wetnessMultiplier, new Vector4(1, 0, 0, 0));
        Add(ShaderParamNames.globalAnimUV1, new Vector4(0, 1, 0, 0));
        Add(ShaderParamNames.globalAnimUV0, new Vector4(1, 0, 0, 0));

        for (int i = 0; i < pVals.Count; i++)
            if (pVals[i].DataType == 1)
                pVals[i].Unknown_1h = (byte)(160 + ((pVals.Count - 1) - i));

        var block = shader.ParametersList;
        block.Hashes = pNames.Select(x => (MetaName)x).ToArray();
        block.Parameters = pVals.ToArray();
        block.Count = pVals.Count;

        shader.ParameterSize = block.ParametersSize;
        shader.ParameterDataSize = (ushort)(block.BlockLength + 36);
        shader.ParameterCount = (byte)pVals.Count;
        shader.TextureParametersCount = block.TextureParamsCount;
        shader.RenderBucketMask = (1u << shader.RenderBucket) | 0xFF00;
        return shader;
    }
}
