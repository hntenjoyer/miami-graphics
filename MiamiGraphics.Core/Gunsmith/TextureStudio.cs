#nullable disable
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using MiamiGraphics.Core.Services;

using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.Gunsmith;

public sealed class TextureStudio
{
    private readonly string _extractRoot;
    private readonly string _glbCache;
    private readonly string _workRoot;
    private readonly object _gate = new();

    internal static readonly JsonSerializerOptions AnimReqJson =
        new() { IncludeFields = true };
    private static readonly JsonSerializerOptions GlassJson =
        new() { WriteIndented = false };

    public TextureStudio(string extractRoot, string glbCache, string workRoot)
    {
        _extractRoot = extractRoot;
        _glbCache = glbCache;
        _workRoot = workRoot;
        Directory.CreateDirectory(_workRoot);
    }

    public List<PackInfo> ListPacks()
    {
        var result = new List<PackInfo>();
        if (!Directory.Exists(_extractRoot)) return result;
        foreach (var packDir in Directory.GetDirectories(_extractRoot).OrderBy(x => x))
        {
            var pack = new PackInfo { Name = Path.GetFileName(packDir) };
            foreach (var gunDir in Directory.GetDirectories(packDir).OrderBy(x => x))
            {
                string gun = Path.GetFileName(gunDir);
                if (Directory.GetFiles(gunDir, "*.ydr").Length == 0) continue;
                pack.Guns.Add(new GunBrief
                {
                    Name = gun,
                    Edited = Directory.Exists(WorkDir(pack.Name, gun)),
                });
            }
            if (pack.Guns.Count > 0) result.Add(pack);
        }
        return result;
    }

    public GunDetail GetGunDetail(string pack, string gun)
    {
        lock (_gate)
        {
            string dir = ActiveDir(pack, gun);
            string src = PickSourceYdr(dir)
                ?? throw new FileNotFoundException(Loc.T("error.noYdrIn", ("dir", dir)));

            var ydr = new YdrFile();
            ydr.Load(File.ReadAllBytes(src));
            var d = ydr.Drawable;

            var detail = new GunDetail
            {
                Pack = pack,
                Gun = gun,
                SourceYdr = Path.GetFileName(src),
                Edited = Directory.Exists(WorkDir(pack, gun)),
                Files = Directory.GetFiles(dir)
                    .Select(Path.GetFileName)
                    .Where(n => n.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)
                             || n.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n).ToList(),
                Bones = d?.Skeleton?.Bones?.Items?.Length ?? 0,
            };

            var bonesArr = d?.Skeleton?.Bones?.Items;
            if (bonesArr != null)
                foreach (var bn in bonesArr)
                    if (!string.IsNullOrEmpty(bn?.Name)) detail.BoneNames.Add(bn.Name);

            var seenTex = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var resFile in EnumerateResourceFiles(dir))
            {
                var td = LoadDict(resFile);
                var items = td?.Textures?.data_items;
                if (items == null) continue;
                foreach (var t in items)
                {
                    if (t?.Name == null || !seenTex.Add(t.Name)) continue;
                    detail.Textures.Add(new TexInfo
                    {
                        Name = t.Name,
                        Width = t.Width,
                        Height = t.Height,
                        Format = t.Format.ToString(),
                        Mips = t.Levels,
                        Source = Path.GetFileName(resFile),
                    });
                }
            }

            var shArr = d?.ShaderGroup?.Shaders?.data_items;
            if (shArr != null)
                for (int i = 0; i < shArr.Length; i++)
                {
                    var sh = shArr[i];
                    var si = new ShaderInfo { Index = i, Bucket = sh.RenderBucket };
                    var pl = sh.ParametersList;
                    if (pl?.Parameters != null && pl.Hashes != null)
                        for (int p = 0; p < pl.Parameters.Length; p++)
                        {
                            if (pl.Parameters[p]?.Data is not TextureBase tb ||
                                string.IsNullOrEmpty(tb.Name)) continue;
                            string hash = pl.Hashes[p].ToString().ToLowerInvariant();
                            if (si.Diffuse == null && hash.Contains("diffuse")) si.Diffuse = tb.Name;
                            else if (si.Normal == null && (hash.Contains("bump") || hash.Contains("normal"))) si.Normal = tb.Name;
                            else if (si.Spec == null && hash.Contains("spec")) si.Spec = tb.Name;
                            si.Diffuse ??= tb.Name;
                        }
                    detail.Shaders.Add(si);
                }

