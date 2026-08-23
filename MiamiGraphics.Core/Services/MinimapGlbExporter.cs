#nullable disable
using CodeWalker.GameFiles;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
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
    public static class MinimapGlbExporter
    {
        private static void Log(string s) => Console.WriteLine("[MAP→GLB] " + s);

        private static readonly string[] MapRpfNames = { "minimap.rpf", "spexvector.rpf" };

        public static Task<bool> ExportFromPackageAsync(string packageDir, string outputGlb)
            => Task.Run(() => ExportFromPackage(packageDir, outputGlb));

        public static bool ExportFromPackage(string packageDir, string outputGlb)
        {
            try
            {
                var ydds = new List<(string Name, YddFile Ydd)>();

                foreach (var rpfPath in Directory.EnumerateFiles(packageDir, "*.rpf", SearchOption.AllDirectories))
                {
                    var name = Path.GetFileName(rpfPath);
                    if (!MapRpfNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    Log($"Читаю {name} ({new FileInfo(rpfPath).Length / 1024} KB)");
                    int before = ydds.Count;
                    try
                    {
                        using var fs = new FileStream(rpfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        using var arc = RageArchiveWrapper7.Open(fs, name, leaveOpen: true);
                        CollectYdds(arc.Root, ydds);
                    }
                    catch (Exception ex)
                    {
                        Log($"  RageLib не открыл {name}: {ex.GetType().Name}: {ex.Message} - пробую CodeWalker");
                    }
                    if (ydds.Count == before)
                        CollectYddsSanitized(rpfPath, name, ydds);
                    if (ydds.Count == before)
                        CollectYddsViaCodeWalker(rpfPath, ydds);
                }

                if (ydds.Count == 0)
                {
                    foreach (var yddPath in Directory.EnumerateFiles(packageDir, "*.ydd", SearchOption.AllDirectories))
                    {
                        var ydd = TryLoadYdd(Path.GetFileName(yddPath), File.ReadAllBytes(yddPath));
                        if (ydd != null) ydds.Add((Path.GetFileName(yddPath), ydd));
                    }
                }

                Log($"Найдено ydd: {ydds.Count}");
                if (ydds.Count == 0) { Log("ERROR: в пакете нет векторных тайлов (.ydd)"); return false; }

                return ExportLoaded(ydds, outputGlb);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("MAP→GLB EXCEPTION:");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                return false;
            }
        }

        private static YddFile TryLoadYdd(string name, byte[] fullResourceBytes)
        {
            try
            {
                var ydd = new YddFile();
                ydd.Load(fullResourceBytes);
                return ydd;
            }
            catch (Exception ex)
            {
                Log($"  WARN: {name} не распарсился: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void CollectYddsSanitized(string rpfPath, string name, List<(string, YddFile)> ydds)
        {
            byte[] raw;
            try { raw = File.ReadAllBytes(rpfPath); }
            catch (Exception ex) { Log($"  sanitize: не прочитался файл: {ex.Message}"); return; }
            if (raw.Length < 16) return;

            uint entries = BitConverter.ToUInt32(raw, 4);
            uint namesLen = BitConverter.ToUInt32(raw, 8);

            foreach (var mask in new uint[] { 0xFFFF, 0xFFFFF, 0xFFFFFF })
            {
                uint fixedLen = namesLen & mask;
                if (fixedLen == namesLen) continue;
                long tocEnd = 16L + 16L * entries + fixedLen;
                if (tocEnd > raw.Length) continue;

                var patched = (byte[])raw.Clone();
                BitConverter.GetBytes(fixedLen).CopyTo(patched, 8);
                try
                {
                    using var ms = new MemoryStream(patched);
                    using var arc = RageArchiveWrapper7.Open(ms, name, leaveOpen: true);
                    int before = ydds.Count;
                    CollectYdds(arc.Root, ydds);
                    if (ydds.Count > before)
                    {
                        Log($"  sanitize: NamesLength 0x{namesLen:X8} → 0x{fixedLen:X} - извлечено {ydds.Count - before} ydd");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Log($"  sanitize mask 0x{mask:X}: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        private static void CollectYddsViaCodeWalker(string rpfPath, List<(string, YddFile)> ydds)
        {
            try
            {
                if (GTA5Keys.PC_NG_KEYS == null || GTA5Keys.PC_NG_KEYS.Length == 0)
                {
                    GTA5Keys.PC_AES_KEY = RageLib.GTA5.Cryptography.GTA5Constants.PC_AES_KEY;
                    GTA5Keys.PC_NG_KEYS = RageLib.GTA5.Cryptography.GTA5Constants.PC_NG_KEYS;
                    GTA5Keys.PC_NG_DECRYPT_TABLES = RageLib.GTA5.Cryptography.GTA5Constants.PC_NG_DECRYPT_TABLES;
                    GTA5Keys.PC_LUT = RageLib.GTA5.Cryptography.GTA5Constants.PC_LUT;
                }
                if (GTA5Keys.PC_AES_KEY == null)
                {
                    Log("  CodeWalker fallback: ключи не загружены (GTA5Constants пуст) - пропуск");
                    return;
                }

                var rpf = new RpfFile(rpfPath, Path.GetFileName(rpfPath));
                rpf.ScanStructure(_ => { }, e => Log("  [cw scan] " + e));

                void Walk(RpfFile f)
                {
                    if (f.AllEntries != null)
                    {
                        foreach (var e in f.AllEntries)
                        {
                            if (e is not RpfFileEntry fe) continue;
                            if (!(fe.NameLower?.EndsWith(".ydd") ?? false)) continue;
                            try
                            {
                                var ydd = RpfFile.GetFile<YddFile>(fe);
                                if (ydd?.Drawables is { Length: > 0 })
                                    ydds.Add((fe.Name, ydd));
                            }
                            catch (Exception ex) { Log($"  WARN: cw {fe.Name}: {ex.Message}"); }
                        }
                    }
                    if (f.Children != null)
                        foreach (var c in f.Children) Walk(c);
                }
                Walk(rpf);
                Log($"  CodeWalker fallback: извлечено {ydds.Count} ydd");
            }
            catch (Exception ex)
            {
                Log($"  CodeWalker fallback fail: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void CollectYdds(IArchiveDirectory dir, List<(string, YddFile)> ydds)
        {
            foreach (var f in dir.GetFiles())
            {
                if (f.Name.EndsWith(".ydd", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var ms = new MemoryStream();
                        f.Export(ms);
                        var ydd = TryLoadYdd(f.Name, ms.ToArray());
                        if (ydd != null) ydds.Add((f.Name, ydd));
                    }
                    catch (Exception ex) { Log($"  WARN: export {f.Name} fail: {ex.Message}"); }
                }
                else if (f.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && f is IArchiveBinaryFile bin)
                {
                    try
                    {
                        using var stream = bin.GetStream();
                        using var nested = RageArchiveWrapper7.Open(stream, bin.Name, leaveOpen: true);
                        CollectYdds(nested.Root, ydds);
                    }
                    catch (Exception ex) { Log($"  WARN: nested {f.Name} не открылся: {ex.Message}"); }
                }
            }
            foreach (var sub in dir.GetDirectories())
                CollectYdds(sub, ydds);
        }

        public static bool ExportFromYdds(IEnumerable<(string Name, byte[] Bytes)> ydds, string outputGlb)
        {
            var loaded = new List<(string, YddFile)>();
            foreach (var (name, bytes) in ydds)
            {
                var ydd = TryLoadYdd(name, bytes);
                if (ydd != null) loaded.Add((name, ydd));
            }
            return ExportLoaded(loaded, outputGlb);
        }

        private static bool ExportLoaded(List<(string Name, YddFile Ydd)> ydds, string outputGlb)
        {
            var mat = new MaterialBuilder("map")
                .WithDoubleSide(true)
                .WithUnlitShader()
                .WithBaseColor(new Vector4(1, 1, 1, 1));

            var mesh = new MeshBuilder<VertexPosition, VertexColor1>("map");
            var prim = mesh.UsePrimitive(mat);

            long verts = 0, tris = 0;
            int dicts = 0, drawables = 0;

            foreach (var (name, ydd) in ydds)
            {
                var items = ydd.Drawables;
                if (items == null || items.Length == 0) continue;
                dicts++;

                foreach (var dr in items)
                {
                    var models = dr?.DrawableModels?.High;
                    if (models == null || models.Length == 0) continue;
                    drawables++;

                    foreach (var model in models)
                    {
                        if (model?.Geometries == null) continue;
                        foreach (var geom in model.Geometries)
                        {
                            var vd = geom?.VertexData;
                            if (vd?.VertexBytes == null || geom.IndexBuffer?.Indices == null) continue;
                            AppendGeometry(prim, vd, geom.IndexBuffer.Indices, ref verts, ref tris);
                        }
                    }
                }
            }

            Log($"словарей={dicts} drawables={drawables} вершин={verts} треугольников={tris}");
            if (tris == 0) { Log("ERROR: нет геометрии"); return false; }

            var scene = new SceneBuilder();
            scene.AddRigidMesh(mesh, Matrix4x4.Identity);
            var model2 = scene.ToGltf2();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputGlb)) ?? ".");
            model2.SaveGLB(outputGlb);
            Log($"✓ {outputGlb} ({new FileInfo(outputGlb).Length / 1024.0 / 1024.0:F1} MB)");
            return true;
        }

        private static float SrgbToLinear(byte b)
        {
            float c = b / 255f;
            return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }

        private static void AppendGeometry(
            PrimitiveBuilder<MaterialBuilder, VertexPosition, VertexColor1, VertexEmpty> prim,
            VertexData vd, ushort[] indices, ref long verts, ref long tris)
        {
            int vc = vd.VertexCount, stride = vd.VertexStride;
            var bytes = vd.VertexBytes;
            var info = vd.Info;

            int posOff = info.GetComponentOffset((int)VertexSemantics.Position);
            var posType = info.GetComponentType((int)VertexSemantics.Position);
            if (posOff < 0) return;

            int colOff = -1;
            try
            {
                if (info.GetComponentType((int)VertexSemantics.Colour0) != VertexComponentType.Nothing)
                    colOff = info.GetComponentOffset((int)VertexSemantics.Colour0);
            }
            catch { }

            var positions = new Vector3[vc];
            var colors = new Vector4[vc];
            for (int v = 0; v < vc; v++)
            {
                int o = v * stride + posOff;
                Vector3 p = posType switch
                {
                    VertexComponentType.Float3 or VertexComponentType.Float4 => new Vector3(
                        BitConverter.ToSingle(bytes, o),
                        BitConverter.ToSingle(bytes, o + 4),
                        BitConverter.ToSingle(bytes, o + 8)),
                    VertexComponentType.Half4 => new Vector3(
                        (float)BitConverter.ToHalf(bytes, o),
                        (float)BitConverter.ToHalf(bytes, o + 2),
                        (float)BitConverter.ToHalf(bytes, o + 4)),
                    _ => Vector3.Zero,
                };
                positions[v] = new Vector3(p.X, p.Z, -p.Y);

                if (colOff >= 0)
                {
                    int co = v * stride + colOff;
                    colors[v] = new Vector4(
                        SrgbToLinear(bytes[co]), SrgbToLinear(bytes[co + 1]),
                        SrgbToLinear(bytes[co + 2]), bytes[co + 3] / 255f);
                }
                else colors[v] = Vector4.One;
            }

            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                int a = indices[t], b = indices[t + 1], c = indices[t + 2];
                if (a >= vc || b >= vc || c >= vc) continue;
                prim.AddTriangle(
                    (new VertexPosition(positions[a]), new VertexColor1(colors[a])),
                    (new VertexPosition(positions[b]), new VertexColor1(colors[b])),
                    (new VertexPosition(positions[c]), new VertexColor1(colors[c])));
            }
            verts += vc;
            tris += indices.Length / 3;
        }
    }
}
