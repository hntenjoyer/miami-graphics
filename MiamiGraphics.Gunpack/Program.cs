#nullable disable
using System.Text;
using MiamiGraphics.Gunpack.Services;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

string extractRoot = Environment.GetEnvironmentVariable("GUNPACK_EXTRACT")
    ?? Path.Combine(Path.GetTempPath(), "gunpack_analysis", "_extract");
string glbCache = Environment.GetEnvironmentVariable("GUNPACK_GLB")
    ?? Path.Combine(Path.GetTempPath(), "gunpack_analysis", "_glb");
string workRoot = Environment.GetEnvironmentVariable("GUNPACK_WORK")
    ?? Path.Combine(Path.GetTempPath(), "gunpack_analysis", "_work");

var studio = new TextureStudio(extractRoot, glbCache, workRoot);

if (args.Length >= 1 && (args[0] == "roundtrip" || args[0] == "roundtrip-pack"))
{
    Environment.Exit(RoundtripTest.Run(studio, extractRoot, args));
    return;
}

if (args.Length >= 2 && args[0] == "dump")
{
    Console.WriteLine(MiamiGraphics.Gunpack.Services.DrawableDump.Dump(args[1]));
    return;
}

if (args.Length >= 3 && args[0] == "attach-test")
{
    string tpack = args[1], tgun = args[2];
    string tdir = Path.Combine(extractRoot, tpack, tgun);
    var srcYdr = MiamiGraphics.Gunpack.Services.TextureStudio.PickSourceYdr(tdir);
    var ty = new CodeWalker.GameFiles.YdrFile();
    ty.Load(File.ReadAllBytes(srcYdr));
    var c = ty.Drawable.BoundingCenter;
    float topZ = ty.Drawable.BoundingBoxMax.Z;

    string kind = args.Length >= 4 ? args[3] : "plate";
    byte[] tpng = kind == "pendant" ? MakeStarPng() : MakeTestPng();
    var res = studio.Attach(tpack, tgun, tpng, kind,
        c.X, c.Y, topZ + 0.07f,
        0, -1, 0,
        0.14f, 0.18f, "brelok_test", glbSpace: false);
    Console.WriteLine(res.Ok ? $"ATTACH OK ({kind}): {string.Join(", ", res.Patched)}" : "ATTACH FAIL: " + res.Error);
    Environment.Exit(res.Ok ? 0 : 1);
    return;
}

static byte[] MakeStarPng()
{
    using var bmp = new System.Drawing.Bitmap(256, 256);
    using var g = System.Drawing.Graphics.FromImage(bmp);
    g.Clear(System.Drawing.Color.Transparent);
    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
    var pts = new System.Drawing.PointF[10];
    for (int i = 0; i < 10; i++)
    {
        double r = i % 2 == 0 ? 118 : 52, a = -Math.PI / 2 + i * Math.PI / 5;
        pts[i] = new System.Drawing.PointF(128 + (float)(r * Math.Cos(a)), 128 + (float)(r * Math.Sin(a)));
    }
    using var br = new System.Drawing.Drawing2D.LinearGradientBrush(
        new System.Drawing.Point(0, 0), new System.Drawing.Point(256, 256),
        System.Drawing.Color.Gold, System.Drawing.Color.OrangeRed);
    g.FillPolygon(br, pts);
    using var pen = new System.Drawing.Pen(System.Drawing.Color.Black, 7);
    g.DrawPolygon(pen, pts);
    using var ms = new MemoryStream();
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    return ms.ToArray();
}

static byte[] MakeTestPng()
{
    using var bmp = new System.Drawing.Bitmap(256, 256);
    using var g = System.Drawing.Graphics.FromImage(bmp);
    g.Clear(System.Drawing.Color.FromArgb(255, 255, 214, 0));
    g.FillEllipse(System.Drawing.Brushes.Black, 78, 60, 40, 40);
    g.FillEllipse(System.Drawing.Brushes.Black, 138, 60, 40, 40);
    using var f = new System.Drawing.Font("Segoe UI", 44, System.Drawing.FontStyle.Bold);
    var sf = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center };
    g.DrawString("MOPS", f, System.Drawing.Brushes.Magenta, 128, 140, sf);
    using var pen = new System.Drawing.Pen(System.Drawing.Color.Black, 10);
    g.DrawRectangle(pen, 5, 5, 246, 246);
    using var ms = new MemoryStream();
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    return ms.ToArray();
}