            var models = d?.DrawableModels?.High;
            if (models != null)
                foreach (var m in models)
                    if (m?.Geometries != null)
                        foreach (var g in m.Geometries)
                        {
                            detail.Geoms++;
                            detail.Verts += g.VertexData?.VertexCount ?? 0;
                        }

            detail.Glass = ReadGlass(dir);

            return detail;
        }
    }

    public string GetGlbPath(string pack, string gun)
    {
        lock (_gate)
        {
            string work = WorkDir(pack, gun);
            if (Directory.Exists(work))
            {
                string workGlb = Path.Combine(work, "_preview.glb");
                if (IsPreviewStale(work, workGlb)) BuildGlb(work, workGlb);
                return workGlb;
            }

            string cached = Path.Combine(_glbCache, pack, gun + ".glb");
            if (File.Exists(cached)) return cached;

            Directory.CreateDirectory(Path.GetDirectoryName(cached));
            BuildGlb(OrigDir(pack, gun), cached);
            return cached;
        }
    }

    public string GetGameLikeGlbPath(string pack, string gun)
    {
        lock (_gate)
        {
            string work = WorkDir(pack, gun);
            if (!Directory.Exists(work)) return GetGlbPathNoLock(pack, gun);

            string glb = Path.Combine(work, "_preview_game.glb");
            if (IsPreviewStale(work, glb, includeGameLayers: true)) BuildWorkGlb(work, glb);
            return glb;
        }
    }

    private string GetGlbPathNoLock(string pack, string gun)
    {
        string cached = Path.Combine(_glbCache, pack, gun + ".glb");
        if (File.Exists(cached)) return cached;
        Directory.CreateDirectory(Path.GetDirectoryName(cached));
        BuildGlb(OrigDir(pack, gun), cached);
        return cached;
    }

    private static bool IsPreviewStale(string workDir, string glbPath)
        => IsPreviewStale(workDir, glbPath, includeGameLayers: false);

    private static bool IsPreviewStale(string workDir, string glbPath, bool includeGameLayers)
    {
        if (!File.Exists(glbPath)) return true;
        var glbTime = File.GetLastWriteTimeUtc(glbPath);

        foreach (var f in EnumerateResourceFiles(workDir))
            if (File.GetLastWriteTimeUtc(f) > glbTime) return true;

        if (!includeGameLayers) return false;

        string glass = Path.Combine(workDir, "_glass.json");
        if (File.Exists(glass) && File.GetLastWriteTimeUtc(glass) > glbTime) return true;

        string animDir = Path.Combine(workDir, "_anim");
        if (Directory.Exists(animDir))
            foreach (var f in Directory.GetFiles(animDir))
                if (File.GetLastWriteTimeUtc(f) > glbTime) return true;

        return false;
    }

    private void BuildWorkGlb(string workDir, string outGlb)
    {
        var glass = ReadGlass(workDir);
        string animYdr = FindAnimDetailYdr(workDir);

        if (!glass.Any && animYdr is null) { BuildGlb(workDir, outGlb); return; }

        string tmp = Path.Combine(Path.GetTempPath(), "mg_glbprev_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        try
        {
            foreach (var file in EnumerateResourceFiles(workDir))
            {
                var bytes = File.ReadAllBytes(file);
                if (glass.Any)
                {
                    try
                    {
                        var t = file.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)
                            ? GlassService.TransformYdr(bytes, glass)
                            : GlassService.TransformYtd(bytes, glass);
                        if (t != null) bytes = t;
                    }
                    catch {}
                }
                File.WriteAllBytes(Path.Combine(tmp, Path.GetFileName(file)), bytes);
            }

            BuildGlb(tmp, outGlb, animYdr, animYdr is null ? null : BuildAnimTag(workDir));
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    private static string FindAnimDetailYdr(string workDir)
    {
        string animDir = Path.Combine(workDir, "_anim");
        if (!Directory.Exists(animDir)) return null;
        return Directory.GetFiles(animDir, "*.ydr").FirstOrDefault();
    }

    private static string BuildAnimTag(string workDir)
    {
        try
        {
            string req = Path.Combine(workDir, "_anim", "_request.json");
            if (!File.Exists(req)) return null;
            var r = JsonSerializer.Deserialize<AnimatedDetailService.GenRequest>(
                File.ReadAllBytes(req), AnimReqJson);
            if (r == null) return null;
            string mode = string.IsNullOrWhiteSpace(r.AnimMode) ? "uv" : r.AnimMode.Trim().ToLowerInvariant();
            string F(float v) => v.ToString("0.###", global::System.Globalization.CultureInfo.InvariantCulture);
            return $"MGANIM~{mode}~{F(r.ScrollU)}~{F(r.ScrollV)}~{F(r.PeriodSec)}~{F(r.AxisX)}~{F(r.AxisY)}~{F(r.AxisZ)}~{F(r.AmplitudeDeg)}";
        }
        catch { return null; }
    }

    private static void BuildGlb(string gunDir, string outGlb, string extraYdr = null, string animTag = null)
    {
        string src = PickSourceYdr(gunDir)
            ?? throw new FileNotFoundException(Loc.T("error.noYdrIn", ("dir", gunDir)));
        var ytds = Directory.GetFiles(gunDir, "*.ytd")
            .OrderBy(p => IsHiVariant(p) ? 1 : 0)
            .ToList();
        var extras = string.IsNullOrEmpty(extraYdr) ? null : new List<string> { extraYdr };
        bool skipBake = File.Exists(Path.Combine(gunDir, PaletteBakedMarker));
        bool ok = YdrToGltfConverter.ConvertAsync(src, outGlb, ytds, extras, animTag, skipBake).GetAwaiter().GetResult();
        if (!ok || !File.Exists(outGlb))
            throw new InvalidOperationException(Loc.T("error.glbConvertFailed", ("file", src)));
    }

    public byte[] GetTexturePng(string pack, string gun, string name, int? size = null)
    {
        lock (_gate)
        {
            foreach (var file in EnumerateResourceFiles(ActiveDir(pack, gun)))
            {
                var td = LoadDict(file);
                var tex = FindTexture(td, name);
                if (tex == null) continue;
                var png = TextureCodec.ToPng(tex);
                return size is > 0 ? TextureCodec.ResizePng(png, size.Value) : png;
            }
            throw new FileNotFoundException(Loc.T("error.textureNotFoundInGun", ("name", name), ("pack", pack), ("gun", gun)));
        }
    }

    public const string PaletteBakedMarker = "_vanilla_palette.json";

    public static int BakeVanillaPalettes(string gunDir)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ydrPath in Directory.GetFiles(gunDir, "*.ydr"))
        {
            Drawable d;
            try
            {
                var ydr = new YdrFile();
                ydr.Load(File.ReadAllBytes(ydrPath));
                d = ydr.Drawable;
            }
            catch { continue; }
            var shArr = d?.ShaderGroup?.Shaders?.data_items;
            if (shArr == null) continue;
            foreach (var sh in shArr)
            {
                var pl = sh?.ParametersList;
                if (pl?.Parameters == null || pl.Hashes == null) continue;
                string diffuse = null, palette = null;
                for (int p = 0; p < pl.Parameters.Length; p++)
                {
                    if (pl.Parameters[p]?.Data is not TextureBase tb || string.IsNullOrEmpty(tb.Name)) continue;
                    var hash = pl.Hashes[p].ToString().ToLowerInvariant();
                    if (tb.Name.EndsWith("pal", StringComparison.OrdinalIgnoreCase)) palette ??= tb.Name;
                    else if (hash.Contains("diffuse")) diffuse ??= tb.Name;
                }
                if (diffuse != null && palette != null && !pairs.ContainsKey(diffuse))
                    pairs[diffuse] = palette;
            }
        }
        if (pairs.Count == 0) return 0;

        byte[] ReadPng(string name)
        {
            foreach (var file in EnumerateResourceFiles(gunDir))
            {
                var tex = FindTexture(LoadDict(file), name);
                if (tex != null) return TextureCodec.ToPng(tex);
            }
            return null;
        }

        int baked = 0;
        var patched = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (diffuse, palette) in pairs)
        {
            var dPng = ReadPng(diffuse);
            var pPng = ReadPng(palette);
            if (dPng == null || pPng == null) continue;
            try
            {
                patched[diffuse] = Services.YdrToGltfConverter.BakePaletteTint(dPng, pPng, 0);
                baked++;
            }
            catch {}
        }
        if (baked == 0) return 0;

        foreach (var palette in pairs.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var pPng = ReadPng(palette);
            if (pPng == null) continue;
            var (bgra, w, h) = TextureCodec.PngToBgra(pPng);
            for (int i = 0; i < bgra.Length; i++) bgra[i] = 255;
            using var ms = new global::System.IO.MemoryStream();
            using (var bmp = new global::System.Drawing.Bitmap(w, h,
                global::System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                var rect = new global::System.Drawing.Rectangle(0, 0, w, h);
                var bd = bmp.LockBits(rect, global::System.Drawing.Imaging.ImageLockMode.WriteOnly,
                    global::System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                global::System.Runtime.InteropServices.Marshal.Copy(bgra, 0, bd.Scan0, bgra.Length);
                bmp.UnlockBits(bd);
                bmp.Save(ms, global::System.Drawing.Imaging.ImageFormat.Png);
            }
            patched[palette] = ms.ToArray();
        }

        var pending = new List<(string Path, byte[] Bytes)>();
        foreach (var file in EnumerateResourceFiles(gunDir))
        {
            bool touched = false;
            if (file.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
            {
                var ydr = new YdrFile();
                ydr.Load(File.ReadAllBytes(file));
                foreach (var (name, png) in patched)
                {
                    var (bgra, w, h) = TextureCodec.PngToBgra(png);
                    touched |= MutateTexture(ydr.Drawable?.ShaderGroup?.TextureDictionary, name, bgra, w, h);
                }
                if (touched) pending.Add((file, ydr.Save()));
            }
            else
            {
                var ytd = new YtdFile();
                ytd.Load(File.ReadAllBytes(file));
                foreach (var (name, png) in patched)
                {
                    var (bgra, w, h) = TextureCodec.PngToBgra(png);
                    touched |= MutateTexture(ytd.TextureDict, name, bgra, w, h);
                }
                if (touched) pending.Add((file, ytd.Save()));
            }
        }
        foreach (var (path, bytes) in pending) File.WriteAllBytes(path, bytes);

        File.WriteAllText(Path.Combine(gunDir, PaletteBakedMarker),
            JsonSerializer.Serialize(new { baked = pairs.Keys.ToArray(), palettes = pairs.Values.Distinct().ToArray() }));
        return baked;
    }

    public ReplaceResult ReplaceTexture(string pack, string gun, string name, byte[] pngBytes)
    {
        lock (_gate)
        {
            var (bgra, w, h) = TextureCodec.PngToBgra(pngBytes);
            string work = EnsureWorkCopy(pack, gun);

            var pending = new List<(string Path, byte[] Bytes)>();
            foreach (var file in EnumerateResourceFiles(work))
            {
                if (file.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
                {
                    var ydr = new YdrFile();
                    ydr.Load(File.ReadAllBytes(file));
                    if (MutateTexture(ydr.Drawable?.ShaderGroup?.TextureDictionary, name, bgra, w, h))
                        pending.Add((file, ydr.Save()));
                }
                else
                {
                    var ytd = new YtdFile();
                    ytd.Load(File.ReadAllBytes(file));
                    if (MutateTexture(ytd.TextureDict, name, bgra, w, h))
                        pending.Add((file, ytd.Save()));
                }
            }

            if (pending.Count == 0)
                return new ReplaceResult { Ok = false, Error = Loc.T("error.textureNotFoundAnywhere", ("name", name)) };

            var patched = new List<string>();
            foreach (var (path, bytes) in pending)
            {
                File.WriteAllBytes(path, bytes);
                patched.Add(Path.GetFileName(path));
            }

            string glb = Path.Combine(work, "_preview.glb");
            BuildGlb(work, glb);

            return new ReplaceResult { Ok = true, Patched = patched, Width = w, Height = h };
        }
    }

    public ReplaceResult Attach(string pack, string gun, byte[] png, string kind,
        float px, float py, float pz, float nx, float ny, float nz,
        float size, float depthFrac, string name, bool glbSpace = true)
    {
        lock (_gate)
        {
            string work = EnsureWorkCopy(pack, gun);
            var patched = string.Equals(kind, "pendant", StringComparison.OrdinalIgnoreCase)
                ? AccessoryService.AttachPendant(work, png, px, py, pz, nx, ny, nz, size, depthFrac, name, glbSpace)
                : AccessoryService.AttachQuad(work, png, px, py, pz, nx, ny, nz, size, name, glbSpace);
            if (patched.Count == 0)
                return new ReplaceResult { Ok = false, Error = Loc.T("error.noSuitableYdr") };
            string glb = Path.Combine(work, "_preview.glb");
            BuildGlb(work, glb);
            return new ReplaceResult { Ok = true, Patched = patched };
        }
    }

    public ReplaceResult AttachRawMesh(string pack, string gun, byte[] png,
        float[] pos, float[] nrm, float[] uv, int[] idx, string name)
    {
        lock (_gate)
        {
            string work = EnsureWorkCopy(pack, gun);
            var patched = AccessoryService.AttachRawMesh(work, png, pos, nrm, uv, idx, name);
            if (patched.Count == 0) return new ReplaceResult { Ok = false, Error = Loc.T("error.noSuitableYdr") };
            BuildGlb(work, Path.Combine(work, "_preview.glb"));
            return new ReplaceResult { Ok = true, Patched = patched };
        }
    }

    public List<string> ListAccessories(string pack, string gun)
    {
        lock (_gate) { return AccessoryService.ListAccessories(ActiveDir(pack, gun)); }
    }

    public ReplaceResult RemoveAccessory(string pack, string gun, string name)
    {
        lock (_gate)
        {
            string work = EnsureWorkCopy(pack, gun);
            var patched = AccessoryService.RemoveAccessory(work, name);
            if (patched.Count == 0)
                return new ReplaceResult { Ok = false, Error = Loc.T("error.model3dNotFound", ("name", name)) };
            BuildGlb(work, Path.Combine(work, "_preview.glb"));
            return new ReplaceResult { Ok = true, Patched = patched };
        }
    }

    public AnimatedDetailService.GenResult GenerateAnimatedDetail(
        string pack, string gun, AnimatedDetailService.GenRequest req)
    {
        lock (_gate)
        {
            string work = EnsureWorkCopy(pack, gun);
            if (string.IsNullOrWhiteSpace(req.WeaponModel))
            {
                string src = PickSourceYdr(work);
                if (src != null)
                {
                    var n = Path.GetFileNameWithoutExtension(src);
                    if (n.EndsWith("_hi", StringComparison.OrdinalIgnoreCase)) n = n[..^3];
                    req.WeaponModel = n;
                }
            }
            string animDir = Path.Combine(work, "_anim");
            var res = AnimatedDetailService.Generate(animDir, req);

            try
            {
                File.WriteAllText(
                    Path.Combine(animDir, "_request.json"),
                    JsonSerializer.Serialize(req, AnimReqJson),
                    new UTF8Encoding(false));
            }
            catch {}

            return res;
        }
    }

    public string GetAnimFilePath(string pack, string gun, string file)
    {
        lock (_gate)
        {
            string safe = Path.GetFileName(Sanitize(file));
            string p = Path.Combine(WorkDir(pack, gun), "_anim", safe);
            if (!File.Exists(p)) throw new FileNotFoundException(Loc.T("error.animFileMissing", ("file", safe)));
            return p;
        }
    }

    public object Reset(string pack, string gun)
    {
        lock (_gate)
        {
            string work = WorkDir(pack, gun);
            if (Directory.Exists(work)) Directory.Delete(work, recursive: true);
            return new { ok = true };
        }
    }

    public object ExportInfo(string pack, string gun)
    {
        lock (_gate)
        {
            string work = WorkDir(pack, gun);
            if (!Directory.Exists(work)) return new { ok = false, error = Loc.T("error.noEdits") };
            var files = Directory.GetFiles(work)
                .Where(f => !f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName).ToList();
            return new { ok = true, dir = work, files };
        }
    }

    public GlassState SetGlass(string pack, string gun, string[] texNames, bool on, float opacity, string color)
    {
        lock (_gate)
        {
            string work = EnsureWorkCopy(pack, gun);
            var st = ReadGlass(work);
            foreach (var raw in texNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (on) st.Textures[raw] = new GlassTex { Opacity = opacity, Color = color ?? "#7fdfff" };
                else st.Textures.Remove(raw);
            }
            WriteGlass(work, st);
            return st;
        }
    }

    public GlassState GetGlassState(string pack, string gun)
    {
        lock (_gate) { return ReadGlass(ActiveDir(pack, gun)); }
    }

    public Dictionary<string, byte[]> BuildInstallFiles(string pack, string gun)
    {
        lock (_gate)
        {
            string dir = ActiveDir(pack, gun);
            var glass = ReadGlass(dir);
            var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in EnumerateResourceFiles(dir))
            {
                var bytes = File.ReadAllBytes(file);
                if (glass.Any)
                {
                    try
                    {
                        var t = file.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)
                            ? GlassService.TransformYdr(bytes, glass)
                            : GlassService.TransformYtd(bytes, glass);
                        if (t != null) bytes = t;
                    }
                    catch {}
                }
                map[Path.GetFileName(file)] = bytes;
            }
            return map;
        }
    }

    private static GlassState ReadGlass(string dir)
    {
        try
        {
            string p = Path.Combine(dir, "_glass.json");
            if (File.Exists(p))
                return JsonSerializer.Deserialize<GlassState>(File.ReadAllText(p)) ?? new GlassState();
        }
        catch { }
        return new GlassState();
    }

    private static void WriteGlass(string dir, GlassState st)
    {
        string p = Path.Combine(dir, "_glass.json");
        if (st == null || !st.Any) { try { if (File.Exists(p)) File.Delete(p); } catch { } return; }
        File.WriteAllText(p, JsonSerializer.Serialize(st, GlassJson));
    }

    private string OrigDir(string pack, string gun) => Path.Combine(_extractRoot, Sanitize(pack), Sanitize(gun));
    private string WorkDir(string pack, string gun) => Path.Combine(_workRoot, Sanitize(pack), Sanitize(gun));

    private string ActiveDir(string pack, string gun)
    {
        string work = WorkDir(pack, gun);
        return Directory.Exists(work) ? work : OrigDir(pack, gun);
    }

    private string EnsureWorkCopy(string pack, string gun)
    {
        string work = WorkDir(pack, gun);
        if (!Directory.Exists(work))
        {
            string orig = OrigDir(pack, gun);
            Directory.CreateDirectory(work);
            foreach (var f in Directory.GetFiles(orig))
                if (!f.EndsWith("_summary.json", StringComparison.OrdinalIgnoreCase))
                    File.Copy(f, Path.Combine(work, Path.GetFileName(f)), overwrite: true);
            string origAnim = Path.Combine(orig, "_anim");
            if (Directory.Exists(origAnim))
            {
                string workAnim = Path.Combine(work, "_anim");
                Directory.CreateDirectory(workAnim);
                foreach (var f in Directory.GetFiles(origAnim))
                    File.Copy(f, Path.Combine(workAnim, Path.GetFileName(f)), overwrite: true);
            }
        }
        return work;
    }

    private static string Sanitize(string part)
    {
        if (string.IsNullOrWhiteSpace(part) ||
            part.Contains("..") || part.Contains('/') || part.Contains('\\') || part.Contains(':'))
            throw new ArgumentException(Loc.T("error.invalidName", ("name", part)));
        return part;
    }

    private static bool IsHiVariant(string path)
    {
        var n = Path.GetFileNameWithoutExtension(path);
        return n.EndsWith("_hi", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith("+hi", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateResourceFiles(string dir)
        => Directory.GetFiles(dir)
            .Where(f => f.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => IsHiVariant(f) ? 0 : 1)
            .ThenBy(f => f);

    public static string PickSourceYdr(string dir)
    {
        var ydrs = Directory.GetFiles(dir, "*.ydr");
        return ydrs.FirstOrDefault(f =>
                   {
                       var n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                       return n.EndsWith("_hi") && !n.Contains("_mag") && !n.Contains("_sight");
                   })
            ?? ydrs.FirstOrDefault(f =>
                   {
                       var n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                       return !n.EndsWith("_hi") && !n.Contains("_mag") && !n.Contains("_sight");
                   })
            ?? ydrs.FirstOrDefault();
    }

    private static TextureDictionary LoadDict(string file)
    {
        try
        {
            if (file.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase))
            {
                var ydr = new YdrFile();
                ydr.Load(File.ReadAllBytes(file));
                return ydr.Drawable?.ShaderGroup?.TextureDictionary;
            }
            var ytd = new YtdFile();
            ytd.Load(File.ReadAllBytes(file));
            return ytd.TextureDict;
        }
        catch { return null; }
    }

    private static Texture FindTexture(TextureDictionary td, string name)
        => td?.Textures?.data_items?
            .FirstOrDefault(t => string.Equals(t?.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool MutateTexture(TextureDictionary td, string name, byte[] bgra, int w, int h)
    {
        var tex = FindTexture(td, name);
        if (tex == null) return false;

        GameTextureWriter.Apply(tex, bgra, w, h);
        return true;
    }
}

public sealed class PackInfo
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("guns")] public List<GunBrief> Guns { get; set; } = new();
}

public sealed class GunBrief
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("edited")] public bool Edited { get; set; }
}

