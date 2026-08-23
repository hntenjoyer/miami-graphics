using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using MiamiGraphics.Bridge;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Services;
using MiamiGraphics.Shell.Admin;

namespace MiamiGraphics.Shell.Services;

public sealed class GunSkinEditor
{
    private readonly IGunpackRepository _packs;
    private readonly SupabaseClient _supabase;
    private readonly SupabaseCustomGunRepository _customRepo;
    private readonly IRemoteStorage _remoteStorage;
    private readonly ISelectedGunsInstaller _selectedGuns;
    private readonly PackZipCache _packZip = new();

    private const int FreeSlots = 5;
    private static readonly ISelectedGunsInstaller.EmitProgress NoopEmit = (_, _, _, _) => { };

    private sealed record Draft(
        string WorkDir, string BaseName, string WeaponPrefix, string Category,
        string DisplayName, string? CustomGunId);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Draft> _drafts = new();

    public GunSkinEditor(
        IGunpackRepository packs,
        SupabaseClient supabase,
        SupabaseCustomGunRepository customRepo,
        IRemoteStorage remoteStorage,
        ISelectedGunsInstaller selectedGuns)
    {
        _packs = packs;
        _supabase = supabase;
        _customRepo = customRepo;
        _remoteStorage = remoteStorage;
        _selectedGuns = selectedGuns;
    }

    private static string Root => MiamiGraphics.Core.System.AppDataRoot.Dir("gunskins");

    private static string Hex(int n) => Guid.NewGuid().ToString("N")[..n];

    public async Task<WorkshopSessionDto> OpenAsync(WorkshopOpenRequestDto req, CancellationToken ct = default)
    {
        var draftId = "draft_" + Hex(8);
        var workDir = Path.Combine(Root, draftId);
        Directory.CreateDirectory(workDir);

        string baseName, weaponPrefix, category, displayName;
        string? customGunId = null;

        if (!string.IsNullOrWhiteSpace(req.CustomGunId))
        {
            var row = await _customRepo.GetOwnOrPublishedByIdAsync(req.CustomGunId!)
                ?? throw new InvalidOperationException(Loc.T("error.skinNotFound"));
            customGunId = row.Id; baseName = row.BaseName; weaponPrefix = row.WeaponPrefix;
            category = row.Category; displayName = row.DisplayName;
            if (!string.IsNullOrWhiteSpace(row.FilesUrl))
                await DownloadAndExtractAsync(row.FilesUrl!, workDir, ct);
            else
                (baseName, weaponPrefix, category) = await ResolveBaseIntoAsync(row.InternalName, workDir, ct);
        }
        else
        {
            var internalName = req.BaseInternalName
                ?? throw new ArgumentException("baseInternalName или customGunId обязателен");
            (baseName, weaponPrefix, category) = await ResolveBaseIntoAsync(internalName, workDir, ct);
            displayName = Loc.T("misc.newSkin");
        }

        var textures = GunSkinTextures.List(workDir)
            .Select(t => new WorkshopTextureDto(
                t.Name, t.Width, t.Height, t.Role,
                "data:image/png;base64," + Convert.ToBase64String(t.Png)))
            .ToList();

        _drafts[draftId] = new Draft(workDir, baseName, weaponPrefix, category, displayName, customGunId);

        return new WorkshopSessionDto(draftId, customGunId, displayName, baseName, weaponPrefix, category, null, textures);
    }

