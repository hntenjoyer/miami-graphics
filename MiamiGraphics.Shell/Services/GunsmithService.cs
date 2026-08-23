using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.Gunsmith;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Shell.Admin;

namespace MiamiGraphics.Shell.Services;

public sealed class GunsmithService
{
    public const string CustomPack = "_custom";
    public const string VanillaPack = "_vanilla";

    private readonly IGunpackRepository _packs;
    private readonly SupabaseClient _supabase;
    private readonly SupabaseCustomGunRepository _customRepo;
    private readonly IRemoteStorage _remoteStorage;
    private readonly ISelectedGunsInstaller _selectedGuns;
    private readonly PackZipCache _packZip;
    private readonly TextureStudio _studio;
    private readonly Func<Task<string>> _resolveGtaPath;

    private readonly HashSet<string> _consumedFlowSessions = new(StringComparer.Ordinal);
    private readonly object _flowLock = new();

    private const int FreeSlots = 5;
    private static readonly ISelectedGunsInstaller.EmitProgress NoopEmit = (_, _, _, _) => { };

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string phase, int pct)> _progress = new();
    private static string PKey(string pack, string gun) => pack + "|" + gun;
    private void SetProgress(string pack, string gun, string phase, int pct) => _progress[PKey(pack, gun)] = (phase, pct);

    private void DumpAttachCrash(string pack, string gun, byte[] png, IReadOnlyDictionary<string, string> query, Exception ex)
    {
        try
        {
            var root = Path.Combine(CacheRoot, "_crash");
            Directory.CreateDirectory(root);
            foreach (var old in Directory.GetDirectories(root).OrderByDescending(x => x, StringComparer.Ordinal).Skip(2))
                try { Directory.Delete(old, recursive: true); } catch { }
            var dir = Path.Combine(root, DateTime.Now.ToString("MMdd_HHmmss"));
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "attach.png"), png);
            File.WriteAllText(Path.Combine(dir, "params.txt"),
                string.Join(Environment.NewLine, query.Select(kv => kv.Key + "=" + kv.Value))
                + Environment.NewLine + Environment.NewLine + ex);
            foreach (var f in ResourceFiles(ActiveDir(pack, gun)))
                File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), overwrite: true);
        }
        catch {}
    }

    private static void LogErr(string where, Exception ex)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiamiGraphics", "gunsmith-api.log");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} ERR {where}: {ex}{Environment.NewLine}");
        }
        catch {}
    }
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private static readonly JsonSerializerOptions AnimReqJson = new() { IncludeFields = true };

    private static string CacheRoot => MiamiGraphics.Core.System.AppDataRoot.Dir("gunsmith");

    private string ExtractRoot => Path.Combine(CacheRoot, "extract");
    private string WorkRoot    => Path.Combine(CacheRoot, "work");
    private string GlbRoot     => Path.Combine(CacheRoot, "glb");

    public GunsmithService(
        IGunpackRepository packs,
        SupabaseClient supabase,
        SupabaseCustomGunRepository customRepo,
        IRemoteStorage remoteStorage,
        ISelectedGunsInstaller selectedGuns,
        PackZipCache packZip,
        Func<Task<string>> resolveGtaPath)
    {
        _packs = packs;
        _supabase = supabase;
        _customRepo = customRepo;
        _remoteStorage = remoteStorage;
        _selectedGuns = selectedGuns;
        _packZip = packZip;
        _resolveGtaPath = resolveGtaPath;
        Directory.CreateDirectory(ExtractRoot);
        Directory.CreateDirectory(WorkRoot);
        Directory.CreateDirectory(GlbRoot);
        _studio = new TextureStudio(ExtractRoot, GlbRoot, WorkRoot);
    }

    public readonly record struct ApiResponse(int Status, string ContentType, byte[] Body)
    {
        public static ApiResponse Ok(byte[] body, string ct) => new(200, ct, body);
        public static ApiResponse JsonOk(string json) => new(200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(json));
        public static ApiResponse Png(byte[] body) => new(200, "image/png", body);
        public static ApiResponse Glb(byte[] body) => new(200, "model/gltf-binary", body);
        public static ApiResponse Error(int status, string message) =>
            new(status, "application/json; charset=utf-8",
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { ok = false, error = message }, Json)));
    }

    public async Task<ApiResponse> HandleAsync(
        string method, string path, IReadOnlyDictionary<string, string> query, byte[] body, CancellationToken ct)
    {
        try
        {
            string P(string k) => query.TryGetValue(k, out var v) ? v : "";
            switch (path)
            {
                case "/api/progress":
                {
                    var pr = _progress.TryGetValue(PKey(P("pack"), P("gun")), out var v) ? v : (phase: "idle", pct: 0);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(new { phase = pr.phase, pct = pr.pct }, Json));
                }

                case "/api/packs":
                    return ApiResponse.JsonOk(await CatalogJsonAsync(ct));

                case "/api/gun":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(_studio.GetGunDetail(pack, gun), Json));
                }

                case "/api/glb":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    var glbPath = _studio.GetGlbPath(pack, gun);
                    return ApiResponse.Glb(await File.ReadAllBytesAsync(glbPath, ct));
                }

                case "/api/texture":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    if (method == "POST")
                    {
                        var res = _studio.ReplaceTexture(pack, gun, P("name"), body);
                        return ApiResponse.JsonOk(JsonSerializer.Serialize(res, Json));
                    }
                    int? size = int.TryParse(P("size"), out var s) ? s : null;
                    return ApiResponse.Png(_studio.GetTexturePng(pack, gun, P("name"), size));
                }

                case "/api/attach":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    float F(string k) => float.TryParse(P(k), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
                    float depth = query.ContainsKey("depth") ? F("depth") : 0.15f;
                    try
                    {
                        var res = _studio.Attach(pack, gun, body, string.IsNullOrEmpty(P("kind")) ? "plate" : P("kind"),
                            F("px"), F("py"), F("pz"), F("nx"), F("ny"), F("nz"), F("size"), depth, P("name"));
                        return ApiResponse.JsonOk(JsonSerializer.Serialize(res, Json));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        DumpAttachCrash(pack, gun, body, query, ex);
                        throw;
                    }
                }

                case "/api/attach-mesh":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    var dto = JsonSerializer.Deserialize<AttachMeshDto>(body,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? throw new ArgumentException("пустой запрос");
                    var res = _studio.AttachRawMesh(pack, gun, Convert.FromBase64String(dto.Png),
                        Floats(dto.Pos), Floats(dto.Nrm), Floats(dto.Uv), Ints(dto.Idx), P("name"));
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(res, Json));
                }

                case "/api/accessories":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(_studio.ListAccessories(pack, gun), Json));
                }

                case "/api/accessory/remove":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(_studio.RemoveAccessory(pack, gun, P("name")), Json));
                }

                case "/api/reset":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    ClearAnimInstalled(pack, gun);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(_studio.Reset(pack, gun), Json));
                }

                case "/api/export":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(_studio.ExportInfo(pack, gun), Json));
                }

                case "/api/saves":
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(await ListSavesAsync(), Json));

                case "/api/save":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    var gate = await CheckFlowAttemptAsync(P("flow"), P("flowGun"), P("session"));
                    if (gate != null) return ApiResponse.JsonOk(JsonSerializer.Serialize(new { ok = false, error = gate }, Json));
                    var res = await SaveToSlotAsync(pack, gun, P("slot"), P("name"), P("owner"), ct);
                    await ConsumeFlowAttemptIfOkAsync(res, P("flow"), P("flowGun"), P("session"));
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(res, Json));
                }

                case "/api/save-load":
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(await LoadSlotAsync(P("slot")), Json));

                case "/api/save-delete":
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(await DeleteSlotAsync(P("slot")), Json));

                case "/api/anim":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    var dto = JsonSerializer.Deserialize<AnimReqDto>(body,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? throw new ArgumentException("пустой запрос");
                    var req = new AnimatedDetailService.GenRequest
                    {
                        SourceKind = string.IsNullOrWhiteSpace(dto.SourceKind) ? "pendant" : dto.SourceKind,
                        Png = Convert.FromBase64String(dto.Png ?? ""),
                        Pos = NFloats(dto.Pos), Nrm = NFloats(dto.Nrm), Uv = NFloats(dto.Uv), Idx = NInts(dto.Idx),
                        Size = dto.Size ?? 0.12f, DepthFrac = dto.DepthFrac ?? 0.15f,
                        AnimMode = string.IsNullOrWhiteSpace(dto.AnimMode) ? "uv" : dto.AnimMode,
                        ScrollU = dto.ScrollU ?? 1f, ScrollV = dto.ScrollV ?? 0f,
                        AxisX = dto.AxisX ?? 1f, AxisY = dto.AxisY ?? 0f, AxisZ = dto.AxisZ ?? 0f,
                        AmplitudeDeg = dto.AmplitudeDeg ?? 20f, PeriodSec = dto.PeriodSec ?? 2.0f,
                        AttachBone = string.IsNullOrWhiteSpace(dto.AttachBone) ? "gun_root" : dto.AttachBone,
                        RotX = dto.RotX ?? 0f, RotY = dto.RotY ?? 0f, RotZ = dto.RotZ ?? 0f,
                        OffX = dto.OffX ?? 0f, OffY = dto.OffY ?? 0f, OffZ = dto.OffZ ?? 0f,
                        Mirror = dto.Mirror ?? false,
                        Name = string.IsNullOrWhiteSpace(dto.Name) ? "anim" : dto.Name,
                    };
                    var res = _studio.GenerateAnimatedDetail(pack, gun, req);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(res, Json));
                }

                case "/api/anim-file":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    var fpath = _studio.GetAnimFilePath(pack, gun, P("file"));
                    var bytes = await File.ReadAllBytesAsync(fpath, ct);
                    return fpath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)
                        ? ApiResponse.Glb(bytes)
                        : ApiResponse.Ok(bytes, "application/octet-stream");
                }

                case "/api/anim-install":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    var dto = JsonSerializer.Deserialize<AnimReqDto>(body,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? throw new ArgumentException("пустой запрос");
                    var req = new AnimatedDetailService.GenRequest
                    {
                        SourceKind = string.IsNullOrWhiteSpace(dto.SourceKind) ? "pendant" : dto.SourceKind,
                        Png = Convert.FromBase64String(dto.Png ?? ""),
                        Pos = NFloats(dto.Pos), Nrm = NFloats(dto.Nrm), Uv = NFloats(dto.Uv), Idx = NInts(dto.Idx),
                        Size = dto.Size ?? 0.12f, DepthFrac = dto.DepthFrac ?? 0.15f,
                        AnimMode = string.IsNullOrWhiteSpace(dto.AnimMode) ? "uv" : dto.AnimMode,
                        ScrollU = dto.ScrollU ?? 1f, ScrollV = dto.ScrollV ?? 0f,
                        AxisX = dto.AxisX ?? 1f, AxisY = dto.AxisY ?? 0f, AxisZ = dto.AxisZ ?? 0f,
                        AmplitudeDeg = dto.AmplitudeDeg ?? 20f, PeriodSec = dto.PeriodSec ?? 2.0f,
                        AttachBone = string.IsNullOrWhiteSpace(dto.AttachBone) ? "gun_root" : dto.AttachBone,
                        RotX = dto.RotX ?? 0f, RotY = dto.RotY ?? 0f, RotZ = dto.RotZ ?? 0f,
                        OffX = dto.OffX ?? 0f, OffY = dto.OffY ?? 0f, OffZ = dto.OffZ ?? 0f,
                        Mirror = dto.Mirror ?? false,
                        Name = string.IsNullOrWhiteSpace(dto.Name) ? "anim" : dto.Name,
                    };

                    var (aInt, aDisp) = await ResolveAppliedLabelAsync(pack, gun);

                    AnimatedDetailService.GameInstallSet set;
                    try { set = AnimatedDetailService.BuildGameInstall(req, aInt); }
                    catch (ArgumentException ex) { return ApiResponse.Error(400, ex.Message); }

                    var merged = new Dictionary<string, byte[]>(_studio.BuildInstallFiles(pack, gun),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (var (n, b) in set.StreamedFiles) merged[n] = b;

                    var loose = set.LooseFiles
                        .Select(e => new TargetDlcEditor.AnimLooseFile(e.RelPath, e.Bytes, e.FileType, e.Contents))
                        .ToList();
                    (string, string, byte[])? metaPatch = set.UpdateRpfMeta != null
                        ? (set.UpdateRpfMeta.DlcName, set.UpdateRpfMeta.RelPath, set.UpdateRpfMeta.Bytes)
                        : null;

                    var res = await _selectedGuns.ApplyStandaloneAnimAsync(aInt, aDisp, pack, merged, loose, metaPatch, NoopEmit, ct);
                    if (res.Success) MarkAnimInstalled(pack, gun);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(new
                    {
                        ok = res.Success, message = res.ErrorMessage,
                        component = set.ComponentName, model = set.ModelName, weapon = set.WeaponKey,
                    }, Json));
                }

                case "/api/glass":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    var dto = JsonSerializer.Deserialize<GlassReqDto>(body,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new GlassReqDto();
                    var st = _studio.SetGlass(pack, gun, dto.Textures ?? Array.Empty<string>(),
                        dto.On, dto.Opacity ?? 0.4f, dto.Color);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(new { ok = true, count = st.Textures.Count }, Json));
                }

                case "/api/apply":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    var (anim, animError) = await ApplyAsync(pack, gun, ct);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(new { ok = true, anim, animError }, Json));
                }

                case "/api/anim-support":
                {
                    var gunKey = P("gun") ?? "";
                    if (string.Equals(P("pack"), CustomPack, StringComparison.OrdinalIgnoreCase))
                        (gunKey, _) = await ResolveAppliedLabelAsync(P("pack"), gunKey);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(new
                    {
                        ok = true,
                        supported = Core.Gunsmith.AnimatedDetailService.IsInstallSupported(gunKey),
                        supportedList = Core.Gunsmith.AnimatedDetailService.SupportedListText,
                    }, Json));
                }

                case "/api/publish":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    var gate = await CheckFlowAttemptAsync(P("flow"), P("flowGun"), P("session"));
                    if (gate != null) return ApiResponse.JsonOk(JsonSerializer.Serialize(new { ok = false, error = gate }, Json));
                    var meta = JsonSerializer.Deserialize<PublishMeta>(body,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new PublishMeta();
                    var row = await PublishAsync(pack, gun, meta, P("owner"), P("ownerName"), ct);
                    await ConsumeFlowAttemptIfOkAsync(new { ok = true }, P("flow"), P("flowGun"), P("session"));
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(new { ok = true, id = row.Id }, Json));
                }

                case "/api/limits":
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(await FlowLimitsJsonAsync(), Json));

                case "/api/ownpack/state":
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(await OwnPackStateAsync(), Json));

                case "/api/ownpack/save":
                {
                    var (pack, gun) = await EnsureAsync(P("pack"), P("gun"), ct);
                    var res = await SaveOwnPackGunAsync(pack, gun, P("packId"), P("packName"), P("name"), P("owner"), ct);
                    return ApiResponse.JsonOk(JsonSerializer.Serialize(res, Json));
                }

                default:
                    return ApiResponse.Error(404, $"неизвестный endpoint: {path}");
            }
        }
        catch (OperationCanceledException) { return ApiResponse.Error(499, Loc.T("error.cancelled")); }
        catch (FileNotFoundException ex) { LogErr(path, ex); return ApiResponse.Error(404, ex.Message); }
        catch (DirectoryNotFoundException ex) { LogErr(path, ex); return ApiResponse.Error(404, ex.Message); }
        catch (ArgumentException ex) { LogErr(path, ex); return ApiResponse.Error(400, ex.Message); }
        catch (Exception ex) { LogErr(path, ex); return ApiResponse.Error(500, ex.Message); }
    }

    private async Task<List<T>> SelectAllPagedAsync<T>(string table, string select, CancellationToken ct)
    {
        const int page = 1000;
        var all = new List<T>();
        for (int off = 0; off <= 20000; off += page)
        {
            var rows = await _supabase.SelectAsync<T>(table, $"{select}&order=id.asc&limit={page}&offset={off}", ct);
            all.AddRange(rows);
            if (rows.Count < page) break;
        }
        return all;
    }

    private async Task<string> CatalogJsonAsync(CancellationToken ct)
    {
        var packs = await _packs.ListAsync(new GunpackFilter { Status = "published" });
        var guns = await SelectAllPagedAsync<CatGunRow>(
            "gunpack_guns",
            "select=gunpack_id,base_name,weapon_prefix,category,display_name,preview_url,is_hidden,sort_order", ct);

        var byPack = guns
            .Where(g => !g.IsHidden && !string.IsNullOrWhiteSpace(g.GunpackId)
                     && !string.IsNullOrWhiteSpace((g.WeaponPrefix ?? "") + (g.BaseName ?? "")))
            .GroupBy(g => g.GunpackId!)
            .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);

        var outPacks = new List<object>();
        foreach (var p in packs)
        {
            if (string.IsNullOrWhiteSpace(p.PackZipUrl)) continue;
            if (!byPack.TryGetValue(p.Id, out var pg) || pg.Count == 0) continue;

            var gunItems = pg
                .OrderBy(g => g.SortOrder).ThenBy(g => g.BaseName)
                .Select(g => new
                {
                    id = (g.WeaponPrefix ?? "") + (g.BaseName ?? ""),
                    name = string.IsNullOrWhiteSpace(g.DisplayName) ? (g.BaseName ?? "") : g.DisplayName!,
                    category = g.Category ?? "assault",
                    previewUrl = g.PreviewUrl,
                    edited = Directory.Exists(Path.Combine(WorkRoot, Safe(p.Id), Safe((g.WeaponPrefix ?? "") + (g.BaseName ?? "")))),
                })
                .ToList<object>();

            outPacks.Add(new { id = p.Id, name = p.Name, guns = gunItems });
        }

        return JsonSerializer.Serialize(outPacks, Json);
    }

    private async Task<(string pack, string gun)> EnsureAsync(string pack, string gun, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pack) || string.IsNullOrWhiteSpace(gun))
            throw new ArgumentException("pack и gun обязательны");

        var dir = Path.Combine(ExtractRoot, Safe(pack), Safe(gun));
        if (HasYdr(dir)) return (pack, gun);
        Directory.CreateDirectory(dir);

        if (string.Equals(pack, CustomPack, StringComparison.OrdinalIgnoreCase))
        {
            SetProgress(pack, gun, "download", 0);
            var row = await _customRepo.GetOwnOrPublishedByIdAsync(gun)
                ?? throw new FileNotFoundException(Loc.T("error.skinNotFound"));
            if (!string.IsNullOrWhiteSpace(row.FilesUrl))
                await DownloadAndExtractAsync(row.FilesUrl!, dir, ct);
            else if (!string.IsNullOrWhiteSpace(row.InternalName))
                await MaterializeBaseAsync(row.InternalName!, dir, ct);
            else
                throw new FileNotFoundException(Loc.T("error.skinHasNoFiles"));
            SetProgress(pack, gun, "build", 100);
        }
        else if (string.Equals(pack, VanillaPack, StringComparison.OrdinalIgnoreCase))
        {
            bool fromStorage = false;
            try
            {
                var asset = await GetVanillaAssetAsync(gun, ct);
                if (asset is { FilesUrl: { Length: > 0 } })
                {
                    SetProgress(pack, gun, "download", 0);
                    await DownloadAndExtractAsync(asset.FilesUrl!, dir, ct, asset.Sha256);
                    fromStorage = HasYdr(dir);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogErr($"vanilla/{gun}", new Exception("база с хранилища не приехала, иду в файлы игры: " + ex.Message));
            }

            if (!fromStorage)
            {
                SetProgress(pack, gun, "extract", 10);
                var gtaPath = await _resolveGtaPath();
                if (string.IsNullOrWhiteSpace(gtaPath) || !Directory.Exists(gtaPath))
                    throw new InvalidOperationException(Loc.T("error.gtaFolderNotFoundSetInLauncher"));
                await Task.Run(() =>
                {
                    var (ydrName, ydr, ytds) = MiamiGraphics.Core.WhitelistRenderTool.ExtractVanilla(gtaPath, gun);
                    File.WriteAllBytes(Path.Combine(dir, ydrName), ydr);
                    foreach (var (name, bytes) in ytds)
                        File.WriteAllBytes(Path.Combine(dir, name), bytes);
                }, ct);
            }

            await Task.Run(() =>
            {
                var baked = TextureStudio.BakeVanillaPalettes(dir);
                if (baked > 0) LogErr($"vanilla/{gun}", new Exception($"palette baked into {baked} diffuse(s) [инфо, не ошибка]"));
            }, ct);
            SetProgress(pack, gun, "build", 100);
        }
        else
        {
            await MaterializeGunAsync(pack, gun, dir, ct);
        }

        if (!HasYdr(dir)) throw new FileNotFoundException(Loc.T("error.gunFilesUnavailable"));
        return (pack, gun);
    }

    private async Task MaterializeGunAsync(string gunpackId, string internalName, string dir, CancellationToken ct)
    {
        var guns = await _packs.ListGunsAsync(gunpackId);
        var g = guns.FirstOrDefault(x =>
            string.Equals((x.WeaponPrefix ?? "") + (x.BaseName ?? ""), internalName, StringComparison.OrdinalIgnoreCase)
            && x.Files is { Count: > 0 })
            ?? throw new FileNotFoundException(Loc.T("error.gunNotInThisPack", ("gun", internalName)));

        var pack = await _packs.GetByIdAsync(gunpackId)
            ?? throw new FileNotFoundException(Loc.T("error.packNotFound"));
        if (string.IsNullOrWhiteSpace(pack.PackZipUrl) || string.IsNullOrWhiteSpace(pack.PackZipSha256))
            throw new InvalidOperationException(Loc.T("error.packHasNoPackZip"));

        var prog = new Progress<(long received, long total)>(t =>
            SetProgress(gunpackId, internalName, "download",
                t.total > 0 ? (int)Math.Min(100, t.received * 100 / t.total) : 0));

        Dictionary<string, byte[]>? files = null;

        var cached = PackZipCache.CachedPathOrNull(pack.PackZipSha256!);
        if (cached != null)
        {
            SetProgress(gunpackId, internalName, "extract", 100);
            files = PackZipCache.ExtractFiles(cached, g.Files);
        }

        if (files == null || files.Count == 0)
        {
            SetProgress(gunpackId, internalName, "download", 0);
            files = await RangeZipFetcher.TryFetchAsync(pack.PackZipUrl!, g.Files, prog, ct);
        }

        if (files == null || files.Count == 0)
        {
            SetProgress(gunpackId, internalName, "download", 0);
            var zip = await _packZip.EnsurePackZipAsync(pack.PackZipUrl!, pack.PackZipSha256!, pack.PackZipSize, prog, ct);
            files = PackZipCache.ExtractFiles(zip, g.Files);
        }

        SetProgress(gunpackId, internalName, "build", 100);
        foreach (var (name, bytes) in files)
            await File.WriteAllBytesAsync(Path.Combine(dir, name), bytes, ct);
    }

    private async Task MaterializeBaseAsync(string internalName, string dir, CancellationToken ct)
    {
        var guns = await SelectAllPagedAsync<ResolveRow>(
            "gunpack_guns", "select=gunpack_id,base_name,weapon_prefix,files", ct);
        var g = guns.FirstOrDefault(x =>
            string.Equals((x.WeaponPrefix ?? "") + (x.BaseName ?? ""), internalName, StringComparison.OrdinalIgnoreCase)
            && x.Files is { Count: > 0 } && !string.IsNullOrWhiteSpace(x.GunpackId))
            ?? throw new FileNotFoundException(Loc.T("error.gunNotFound", ("gun", internalName)));
        await MaterializeGunAsync(g.GunpackId!, internalName, dir, ct);
    }

    private static async Task DownloadAndExtractAsync(string url, string dir, CancellationToken ct,
        string? expectSha256 = null)
    {
        var tmpZip = Path.Combine(dir, "_src.zip");
        await Bridge.AppBridge.DownloadViaMirrorAsync(url, tmpZip, null, ct);
        if (!string.IsNullOrWhiteSpace(expectSha256))
        {
            string got;
            await using (var fs = File.OpenRead(tmpZip))
                got = Convert.ToHexString(await global::System.Security.Cryptography.SHA256.HashDataAsync(fs, ct)).ToLowerInvariant();
            if (!string.Equals(got, expectSha256!.Trim().ToLowerInvariant(), StringComparison.Ordinal))
            {
                try { File.Delete(tmpZip); } catch { }
                throw new InvalidOperationException(Loc.T("error.downloadedArchiveChecksumMismatch"));
            }
        }
        using (var za = ZipFile.OpenRead(tmpZip))
            foreach (var e in za.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;
                if (e.FullName.Replace('\\', '/').StartsWith("_anim/", StringComparison.OrdinalIgnoreCase))
                {
                    var animDir = Path.Combine(dir, "_anim");
                    Directory.CreateDirectory(animDir);
                    e.ExtractToFile(
                        MiamiGraphics.Core.System.SafePath.ResolveLeafInside(animDir, e.Name, Loc.T("misc.animDetailFileFromArchive")),
                        overwrite: true);
                    continue;
                }
                bool keep = e.Name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)
                         || e.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)
                         || e.Name.Equals("_glass.json", StringComparison.OrdinalIgnoreCase)
                         || e.Name.Equals(TextureStudio.PaletteBakedMarker, StringComparison.OrdinalIgnoreCase);
                if (!keep) continue;
                e.ExtractToFile(
                    MiamiGraphics.Core.System.SafePath.ResolveLeafInside(dir, e.Name, Loc.T("misc.gunFileFromArchive")),
                    overwrite: true);
            }
        try { File.Delete(tmpZip); } catch { }
    }

    private async Task<(bool anim, string animError)> ApplyAsync(string pack, string gun, CancellationToken ct)
    {
        var files = _studio.BuildInstallFiles(pack, gun);
        if (files.Count == 0) throw new InvalidOperationException(Loc.T("error.noFilesToInstall"));

        bool installedAnim = false;
        string animError = null;
        var animDir = Path.Combine(ActiveDir(pack, gun), "_anim");
        var reqPath = Path.Combine(animDir, "_request.json");

        var (internalName, displayName) = await ResolveAppliedLabelAsync(pack, gun);

        if (AnimInstallMarked(pack, gun) && File.Exists(reqPath) && Directory.GetFiles(animDir, "*.ydr").Length > 0)
        {
            AnimatedDetailService.GameInstallSet set = null;
            try
            {
                var req = JsonSerializer.Deserialize<AnimatedDetailService.GenRequest>(
                    await File.ReadAllBytesAsync(reqPath, ct), AnimReqJson);
                if (req != null) set = AnimatedDetailService.BuildGameInstall(req, internalName);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                LogErr("apply/anim", ex);
                animError = ex.Message;
                set = null;
            }

            if (set != null)
            {
                var merged = new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase);
                foreach (var (n, b) in set.StreamedFiles) merged[n] = b;

                var loose = set.LooseFiles
                    .Select(e => new TargetDlcEditor.AnimLooseFile(e.RelPath, e.Bytes, e.FileType, e.Contents))
                    .ToList();
                (string, string, byte[])? metaPatch = set.UpdateRpfMeta != null
                    ? (set.UpdateRpfMeta.DlcName, set.UpdateRpfMeta.RelPath, set.UpdateRpfMeta.Bytes)
                    : null;

                var ares = await _selectedGuns.ApplyStandaloneAnimAsync(internalName, displayName, pack, merged, loose, metaPatch, NoopEmit, ct);
                if (!ares.Success)
                    throw new InvalidOperationException(ares.ErrorMessage ?? Loc.T("error.animDetailInstallFailed"));
                installedAnim = true;
            }
        }

        if (!installedAnim)
        {
            var res = await _selectedGuns.ApplyStandaloneCustomAsync(internalName, displayName, pack, files, NoopEmit, ct);
            if (!res.Success) throw new InvalidOperationException(res.ErrorMessage ?? Loc.T("error.skinInstallFailed"));
        }
        return (installedAnim, animError);
    }

    private async Task<(string internalName, string displayName)> ResolveAppliedLabelAsync(string pack, string gun)
    {
        try
        {
            if (string.Equals(pack, CustomPack, StringComparison.OrdinalIgnoreCase))
            {
                var row = await _customRepo.GetOwnOrPublishedByIdAsync(gun);
                if (row != null) return (row.InternalName ?? gun, string.IsNullOrWhiteSpace(row.DisplayName) ? (row.InternalName ?? gun) : row.DisplayName);
                return (gun, gun);
            }
            var guns = await _packs.ListGunsAsync(pack);
            var g = guns.FirstOrDefault(x =>
                string.Equals((x.WeaponPrefix ?? "") + (x.BaseName ?? ""), gun, StringComparison.OrdinalIgnoreCase));
            return (gun, g != null && !string.IsNullOrWhiteSpace(g.DisplayName) ? g.DisplayName! : gun);
        }
        catch { return (gun, gun); }
    }

    private async Task<CustomGunItem> PublishAsync(
        string pack, string gun, PublishMeta meta, string ownerUserId, string ownerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            throw new InvalidOperationException(Loc.T("error.loginRequiredToPublishSkins"));

        var displayName = string.IsNullOrWhiteSpace(meta.DisplayName) ? Loc.T("misc.mySkin") : meta.DisplayName!;
        var description = meta.Description ?? "";
        string? editId = string.Equals(pack, CustomPack, StringComparison.OrdinalIgnoreCase) ? gun : null;
        var saved = await UploadAndSaveDraftAsync(pack, gun, editId, displayName, description, meta.Category, ownerUserId, ct,
                        requireGlb: true)
            ?? throw new InvalidOperationException(Loc.T("error.skinSaveFailed"));

        try
        {
            return await _customRepo.PublishSecureAsync(saved.Id)
                ?? throw new InvalidOperationException(Loc.T("error.skinSavedNotSentToModeration"));
        }
        catch when (editId is null)
        {
            try { await _customRepo.DeleteAsync(saved.Id); }
            catch (Exception delEx) { LogErr("publish/rollback", delEx); }
            throw;
        }
    }

    private async Task<CustomGunItem?> UploadAndSaveDraftAsync(
        string pack, string gun, string? slotId, string displayName, string description, string? category0,
        string ownerUserId, CancellationToken ct, bool requireGlb = false)
    {
        var (filesUrl, sha, glbUrl, previewUrl) = await UploadArtifactsAsync(pack, gun, requireGlb, ct);

        var (baseName, weaponPrefix, internalName, category) = await ResolveSaveMetaAsync(pack, gun, category0);

        return await _customRepo.SaveDraftAsync(slotId, baseName, weaponPrefix, internalName,
                    displayName, description, category, glbUrl, filesUrl, sha, previewUrl)
             ?? await _customRepo.SaveDraftAsync(null, baseName, weaponPrefix, internalName,
                    displayName, description, category, glbUrl, filesUrl, sha, previewUrl);
    }

    private async Task<(string filesUrl, string sha, string? glbUrl, string? previewUrl)> UploadArtifactsAsync(
        string pack, string gun, bool requireGlb, CancellationToken ct, Action<string, int>? progress = null)
    {
        var activeDir = ActiveDir(pack, gun);
        var resFiles = ResourceFiles(activeDir).ToList();
        if (resFiles.Count == 0) throw new InvalidOperationException(Loc.T("error.noEditedFiles"));

        progress?.Invoke("upload", 10);
        var tmpZip = Path.Combine(CacheRoot, "_save_" + Hex(12) + ".zip");
        string filesUrl, sha;
        try
        {
            try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
            using (var zip = ZipFile.Open(tmpZip, ZipArchiveMode.Create))
            {
                foreach (var f in resFiles) zip.CreateEntryFromFile(f, Path.GetFileName(f));
                var glassPath = Path.Combine(activeDir, "_glass.json");
                if (File.Exists(glassPath)) zip.CreateEntryFromFile(glassPath, "_glass.json");
                var palMark = Path.Combine(activeDir, TextureStudio.PaletteBakedMarker);
                if (File.Exists(palMark)) zip.CreateEntryFromFile(palMark, TextureStudio.PaletteBakedMarker);
                var animSrcDir = Path.Combine(activeDir, "_anim");
                if (AnimInstallMarked(pack, gun) && Directory.Exists(animSrcDir))
                    foreach (var f in Directory.GetFiles(animSrcDir))
                        if (!f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(AnimInstallMarker, StringComparison.OrdinalIgnoreCase))
                            zip.CreateEntryFromFile(f, "_anim/" + Path.GetFileName(f));
            }
            sha = Sha256File(tmpZip);
            var zipBytes = await File.ReadAllBytesAsync(tmpZip, ct);
            filesUrl = await _supabase.UploadCustomGunFileAsync(zipBytes, "files", ct)
                ?? throw new InvalidOperationException(Loc.T("error.serverRejectedSkinFiles"));
        }
        finally { try { File.Delete(tmpZip); } catch { } }
        progress?.Invoke("upload", 40);

        string? glbUrl = null;
        string? previewUrl = null;
        try
        {
            var glbPath = _studio.GetGameLikeGlbPath(pack, gun);
            if (File.Exists(glbPath))
            {
                var glbBytes = await File.ReadAllBytesAsync(glbPath, ct);
                glbUrl = await _supabase.UploadCustomGunFileAsync(glbBytes, "glb", ct);
                progress?.Invoke("render", 55);
                previewUrl = await TryRenderPreviewAsync(glbPath, ct);
            }
        }
        catch (Exception ex)
        {
            LogErr("save/glb", ex);
            if (requireGlb)
                throw new InvalidOperationException(
                    Loc.T("error.skinPreviewPrepFailed", ("reason", ex.Message)), ex);
        }
        if (requireGlb && string.IsNullOrWhiteSpace(glbUrl))
            throw new InvalidOperationException(Loc.T("error.skinPreviewMissingOpenEditor"));
        progress?.Invoke("render", 88);

        return (filesUrl, sha, glbUrl, previewUrl);
    }

    private async Task<(string baseName, string weaponPrefix, string internalName, string category)>
        ResolveSaveMetaAsync(string pack, string gun, string? category0)
    {
        string baseName, weaponPrefix, internalName = gun, category = category0 ?? "";
        if (string.Equals(pack, CustomPack, StringComparison.OrdinalIgnoreCase))
        {
            var existing = await _customRepo.GetOwnOrPublishedByIdAsync(gun);
            baseName     = existing?.BaseName ?? "";
            weaponPrefix = existing?.WeaponPrefix ?? "";
            internalName = string.IsNullOrWhiteSpace(existing?.InternalName) ? gun : existing!.InternalName;
            if (string.IsNullOrWhiteSpace(category)) category = existing?.Category ?? "assault";
        }
        else if (string.Equals(pack, VanillaPack, StringComparison.OrdinalIgnoreCase))
        {
            (baseName, weaponPrefix, var vanCategory) = VanillaMeta(gun);
            if (string.IsNullOrWhiteSpace(category)) category = vanCategory;
        }
        else
        {
            (baseName, weaponPrefix, var gunCategory) = await ResolveMetaAsync(pack, gun);
            if (string.IsNullOrWhiteSpace(category)) category = gunCategory;
        }
        if (string.IsNullOrWhiteSpace(category)) category = "assault";
        return (baseName, weaponPrefix, internalName, category);
    }

    private static (string baseName, string weaponPrefix, string category) VanillaMeta(string internalName)
    {
        var m = System.Text.RegularExpressions.Regex.Match(internalName, @"^(w_[a-z]+_)(.+)$");
        if (!m.Success) return (internalName, "", "assault");
        var prefix = m.Groups[1].Value;
        var category = prefix switch
        {
            "w_sg_" => "shotgun", "w_pi_" => "pistol", "w_sb_" => "smg",
            "w_ar_" => "assault", "w_sr_" => "sniper", "w_mg_" => "mg",
            _ => "assault",
        };
        return (m.Groups[2].Value, prefix, category);
    }

    private async Task<string?> TryRenderPreviewAsync(string glbPath, CancellationToken ct)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "cgprev_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            if (!RendererBootstrapper.IsAlreadyInstalled())
                await new RendererBootstrapper().EnsureInstalledAsync(null, ct);
            Directory.CreateDirectory(workDir);
            var outPng = Path.Combine(workDir, "preview.webp");
            var ok = await Core.Services.GlbToPngRenderer.RenderAsync(glbPath, outPng, 1280, 800);
            if (!ok || !File.Exists(outPng)) return null;
            var bytes = await File.ReadAllBytesAsync(outPng, ct);
            return await _supabase.UploadCustomGunFileAsync(bytes, "preview", ct);
        }
        catch (Exception ex) { LogErr("save/preview", ex); return null; }
        finally { try { Directory.Delete(workDir, recursive: true); } catch { } }
    }

    private async Task<(string baseName, string weaponPrefix, string category)> ResolveMetaAsync(string gunpackId, string internalName)
    {
        var guns = await _packs.ListGunsAsync(gunpackId);
        var g = guns.FirstOrDefault(x =>
            string.Equals((x.WeaponPrefix ?? "") + (x.BaseName ?? ""), internalName, StringComparison.OrdinalIgnoreCase));
        if (g != null)
            return (g.BaseName ?? "", g.WeaponPrefix ?? "", string.IsNullOrWhiteSpace(g.Category) ? "assault" : g.Category);
        return (internalName, "", "assault");
    }

    private async Task<object> ListSavesAsync()
    {
        List<CustomGunItem> mine;
        try { mine = await _customRepo.MineAsync(); }
        catch (Exception ex) { LogErr("saves/list", ex); mine = new List<CustomGunItem>(); }

        mine = mine.Where(m => string.IsNullOrEmpty(m.UserGunpackId)).ToList();

        var slots = mine.Where(m => m.Status == "saved")
            .OrderByDescending(m => m.UpdatedAt)
            .Select(m => new
            {
                slotId  = m.Id,
                name    = m.DisplayName,
                pack    = CustomPack,
                gun     = m.Id,
                gunName = m.DisplayName,
                savedAt = m.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            })
            .ToList();

        int publications = mine.Count(m => m.Status is "pending" or "published");
        return new
        {
            ok = true, slots, max = FreeSlots,
            localSaves = slots.Count, publications,
            used = slots.Count + publications,
        };
    }

    private async Task<object> SaveToSlotAsync(string pack, string gun, string slotId, string name, string owner, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(owner))
            return new { ok = false, error = Loc.T("error.loginRequiredToSave") };
        var displayName = string.IsNullOrWhiteSpace(name) ? gun : name;
        try
        {
            var saved = await UploadAndSaveDraftAsync(
                pack, gun, string.IsNullOrWhiteSpace(slotId) ? null : slotId, displayName, "", null, owner, ct);
            if (saved == null)
                return new { ok = false, error = Loc.T("error.slotLimitReached", ("limit", FreeSlots)) };
            return new { ok = true, slotId = saved.Id };
        }
        catch (Exception ex) { LogErr("save/slot", ex); return new { ok = false, error = ex.Message }; }
    }

    private async Task<object> LoadSlotAsync(string slotId)
    {
        CustomGunItem? row;
        try { row = await _customRepo.GetOwnOrPublishedByIdAsync(slotId); }
        catch (Exception ex) { LogErr("save/load", ex); return new { ok = false, error = ex.Message }; }
        if (row == null) return new { ok = false, error = Loc.T("error.slotNotFound") };
        return new { ok = true, pack = CustomPack, gun = slotId, gunName = row.DisplayName };
    }

    private async Task<object> FlowLimitsJsonAsync()
    {
        List<SupabaseCustomGunRepository.WorkshopAttemptRow> rows;
        try { rows = await _customRepo.WorkshopLimitsAsync(); }
        catch (Exception ex) { LogErr("limits", ex); rows = new(); }
        return new
        {
            ok = true,
            standard = new
            {
                maxPerGun = WorkshopFlowLimits.StandardPerGun,
                perGun = rows.Where(r => r.Flow == "standard")
                             .ToDictionary(r => r.GunKey, r => r.Used),
            },
            packbase = new
            {
                used = rows.FirstOrDefault(r => r.Flow == "packbase")?.Used ?? 0,
                max  = WorkshopFlowLimits.PackBaseTotal,
            },
            ownpack = new
            {
                used = rows.FirstOrDefault(r => r.Flow == "ownpack")?.Used ?? 0,
                max  = WorkshopFlowLimits.OwnPackTotal,
            },
            gunCap = WorkshopFlowLimits.OwnPackGunCap,
        };
    }

    private async Task<object> OwnPackStateAsync()
    {
        List<SupabaseCustomGunRepository.UserGunpackMineRow> mine;
        List<SupabaseCustomGunRepository.WorkshopAttemptRow> rows;
        try
        {
            mine = await _customRepo.UserGunpackMineAsync();
            rows = await _customRepo.WorkshopLimitsAsync();
        }
        catch (Exception ex) { LogErr("ownpack/state", ex); return new { ok = false, error = ex.Message }; }
        var used = rows.FirstOrDefault(r => r.Flow == "ownpack")?.Used ?? 0;
        return new
        {
            ok = true,
            packs = mine.Select(p => new { id = p.Id, name = p.Name, gunCount = p.GunCount }).ToList(),
            used, max = WorkshopFlowLimits.OwnPackTotal,
            gunCap = WorkshopFlowLimits.OwnPackGunCap,
        };
    }

    private async Task<string?> CheckFlowAttemptAsync(string flow, string flowGun, string session)
    {
        if (flow is not ("standard" or "packbase")) return null;
        var sessionKey = FlowSessionKey(flow, flowGun, session);
        lock (_flowLock) { if (_consumedFlowSessions.Contains(sessionKey)) return null; }
        try
        {
            var rows = await _customRepo.WorkshopLimitsAsync();
            if (flow == "standard")
            {
                var used = rows.FirstOrDefault(r => r.Flow == "standard"
                    && string.Equals(r.GunKey, flowGun, StringComparison.OrdinalIgnoreCase))?.Used ?? 0;
                if (used >= WorkshopFlowLimits.StandardPerGun)
                    return Loc.T("error.flowLimitStandardPerGun", ("limit", WorkshopFlowLimits.StandardPerGun));
            }
            else
            {
                var used = rows.FirstOrDefault(r => r.Flow == "packbase")?.Used ?? 0;
                if (used >= WorkshopFlowLimits.PackBaseTotal)
                    return Loc.T("error.flowLimitPackBase", ("limit", WorkshopFlowLimits.PackBaseTotal));
            }
            return null;
        }
        catch (Exception ex) { LogErr("flow/gate", ex); return null; }
    }

    private async Task ConsumeFlowAttemptIfOkAsync(object result, string flow, string flowGun, string session)
    {
        if (flow is not ("standard" or "packbase")) return;
        var el = JsonSerializer.SerializeToElement(result, Json);
        if (!(el.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True)) return;

        var sessionKey = FlowSessionKey(flow, flowGun, session);
        lock (_flowLock) { if (!_consumedFlowSessions.Add(sessionKey)) return; }
        try
        {
            await _customRepo.WorkshopConsumeAsync(flow, flow == "standard" ? flowGun : "");
        }
        catch (Exception ex)
        {
            LogErr("flow/consume", ex);
        }
    }

    private static string FlowSessionKey(string flow, string flowGun, string session) =>
        flow + "|" + flowGun + "|" + (string.IsNullOrWhiteSpace(session) ? "-" : session);

    private async Task<object> SaveOwnPackGunAsync(
        string pack, string gun, string packId, string packName, string name, string owner, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(owner))
            return new { ok = false, error = Loc.T("error.loginRequiredToBuildOwnGunpack") };
        var displayName = string.IsNullOrWhiteSpace(name) ? gun : name;
        try
        {
            SetProgress(pack, gun, "upload", 5);
            var (filesUrl, sha, glbUrl, previewUrl) = await UploadArtifactsAsync(
                pack, gun, requireGlb: true, ct, (phase, pct) => SetProgress(pack, gun, phase, pct));

            SetProgress(pack, gun, "publish", 92);
            var (baseName, weaponPrefix, internalName, category) = await ResolveSaveMetaAsync(pack, gun, null);

            var res = await _customRepo.UserGunpackSaveGunAsync(
                string.IsNullOrWhiteSpace(packId) ? null : packId,
                string.IsNullOrWhiteSpace(packName) ? null : packName,
                baseName, weaponPrefix, internalName, displayName, category,
                glbUrl, previewUrl, filesUrl, sha);

            SetProgress(pack, gun, "done", 100);
            return new
            {
                ok = true,
                packId = res.PackId, packName = res.PackName,
                gunId = res.GunId, gunCount = res.GunCount,
                gunCap = WorkshopFlowLimits.OwnPackGunCap,
            };
        }
        catch (Exception ex)
        {
            SetProgress(pack, gun, "error", 0);
            LogErr("ownpack/save", ex);
            return new { ok = false, error = FriendlyOwnPackError(ex) };
        }
    }

    private static string FriendlyOwnPackError(Exception ex)
    {
        var msg = ex.Message ?? "";
        if (msg.Contains("limit_reached", StringComparison.OrdinalIgnoreCase))
            return Loc.T("error.flowLimitOwnPack", ("limit", WorkshopFlowLimits.OwnPackTotal));
        if (msg.Contains("gun limit reached", StringComparison.OrdinalIgnoreCase))
            return Loc.T("error.ownPackGunCapReached", ("limit", WorkshopFlowLimits.OwnPackGunCap));
        return msg;
    }

    private async Task<object> DeleteSlotAsync(string slotId)
    {
        try { await _customRepo.DeleteAsync(slotId); return new { ok = true }; }
        catch (Exception ex) { LogErr("save/delete", ex); return new { ok = false, error = ex.Message }; }
    }

    private string ActiveDir(string pack, string gun)
    {
        var work = Path.Combine(WorkRoot, Safe(pack), Safe(gun));
        return Directory.Exists(work) ? work : Path.Combine(ExtractRoot, Safe(pack), Safe(gun));
    }

    private static bool HasYdr(string dir)
        => Directory.Exists(dir) && Directory.GetFiles(dir, "*.ydr").Length > 0;

    private const string AnimInstallMarker = "_installed.flag";

    private string AnimMarkerPath(string pack, string gun)
        => Path.Combine(ActiveDir(pack, gun), "_anim", AnimInstallMarker);

    private bool AnimInstallMarked(string pack, string gun)
        => File.Exists(AnimMarkerPath(pack, gun));

    private void MarkAnimInstalled(string pack, string gun)
    {
        try
        {
            var p = AnimMarkerPath(pack, gun);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, "1");
        }
        catch (Exception ex) { LogErr("anim/mark", ex); }
    }

    private void ClearAnimInstalled(string pack, string gun)
    {
        try
        {
            var p = AnimMarkerPath(pack, gun);
            if (File.Exists(p)) File.Delete(p);
        }
        catch (Exception ex) { LogErr("anim/unmark", ex); }
    }

    private static IEnumerable<string> ResourceFiles(string dir)
        => Directory.Exists(dir)
            ? Directory.GetFiles(dir).Where(f =>
                f.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase))
            : Enumerable.Empty<string>();

    private static Dictionary<string, byte[]> ReadDirFiles(string dir)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ResourceFiles(dir)) map[Path.GetFileName(f)] = File.ReadAllBytes(f);
        return map;
    }

    private static string Safe(string s)
    {
        var chars = s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray();
        var clean = new string(chars);
        return clean.Length == 0 ? "x" : clean;
    }

    private static string Sanitize(string s)
    {
        var chars = s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray();
        var clean = new string(chars);
        return clean.Length == 0 ? "anon" : clean;
    }

    private static string Hex(int n) => Guid.NewGuid().ToString("N")[..Math.Min(n, 32)];

    private static string Sha256File(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private static float[] Floats(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return Array.Empty<float>();
        var by = Convert.FromBase64String(b64);
        var f = new float[by.Length / 4];
        Buffer.BlockCopy(by, 0, f, 0, f.Length * 4);
        return f;
    }

    private static int[] Ints(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return Array.Empty<int>();
        var by = Convert.FromBase64String(b64);
        var u = new int[by.Length / 4];
        Buffer.BlockCopy(by, 0, u, 0, u.Length * 4);
        return u;
    }

    private static float[]? NFloats(string? b64) => string.IsNullOrEmpty(b64) ? null : Floats(b64);
    private static int[]? NInts(string? b64) => string.IsNullOrEmpty(b64) ? null : Ints(b64);

    private sealed class AttachMeshDto
    {
        public string Pos { get; set; } = "";
        public string Nrm { get; set; } = "";
        public string Uv  { get; set; } = "";
        public string Idx { get; set; } = "";
        public string Png { get; set; } = "";
    }

    private sealed class PublishMeta
    {
        public string? DisplayName { get; set; }
        public string? Description { get; set; }
        public string? Category    { get; set; }
    }

    private sealed class AnimReqDto
    {
        public string? SourceKind { get; set; }
        public string? Png { get; set; }
        public string? Pos { get; set; }
        public string? Nrm { get; set; }
        public string? Uv  { get; set; }
        public string? Idx { get; set; }
        public float?  Size { get; set; }
        public float?  DepthFrac { get; set; }
        public string? AnimMode { get; set; }
        public float?  ScrollU { get; set; }
        public float?  ScrollV { get; set; }
        public float?  AxisX { get; set; }
        public float?  AxisY { get; set; }
        public float?  AxisZ { get; set; }
        public float?  AmplitudeDeg { get; set; }
        public float?  PeriodSec { get; set; }
        public string? AttachBone { get; set; }
        public float?  RotX { get; set; }
        public float?  RotY { get; set; }
        public float?  RotZ { get; set; }
        public float?  OffX { get; set; }
        public float?  OffY { get; set; }
        public float?  OffZ { get; set; }
        public bool?   Mirror { get; set; }
        public string? Name { get; set; }
    }

    private sealed class GlassReqDto
    {
        public string[]? Textures { get; set; }
        public bool On { get; set; }
        public float? Opacity { get; set; }
        public string? Color { get; set; }
    }

    private sealed class CatGunRow
    {
        [JsonPropertyName("gunpack_id")]    public string? GunpackId    { get; set; }
        [JsonPropertyName("base_name")]     public string? BaseName     { get; set; }
        [JsonPropertyName("weapon_prefix")] public string? WeaponPrefix { get; set; }
        [JsonPropertyName("category")]      public string? Category     { get; set; }
        [JsonPropertyName("display_name")]  public string? DisplayName  { get; set; }
        [JsonPropertyName("preview_url")]   public string? PreviewUrl   { get; set; }
        [JsonPropertyName("is_hidden")]     public bool    IsHidden     { get; set; }
        [JsonPropertyName("sort_order")]    public int     SortOrder    { get; set; }
    }

    private sealed class VanillaAssetRow
    {
        [JsonPropertyName("internal_name")] public string? InternalName { get; set; }
        [JsonPropertyName("files_url")]     public string? FilesUrl     { get; set; }
        [JsonPropertyName("sha256")]        public string? Sha256       { get; set; }
        [JsonPropertyName("size_bytes")]    public long    SizeBytes    { get; set; }
    }

    private List<VanillaAssetRow>? _vanillaAssets;
    private readonly SemaphoreSlim _vanillaAssetsGate = new(1, 1);

    private async Task<VanillaAssetRow?> GetVanillaAssetAsync(string internalName, CancellationToken ct)
    {
        if (_vanillaAssets is null)
        {
            await _vanillaAssetsGate.WaitAsync(ct);
            try
            {
                _vanillaAssets ??= await SelectAllPagedAsync<VanillaAssetRow>(
                    "vanilla_gun_assets", "internal_name,files_url,sha256,size_bytes", ct);
            }
            finally { _vanillaAssetsGate.Release(); }
        }
        return _vanillaAssets.FirstOrDefault(r =>
            string.Equals(r.InternalName, internalName, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class ResolveRow
    {
        [JsonPropertyName("gunpack_id")]    public string? GunpackId    { get; set; }
        [JsonPropertyName("base_name")]     public string? BaseName     { get; set; }
        [JsonPropertyName("weapon_prefix")] public string? WeaponPrefix { get; set; }
        [JsonPropertyName("files")]         public List<string>? Files  { get; set; }
    }
}