public sealed class GunDetail
{
    [JsonPropertyName("pack")] public string Pack { get; set; }
    [JsonPropertyName("gun")] public string Gun { get; set; }
    [JsonPropertyName("sourceYdr")] public string SourceYdr { get; set; }
    [JsonPropertyName("edited")] public bool Edited { get; set; }
    [JsonPropertyName("files")] public List<string> Files { get; set; } = new();
    [JsonPropertyName("bones")] public int Bones { get; set; }
    [JsonPropertyName("boneNames")] public List<string> BoneNames { get; set; } = new();
    [JsonPropertyName("geoms")] public int Geoms { get; set; }
    [JsonPropertyName("verts")] public int Verts { get; set; }
    [JsonPropertyName("textures")] public List<TexInfo> Textures { get; set; } = new();
    [JsonPropertyName("shaders")] public List<ShaderInfo> Shaders { get; set; } = new();
    [JsonPropertyName("glass")] public GlassState Glass { get; set; }
}

public sealed class TexInfo
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("format")] public string Format { get; set; }
    [JsonPropertyName("mips")] public int Mips { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; }
}

public sealed class ShaderInfo
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("bucket")] public int Bucket { get; set; }
    [JsonPropertyName("diffuse")] public string Diffuse { get; set; }
    [JsonPropertyName("normal")] public string Normal { get; set; }
    [JsonPropertyName("spec")] public string Spec { get; set; }
}

public sealed class ReplaceResult
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("error")] public string Error { get; set; }
    [JsonPropertyName("patched")] public List<string> Patched { get; set; } = new();
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}