if (args.Length >= 3 && args[0] == "anim-gen")
{
    string apack = args[1], agun = args[2];
    string amode = args.Length >= 4 ? args[3] : "uv";
    byte[] png = MakeStarPng();
    var req = new AnimatedDetailService.GenRequest
    {
        SourceKind = "pendant", Png = png, Size = 0.12f, DepthFrac = 0.15f,
        AnimMode = amode, ScrollU = 1f, ScrollV = 0f,
        AxisX = 1f, AxisY = 0f, AxisZ = 0f, AmplitudeDeg = 20f, PeriodSec = 2.0f,
        AttachBone = "Gun_Main_Bone", Name = "mg_" + amode,
    };
    var res = studio.GenerateAnimatedDetail(apack, agun, req);
    Console.WriteLine($"[anim-gen] ok={res.Ok} model={res.DetailModel} dict={res.ClipDict} comp={res.ComponentName}");
    Console.WriteLine($"[anim-gen] files: {string.Join(", ", res.Files)}");
    Console.WriteLine($"[anim-gen] previewGlb={res.PreviewGlb}");

    string animDir = Path.Combine(workRoot, apack, agun, "_anim");
    int fail = 0;
    void Check(string label, Action a) { try { a(); Console.WriteLine($"  [OK] {label}"); } catch (Exception ex) { Console.WriteLine($"  [FAIL] {label}: {ex.Message}"); fail++; } }
    Check("ydr reload", () => { var y = new CodeWalker.GameFiles.YdrFile(); y.Load(File.ReadAllBytes(Path.Combine(animDir, res.DetailModel + ".ydr"))); if (y.Drawable?.Skeleton?.Bones?.Items?.Length != 2) throw new Exception("не 2 кости"); });
    Check("ycd reload", () => { var y = new CodeWalker.GameFiles.YcdFile(); CodeWalker.GameFiles.RpfFile.LoadResourceFile(y, File.ReadAllBytes(Path.Combine(animDir, res.ClipDict + ".ycd")), 46); if (y.ClipMapEntries.Length < 1) throw new Exception("нет клипов"); });
    Check("ytyp reload", () => { var y = new CodeWalker.GameFiles.YtypFile(); y.Load(File.ReadAllBytes(Path.Combine(animDir, res.ClipDict + ".ytyp"))); if (y.AllArchetypes.Length < 1) throw new Exception("нет архетипов"); });
    Check("glb exists", () => { if (res.PreviewGlb == null || !File.Exists(Path.Combine(animDir, res.PreviewGlb))) throw new Exception("нет превью glb"); });
    Environment.Exit(fail);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5210");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 256L * 1024 * 1024);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        ctx.Context.Response.Headers["Pragma"] = "no-cache";
    }
});

app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (ArgumentException ex) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { ok = false, error = ex.Message }); }
    catch (FileNotFoundException ex) { ctx.Response.StatusCode = 404; await ctx.Response.WriteAsJsonAsync(new { ok = false, error = ex.Message }); }
    catch (DirectoryNotFoundException ex) { ctx.Response.StatusCode = 404; await ctx.Response.WriteAsJsonAsync(new { ok = false, error = ex.Message }); }
    catch (Exception ex) { ctx.Response.StatusCode = 500; await ctx.Response.WriteAsJsonAsync(new { ok = false, error = ex.Message }); Console.WriteLine($"[500] {ex}"); }
});

app.MapGet("/api/packs", () => studio.ListPacks());

app.MapGet("/api/gun", (string pack, string gun) => studio.GetGunDetail(pack, gun));

app.MapGet("/api/glb", (string pack, string gun) =>
{
    string path = studio.GetGlbPath(pack, gun);
    return Results.File(path, "model/gltf-binary", enableRangeProcessing: true);
});

app.MapGet("/api/texture", (string pack, string gun, string name, int? size) =>
    Results.File(studio.GetTexturePng(pack, gun, name, size), "image/png"));

app.MapPost("/api/texture", async (HttpRequest req, string pack, string gun, string name) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    return Results.Json(studio.ReplaceTexture(pack, gun, name, ms.ToArray()));
});

app.MapPost("/api/attach", async (HttpRequest req, string pack, string gun, string name,
    float px, float py, float pz, float nx, float ny, float nz, float size,
    string kind, float? depth) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    return Results.Json(studio.Attach(pack, gun, ms.ToArray(), kind ?? "plate",
        px, py, pz, nx, ny, nz, size, depth ?? 0.15f, name));
});

