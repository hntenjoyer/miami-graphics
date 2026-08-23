#nullable disable
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using ImageMagick;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace MiamiGraphics.Core.Services
{

    public static class YdrToGltfConverter
    {
        private const bool LogVerbose = true;
        private static void Log(string s) { if (LogVerbose) Console.WriteLine("[YDR→GLB] " + s); }

        private static readonly Dictionary<uint, string> RageHashDict = new()
        {

            { 0xE4DF46D5, "default" }, { 0xC9AAC531, "default_um" },
            { 0x15406984, "default_tnt" }, { 0x2F4B79D0, "default_spec" },
            { 0x43DDE351, "default_spec_um" }, { 0x8AA777A5, "default_alpha" },
            { 0xD4156C86, "default_detail" }, { 0x5934E569, "default_detail_um" },
            { 0x31797C83, "default_spec_detail" }, { 0xEAABBC5A, "default_terrain_wet" },
            { 0x2DB8D1AA, "alpha" },
            { 0x4F485502, "normal" }, { 0xAC8C0806, "normal_um" },
            { 0x1B147031, "normal_tnt" }, { 0x3757A862, "normal_alpha" },
            { 0xD47D9A30, "normal_cutout" }, { 0x1510EBE7, "normal_decal" },
            { 0x002195AB, "normal_decal_pxm" }, { 0x938E49F1, "normal_decal_tnt" },
            { 0x6FBF8ACF, "normal_um_tnt" }, { 0xF829494A, "normal_reflect" },
            { 0x643D525E, "normal_reflect_alpha" }, { 0x848F3C54, "normal_reflect_decal" },
            { 0x88F0A5A3, "normal_screendooralpha" },
            { 0x38DD00DF, "normal_spec" }, { 0x85BCAFFD, "normal_spec_alpha" },
            { 0x78838E05, "normal_spec_alpha.sps" },
            { 0x52318515, "normal_spec_cutout" }, { 0x6CD4735B, "normal_spec_tnt" },
            { 0xAA650DAF, "normal_spec_emissive" }, { 0x126D79A2, "normal_spec_um" },
            { 0x1C1C2570, "normal_spec_decal" }, { 0x6473CD91, "normal_spec_decal_pxm" },
            { 0x71BACC0D, "normal_spec_decal_tnt" }, { 0xD7C59E09, "normal_spec_detail" },
            { 0xA39BE6F4, "normal_spec_detail_um" }, { 0x2143CD55, "normal_spec_reflect" },
            { 0x4DE16737, "normal_spec_reflect_alpha" },
            { 0xF5D7A727, "normal_spec_reflect_decal" },
            { 0x464A2606, "normal_spec_reflect_emissivenight" },
            { 0xAB7B0A10, "normal_spec_reflect_emissivenight_alpha" },
            { 0x7204A391, "spec" }, { 0x0CB0F2C2, "spec_alpha" },
            { 0xA4F28A79, "spec_decal" }, { 0x508CF631, "spec_decal_pxm" },
            { 0xED86862A, "spec_reflect" }, { 0x382B6CBC, "spec_reflect_alpha" },
            { 0x8F245469, "emissive" }, { 0xB44FD60E, "emissive_additive_alpha" },
            { 0x2EEFD8E9, "emissive_alpha" }, { 0xE8C89CEF, "emissive_alpha_tnt" },
            { 0xE3A69EF4, "emissive_speclum" }, { 0x15C92C35, "emissive_tnt" },
            { 0xB08C6435, "emissivenight" }, { 0x11D5D944, "emissivenight_alpha" },
            { 0x0C3A3E76, "emissivestrong" }, { 0xDD5F0051, "emissivestrong_alpha" },
            { 0xD9F8C9BF, "glass" }, { 0xEB48B995, "glass_normal_spec" },
            { 0xA587608A, "glass_normal_spec_reflect" }, { 0x706A3722, "glass_pv" },
            { 0xC5098EE2, "glass_pv_env" }, { 0xA9A6EB84, "glass_spec" },
            { 0x1A910B87, "glass_emissive" }, { 0x7DBE7C22, "glass_emissive_alpha" },
            { 0x451700E5, "glass_emissivenight" }, { 0x776B6EA5, "glass_emissivenight_alpha" },
            { 0xB8C7819B, "glass_breakable" }, { 0x9F4C6AC8, "glass_breakable_screendooralpha" },
            { 0x7F27189A, "cutout" }, { 0x36D01886, "cutout_um" },
            { 0xF08729D4, "decal" }, { 0x1AB91784, "decal_diff_only_um" },
            { 0xD94D6305, "decal_dirt" }, { 0x1F98BD87, "decal_emissive_only" },
            { 0xD5738C1B, "decal_emissivenight_only" }, { 0xC8D38FCC, "decal_normal_only" },
            { 0x56A60F25, "decal_shadow_only" }, { 0xE8F01193, "decal_spec_only" },
            { 0x14E49F72, "decal_tnt" },
            { 0xDF963388, "weapon_normal_spec" }, { 0x3DC5551F, "weapon_normal_spec_alpha" },
            { 0x0BDA4AF1, "weapon_normal_spec_cutout" }, { 0x8676A645, "weapon_normal_spec_tnt" },
            { 0x9905A1ED, "weapon_normal_spec_palette" }, { 0x6D500164, "weapon_normal_spec_detail" },
            { 0x5FF02C23, "weapon_normal_spec_detail_palette" },
            { 0x9FD82F71, "weapon_normal_spec_palette_detail_2" },

            { 0x11C2EFB0, "default.sps" },
            { 0x11C2F970, "alpha.sps" },
            { 0x085D7C2B, "emissivestrong.sps" },

            { 0x4D52C5FF, "DiffuseSampler" },
            { 0x9CB2462B, "BumpSampler" },
            { 0xE7CCBA6E, "SpecSampler" },
            { 0xE9B4D6F8, "EmissiveMultiplier" },
            { 0x10C76BB8, "HardAlphaBlend" },
            { 0x66B8F32E, "matMaterialColorScale" },
        };

        private static string ResolveHash(uint hash)
        {
            if (RageHashDict.TryGetValue(hash, out var name)) return name;
            return "<unknown:" + hash + ">";
        }

        private static string ResolveHash(string nameAsString)
        {
            if (string.IsNullOrEmpty(nameAsString)) return "";
            if (uint.TryParse(nameAsString, out uint h)) return ResolveHash(h);
            return nameAsString.ToLowerInvariant();
        }

        private static uint JenkinsHash(string s)
        {
            uint h = 0;
            foreach (char ch in s.ToLowerInvariant())
            {
                h += (byte)ch;
                h += (h << 10);
                h ^= (h >> 6);
            }
            h += (h << 3);
            h ^= (h >> 11);
            h += (h << 15);
            return h;
        }

        private static readonly HashSet<uint> EmissiveShaders = BuildHashSet(
            "emissive", "emissive_additive_alpha", "emissive_alpha", "emissive_alpha_tnt",
            "emissive_speclum", "emissive_tnt",
            "emissivenight", "emissivenight_alpha",
            "emissivestrong", "emissivestrong_alpha",
            "glass_emissive", "glass_emissive_alpha",
            "glass_emissivenight", "glass_emissivenight_alpha",
            "decal_emissive_only", "decal_emissivenight_only",
            "normal_spec_emissive",
            "normal_spec_reflect_emissivenight", "normal_spec_reflect_emissivenight_alpha",
            "vehicle_emissive_alpha", "vehicle_emissive_opaque", "vehicle_lightsemissive",

            "emissive.sps", "emissive_alpha.sps", "emissivestrong.sps", "emissivestrong_alpha.sps",
            "emissivenight.sps", "emissivenight_alpha.sps",
            "glass_emissive.sps", "glass_emissive_alpha.sps",
            "normal_spec_emissive.sps", "vehicle_emissive_alpha.sps", "vehicle_emissive_opaque.sps",
            "vehicle_lightsemissive.sps"
        );

        private static readonly HashSet<uint> AlphaBlendShaders = BuildHashSet(
            "alpha", "default_alpha", "normal_alpha", "normal_spec_alpha",
            "spec_alpha", "spec_reflect_alpha",
            "emissive_alpha", "emissive_additive_alpha", "emissive_alpha_tnt",
            "emissivenight_alpha", "emissivestrong", "emissivestrong_alpha",
            "glass", "glass_normal_spec", "glass_normal_spec_reflect",
            "glass_pv", "glass_pv_env", "glass_spec",
            "glass_emissive_alpha", "glass_emissivenight_alpha",
            "vehicle_emissive_alpha", "vehicle_vehglass", "vehicle_vehglass_inner",

            "alpha.sps", "default_alpha.sps", "normal_alpha.sps", "normal_spec_alpha.sps",
            "spec_alpha.sps",
            "emissivestrong.sps", "emissivestrong_alpha.sps", "emissive_alpha.sps",
            "glass.sps", "glass_normal_spec.sps", "glass_pv.sps",
            "glass_emissive_alpha.sps",
            "vehicle_vehglass.sps", "vehicle_vehglass_inner.sps", "vehicle_emissive_alpha.sps"
        );

        private static readonly HashSet<uint> AlphaCutoutShaders = BuildHashSet(
            "cutout", "cutout_um", "normal_cutout", "normal_spec_cutout",
            "weapon_normal_spec_cutout",
            "cutout.sps", "cutout_um.sps", "normal_cutout.sps", "normal_spec_cutout.sps",
            "weapon_normal_spec_cutout.sps"
        );

        private static readonly HashSet<uint> DiffuseSamplers = BuildHashSet(
            "DiffuseSampler", "DiffuseSampler2", "DiffuseSamplerPhase2",
            "DiffuseSamplerPoint", "DiffuseTexSampler",
            "DiffuseTexSampler01", "DiffuseTexSampler02", "DiffuseTexSampler03", "DiffuseTexSampler04",
            "gDiffuse",
            "TextureGrassSampler", "TextureNoWrapSampler", "textureSamp",
            "TextureSampler", "TextureSampler_layer0", "TextureSampler2",
            "BaseSampler", "baseTextureSampler"
        );

        private static readonly HashSet<uint> SpecSamplers = BuildHashSet(
            "SpecSampler", "SpecSampler2", "SpecularSampler", "SpecularTexSampler",
            "SpecMapSampler", "specSamp"
        );

        private static readonly HashSet<uint> NormalSamplers = BuildHashSet(
            "NormalMapSampler", "NormalMapSampler1", "NormalMapSampler2",
            "NormalMapTexSampler", "NormalSampler", "NormalTextureSampler",
            "BumpSampler", "BumpSampler_layer0", "BumpSampler2"
        );

        private static readonly uint EMISSIVE_MULTIPLIER_HASH = JenkinsHash("emissiveMultiplier");

        private static readonly uint MATERIAL_COLOR_SCALE_HASH = JenkinsHash("matMaterialColorScale");

        private static HashSet<uint> BuildHashSet(params string[] names)
        {
            var s = new HashSet<uint>();
            foreach (var n in names) s.Add(JenkinsHash(n));
            return s;
        }

        private static readonly string[] AuxTextureSuffixes =
        {
            "_n", "_norm", "_normal", "_nrm", "_nm",
            "_s", "_spec", "_specular",
            "_b", "_bump", "_bumpmap",
            "_g", "_glow", "_em", "_emis", "_emissive",
            "_alpha", "_a8",
        };

        private static readonly string[] AuxTextureKeywords =
        {
            "normal", "_norm", "spec", "bump", "glow", "emis", "checker",
        };

        private static bool LooksLikeDiffuseTextureName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();

            if (lower.Contains("diff")) return true;
            if (lower.Contains("color") || lower.Contains("albedo") || lower.Contains("basecolor")) return true;
            return false;
        }

        private static bool LooksLikeAuxTextureName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            foreach (var suf in AuxTextureSuffixes)
                if (lower.EndsWith(suf, StringComparison.Ordinal)) return true;
            foreach (var kw in AuxTextureKeywords)
                if (lower.Contains(kw)) return true;
            return false;
        }

        private static readonly string[] NormalTextureKeywords =
        {
            "normal", "_norm", "_nrm", "_nm", "bump",
        };

        private static bool LooksLikeNormalTextureName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var lower = name.ToLowerInvariant();
            foreach (var kw in NormalTextureKeywords)
                if (lower.Contains(kw)) return true;

            if (lower.EndsWith("_n", StringComparison.Ordinal)) return true;
            return false;
        }

        private static KeyValuePair<string, byte[]> PickSalvageDiffuse(
            Dictionary<string, byte[]> pngByName, string expected)
        {
            if (pngByName == null || pngByName.Count == 0)
                return default;
            var lowerExpected = (expected ?? string.Empty).ToLowerInvariant();

            int bestPrefixLen = 0;
            KeyValuePair<string, byte[]> bestPrefix = default;
            foreach (var kv in pngByName)
            {
                if (kv.Value == null || kv.Value.Length == 0) continue;
                if (LooksLikeAuxTextureName(kv.Key)) continue;
                var lowerKey = kv.Key.ToLowerInvariant();
                int n = CommonPrefixLength(lowerKey, lowerExpected);
                if (n > bestPrefixLen)
                {
                    bestPrefixLen = n;
                    bestPrefix    = kv;
                }
            }

            if (bestPrefixLen >= 6 && bestPrefix.Value != null) return bestPrefix;

            foreach (var kv in pngByName)
            {
                if (kv.Value == null || kv.Value.Length == 0) continue;
                if (LooksLikeDiffuseTextureName(kv.Key)) return kv;
            }

            foreach (var kv in pngByName)
            {
                if (kv.Value == null || kv.Value.Length == 0) continue;
                if (!LooksLikeAuxTextureName(kv.Key)) return kv;
            }

            return default;
        }

        private static int CommonPrefixLength(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
                if (a[i] != b[i]) return i;
            return n;
        }

        public sealed class ConvertReport
        {
            public List<string> Warnings { get; } = new();
            public int EmbeddedTextures  { get; set; }
            public int ExternalAdded     { get; set; }
            public int ExternalShadowed  { get; set; }
            public int SalvagedDiffuse   { get; set; }
            public int MissingDiffuse    { get; set; }
        }

        public static Task<bool> ConvertAsync(string ydrPath, string outputPath)
            => Task.Run(() => ConvertCore(ydrPath, outputPath, externalYtdPaths: null, report: null));

        public static Task<bool> ConvertAsync(string ydrPath, string outputPath, IEnumerable<string> externalYtdPaths)
            => Task.Run(() => ConvertCore(ydrPath, outputPath, externalYtdPaths, report: null));

        public static Task<bool> ConvertAsync(string ydrPath, string outputPath, IEnumerable<string> externalYtdPaths, ConvertReport report)
            => Task.Run(() => ConvertCore(ydrPath, outputPath, externalYtdPaths, report));

        public static Task<bool> ConvertAsync(string ydrPath, string outputPath,
            IEnumerable<string> externalYtdPaths, IEnumerable<string> extraYdrPaths, string extraMeshTag = null,
            bool skipPaletteBake = false)
            => Task.Run(() => ConvertCore(ydrPath, outputPath, externalYtdPaths, report: null, extraYdrPaths, extraMeshTag, skipPaletteBake));

        public static Task<bool> ConvertDrawableAsync(Drawable drawable, string outputPath)
            => Task.Run(() => ConvertDrawableCore(drawable, outputPath));

        internal static bool ConvertDrawableCore(Drawable drawable, string outputPath)
            => ConvertDrawableCore(drawable, outputPath, null, null);

        internal static bool ConvertDrawableCore(Drawable drawable, string outputPath, Dictionary<string, byte[]> externalPngs)
            => ConvertDrawableCore(drawable, outputPath, externalPngs, null);

        internal static bool ConvertDrawableCore(Drawable drawable, string outputPath, Dictionary<string, byte[]> externalPngs, ConvertReport report)
            => ConvertDrawableCore(drawable, outputPath, externalPngs, report, extraDrawables: null, extraMeshTag: null);

        internal static bool ConvertDrawableCore(Drawable drawable, string outputPath,
            Dictionary<string, byte[]> externalPngs, ConvertReport report, List<Drawable> extraDrawables,
            string extraMeshTag = null, bool skipPaletteBake = false)
        {
            try
            {
                if (drawable == null) { Log("ERROR: Drawable == null"); return false; }

                var pngByName = ExtractEmbeddedTextures(drawable);
                Log("Извлечено embedded текстур: " + pngByName.Count);
                if (report != null) report.EmbeddedTextures = pngByName.Count;
                if (externalPngs != null && externalPngs.Count > 0)
                {
                    int added = 0, shadowed = 0;
                    foreach (var kv in externalPngs)
                    {
                        if (pngByName.ContainsKey(kv.Key)) { shadowed++; continue; }
                        pngByName[kv.Key] = kv.Value;
                        added++;
                    }
                    Log($"Внешние текстуры: добавлено {added}, пропущено (есть embedded) {shadowed}");
                    if (report != null) { report.ExternalAdded = added; report.ExternalShadowed = shadowed; }
                }

                if (pngByName.Count == 0)
                {
                    Log("WARN: НИ ОДНОЙ текстуры - ни embedded, ни из .ytd. Превью будет белым/серым.");
                    report?.Warnings.Add(
                        "в модели не нашлось ни одной текстуры (ни embedded, ни в .ytd) - " +
                        "превью будет ровно-серым и не совпадёт с игрой");
                }

                var materials = BuildMaterials(drawable, pngByName, report, skipPaletteBake);
                Log("Материалов создано: " + materials.Count);

                NodeBuilder armature = new NodeBuilder("Armature");
                NodeBuilder[] boneNodes = BuildSkeleton(drawable, armature);
                bool hasSkeleton = boneNodes != null && boneNodes.Length > 0;
                Log("Скелет: " + (hasSkeleton ? boneNodes.Length + " костей" : "нет"));

                var scene = new SceneBuilder();
                if (hasSkeleton) scene.AddNode(armature);

                var models = drawable.DrawableModels?.High;
                if (models == null || models.Length == 0) { Log("ERROR: нет High LOD"); return false; }

                int meshCounter = 0;
                foreach (var model in models)
                {
                    if (model?.Geometries == null) continue;
                    bool modelSkin = hasSkeleton && model.HasSkin != 0;
                    foreach (var geom in model.Geometries)
                    {
                        if (geom?.VertexData?.VertexBytes == null || geom.IndexBuffer?.Indices == null) continue;
                        var matBuilder = materials.TryGetValue(geom.ShaderID, out var m) ? m : DefaultMaterial();
                        BuildAndAddMesh(geom, matBuilder, modelSkin, boneNodes, armature, scene, meshCounter++);
                    }
                }

                if (extraDrawables != null)
                {
                    foreach (var extra in extraDrawables)
                    {
                        if (extra == null) continue;
                        var extraPngs = ExtractEmbeddedTextures(extra);
                        var extraMats = BuildMaterials(extra, extraPngs, null);
                        var extraModels = extra.DrawableModels?.High;
                        if (extraModels == null) continue;
                        foreach (var model in extraModels)
                        {
                            if (model?.Geometries == null) continue;
                            foreach (var geom in model.Geometries)
                            {
                                if (geom?.VertexData?.VertexBytes == null || geom.IndexBuffer?.Indices == null) continue;
                                var mb2 = extraMats.TryGetValue(geom.ShaderID, out var em) ? em : DefaultMaterial();
                                string nm = string.IsNullOrEmpty(extraMeshTag)
                                    ? null
                                    : extraMeshTag + "~" + meshCounter;
                                BuildAndAddMesh(geom, mb2, false, null, armature, scene, meshCounter++, nm);
                            }
                        }
                    }
                }

                var gltfModel = scene.ToGltf2();
                var ext = Path.GetExtension(outputPath).ToLowerInvariant();
                if (ext == ".glb") gltfModel.SaveGLB(outputPath);
                else gltfModel.SaveGLTF(outputPath);

                Log("✓ Сохранено: " + outputPath);
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Drawable→GLB EXCEPTION:");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                return false;
            }
        }

        private static bool ConvertCore(string ydrPath, string outputPath, IEnumerable<string> externalYtdPaths, ConvertReport report)
            => ConvertCore(ydrPath, outputPath, externalYtdPaths, report, extraYdrPaths: null, extraMeshTag: null);

        private static bool ConvertCore(string ydrPath, string outputPath,
            IEnumerable<string> externalYtdPaths, ConvertReport report,
            IEnumerable<string> extraYdrPaths, string extraMeshTag = null,
            bool skipPaletteBake = false)
        {
            try
            {
                Log("Чтение: " + ydrPath);
                var data = File.ReadAllBytes(ydrPath);
                var ydr = new YdrFile();
                ydr.Load(data);

                Dictionary<string, byte[]> externalPngs = null;
                if (externalYtdPaths != null)
                {
                    externalPngs = LoadExternalYtdTextures(externalYtdPaths);
                }

                List<Drawable> extras = null;
                if (extraYdrPaths != null)
                {
                    foreach (var p in extraYdrPaths)
                    {
                        if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                        try
                        {
                            var ex = new YdrFile();
                            ex.Load(File.ReadAllBytes(p));
                            if (ex.Drawable != null) (extras ??= new List<Drawable>()).Add(ex.Drawable);
                            Log("Доп. модель в сцену: " + Path.GetFileName(p));
                        }
                        catch (Exception exx)
                        {
                            Log("WARN: доп. модель '" + p + "' не прочиталась: " + exx.Message);
                        }
                    }
                }

                return ConvertDrawableCore(ydr.Drawable, outputPath, externalPngs, report, extras, extraMeshTag, skipPaletteBake);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("YDR→GLB EXCEPTION:");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                return false;
            }
        }

        private static global::System.Drawing.Bitmap LoadBitmap32(byte[] png)
        {
            using var ms = new MemoryStream(png);
            using var src = new global::System.Drawing.Bitmap(ms);
            var bmp = new global::System.Drawing.Bitmap(src.Width, src.Height,
                global::System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var g = global::System.Drawing.Graphics.FromImage(bmp);
            g.DrawImage(src, 0, 0, src.Width, src.Height);
            return bmp;
        }

        internal static byte[] BakePaletteTint(byte[] diffusePng, byte[] palettePng, int row)
        {
            using var pal = LoadBitmap32(palettePng);
            int palW = pal.Width;

            string mode = Environment.GetEnvironmentVariable("YDR_PAL_MODE") ?? "mul";
            if (int.TryParse(Environment.GetEnvironmentVariable("YDR_PAL_ROW"), out var er)) row = er;
            if (row < 0 || row >= pal.Height) row = 0;
            int? tintColEnv = int.TryParse(Environment.GetEnvironmentVariable("YDR_PAL_COL"), out var tc) ? tc : (int?)null;
            float maxCol = 1f;
            if (float.TryParse(Environment.GetEnvironmentVariable("YDR_PAL_MAXCOL"),
                global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var mc)) maxCol = mc;

            byte[] pr = new byte[palW], pg = new byte[palW], pb = new byte[palW];
            var prect = new global::System.Drawing.Rectangle(0, 0, palW, pal.Height);
            var pbd = pal.LockBits(prect, global::System.Drawing.Imaging.ImageLockMode.ReadOnly,
                global::System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                byte[] buf = new byte[pbd.Stride * pal.Height];
                global::System.Runtime.InteropServices.Marshal.Copy(pbd.Scan0, buf, 0, buf.Length);
                for (int x = 0; x < palW; x++)
                {
                    int o = row * pbd.Stride + x * 4;
                    pb[x] = buf[o]; pg[x] = buf[o + 1]; pr[x] = buf[o + 2];
                }
            }
            finally { pal.UnlockBits(pbd); }

            int tci;
            if (tintColEnv.HasValue)
                tci = Math.Clamp(tintColEnv.Value, 0, palW - 1);
            else
            {
                tci = 0; int best = int.MaxValue;
                for (int x = 0; x < palW; x++)
                {
                    int lum = pr[x] + pg[x] + pb[x];
                    if (lum < 24) continue;
                    if (lum < best) { best = lum; tci = x; }
                }
            }
            float tintR = pr[tci], tintG = pg[tci], tintB = pb[tci];

            using var diff = LoadBitmap32(diffusePng);
            int w = diff.Width, h = diff.Height;
            var drect = new global::System.Drawing.Rectangle(0, 0, w, h);
            var dbd = diff.LockBits(drect, global::System.Drawing.Imaging.ImageLockMode.ReadWrite,
                global::System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                int stride = dbd.Stride;
                byte[] buf = new byte[stride * h];
                global::System.Runtime.InteropServices.Marshal.Copy(dbd.Scan0, buf, 0, buf.Length);
                bool mul = mode.Equals("mul", StringComparison.OrdinalIgnoreCase);
                for (int y = 0; y < h; y++)
                {
                    int rowOff = y * stride;
                    for (int x = 0; x < w; x++)
                    {
                        int o = rowOff + x * 4;
                        int gray = buf[o + 2];
                        float r, g, b;
                        if (mul)
                        {
                            float f = gray / 255f;
                            r = tintR * f; g = tintG * f; b = tintB * f;
                        }
                        else
                        {
                            float fu = gray / 255f * (palW - 1) * maxCol;
                            int u0 = (int)fu, u1 = Math.Min(u0 + 1, palW - 1);
                            float t = fu - u0;
                            b = pb[u0] + (pb[u1] - pb[u0]) * t;
                            g = pg[u0] + (pg[u1] - pg[u0]) * t;
                            r = pr[u0] + (pr[u1] - pr[u0]) * t;
                        }
                        buf[o]     = (byte)Math.Clamp(b, 0, 255);
                        buf[o + 1] = (byte)Math.Clamp(g, 0, 255);
                        buf[o + 2] = (byte)Math.Clamp(r, 0, 255);
                        buf[o + 3] = 255;
                    }
                }
                global::System.Runtime.InteropServices.Marshal.Copy(buf, 0, dbd.Scan0, buf.Length);
            }
            finally { diff.UnlockBits(dbd); }

            using var outMs = new MemoryStream();
            diff.Save(outMs, global::System.Drawing.Imaging.ImageFormat.Png);
            return outMs.ToArray();
        }

        private static Dictionary<string, byte[]> LoadExternalYtdTextures(IEnumerable<string> ytdPaths)
        {
            var merged = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var ytdPath in ytdPaths)
            {
                if (string.IsNullOrEmpty(ytdPath) || !File.Exists(ytdPath)) continue;
                try
                {
                    var bytes = File.ReadAllBytes(ytdPath);
                    var ytd = new YtdFile();
                    ytd.Load(bytes);
                    var pngs = ExtractTexturesFromDict(ytd.TextureDict);
                    foreach (var kv in pngs) merged[kv.Key] = kv.Value;
                    Log($"  YTD '{Path.GetFileName(ytdPath)}': +{pngs.Count} текстур");
                }
                catch (Exception ex)
                {
                    Log($"  WARN YTD '{Path.GetFileName(ytdPath)}': {ex.GetType().Name}: {ex.Message}");
                }
            }
            return merged;
        }

        private static Dictionary<string, byte[]> ExtractEmbeddedTextures(Drawable drawable)
            => ExtractTexturesFromDict(drawable?.ShaderGroup?.TextureDictionary);

        internal static Dictionary<string, byte[]> ExtractTexturesFromDict(TextureDictionary td)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var items = td?.Textures?.data_items;
            if (items == null) return result;

            foreach (var tex in items)
            {
                if (tex == null || string.IsNullOrEmpty(tex.Name)) continue;
                try
                {
                    var pngBytes = TryDecodeTexture(tex);
                    if (pngBytes != null && pngBytes.Length > 0)
                        result[tex.Name] = pngBytes;
                }
                catch (Exception ex) { Log("  WARN '" + tex.Name + "': " + ex.Message); }
            }
            return result;
        }

        private static byte[] TryDecodeTexture(CodeWalker.GameFiles.Texture tex)
        {

            byte[] bgra = null;
            try { bgra = CodeWalker.Utils.DDSIO.GetPixels(tex, 0); }
            catch (Exception ex) { Log("  DDSIO threw on '" + tex.Name + "': " + ex.Message); }

            int w = tex.Width, h = tex.Height;
            int expected = w * h * 4;
            if (bgra != null && bgra.Length >= expected && w > 0 && h > 0)
            {
                using var bmp = new global::System.Drawing.Bitmap(w, h, global::System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var rect = new global::System.Drawing.Rectangle(0, 0, w, h);
                var bd = bmp.LockBits(rect,
                    global::System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    global::System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    IntPtr scan0 = bd.Scan0;
                    int srcRow = w * 4;
                    for (int y = 0; y < h; y++)
                    {
                        global::System.Runtime.InteropServices.Marshal.Copy(
                            bgra, y * srcRow, scan0 + y * bd.Stride, srcRow);
                    }
                }
                finally { bmp.UnlockBits(bd); }

                using var ms = new MemoryStream();
                bmp.Save(ms, global::System.Drawing.Imaging.ImageFormat.Png);

                long alphaSum = 0;
                for (int i = 3; i < bgra.Length; i += 4) alphaSum += bgra[i];
                double avgAlpha = (alphaSum / (double)(w * h)) / 255.0;
                Log(string.Format("  texture: {0} ({1}x{2}) fmt={3} avgAlpha={4:F2} [DDSIO]",
                    tex.Name, w, h, tex.Format, avgAlpha));
                return ms.ToArray();
            }

            Log("  WARN '" + tex.Name + "': DDSIO returned empty - пробую Magick fallback");
            try
            {
                byte[] ddsBytes = CodeWalker.Utils.DDSIO.GetDDSFile(tex);
                if (ddsBytes == null || ddsBytes.Length == 0)
                {
                    Log("  WARN '" + tex.Name + "': GetDDSFile also returned empty - texture data missing");
                    return null;
                }
                using var image = new MagickImage(ddsBytes);

                image.Format = MagickFormat.Png32;
                using var outMs = new MemoryStream();
                image.Write(outMs, MagickFormat.Png);
                Log(string.Format("  texture: {0} ({1}x{2}) fmt={3} [Magick fallback]",
                    tex.Name, w, h, tex.Format));
                return outMs.ToArray();
            }
            catch (Exception ex)
            {
                Log("  WARN '" + tex.Name + "': Magick fallback also failed: " + ex.Message);
                return null;
            }
        }

        private static Dictionary<ushort, MaterialBuilder> BuildMaterials(
            Drawable drawable, Dictionary<string, byte[]> pngByName)
            => BuildMaterials(drawable, pngByName, null);

        private static Dictionary<ushort, MaterialBuilder> BuildMaterials(
            Drawable drawable, Dictionary<string, byte[]> pngByName, ConvertReport report,
            bool skipPaletteBake = false)
        {
            var result = new Dictionary<ushort, MaterialBuilder>();
            var shaders = drawable.ShaderGroup?.Shaders?.data_items;
            if (shaders == null) return result;

            for (ushort i = 0; i < shaders.Length; i++)
            {
                var sh = shaders[i];
                if (sh == null) continue;

                uint shaderNameHash = ParseShaderHash(sh.Name);
                uint shaderFileNameHash = ParseShaderHash(sh.FileName);

                string diffuse = null, normal = null, palette = null;
                bool hasSpecSampler = false;
                float emissiveStrength = 0f;

                Vector4 colorScale = new Vector4(1f, 1f, 1f, 1f);
                bool colorScaleFound = false;
                var pl = sh.ParametersList;
                if (pl?.Parameters != null && pl.Hashes != null)
                {
                    for (int p = 0; p < pl.Parameters.Length; p++)
                    {
                        var param = pl.Parameters[p];
                        if (param == null) continue;
                        uint paramHash = (uint)pl.Hashes[p];

                        if (param.Data is TextureBase tb && !string.IsNullOrEmpty(tb.Name))
                        {
                            if (diffuse == null && DiffuseSamplers.Contains(paramHash))
                                diffuse = tb.Name;
                            else if (normal == null && NormalSamplers.Contains(paramHash))
                                normal = tb.Name;
                            if (SpecSamplers.Contains(paramHash))
                                hasSpecSampler = true;
                            if (palette == null && tb.Name.EndsWith("pal", StringComparison.OrdinalIgnoreCase))
                                palette = tb.Name;
                        }

                        else if (param.Data is Vector4[] arr && arr.Length > 0
                                 && paramHash == EMISSIVE_MULTIPLIER_HASH)
                        {
                            emissiveStrength = arr[0].X;
                        }
                        else if (param.Data is Vector4 v4 && paramHash == EMISSIVE_MULTIPLIER_HASH)
                        {
                            emissiveStrength = v4.X;
                        }

                        else if (param.Data is Vector4[] cscArr && cscArr.Length > 0
                                 && paramHash == MATERIAL_COLOR_SCALE_HASH)
                        {
                            colorScale = cscArr[0];
                            colorScaleFound = true;
                        }
                        else if (param.Data is Vector4 cscV4 && paramHash == MATERIAL_COLOR_SCALE_HASH)
                        {
                            colorScale = cscV4;
                            colorScaleFound = true;
                        }
                    }
                }

                if (diffuse == null && pl?.Parameters != null)
                {
                    string namedDiffuse  = null;
                    string nonAuxFallback = null;
                    string anyTexture     = null;
                    foreach (var p in pl.Parameters)
                    {
                        if (p?.Data is not TextureBase tb || string.IsNullOrEmpty(tb.Name)) continue;
                        anyTexture ??= tb.Name;
                        if (LooksLikeDiffuseTextureName(tb.Name))
                        {
                            namedDiffuse = tb.Name;
                            break;
                        }
                        if (nonAuxFallback == null && !LooksLikeAuxTextureName(tb.Name))
                            nonAuxFallback = tb.Name;
                    }
                    diffuse = namedDiffuse ?? nonAuxFallback ?? anyTexture;
                }

                if (normal == null && pl?.Parameters != null)
                {
                    foreach (var p in pl.Parameters)
                    {
                        if (p?.Data is not TextureBase tb || string.IsNullOrEmpty(tb.Name)) continue;
                        if (LooksLikeNormalTextureName(tb.Name))
                        {
                            normal = tb.Name;
                            break;
                        }
                    }
                }

                string nameStr = ResolveHash(sh.Name.ToString());
                string fileNameStr = ResolveHash(sh.FileName.ToString());

                string check = (nameStr + " " + fileNameStr).ToLowerInvariant();

                bool isEmissive = check.Contains("emissive");
                bool isAlphaBlend = check.Contains("alpha") || check.Contains("glass") || sh.RenderBucket >= 1;
                bool isAlphaCutout = check.Contains("cutout") || check.Contains("decal");
                bool isWeapon = check.Contains("weapon");
                bool isGlass  = check.Contains("glass") || shaderFileNameHash == 0x78838E05;
                bool isMetal  = isWeapon
                                || check.Contains("vehicle_paint")
                                || check.Contains("vehicle_mesh")
                                || check.Contains("vehicle_chrome");

                if (check.Contains("emissivestrong")) { isAlphaBlend = true; isEmissive = true; }

                isEmissive = isEmissive || EmissiveShaders.Contains(shaderNameHash) || EmissiveShaders.Contains(shaderFileNameHash);
                isAlphaBlend = isAlphaBlend || AlphaBlendShaders.Contains(shaderNameHash) || AlphaBlendShaders.Contains(shaderFileNameHash);
                isAlphaCutout = isAlphaCutout || AlphaCutoutShaders.Contains(shaderNameHash) || AlphaCutoutShaders.Contains(shaderFileNameHash);

                AlphaMode alphaMode;
                if (isAlphaBlend) alphaMode = AlphaMode.BLEND;
                else if (isAlphaCutout) alphaMode = AlphaMode.MASK;
                else alphaMode = AlphaMode.OPAQUE;

                bool isDoubleSided = isAlphaBlend || isAlphaCutout;

                Log(string.Format(
                    "  shader[{0}] Name={1} ({2}) FileName={3} ({4}) bucket={5} -> emissive={6} alpha={7} doubleSided={8} weapon={9}",
                    i, sh.Name, nameStr, sh.FileName, fileNameStr, sh.RenderBucket,
                    isEmissive, alphaMode, isDoubleSided, isWeapon));
                Log(string.Format(
                    "    diffuse={0} normal={1} emissiveStrength={2}",
                    diffuse ?? "<none>", normal ?? "<none>", emissiveStrength));
                if (colorScaleFound)
                {
                    Log(string.Format(
                        "    matMaterialColorScale={0:F3},{1:F3},{2:F3},{3:F3}",
                        colorScale.X, colorScale.Y, colorScale.Z, colorScale.W));
                }

                float metallic, roughness;
                if (isMetal) { metallic = 0.4f; roughness = 0.5f; }
                else if (isGlass) { metallic = 0.0f; roughness = 0.10f; }
                else { metallic = 0.0f; roughness = 0.6f; }

                if (!isGlass && !isEmissive && normal == null && !hasSpecSampler)
                {
                    metallic  = 0.0f;
                    roughness = 0.92f;
                }

                var mb = new MaterialBuilder("Mat_" + i)
                    .WithDoubleSide(isDoubleSided)
                    .WithMetallicRoughnessShader()
                    .WithMetallicRoughness(metallic, roughness);

                if (alphaMode == AlphaMode.MASK) mb.WithAlpha(alphaMode, 0.5f);
                else mb.WithAlpha(alphaMode);

                byte[] resolvedDiffuse = null;
                string resolvedDiffuseKey = diffuse;
                if (diffuse != null && pngByName.TryGetValue(diffuse, out var dpng))
                {
                    resolvedDiffuse = dpng;
                }
                else if (diffuse != null)
                {
                    Log("    WARN: diffuse '" + diffuse + "' не в pngByName - пробую salvage");
                    var salvage = PickSalvageDiffuse(pngByName, diffuse);
                    if (salvage.Value != null)
                    {
                        resolvedDiffuse    = salvage.Value;
                        resolvedDiffuseKey = salvage.Key;
                        Log("    → salvage chose: " + salvage.Key);
                        if (report != null)
                        {
                            report.SalvagedDiffuse++;
                            report.Warnings.Add(
                                $"шейдер #{i}: текстура '{diffuse}' не найдена, подставлена '{salvage.Key}' " +
                                "- цвет в превью не совпадёт с игрой");
                        }
                    }
                    else if (report != null)
                    {
                        report.MissingDiffuse++;
                        report.Warnings.Add(
                            $"шейдер #{i}: текстура '{diffuse}' не найдена и заменить нечем - деталь будет белой");
                    }
                }
                if (!skipPaletteBake && resolvedDiffuse != null && palette != null
                    && pngByName.TryGetValue(palette, out var palPng))
                {
                    try
                    {
                        resolvedDiffuse = BakePaletteTint(resolvedDiffuse, palPng, 0);
                        Log("    → palette baked: " + palette + " (row 0)");
                    }
                    catch (Exception ex) { Log("    palette bake fail: " + ex.Message); }
                }

                if (resolvedDiffuse != null)
                {
                    mb.WithChannelImage(KnownChannel.BaseColor, new SharpGLTF.Memory.MemoryImage(resolvedDiffuse));
                    Log("    → BaseColor: " + resolvedDiffuseKey);
                }

                if (colorScaleFound &&
                    !(Math.Abs(colorScale.X - 1f) < 0.001f &&
                      Math.Abs(colorScale.Y - 1f) < 0.001f &&
                      Math.Abs(colorScale.Z - 1f) < 0.001f &&
                      Math.Abs(colorScale.W - 1f) < 0.001f))
                {

                    Vector4 clamped = new Vector4(
                        Math.Clamp(colorScale.X, 0f, 1f),
                        Math.Clamp(colorScale.Y, 0f, 1f),
                        Math.Clamp(colorScale.Z, 0f, 1f),
                        Math.Clamp(colorScale.W, 0f, 1f));
                    mb.WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, clamped);
                    Log(string.Format(
                        "    → baseColorFactor: ({0:F3}, {1:F3}, {2:F3}, {3:F3})",
                        clamped.X, clamped.Y, clamped.Z, clamped.W));
                }

                if (normal != null && pngByName.TryGetValue(normal, out var npng))
                {
                    mb.WithChannelImage(KnownChannel.Normal, new SharpGLTF.Memory.MemoryImage(npng));
                    Log("    → Normal: " + normal);
                }

                if (isEmissive && resolvedDiffuse != null)
                {
                    mb.WithChannelImage(KnownChannel.Emissive, new SharpGLTF.Memory.MemoryImage(resolvedDiffuse));

                    mb.WithChannelParam(KnownChannel.Emissive, KnownProperty.RGB, new Vector3(1f, 1f, 1f));

                    if (emissiveStrength > 1f)
                    {
                        try { mb.WithEmissive(new SharpGLTF.Memory.MemoryImage(resolvedDiffuse), Vector3.One, emissiveStrength); }
                        catch {  }
                    }
                    Log("    → Emissive подключён (strength=" + emissiveStrength + ")");
                }

                result[i] = mb;
            }
            return result;
        }

        private static uint ParseShaderHash(object name)
        {
            if (name == null) return 0;
            string s = name.ToString();
            if (string.IsNullOrEmpty(s)) return 0;
            if (uint.TryParse(s, out uint h)) return h;
            return JenkinsHash(s);
        }

        private static MaterialBuilder DefaultMaterial()
        {
            return new MaterialBuilder("Mat_default")
                .WithDoubleSide(false)
                .WithMetallicRoughnessShader()
                .WithMetallicRoughness(0.0f, 0.7f)
                .WithBaseColor(new Vector4(0.7f, 0.7f, 0.7f, 1f));
        }

        private static NodeBuilder[] BuildSkeleton(Drawable drawable, NodeBuilder root)
        {
            var bones = drawable.Skeleton?.Bones?.Items;
            if (bones == null || bones.Length == 0) return null;

            var nodes = new NodeBuilder[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                nodes[i] = new NodeBuilder(string.IsNullOrEmpty(b.Name) ? ("bone_" + i) : b.Name);
                var t = b.Translation;
                var r = b.Rotation;
                var s = b.Scale;

                if (b.ParentIndex < 0)
                {
                    nodes[i].WithLocalTranslation(new Vector3(t.X, t.Z, -t.Y));
                    nodes[i].WithLocalRotation(new Quaternion(r.X, r.Z, -r.Y, r.W));
                }
                else
                {
                    nodes[i].WithLocalTranslation(new Vector3(t.X, t.Y, t.Z));
                    nodes[i].WithLocalRotation(new Quaternion(r.X, r.Y, r.Z, r.W));
                }
                nodes[i].WithLocalScale(new Vector3(
                    s.X == 0 ? 1 : s.X, s.Y == 0 ? 1 : s.Y, s.Z == 0 ? 1 : s.Z));
            }
            for (int i = 0; i < bones.Length; i++)
            {
                short pi = bones[i].ParentIndex;
                if (pi >= 0 && pi < bones.Length) nodes[pi].AddNode(nodes[i]);
                else root.AddNode(nodes[i]);
            }
            return nodes;
        }

        private static int SizeOf(VertexComponentType ct) => ct switch
        {
            VertexComponentType.Half2 => 4,
            VertexComponentType.Float => 4,
            VertexComponentType.Half4 => 8,
            VertexComponentType.FloatUnk => 4,
            VertexComponentType.Float2 => 8,
            VertexComponentType.Float3 => 12,
            VertexComponentType.Float4 => 16,
            VertexComponentType.UByte4 => 4,
            VertexComponentType.Colour => 4,
            VertexComponentType.RGBA8SNorm => 4,
            _ => 0
        };

        private static void BuildAndAddMesh(
            DrawableGeometry geom, MaterialBuilder mat,
            bool useSkinning, NodeBuilder[] boneNodes, NodeBuilder armature,
            SceneBuilder scene, int meshIdx)
            => BuildAndAddMesh(geom, mat, useSkinning, boneNodes, armature, scene, meshIdx, null);

        private static void BuildAndAddMesh(
            DrawableGeometry geom, MaterialBuilder mat,
            bool useSkinning, NodeBuilder[] boneNodes, NodeBuilder armature,
            SceneBuilder scene, int meshIdx, string meshName)
        {
            var vd = geom.VertexData;
            int vertexCount = vd.VertexCount;
            int stride = vd.VertexStride;
            var bytes = vd.VertexBytes;
            var info = vd.Info;
            var indices = geom.IndexBuffer.Indices;

            Log($"  Mesh[{meshIdx}] vertices={vertexCount} stride={stride} indices={indices?.Length ?? 0}");
            Log($"  VertexDeclaration Flags=0x{info.Flags:X8} Types=0x{(ulong)info.Types:X16}");

            bool HasUsable(VertexSemantics sem)
            {
                if (!info.HasSemantic(sem)) return false;
                int o = info.GetComponentOffset((int)sem);
                if (o < 0) return false;
                int sz = SizeOf(info.GetComponentType((int)sem));
                return sz > 0 && o + sz <= stride;
            }

            if (stride <= 0 || bytes == null || (long)vertexCount * stride > bytes.Length)
            {
                Log($"  WARN Mesh[{meshIdx}]: буфер вершин {bytes?.Length ?? 0} б не вмещает " +
                    $"{vertexCount}×{stride} - деталь пропущена");
                return;
            }
            for (int s = 0; s < 16; s++)
            {
                if (((info.Flags >> s) & 0x1) != 1) continue;
                var sem = (VertexSemantics)s;
                var ct = info.GetComponentType(s);
                var off = info.GetComponentOffset(s);
                Log($"    sem[{s,2}]={sem,-15} type={ct} offset={off}");
            }

            if (vertexCount > 0 && HasUsable(VertexSemantics.TexCoord0))
            {
                int sampleN = Math.Min(5, vertexCount);
                var uvType = info.GetComponentType((int)VertexSemantics.TexCoord0);
                Log($"    TexCoord0 type={uvType}, first {sampleN} raw UVs:");
                for (int v = 0; v < sampleN; v++)
                {
                    var raw = ReadUV(info, bytes, v * stride);
                    Log($"      v[{v}]=({raw.X:F4}, {raw.Y:F4})");
                }
            }

            if (vertexCount > 0 && HasUsable(VertexSemantics.TexCoord1))
            {
                int sampleN = Math.Min(3, vertexCount);
                var uv1Type = info.GetComponentType((int)VertexSemantics.TexCoord1);
                int uv1Off = info.GetComponentOffset((int)VertexSemantics.TexCoord1);
                Log($"    TexCoord1 type={uv1Type} offset={uv1Off}, first {sampleN} raw UVs:");
                for (int v = 0; v < sampleN; v++)
                {
                    int o = v * stride + uv1Off;
                    Vector2 r = uv1Type == VertexComponentType.Float2
                        ? new Vector2(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4))
                        : uv1Type == VertexComponentType.Half2
                            ? new Vector2(HalfToFloat(BitConverter.ToUInt16(bytes, o)),
                                          HalfToFloat(BitConverter.ToUInt16(bytes, o + 2)))
                            : Vector2.Zero;
                    Log($"      v[{v}]=({r.X:F4}, {r.Y:F4})");
                }
            }

            var boneIds = TryGetBoneMapping(geom);
            bool canSkin = useSkinning && boneNodes != null
                          && HasUsable(VertexSemantics.BlendWeights)
                          && HasUsable(VertexSemantics.BlendIndices);

            var positions = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];
            var jointWeights = canSkin ? new (int, float)[vertexCount][] : null;

            for (int v = 0; v < vertexCount; v++)
            {
                int basePos = v * stride;

                Vector3 rawPos = ReadComp3(info, bytes, basePos, VertexSemantics.Position);
                Vector3 rawNorm = HasUsable(VertexSemantics.Normal)
                    ? ReadNormal(info, bytes, basePos)
                    : Vector3.UnitZ;
                Vector2 rawUv = HasUsable(VertexSemantics.TexCoord0)
                    ? ReadUV(info, bytes, basePos)
                    : Vector2.Zero;

                positions[v] = new Vector3(rawPos.X,  rawPos.Z, -rawPos.Y);
                normals[v]   = new Vector3(rawNorm.X, rawNorm.Z, -rawNorm.Y);
                uvs[v]       = rawUv;

                if (canSkin)
                {
                    int biOff = info.GetComponentOffset((int)VertexSemantics.BlendIndices);
                    int bwOff = info.GetComponentOffset((int)VertexSemantics.BlendWeights);
                    if (biOff < 0 || bwOff < 0) { jointWeights[v] = OneJoint(0); continue; }

                    byte i0 = bytes[basePos + biOff + 0];
                    byte i1 = bytes[basePos + biOff + 1];
                    byte i2 = bytes[basePos + biOff + 2];
                    byte i3 = bytes[basePos + biOff + 3];
                    byte w0 = bytes[basePos + bwOff + 0];
                    byte w1 = bytes[basePos + bwOff + 1];
                    byte w2 = bytes[basePos + bwOff + 2];
                    byte w3 = bytes[basePos + bwOff + 3];

                    int g0 = MapBone(boneIds, i0, boneNodes.Length);
                    int g1 = MapBone(boneIds, i1, boneNodes.Length);
                    int g2 = MapBone(boneIds, i2, boneNodes.Length);
                    int g3 = MapBone(boneIds, i3, boneNodes.Length);

                    float fw0 = w0 / 255f, fw1 = w1 / 255f, fw2 = w2 / 255f, fw3 = w3 / 255f;
                    float sum = fw0 + fw1 + fw2 + fw3;
                    if (sum > 0.001f) { fw0 /= sum; fw1 /= sum; fw2 /= sum; fw3 /= sum; }
                    else { fw0 = 1; fw1 = fw2 = fw3 = 0; }

                    jointWeights[v] = new (int, float)[]
                    {
                        (g0, fw0), (g1, fw1), (g2, fw2), (g3, fw3)
                    };
                }
            }

            if (canSkin)
            {
                var mb = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexJoints4>("Mesh_" + meshIdx);
                var prim = mb.UsePrimitive(mat);
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                    if (a >= vertexCount || b >= vertexCount || c >= vertexCount) continue;
                    var va = MakeSkinned(positions[a], normals[a], uvs[a], jointWeights[a]);
                    var vb = MakeSkinned(positions[b], normals[b], uvs[b], jointWeights[b]);
                    var vc = MakeSkinned(positions[c], normals[c], uvs[c], jointWeights[c]);
                    prim.AddTriangle(va, vb, vc);
                }
                scene.AddSkinnedMesh(mb, armature.WorldMatrix, boneNodes);
            }
            else
            {
                var mb = new MeshBuilder<VertexPositionNormal, VertexTexture1>(meshName ?? ("Mesh_" + meshIdx));
                var prim = mb.UsePrimitive(mat);
                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                    if (a >= vertexCount || b >= vertexCount || c >= vertexCount) continue;
                    var va = (new VertexPositionNormal(positions[a], normals[a]), new VertexTexture1(uvs[a]));
                    var vb = (new VertexPositionNormal(positions[b], normals[b]), new VertexTexture1(uvs[b]));
                    var vc = (new VertexPositionNormal(positions[c], normals[c]), new VertexTexture1(uvs[c]));
                    prim.AddTriangle(va, vb, vc);
                }
                scene.AddRigidMesh(mb, Matrix4x4.Identity);
            }
        }

        private static (int, float)[] OneJoint(int idx)
            => new (int, float)[] { (idx, 1f), (0, 0f), (0, 0f), (0, 0f) };

        private static int MapBone(ushort[] mapping, byte localIdx, int boneCount)
        {
            int idx = (mapping != null && localIdx < mapping.Length) ? mapping[localIdx] : localIdx;
            if (idx < 0 || idx >= boneCount) idx = 0;
            return idx;
        }

        private static ushort[] TryGetBoneMapping(DrawableGeometry geom)
        {
            try { return geom.BoneIds; } catch { return null; }
        }

        private static (VertexPositionNormal, VertexTexture1, VertexJoints4) MakeSkinned(
            Vector3 p, Vector3 n, Vector2 uv, (int, float)[] jw)
        {
            return (
                new VertexPositionNormal(p, n),
                new VertexTexture1(uv),
                new VertexJoints4(jw[0], jw[1], jw[2], jw[3])
            );
        }

        private static Vector3 ReadComp3(VertexDeclaration info, byte[] b, int basePos, VertexSemantics sem)
        {
            int idx = (int)sem;
            int off = info.GetComponentOffset(idx);
            var ct = info.GetComponentType(idx);
            if (off < 0) return Vector3.Zero;
            int o = basePos + off;
            switch (ct)
            {
                case VertexComponentType.Float3:
                case VertexComponentType.Float4:
                    return new Vector3(
                        BitConverter.ToSingle(b, o),
                        BitConverter.ToSingle(b, o + 4),
                        BitConverter.ToSingle(b, o + 8));
                case VertexComponentType.Half4:
                    return new Vector3(
                        HalfToFloat(BitConverter.ToUInt16(b, o)),
                        HalfToFloat(BitConverter.ToUInt16(b, o + 2)),
                        HalfToFloat(BitConverter.ToUInt16(b, o + 4)));
                default:
                    return Vector3.Zero;
            }
        }

        private static Vector2 ReadUV(VertexDeclaration info, byte[] b, int basePos)
        {
            int idx = (int)VertexSemantics.TexCoord0;
            int off = info.GetComponentOffset(idx);
            var ct = info.GetComponentType(idx);
            if (off < 0) return Vector2.Zero;
            int o = basePos + off;
            switch (ct)
            {
                case VertexComponentType.Float2:
                    return new Vector2(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4));
                case VertexComponentType.Half2:
                    return new Vector2(
                        HalfToFloat(BitConverter.ToUInt16(b, o)),
                        HalfToFloat(BitConverter.ToUInt16(b, o + 2)));
                default:
                    return Vector2.Zero;
            }
        }

        private static Vector3 ReadNormal(VertexDeclaration info, byte[] b, int basePos)
        {
            int idx = (int)VertexSemantics.Normal;
            int off = info.GetComponentOffset(idx);
            var ct = info.GetComponentType(idx);
            if (off < 0) return Vector3.UnitZ;
            int o = basePos + off;
            Vector3 n;
            switch (ct)
            {
                case VertexComponentType.Float3:
                case VertexComponentType.Float4:
                    n = new Vector3(BitConverter.ToSingle(b, o),
                                     BitConverter.ToSingle(b, o + 4),
                                     BitConverter.ToSingle(b, o + 8));
                    break;
                case VertexComponentType.RGBA8SNorm:
                    n = new Vector3(((sbyte)b[o]) / 127f, ((sbyte)b[o + 1]) / 127f, ((sbyte)b[o + 2]) / 127f);
                    break;
                case VertexComponentType.Half4:
                    n = new Vector3(
                        HalfToFloat(BitConverter.ToUInt16(b, o)),
                        HalfToFloat(BitConverter.ToUInt16(b, o + 2)),
                        HalfToFloat(BitConverter.ToUInt16(b, o + 4)));
                    break;
                default: return Vector3.UnitZ;
            }
            float lsq = n.LengthSquared();
            return lsq < 1e-8f ? Vector3.UnitZ : Vector3.Normalize(n);
        }

        private static float HalfToFloat(ushort h)
        {
            uint sign = (uint)(h >> 15) & 0x1;
            uint exp = (uint)(h >> 10) & 0x1F;
            uint mant = (uint)h & 0x3FF;
            uint f;
            if (exp == 0)
            {
                if (mant == 0) f = sign << 31;
                else
                {
                    while ((mant & 0x400) == 0) { mant <<= 1; exp = unchecked(exp - 1); }
                    exp = unchecked(exp + 1); mant &= ~0x400u;
                    f = (sign << 31) | ((exp + (127 - 15)) << 23) | (mant << 13);
                }
            }
            else if (exp == 31) f = (sign << 31) | 0x7F800000 | (mant << 13);
            else f = (sign << 31) | ((exp + (127 - 15)) << 23) | (mant << 13);
            return BitConverter.Int32BitsToSingle(unchecked((int)f));
        }
    }

    internal static class VertexDeclarationExt
    {
        public static bool HasSemantic(this VertexDeclaration decl, VertexSemantics sem)
        {
            try
            {
                var ct = decl.GetComponentType((int)sem);
                if (ct == VertexComponentType.Nothing) return false;
                int off = decl.GetComponentOffset((int)sem);
                return off >= 0;
            }
            catch { return false; }
        }
    }
}
