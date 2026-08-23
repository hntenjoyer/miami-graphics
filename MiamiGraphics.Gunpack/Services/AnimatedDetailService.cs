#nullable disable
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using SharpDX;
using CodeWalker;
using CodeWalker.GameFiles;

namespace MiamiGraphics.Gunpack.Services;

public static class AnimatedDetailService
{
    private const uint SkelVFT = 1080114336;
    private const uint GeomVFT = 1080133528;
    private const uint VBufVFT = 1080153080;
    private const uint IBufVFT = 1080152408;
    private const uint JointsVFT = 1080130656;
    private const ushort MovableBoneTag = 417;
    private const uint ArchetypeFlags = 525312;

    public sealed class GenRequest
    {
        public string SourceKind;
        public byte[] Png;
        public float[] Pos, Nrm, Uv;
        public int[] Idx;
        public float Size = 0.12f;
        public float DepthFrac = 0.15f;
        public string AnimMode = "uv";
        public float ScrollU = 1f, ScrollV = 0f;
        public float AxisX = 1f, AxisY = 0f, AxisZ = 0f;
        public float AmplitudeDeg = 20f;
        public float PeriodSec = 2.0f;
        public string AttachBone = "Gun_Main_Bone";
        public string Name = "anim";
        public string WeaponModel;
    }