app.MapPost("/api/attach-mesh", async (HttpRequest req, string pack, string gun, string name) =>
{
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var dto = System.Text.Json.JsonSerializer.Deserialize<AttachMeshDto>(ms.ToArray(),
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (dto == null) return Results.BadRequest(new { ok = false, error = "пустой запрос" });
    float[] F(string b64) { var by = Convert.FromBase64String(b64 ?? ""); var f = new float[by.Length / 4]; Buffer.BlockCopy(by, 0, f, 0, f.Length * 4); return f; }
    int[] I(string b64) { var by = Convert.FromBase64String(b64 ?? ""); var u = new int[by.Length / 4]; Buffer.BlockCopy(by, 0, u, 0, u.Length * 4); return u; }
    var png = Convert.FromBase64String(dto.Png);
    return Results.Json(studio.AttachRawMesh(pack, gun, png, F(dto.Pos), F(dto.Nrm), F(dto.Uv), I(dto.Idx), name));
});

app.MapGet("/api/accessories", (string pack, string gun) => studio.ListAccessories(pack, gun));

app.MapPost("/api/accessory/remove", (string pack, string gun, string name) =>
    Results.Json(studio.RemoveAccessory(pack, gun, name)));

app.MapPost("/api/reset", (string pack, string gun) => Results.Json(studio.Reset(pack, gun)));

app.MapGet("/api/export", (string pack, string gun) => Results.Json(studio.ExportInfo(pack, gun)));

app.MapPost("/api/anim", async (HttpRequest httpReq, string pack, string gun) =>
{
    using var ms = new MemoryStream();
    await httpReq.Body.CopyToAsync(ms);
    var dto = System.Text.Json.JsonSerializer.Deserialize<AnimDto>(ms.ToArray(),
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    if (dto == null) return Results.BadRequest(new { ok = false, error = "пустой запрос" });

    float[] F(string b64) { if (string.IsNullOrEmpty(b64)) return null; var by = Convert.FromBase64String(b64); var f = new float[by.Length / 4]; Buffer.BlockCopy(by, 0, f, 0, f.Length * 4); return f; }
    int[] I(string b64) { if (string.IsNullOrEmpty(b64)) return null; var by = Convert.FromBase64String(b64); var u = new int[by.Length / 4]; Buffer.BlockCopy(by, 0, u, 0, u.Length * 4); return u; }

    var req = new AnimatedDetailService.GenRequest
    {
        SourceKind = dto.SourceKind ?? "pendant",
        Png = Convert.FromBase64String(dto.Png ?? ""),
        Pos = F(dto.Pos), Nrm = F(dto.Nrm), Uv = F(dto.Uv), Idx = I(dto.Idx),
        Size = dto.Size ?? 0.12f,
        DepthFrac = dto.DepthFrac ?? 0.15f,
        AnimMode = dto.AnimMode ?? "uv",
        ScrollU = dto.ScrollU ?? 1f, ScrollV = dto.ScrollV ?? 0f,
        AxisX = dto.AxisX ?? 1f, AxisY = dto.AxisY ?? 0f, AxisZ = dto.AxisZ ?? 0f,
        AmplitudeDeg = dto.AmplitudeDeg ?? 20f,
        PeriodSec = dto.PeriodSec ?? 2.0f,
        AttachBone = string.IsNullOrWhiteSpace(dto.AttachBone) ? "Gun_Main_Bone" : dto.AttachBone,
        Name = string.IsNullOrWhiteSpace(dto.Name) ? "anim" : dto.Name,
    };
    return Results.Json(studio.GenerateAnimatedDetail(pack, gun, req));
});

app.MapGet("/api/anim-file", (string pack, string gun, string file) =>
{
    string path = studio.GetAnimFilePath(pack, gun, file);
    string ctype = file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ? "model/gltf-binary" : "application/octet-stream";
    return Results.File(path, ctype, enableRangeProcessing: true);
});

Console.WriteLine("[Gunpack] http://localhost:5210");
Console.WriteLine($"[Gunpack] extract: {extractRoot}");
Console.WriteLine($"[Gunpack] work:    {workRoot}");
app.Run();

public sealed class AttachMeshDto
{
    public string Pos { get; set; } = "";
    public string Nrm { get; set; } = "";
    public string Uv  { get; set; } = "";
    public string Idx { get; set; } = "";
    public string Png { get; set; } = "";
}

public sealed class AnimDto
{
    public string SourceKind { get; set; }
    public string Png { get; set; }
    public string Pos { get; set; }
    public string Nrm { get; set; }
    public string Uv { get; set; }
    public string Idx { get; set; }
    public float? Size { get; set; }
    public float? DepthFrac { get; set; }
    public string AnimMode { get; set; }
    public float? ScrollU { get; set; }
    public float? ScrollV { get; set; }
    public float? AxisX { get; set; }
    public float? AxisY { get; set; }
    public float? AxisZ { get; set; }
    public float? AmplitudeDeg { get; set; }
    public float? PeriodSec { get; set; }
    public string AttachBone { get; set; }
    public string Name { get; set; }
}
