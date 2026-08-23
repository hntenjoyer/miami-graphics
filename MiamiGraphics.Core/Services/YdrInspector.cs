#nullable disable
using CodeWalker.GameFiles;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace MiamiGraphics.Core.Services
{
    public static class YdrInspector
    {
        public static async Task<string> InspectAndSaveAsync(string ydrPath)
        {
            return await Task.Run(() =>
            {
                var sb = new StringBuilder();
                void W(string s) => sb.AppendLine(s);
                void H(string s) { W(""); W("=== " + s + " ==="); }

                try
                {
                    W("File: " + ydrPath);
                    W("Size: " + new FileInfo(ydrPath).Length + " bytes");
                    var data = File.ReadAllBytes(ydrPath);
                    var ydr = new YdrFile();
                    ydr.Load(data);
                    var d = ydr.Drawable;
                    if (d == null) { W("DRAWABLE NULL"); SaveReport(ydrPath, sb); return ydrPath + ".inspect.txt"; }

                    H("DRAWABLE");
                    W("Name: " + (d.Name ?? "<null>"));
                    W("BoundingSphereRadius: " + d.BoundingSphereRadius);
                    W("Has Skeleton: " + (d.Skeleton != null) + (d.Skeleton != null ? " (bones=" + (d.Skeleton.Bones?.Items?.Length ?? 0) + ")" : ""));
                    W("LightAttributes: " + (d.LightAttributes?.data_items?.Length ?? 0));

                    H("LOD MODELS");
                    W("High: " + (d.DrawableModels?.High?.Length ?? 0));
                    W("Med:  " + (d.DrawableModels?.Med?.Length ?? 0));
                    W("Low:  " + (d.DrawableModels?.Low?.Length ?? 0));
                    W("VLow: " + (d.DrawableModels?.VLow?.Length ?? 0));

                    H("EMBEDDED TEXTURES");
                    var td = d.ShaderGroup?.TextureDictionary;
                    var tex = td?.Textures?.data_items;
                    W("Count: " + (tex?.Length ?? 0));
                    if (tex != null)
                        foreach (var t in tex)
                            W(string.Format("  {0,-50} {1}x{2} fmt={3} mips={4}", t.Name, t.Width, t.Height, t.Format, t.Levels));

                    H("SHADERS");
                    var shaders = d.ShaderGroup?.Shaders?.data_items;
                    W("Count: " + (shaders?.Length ?? 0));
                    if (shaders != null)
                    {
                        for (int si = 0; si < shaders.Length; si++)
                        {
                            var sh = shaders[si];
                            W("");
                            W(string.Format("[Shader {0}] Name='{1}' FileName='{2}' RenderBucket={3}",
                                si, sh.Name, sh.FileName, sh.RenderBucket));
                            var pl = sh.ParametersList;
                            if (pl == null) { W("  ParametersList=null"); continue; }
                            W("  Params (" + (pl.Parameters?.Length ?? 0) + "):");
                            if (pl.Parameters != null && pl.Hashes != null)
                            {
                                for (int pi = 0; pi < pl.Parameters.Length; pi++)
                                {
                                    var p = pl.Parameters[pi];
                                    var hash = pi < pl.Hashes.Length ? pl.Hashes[pi].ToString() : "?";
                                    string val;
                                    if (p?.Data is TextureBase tb)
                                        val = "Texture name='" + (tb.Name ?? "<null>") + "' nameHash=" + tb.NameHash;
                                    else if (p?.Data is global::System.Numerics.Vector4[] va)
                                    {
                                        val = "Vector4[" + va.Length + "]";
                                        if (va.Length > 0)
                                            val += " first=(" + va[0].X + "," + va[0].Y + "," + va[0].Z + "," + va[0].W + ")";
                                    }
                                    else if (p?.Data is global::System.Numerics.Vector4 v4)
                                        val = "Vector4 (" + v4.X + "," + v4.Y + "," + v4.Z + "," + v4.W + ")";
                                    else if (p?.Data != null)
                                        val = p.Data.GetType().Name;
                                    else
                                        val = "<null>";
                                    W("    [" + pi + "] hash=" + hash + " -> " + val);
                                }
                            }
                        }
                    }

                    H("GEOMETRY (High LOD)");
                    var models = d.DrawableModels?.High;
                    if (models != null)
                    {
                        for (int mi = 0; mi < models.Length; mi++)
                        {
                            var m = models[mi];
                            W("");
                            W("[Model " + mi + "] Geoms=" + (m.Geometries?.Length ?? 0) + " HasSkin=" + m.HasSkin);
                            if (m.Geometries == null) continue;
                            for (int gi = 0; gi < m.Geometries.Length; gi++)
                            {
                                var g = m.Geometries[gi];
                                W(string.Format("  [Geom {0}] ShaderID={1} Verts={2} Indices={3} Stride={4}",
                                    gi, g.ShaderID, g.VertexData?.VertexCount ?? 0,
                                    g.IndexBuffer?.Indices?.Length ?? 0, g.VertexData?.VertexStride ?? 0));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    W("");
                    W("!!! EXCEPTION !!!");
                    W(ex.ToString());
                }

                SaveReport(ydrPath, sb);
                return ydrPath + ".inspect.txt";
            });
        }

        private static void SaveReport(string ydrPath, StringBuilder sb)
        {
            File.WriteAllText(ydrPath + ".inspect.txt", sb.ToString());
        }
    }
}