    public sealed class GenResult
    {
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("error")] public string Error { get; set; }
        [JsonPropertyName("files")] public List<string> Files { get; set; } = new();
        [JsonPropertyName("detailModel")] public string DetailModel { get; set; }
        [JsonPropertyName("clipDict")] public string ClipDict { get; set; }
        [JsonPropertyName("componentName")] public string ComponentName { get; set; }
        [JsonPropertyName("previewGlb")] public string PreviewGlb { get; set; }
        [JsonPropertyName("anim")] public AnimMeta Anim { get; set; }
    }

    public sealed class AnimMeta
    {
        [JsonPropertyName("mode")] public string Mode { get; set; }
        [JsonPropertyName("periodSec")] public float PeriodSec { get; set; }
        [JsonPropertyName("scrollU")] public float ScrollU { get; set; }
        [JsonPropertyName("scrollV")] public float ScrollV { get; set; }
        [JsonPropertyName("axis")] public float[] Axis { get; set; }
        [JsonPropertyName("amplitudeDeg")] public float AmplitudeDeg { get; set; }
        [JsonPropertyName("attachBone")] public string AttachBone { get; set; }
    }

    private static string San(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in (s ?? "").ToLowerInvariant())
            if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
        var r = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(r) ? "anim" : r;
    }

    public static GenResult Generate(string animDir, GenRequest req)
    {
        Directory.CreateDirectory(animDir);

        string modelName = San(req.Name);
        string dictName = modelName + "_anim";
        string compName = "COMPONENT_" + modelName.ToUpperInvariant();

        bool boneAnim = req.AnimMode is "swing" or "spin";
        byte skinBone = (byte)(boneAnim ? 1 : 0);

        var (bgra, tw, th) = TextureCodec.PngToBgra(req.Png);
        var verts = new List<AccessoryService.MeshVert>();
        var idx = new List<ushort>();
        BuildGeometry(req, bgra, tw, th, verts, idx);
        if (verts.Count == 0) throw new InvalidOperationException("пустая геометрия детали");

        var ydrBytes = BuildDetailYdr(modelName, bgra, tw, th, verts, idx, skinBone,
            out Vector3 bbMin, out Vector3 bbMax, out Vector3 bsCentre, out float bsRadius);
        string ydrPath = Path.Combine(animDir, modelName + ".ydr");
        File.WriteAllBytes(ydrPath, ydrBytes);

        int frames = Math.Clamp((int)Math.Round(req.PeriodSec * 30f) + 1, 8, 600);
        float rate = 1.0f;
        string ycdXml = req.AnimMode switch
        {
            "swing" => BuildBoneRotYcdXml(modelName, MovableBoneTag, req.PeriodSec, frames,
                            new Vector3(req.AxisX, req.AxisY, req.AxisZ), req.AmplitudeDeg, spin: false, rate),
            "spin" => BuildBoneRotYcdXml(modelName, MovableBoneTag, req.PeriodSec, frames,
                            new Vector3(req.AxisX, req.AxisY, req.AxisZ), 0f, spin: true, rate),
            _ => BuildUvScrollYcdXml(modelName, req.PeriodSec, frames, req.ScrollU, req.ScrollV, rate),
        };
        byte[] ycdBytes = XmlYcd.GetYcd(ycdXml).Save();
        string ycdPath = Path.Combine(animDir, dictName + ".ycd");
        File.WriteAllBytes(ycdPath, ycdBytes);

        byte[] ytypBytes = BuildYtyp(modelName, dictName, bbMin, bbMax, bsCentre, bsRadius);
        string ytypPath = Path.Combine(animDir, dictName + ".ytyp");
        File.WriteAllBytes(ytypPath, ytypBytes);

        string metaName = modelName + "_comp.meta";
        File.WriteAllText(Path.Combine(animDir, metaName),
            BuildComponentMeta(compName, modelName, req.AttachBone), new UTF8Encoding(false));

        var files = new List<string> { Path.GetFileName(ydrPath), Path.GetFileName(ycdPath),
            Path.GetFileName(ytypPath), metaName };
        if (!string.IsNullOrWhiteSpace(req.WeaponModel))
        {
            string patchName = "weapons_" + San(req.WeaponModel) + ".attachpoint.xml";
            File.WriteAllText(Path.Combine(animDir, patchName),
                BuildWeaponsAttachSnippet(req.AttachBone, compName), new UTF8Encoding(false));
            files.Add(patchName);
        }

        File.WriteAllText(Path.Combine(animDir, "_content_snippet.xml"),
            BuildContentXmlSnippet(modelName, dictName), new UTF8Encoding(false));
        files.Add("_content_snippet.xml");

        var manifest = BuildManifest(modelName, dictName, compName, req);
        File.WriteAllText(Path.Combine(animDir, "_anim_manifest.json"), manifest, new UTF8Encoding(false));
        files.Add("_anim_manifest.json");

        string glbPath = Path.Combine(animDir, modelName + ".glb");
        try
        {
            bool ok = MiamiGraphics.Core.Services.YdrToGltfConverter
                .ConvertAsync(ydrPath, glbPath, new List<string>()).GetAwaiter().GetResult();
            if (!ok) glbPath = null;
        }
        catch { glbPath = null; }

        return new GenResult
        {
            Ok = true,
            Files = files,
            DetailModel = modelName,
            ClipDict = dictName,
            ComponentName = compName,
            PreviewGlb = glbPath != null ? modelName + ".glb" : null,
            Anim = new AnimMeta
            {
                Mode = req.AnimMode,
                PeriodSec = req.PeriodSec,
                ScrollU = req.ScrollU,
                ScrollV = req.ScrollV,
                Axis = new[] { req.AxisX, req.AxisY, req.AxisZ },
                AmplitudeDeg = req.AmplitudeDeg,
                AttachBone = req.AttachBone,
            },
        };
    }

    private static void BuildGeometry(GenRequest req, byte[] bgra, int tw, int th,
        List<AccessoryService.MeshVert> verts, List<ushort> idx)
    {
        if (string.Equals(req.SourceKind, "mesh", StringComparison.OrdinalIgnoreCase) &&
            req.Pos != null && req.Pos.Length >= 9)
        {
            int vc = req.Pos.Length / 3;
            if (vc > 65000) throw new InvalidOperationException($"модель слишком детальная ({vc} вершин)");
            for (int i = 0; i < vc; i++)
            {
                var n = req.Nrm != null && i * 3 + 2 < req.Nrm.Length
                    ? new Vector3(req.Nrm[i * 3], req.Nrm[i * 3 + 1], req.Nrm[i * 3 + 2]) : new Vector3(0, 0, 1);
                if (n.LengthSquared() < 1e-8f) n = new Vector3(0, 0, 1); else n.Normalize();
                verts.Add(new AccessoryService.MeshVert(
                    new Vector3(req.Pos[i * 3], req.Pos[i * 3 + 1], req.Pos[i * 3 + 2]), n,
                    req.Uv != null && i * 2 + 1 < req.Uv.Length ? req.Uv[i * 2] : 0f,
                    req.Uv != null && i * 2 + 1 < req.Uv.Length ? req.Uv[i * 2 + 1] : 0f));
            }
            foreach (var t in req.Idx) idx.Add((ushort)t);
            return;
        }

        var mesh = ExtrudeService.Build(bgra, tw, th, req.Size,
            req.Size * Math.Clamp(req.DepthFrac, 0.03f, 0.6f));
        if (mesh == null) throw new InvalidOperationException("не удалось построить контур из PNG");
        for (int i = 0; i < mesh.Positions.Count; i++)
            verts.Add(new AccessoryService.MeshVert(mesh.Positions[i], mesh.Normals[i],
                mesh.UVs[i].X, mesh.UVs[i].Y));
        idx.AddRange(mesh.Indices);
    }

    private static byte[] BuildDetailYdr(string modelName, byte[] bgra, int tw, int th,
        List<AccessoryService.MeshVert> verts, List<ushort> idx, byte skinBone,
        out Vector3 bbMin, out Vector3 bbMax, out Vector3 bsCentre, out float bsRadius)
    {
        var skel = BuildSkeleton2();
        var tex = BuildTexture(modelName + "_diff", bgra, tw, th);
        var shader = BuildEmissiveAnimShader(tex);
        var geom = BuildGeometryBlock(verts, idx, shader, skinBone, out var aabb);

        var model = new DrawableModel
        {
            SkeletonBinding = 0x00000102,
            RenderMaskFlags = 0x01FF,
            Geometries = new[] { geom },
            GeometriesCount1 = 1, GeometriesCount2 = 1, GeometriesCount3 = 1,
            ShaderMapping = new ushort[] { 0 },
            BoundsData = new[] { aabb },
        };

        var td = new TextureDictionary();
        td.BuildFromTextureList(new List<Texture> { tex });

        var d = new Drawable
        {
            Name = modelName,
            LightAttributes = new ResourceSimpleList64<LightAttributes>(),
            ShaderGroup = new ShaderGroup
            {
                TextureDictionary = td,
                Shaders = new ResourcePointerArray64<ShaderFX> { data_items = new[] { shader } },
                ShadersCount1 = 1, ShadersCount2 = 1,
            },
            Skeleton = skel,
            Joints = new Joints { VFT = JointsVFT },
            DrawableModels = new DrawableModelsBlock { High = new[] { model } },
            LodDistHigh = 9998, LodDistMed = 9998, LodDistLow = 9998, LodDistVlow = 9998,
            BoundingBoxMin = new Vector3(aabb.Min.X, aabb.Min.Y, aabb.Min.Z),
            BoundingBoxMax = new Vector3(aabb.Max.X, aabb.Max.Y, aabb.Max.Z),
        };
        d.BoundingCenter = (d.BoundingBoxMin + d.BoundingBoxMax) * 0.5f;
        d.BoundingSphereRadius = (d.BoundingBoxMax - d.BoundingCenter).Length();
        d.FlagsHigh = 1;
        d.BuildRenderMasks();
        d.BuildAllModels();
        d.BuildVertexDecls();

        bbMin = d.BoundingBoxMin; bbMax = d.BoundingBoxMax;
        bsCentre = d.BoundingCenter; bsRadius = d.BoundingSphereRadius;

        return new YdrFile { Drawable = d }.Save();
    }

    private static Skeleton BuildSkeleton2()
    {
        var b0 = new Bone
        {
            Name = "base", Tag = 0, Index = 0, Index2 = 0, ParentIndex = -1, NextSiblingIndex = -1,
            Flags = (EBoneFlags)0x1077,
            Translation = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One,
            TransformUnk = new Vector4(0, 4, -3, 0),
        };
        var b1 = new Bone
        {
            Name = "1", Tag = MovableBoneTag, Index = 1, Index2 = 1, ParentIndex = 0, NextSiblingIndex = -1,
            Flags = (EBoneFlags)0x0077,
            Translation = Vector3.Zero, Rotation = Quaternion.Identity, Scale = Vector3.One,
            TransformUnk = new Vector4(0, 4, -3, 0),
        };
        var skel = new Skeleton
        {
            Bones = new SkeletonBonesBlock { Items = new[] { b0, b1 } },
            Unknown_1Ch = 0x01000000,
            Unknown_50h = 0x8A0FCC2E,
            Unknown_54h = 0xE7A833BA,
            Unknown_58h = 0x11D3608C,
        };
        skel.BuildIndices();
        skel.BuildBoneTags();
        skel.AssignBoneParents();
        skel.BuildTransformations();
        skel.BuildBonesMap();
        return skel;
    }

    private static ShaderFX BuildEmissiveAnimShader(Texture tex)
    {
        var shader = new ShaderFX
        {
            Name = JenkHash.GenHash("emissive"),
            FileName = JenkHash.GenHash("emissive.sps"),
            RenderBucket = 0,
            Unknown_Ch = 0, Unknown_12h = 32768, Unknown_1Ch = 0,
            Unknown_24h = 0, Unknown_26h = 0, Unknown_28h = 0,
            ParametersList = new ShaderParametersBlock(),
        };
        var names = new[]
        {
            ShaderParamNames.DiffuseSampler,
            ShaderParamNames.matMaterialColorScale,
            ShaderParamNames.HardAlphaBlend,
            ShaderParamNames.useTessellation,
            ShaderParamNames.emissiveMultiplier,
            ShaderParamNames.globalAnimUV1,
            ShaderParamNames.globalAnimUV0,
        };
        var pars = new[]
        {
            new ShaderParameter { DataType = 0, Unknown_1h = 2,   Data = tex },
            new ShaderParameter { DataType = 1, Unknown_1h = 165, Data = new Vector4(1, 0, 0, 1) },
            new ShaderParameter { DataType = 1, Unknown_1h = 164, Data = new Vector4(1, 0, 0, 0) },
            new ShaderParameter { DataType = 1, Unknown_1h = 163, Data = Vector4.Zero },
            new ShaderParameter { DataType = 1, Unknown_1h = 162, Data = new Vector4(1, 0, 0, 0) },
            new ShaderParameter { DataType = 1, Unknown_1h = 161, Data = new Vector4(0, 1, 0, 0) },
            new ShaderParameter { DataType = 1, Unknown_1h = 160, Data = new Vector4(1, 0, 0, 0) },
        };
        var block = shader.ParametersList;
        block.Hashes = names.Select(x => (MetaName)x).ToArray();
        block.Parameters = pars;
        block.Count = pars.Length;
        shader.ParameterSize = block.ParametersSize;
        shader.ParameterDataSize = block.ParametersDataSize;
        shader.ParameterCount = (byte)pars.Length;
        shader.TextureParametersCount = block.TextureParamsCount;
        shader.RenderBucketMask = (1u << shader.RenderBucket) | 0xFF00;
        return shader;
    }

    private static Texture BuildTexture(string name, byte[] bgra, int w, int h) => new()
    {
        Name = name,
        NameHash = JenkHash.GenHash(name.ToLowerInvariant()),
        Width = (ushort)w, Height = (ushort)h, Depth = 1, Levels = 1,
        Format = TextureFormat.D3DFMT_A8R8G8B8,
        Stride = (ushort)(w * 4),
        Data = new TextureData { FullData = bgra },
        VFT = 0, Unknown_4h = 1, Unknown_30h = 1, Unknown_32h = 0,
        Usage = TextureUsage.UNKNOWN, UsageData = 0,
    };

    private static DrawableGeometry BuildGeometryBlock(
        List<AccessoryService.MeshVert> verts, List<ushort> indices,
        ShaderFX shader, byte skinBone, out AABB_s aabb)
    {
        var decl = new VertexDeclaration
        {
            Types = VertexDeclarationTypes.GTAV1, Unknown_6h = 0,
            Flags = 0x5F, Stride = 44, Count = 6,
        };
        var vBytes = new byte[verts.Count * 44];
        using (var ms = new MemoryStream(vBytes))
        using (var bw = new BinaryWriter(ms))
            foreach (var v in verts)
            {
                bw.Write(v.P.X); bw.Write(v.P.Y); bw.Write(v.P.Z);
                bw.Write((byte)255); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
                bw.Write(skinBone); bw.Write((byte)0); bw.Write((byte)0); bw.Write((byte)0);
                bw.Write(v.N.X); bw.Write(v.N.Y); bw.Write(v.N.Z);
                bw.Write((byte)255); bw.Write((byte)255); bw.Write((byte)255); bw.Write((byte)255);
                bw.Write(v.U); bw.Write(v.V);
            }

        aabb = new AABB_s { Min = new Vector4(float.MaxValue), Max = new Vector4(float.MinValue) };
        foreach (var v in verts)
        {
            aabb.Min = Vector4.Min(aabb.Min, new Vector4(v.P, 0));
            aabb.Max = Vector4.Max(aabb.Max, new Vector4(v.P, 0));
        }

        var vData = new VertexData
        {
            Info = decl, VertexType = (VertexType)decl.Flags,
            VertexStride = 44, VertexCount = verts.Count, VertexBytes = vBytes,
        };
        var vBuff = new VertexBuffer
        {
            Data1 = vData, Data2 = vData, Info = decl,
            VertexCount = (uint)verts.Count, VertexStride = 44,
            VFT = VBufVFT, Unknown_4h = 1,
        };
        var iBuff = new IndexBuffer
        {
            IndicesCount = (uint)indices.Count, Indices = indices.ToArray(),
            VFT = IBufVFT, Unknown_4h = 1,
        };
        return new DrawableGeometry
        {
            Shader = shader,
            VertexData = vData, VertexBuffer = vBuff, IndexBuffer = iBuff,
            VFT = GeomVFT, Unknown_4h = 1,
            IndicesCount = (uint)indices.Count,
            TrianglesCount = (uint)indices.Count / 3,
            VerticesCount = (ushort)verts.Count,
            Unknown_62h = 3, VertexStride = 44,
            BoneIds = new ushort[] { 0, 1 },
            BoneIdsCount = 2,
        };
    }

    private static string F(float v) => FloatUtil.ToString(v);

    private static string WrapClipDictXml(string animName, float durationSec, int frames, float rate,
        string boneIdsXml, string seqDataXml)
    {
        var animHash = JenkHash.GenHash(animName.ToLowerInvariant());
        var clipHash = animHash + 1;
        var seqFrameLimit = frames + 30;
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<ClipDictionary>");
        sb.AppendLine(" <Clips>");
        sb.AppendLine("  <Item>");
        sb.AppendLine($"   <Hash>hash_{clipHash:X8}</Hash>");
        sb.AppendLine($"   <Name>pack:/{animName}.clip</Name>");
        sb.AppendLine("   <Type value=\"Animation\" />");
        sb.AppendLine("   <Unknown30 value=\"0\" />");
        sb.AppendLine("   <Tags />");
        sb.AppendLine("   <Properties />");
        sb.AppendLine($"   <AnimationHash>hash_{(uint)animHash:X8}</AnimationHash>");
        sb.AppendLine("   <StartTime value=\"0\" />");
        sb.AppendLine($"   <EndTime value=\"{F(durationSec)}\" />");
        sb.AppendLine($"   <Rate value=\"{F(rate)}\" />");
        sb.AppendLine("  </Item>");
        sb.AppendLine(" </Clips>");
        sb.AppendLine(" <Animations>");
        sb.AppendLine("  <Item>");
        sb.AppendLine($"   <Hash>hash_{(uint)animHash:X8}</Hash>");
        sb.AppendLine("   <Unknown10 value=\"0\" />");
        sb.AppendLine($"   <FrameCount value=\"{frames}\" />");
        sb.AppendLine($"   <SequenceFrameLimit value=\"{seqFrameLimit}\" />");
        sb.AppendLine($"   <Duration value=\"{F(durationSec)}\" />");
        sb.AppendLine($"   <Unknown1C>hash_{clipHash:X8}</Unknown1C>");
        sb.AppendLine("   <BoneIds>");
        sb.Append(boneIdsXml);
        sb.AppendLine("   </BoneIds>");
        sb.AppendLine("   <Sequences>");
        sb.AppendLine("    <Item>");
        sb.AppendLine("     <Hash />");
        sb.AppendLine($"     <FrameCount value=\"{frames}\" />");
        sb.AppendLine("     <SequenceData>");
        sb.Append(seqDataXml);
        sb.AppendLine("     </SequenceData>");
        sb.AppendLine("    </Item>");
        sb.AppendLine("   </Sequences>");
        sb.AppendLine("  </Item>");
        sb.AppendLine(" </Animations>");
        sb.AppendLine("</ClipDictionary>");
        return sb.ToString();
    }

    private static string StaticFloatXml(float v, int ind)
    {
        var p = new string(' ', ind);
        return $"{p}<Item>\n{p} <Type value=\"StaticFloat\" />\n{p} <Value value=\"{F(v)}\" />\n{p}</Item>\n";
    }

    private static string QuantizeFloatXml(float[] values, int ind)
    {
        var p = new string(' ', ind);
        float min = values.Min(), max = values.Max();
        float quantum = Math.Max((max - min) / 65535.0f, 1e-9f);
        var sb = new StringBuilder();
        sb.Append($"{p}<Item>\n{p} <Type value=\"QuantizeFloat\" />\n");
        sb.Append($"{p} <Quantum value=\"{F(quantum)}\" />\n{p} <Offset value=\"{F(min)}\" />\n");
        sb.Append($"{p} <Values>\n{p}  ");
        for (int i = 0; i < values.Length; i++)
        {
            sb.Append(F(values[i]));
            sb.Append(((i + 1) % 10 == 0) && (i + 1 < values.Length) ? $"\n{p}  " : " ");
        }
        sb.Append($"\n{p} </Values>\n{p}</Item>\n");
        return sb.ToString();
    }

    private static string FloatChannelXml(float[] values, int ind)
        => values.All(v => v == values[0]) ? StaticFloatXml(values[0], ind) : QuantizeFloatXml(values, ind);

    private static string BuildUvScrollYcdXml(string animName, float durationSec, int frames,
        float scrollU, float scrollV, float rate)
    {
        var boneIds = new StringBuilder();
        boneIds.Append("    <Item>\n     <BoneId value=\"0\" />\n     <Track value=\"17\" />\n     <Unk0 value=\"0\" />\n    </Item>\n");
        boneIds.Append("    <Item>\n     <BoneId value=\"0\" />\n     <Track value=\"18\" />\n     <Unk0 value=\"0\" />\n    </Item>\n");

        float[] Ramp(float total)
        {
            var vals = new float[frames];
            for (int i = 0; i < frames; i++) vals[i] = total * i / (frames - 1);
            return vals;
        }

        var seq = new StringBuilder();
        seq.Append("      <Item>\n       <Channels>\n");
        if (scrollU == 0f)
            seq.Append("        <Item>\n         <Type value=\"StaticVector3\" />\n         <Value x=\"1\" y=\"0\" z=\"0\" />\n        </Item>\n");
        else { seq.Append(StaticFloatXml(1, 8)); seq.Append(StaticFloatXml(0, 8)); seq.Append(QuantizeFloatXml(Ramp(scrollU), 8)); }
        seq.Append("       </Channels>\n      </Item>\n");
        seq.Append("      <Item>\n       <Channels>\n");
        if (scrollV == 0f)
            seq.Append("        <Item>\n         <Type value=\"StaticVector3\" />\n         <Value x=\"0\" y=\"1\" z=\"0\" />\n        </Item>\n");
        else { seq.Append(StaticFloatXml(0, 8)); seq.Append(StaticFloatXml(1, 8)); seq.Append(QuantizeFloatXml(Ramp(scrollV), 8)); }
        seq.Append("       </Channels>\n      </Item>\n");

        return WrapClipDictXml(animName, durationSec, frames, rate, boneIds.ToString(), seq.ToString());
    }

    private static string BuildBoneRotYcdXml(string animName, ushort boneTag, float durationSec, int frames,
        Vector3 axis, float amplitudeDeg, bool spin, float rate)
    {
        if (axis.LengthSquared() < 1e-8f) axis = new Vector3(1, 0, 0);
        axis.Normalize();
        var xs = new float[frames]; var ys = new float[frames]; var zs = new float[frames]; var ws = new float[frames];
        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)(frames - 1);
            float angDeg = spin ? (360f * t) : (amplitudeDeg * (float)Math.Sin(2.0 * Math.PI * t));
            double half = angDeg * (Math.PI / 180.0) * 0.5;
            float s = (float)Math.Sin(half);
            xs[i] = axis.X * s; ys[i] = axis.Y * s; zs[i] = axis.Z * s; ws[i] = (float)Math.Cos(half);
        }

        var boneIds = $"    <Item>\n     <BoneId value=\"{boneTag}\" />\n     <Track value=\"1\" />\n     <Unk0 value=\"0\" />\n    </Item>\n";
        var seq = new StringBuilder();
        seq.Append("      <Item>\n       <Channels>\n");
        seq.Append(FloatChannelXml(xs, 8));
        seq.Append(FloatChannelXml(ys, 8));
        seq.Append(FloatChannelXml(zs, 8));
        if (spin) seq.Append(FloatChannelXml(ws, 8));
        else seq.Append("        <Item>\n         <Type value=\"CachedQuaternion1\" />\n         <QuatIndex value=\"3\" />\n        </Item>\n");
        seq.Append("       </Channels>\n      </Item>\n");

        return WrapClipDictXml(animName, durationSec, frames, rate, boneIds, seq.ToString());
    }

    private static byte[] BuildYtyp(string modelName, string dictName,
        Vector3 bbMin, Vector3 bbMax, Vector3 bsCentre, float bsRadius)
    {
        JenkIndex.Ensure(modelName);
        JenkIndex.Ensure(dictName);
        var modelHash = new MetaHash(JenkHash.GenHash(modelName));
        var dictHash = new MetaHash(JenkHash.GenHash(dictName));

        var ytyp = new YtypFile { NameHash = dictHash };
        var def = new CBaseArchetypeDef
        {
            name = modelHash,
            assetName = modelHash,
            textureDictionary = modelHash,
            clipDictionary = dictHash,
            drawableDictionary = new MetaHash(0),
            physicsDictionary = new MetaHash(0),
            assetType = rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE,
            bbMin = bbMin, bbMax = bbMax, bsCentre = bsCentre, bsRadius = bsRadius,
            lodDist = 60f, hdTextureDist = 40f,
            flags = ArchetypeFlags,
            specialAttribute = 0,
            extensions = new Array_StructurePointer(),
        };
        var arch = new Archetype();
        arch.Init(ytyp, ref def);
        ytyp.AddArchetype(arch);
        return ytyp.Save();
    }

    private static string BuildComponentMeta(string compName, string modelName, string attachBone) =>