    private async Task<(string baseName, string weaponPrefix, string category)> ResolveBaseIntoAsync(
        string internalName, string workDir, CancellationToken ct)
    {
        var guns = await _supabase.SelectAsync<GunRow>(
            "gunpack_guns",
            "select=gunpack_id,base_name,weapon_prefix,category,files&limit=4000", ct);
        var g = guns.FirstOrDefault(x =>
            string.Equals((x.WeaponPrefix ?? "") + (x.BaseName ?? ""), internalName, StringComparison.OrdinalIgnoreCase)
            && x.Files is { Count: > 0 } && !string.IsNullOrWhiteSpace(x.GunpackId))
            ?? throw new InvalidOperationException(Loc.T("error.gunNotInAnyPack", ("gun", internalName)));

        var pack = await _packs.GetByIdAsync(g.GunpackId!)
            ?? throw new InvalidOperationException(Loc.T("error.sourcePackNotFound"));
        if (string.IsNullOrWhiteSpace(pack.PackZipUrl) || string.IsNullOrWhiteSpace(pack.PackZipSha256))
            throw new InvalidOperationException(Loc.T("error.sourcePackNoPackZip"));

        var zip = await _packZip.EnsurePackZipAsync(pack.PackZipUrl!, pack.PackZipSha256!, pack.PackZipSize, null, ct);
        var extracted = PackZipCache.ExtractFiles(zip, g.Files!);
        if (extracted.Count == 0)
            throw new InvalidOperationException(Loc.T("error.gunFilesExtractFailed"));
        foreach (var (name, bytes) in extracted)
            await File.WriteAllBytesAsync(Path.Combine(workDir, name), bytes, ct);

        return (g.BaseName ?? "", g.WeaponPrefix ?? "", string.IsNullOrWhiteSpace(g.Category) ? "assault" : g.Category!);
    }

