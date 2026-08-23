#nullable disable
using System.Text.Json.Serialization;
using CodeWalker.GameFiles;
using CodeWalker.Utils;
using MiamiGraphics.Core.Services;

namespace MiamiGraphics.Gunpack.Services;

public sealed class TextureStudio
{
    private readonly string _extractRoot;
    private readonly string _glbCache;
    private readonly string _workRoot;
    private readonly object _gate = new();

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
                ?? throw new FileNotFoundException($"Нет .ydr в {dir}");

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
                foreach (var b in bonesArr)
                    if (!string.IsNullOrEmpty(b?.Name)) detail.BoneNames.Add(b.Name);

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
                if (!File.Exists(workGlb)) BuildGlb(work, workGlb);
                return workGlb;
            }

            string cached = Path.Combine(_glbCache, pack, gun + ".glb");
            if (File.Exists(cached)) return cached;

            Directory.CreateDirectory(Path.GetDirectoryName(cached));
            BuildGlb(OrigDir(pack, gun), cached);
            return cached;
        }
    }

    private static void BuildGlb(string gunDir, string outGlb)
    {
        string src = PickSourceYdr(gunDir)
            ?? throw new FileNotFoundException($"Нет .ydr в {gunDir}");
        var ytds = Directory.GetFiles(gunDir, "*.ytd")
            .OrderBy(p => p.Contains("_hi", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToList();
        bool ok = YdrToGltfConverter.ConvertAsync(src, outGlb, ytds).GetAwaiter().GetResult();
        if (!ok || !File.Exists(outGlb))
            throw new InvalidOperationException($"GLB конвертация не удалась для {src}");
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
            throw new FileNotFoundException($"Текстура '{name}' не найдена у {pack}/{gun}");
        }
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
                return new ReplaceResult { Ok = false, Error = $"Текстура '{name}' не найдена ни в одном файле" };

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
                return new ReplaceResult { Ok = false, Error = "не найдено подходящих .ydr" };
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
            if (patched.Count == 0) return new ReplaceResult { Ok = false, Error = "не найдено подходящих .ydr" };
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
                return new ReplaceResult { Ok = false, Error = $"3D-модель '{name}' не найдена" };
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
            return AnimatedDetailService.Generate(animDir, req);
        }
    }

    public string GetAnimFilePath(string pack, string gun, string file)
    {
        lock (_gate)
        {
            string safe = Path.GetFileName(Sanitize(file));
            string p = Path.Combine(WorkDir(pack, gun), "_anim", safe);
            if (!File.Exists(p)) throw new FileNotFoundException($"Нет файла _anim/{safe}");
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
            if (!Directory.Exists(work)) return new { ok = false, error = "Нет правок" };
            var files = Directory.GetFiles(work)
                .Where(f => !f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName).ToList();
            return new { ok = true, dir = work, files };
        }
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
        }
        return work;
    }

    private static string Sanitize(string part)
    {
        if (string.IsNullOrWhiteSpace(part) ||
            part.Contains("..") || part.Contains('/') || part.Contains('\\') || part.Contains(':'))
            throw new ArgumentException($"Недопустимое имя: '{part}'");
        return part;
    }

    private static IEnumerable<string> EnumerateResourceFiles(string dir)
        => Directory.GetFiles(dir)
            .Where(f => f.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f);

    internal static string PickSourceYdr(string dir)
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

        tex.Width = (ushort)w;
        tex.Height = (ushort)h;
        tex.Depth = 1;
        tex.Stride = (ushort)(w * 4);
        tex.Format = TextureFormat.D3DFMT_A8R8G8B8;
        tex.Levels = 1;
        tex.Data = new TextureData { FullData = bgra };
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