$@"<?xml version=""1.0"" encoding=""UTF-8""?>
<CWeaponComponentInfoBlob>
  <Data>
  </Data>
  <Infos>
    <Item type=""CWeaponComponentInfo"">
      <Name>{compName}</Name>
      <Model>{modelName}</Model>
      <LocName>WCT_RAIL</LocName>
      <LocDesc>WCD_AT_RAIL</LocDesc>
      <AttachBone>{attachBone}</AttachBone>
      <AccuracyModifier type=""NULL"" />
      <DamageModifier type=""NULL"" />
      <bShownOnWheel value=""false"" />
      <CreateObject value=""true"" />
      <HudDamage value=""0"" />
      <HudSpeed value=""0"" />
      <HudCapacity value=""0"" />
      <HudAccuracy value=""0"" />
      <HudRange value=""0"" />
    </Item>
  </Infos>
  <InfoBlobName>MiamiGraphics - {modelName}</InfoBlobName>
</CWeaponComponentInfoBlob>
";

    private static string BuildWeaponsAttachSnippet(string attachBone, string compName) =>
$@"<!-- Вставить внутрь <AttachPoints> нужного CWeaponInfo (WEAPONINFO_FILE_PATCH). -->
<Item>
  <AttachBone>{attachBone}</AttachBone>
  <Components>
    <Item>
      <Name>{compName}</Name>
      <Default value=""true"" />
    </Item>
  </Components>