    private static async Task DownloadAndExtractAsync(string url, string workDir, CancellationToken ct)
    {
        var tmpZip = Path.Combine(workDir, "_src.zip");
        await Bridge.AppBridge.DownloadViaMirrorAsync(url, tmpZip, null, ct);
        using (var za = ZipFile.OpenRead(tmpZip))
            foreach (var e in za.Entries)
            {
                if (string.IsNullOrEmpty(e.Name)) continue;
                if (e.FullName.Replace('\\', '/').StartsWith("_anim/", StringComparison.OrdinalIgnoreCase))
                {
                    var animDir = Path.Combine(workDir, "_anim");
                    Directory.CreateDirectory(animDir);
                    e.ExtractToFile(
                        MiamiGraphics.Core.System.SafePath.ResolveLeafInside(animDir, e.Name, Loc.T("misc.animDetailFileFromArchive")),
                        overwrite: true);
                    continue;
                }
                bool keep = e.Name.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)
                         || e.Name.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase)
                         || e.Name.Equals("_glass.json", StringComparison.OrdinalIgnoreCase);
                if (!keep) continue;
                e.ExtractToFile(
                    MiamiGraphics.Core.System.SafePath.ResolveLeafInside(workDir, e.Name, Loc.T("misc.gunFileFromArchive")),
                    overwrite: true);
            }
        try { File.Delete(tmpZip); } catch { }
    }

    public Task<WorkshopReplaceResultDto> ReplaceTextureAsync(string draftId, string textureName, string pngBase64)
    {
        var d = Get(draftId);
        var png = Convert.FromBase64String(pngBase64);
        GunSkinTextures.Replace(d.WorkDir, textureName, png);
        return Task.FromResult(new WorkshopReplaceResultDto(null));
    }

    public Task SaveDraftAsync(string draftId) { _ = Get(draftId); return Task.CompletedTask; }

    public async Task ApplyToGameAsync(string draftId, CancellationToken ct = default)
    {
        var d = Get(draftId);
        var files = ReadDirFiles(d.WorkDir);
        if (files.Count == 0) throw new InvalidOperationException(Loc.T("error.noEditedFiles"));
        var internalName = (d.WeaponPrefix ?? "") + (d.BaseName ?? "");
        var res = await _selectedGuns.ApplyStandaloneCustomAsync(
            internalName, d.DisplayName ?? internalName, d.CustomGunId ?? "", files, NoopEmit, ct);
        if (!res.Success) throw new InvalidOperationException(res.ErrorMessage ?? Loc.T("error.skinInstallFailed"));
    }

    public async Task<CustomGunItem> PublishAsync(
        string draftId, WorkshopPublishMetaDto meta, string ownerUserId, string ownerName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ownerUserId))
            throw new InvalidOperationException(Loc.T("error.loginRequiredToPublishSkins"));
        var d = Get(draftId);

        var tmpZip = Path.Combine(Root, draftId + "_pub.zip");
        try { if (File.Exists(tmpZip)) File.Delete(tmpZip); } catch { }
        using (var zip = ZipFile.Open(tmpZip, ZipArchiveMode.Create))
            foreach (var f in ResourceFiles(d.WorkDir))
                zip.CreateEntryFromFile(f, Path.GetFileName(f));

        var sha = Sha256File(tmpZip);
        string filesUrl;
        try
        {
            var zipBytes = await File.ReadAllBytesAsync(tmpZip, ct);
            filesUrl = await _supabase.UploadCustomGunFileAsync(zipBytes, "files", ct)
                ?? throw new InvalidOperationException(Loc.T("error.serverRejectedSkinFiles"));
        }
        finally { try { File.Delete(tmpZip); } catch { } }

        string glbUrl;
        var glbPath = Path.Combine(d.WorkDir, "_publish.glb");
        try
        {
            var srcYdr = MiamiGraphics.Core.Gunsmith.TextureStudio.PickSourceYdr(d.WorkDir)
                ?? throw new FileNotFoundException(Loc.T("error.draftHasNoYdr"));
            var ytds = Directory.GetFiles(d.WorkDir, "*.ytd")
                .OrderBy(p => p.Contains("_hi", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ToList();
            if (!await YdrToGltfConverter.ConvertAsync(srcYdr, glbPath, ytds) || !File.Exists(glbPath))
                throw new InvalidOperationException(Loc.T("error.glbConversionFailed"));
            var glbBytes = await File.ReadAllBytesAsync(glbPath, ct);
            glbUrl = await _supabase.UploadCustomGunFileAsync(glbBytes, "glb", ct)
                ?? throw new InvalidOperationException(Loc.T("error.serverRejectedSkinPreview"));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                Loc.T("error.skinPreviewPrepFailed", ("reason", ex.Message)), ex);
        }
        finally { try { if (File.Exists(glbPath)) File.Delete(glbPath); } catch { } }

        var internalName = (d.WeaponPrefix ?? "") + (d.BaseName ?? "");
        bool createdNewRow = string.IsNullOrWhiteSpace(d.CustomGunId);
        var result = await _customRepo.SaveDraftAsync(
                         createdNewRow ? null : d.CustomGunId,
                         d.BaseName, d.WeaponPrefix, internalName,
                         meta.DisplayName, meta.Description, meta.Category, glbUrl, filesUrl, sha)
                   ?? await _customRepo.SaveDraftAsync(
                         null, d.BaseName, d.WeaponPrefix, internalName,
                         meta.DisplayName, meta.Description, meta.Category, glbUrl, filesUrl, sha)
                   ?? throw new InvalidOperationException(Loc.T("error.skinSaveFailed"));
        try
        {
            result = await _customRepo.PublishSecureAsync(result.Id)
                ?? throw new InvalidOperationException(Loc.T("error.skinSavedNotSentToModeration"));
        }
        catch when (createdNewRow)
        {
            try { await _customRepo.DeleteAsync(result.Id); } catch { }
            throw;
        }

        _drafts.TryRemove(draftId, out _);
        try { if (Directory.Exists(d.WorkDir)) Directory.Delete(d.WorkDir, recursive: true); } catch { }
        return result;
    }

    public async Task InstallAsync(string customGunId, CancellationToken ct = default)
    {
        var row = await _customRepo.GetPublishedByIdAsync(customGunId)
            ?? throw new InvalidOperationException(Loc.T("error.skinNotFound"));
        if (string.IsNullOrWhiteSpace(row.FilesUrl))
            throw new InvalidOperationException(Loc.T("error.skinHasNoFilesYet"));

        var tmp = Path.Combine(Root, "_install_" + Hex(8));
        Directory.CreateDirectory(tmp);
        try
        {
            await DownloadAndExtractAsync(row.FilesUrl!, tmp, ct);
            var files = ReadDirFiles(tmp);
            if (files.Count == 0) throw new InvalidOperationException(Loc.T("error.skinNoResourceFiles"));
            var internalName = !string.IsNullOrWhiteSpace(row.InternalName)
                ? row.InternalName!
                : (row.WeaponPrefix ?? "") + (row.BaseName ?? "");
            var displayName = row.DisplayName ?? internalName;

            var glass = ReadGlassState(tmp);
            if (glass is { Any: true })
                foreach (var key in files.Keys.ToList())
                {
                    try
                    {
                        var t = key.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase)
                            ? MiamiGraphics.Core.Gunsmith.GlassService.TransformYdr(files[key], glass)
                            : MiamiGraphics.Core.Gunsmith.GlassService.TransformYtd(files[key], glass);
                        if (t != null) files[key] = t;
                    }
                    catch {}
                }

            bool animInstalled = false;
            var reqPath = Path.Combine(tmp, "_anim", "_request.json");
            if (File.Exists(reqPath) && Directory.GetFiles(Path.Combine(tmp, "_anim"), "*.ydr").Length > 0)
            {
                try
                {
                    var req = System.Text.Json.JsonSerializer.Deserialize<MiamiGraphics.Core.Gunsmith.AnimatedDetailService.GenRequest>(
                        await File.ReadAllBytesAsync(reqPath, ct),
                        new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
                    if (req != null)
                    {
                        var set = MiamiGraphics.Core.Gunsmith.AnimatedDetailService.BuildGameInstall(req, internalName);
                        var merged = new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase);
                        foreach (var (n, b) in set.StreamedFiles) merged[n] = b;
                        var loose = set.LooseFiles
                            .Select(e => new TargetDlcEditor.AnimLooseFile(e.RelPath, e.Bytes, e.FileType, e.Contents))
                            .ToList();
                        (string, string, byte[])? metaPatch = set.UpdateRpfMeta != null
                            ? (set.UpdateRpfMeta.DlcName, set.UpdateRpfMeta.RelPath, set.UpdateRpfMeta.Bytes)
                            : null;
                        var ares = await _selectedGuns.ApplyStandaloneAnimAsync(
                            internalName, displayName, customGunId, merged, loose, metaPatch, NoopEmit, ct);
                        if (!ares.Success)
                            throw new InvalidOperationException(ares.ErrorMessage ?? Loc.T("error.animDetailInstallFailed"));
                        animInstalled = true;
                    }
                }
                catch (ArgumentException)
                {
                }
            }

            if (!animInstalled)
            {
                var res = await _selectedGuns.ApplyStandaloneCustomAsync(
                    internalName, displayName, customGunId, files, NoopEmit, ct);
                if (!res.Success) throw new InvalidOperationException(res.ErrorMessage ?? Loc.T("error.skinInstallFailed"));
            }
            _ = _customRepo.IncrementDownloadsAsync(customGunId);
        }
        finally { try { Directory.Delete(tmp, recursive: true); } catch { } }
    }

    private static MiamiGraphics.Core.Gunsmith.GlassState? ReadGlassState(string dir)
    {
        try
        {
            var p = Path.Combine(dir, "_glass.json");
            if (File.Exists(p))
                return System.Text.Json.JsonSerializer.Deserialize<MiamiGraphics.Core.Gunsmith.GlassState>(File.ReadAllText(p));
        }
        catch { }
        return null;
    }

    private Draft Get(string draftId)
        => _drafts.TryGetValue(draftId, out var d) ? d
           : throw new InvalidOperationException(Loc.T("error.workshopSessionNotFound"));

    private static IEnumerable<string> ResourceFiles(string dir)
        => Directory.GetFiles(dir).Where(f =>
            f.EndsWith(".ydr", StringComparison.OrdinalIgnoreCase) ||
            f.EndsWith(".ytd", StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, byte[]> ReadDirFiles(string dir)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ResourceFiles(dir)) map[Path.GetFileName(f)] = File.ReadAllBytes(f);
        return map;
    }

    private static string Sha256File(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private static string Sanitize(string s)
    {
        var chars = s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray();
        var clean = new string(chars);
        return clean.Length == 0 ? "anon" : clean;
    }

    private sealed class GunRow
    {
        [JsonPropertyName("gunpack_id")]   public string? GunpackId    { get; set; }
        [JsonPropertyName("base_name")]    public string? BaseName     { get; set; }
        [JsonPropertyName("weapon_prefix")]public string? WeaponPrefix { get; set; }
        [JsonPropertyName("category")]     public string? Category     { get; set; }
        [JsonPropertyName("files")]        public List<string>? Files  { get; set; }
    }
}