</Item>
";

    private static string BuildContentXmlSnippet(string modelName, string dictName) =>
$@"<!-- content.xml: dataFiles + filesToEnable (changeset CCS_PATCHDAY18_NG_STREAMING) -->
<!-- Модель/txt: {modelName}.ydr — в замонтированный rpf (RPF_FILE). -->
<!-- Клип: {dictName}.ycd — в замонтированный rpf (RPF_FILE). -->
<Item>
  <filename>dlc_PATCHDAY18ng:/%PLATFORM%/levels/gta5/{dictName}.ityp</filename>
  <fileType>DLC_ITYP_REQUEST</fileType>
  <overlay value=""false"" />
  <disabled value=""true"" />
  <persistent value=""false"" />
  <contents>CONTENTS_PROPS</contents>
</Item>
";

    private static string BuildManifest(string modelName, string dictName, string compName, GenRequest req)
    {
        var animHash = JenkHash.GenHash(modelName.ToLowerInvariant());
        return
$@"{{
  ""kind"": ""animated_detail"",
  ""model"": ""{modelName}"",
  ""modelHash"": ""0x{animHash:X8}"",
  ""clipDict"": ""{dictName}"",
  ""clipDictHash"": ""0x{JenkHash.GenHash(dictName.ToLowerInvariant()):X8}"",
  ""component"": ""{compName}"",
  ""attachBone"": ""{req.AttachBone}"",
  ""animMode"": ""{req.AnimMode}"",
  ""periodSec"": {F(req.PeriodSec)},
  ""files"": {{
    ""ydr"": ""{modelName}.ydr"",
    ""ycd"": ""{dictName}.ycd"",
    ""ytyp"": ""{dictName}.ytyp"",
    ""componentMeta"": ""{modelName}_comp.meta""
  }},
  ""register"": {{
    ""ydr"": ""RPF_FILE в замонтированный rpf"",
    ""ycd"": ""RPF_FILE в замонтированный rpf"",
    ""ytyp"": ""DLC_ITYP_REQUEST (CONTENTS_PROPS) в content.xml"",
    ""componentMeta"": ""WEAPONCOMPONENTSINFO_FILE в content.xml"",
    ""weaponsPatch"": ""WEAPONINFO_FILE_PATCH — добавить AttachPoint компонента""
  }}
}}
";
    }
}
