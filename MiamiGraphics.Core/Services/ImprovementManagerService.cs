using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.System;

namespace MiamiGraphics.Core.Services
{
    public sealed class ImprovementManagerService
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public delegate Task DownloadAsync(string url, string destPath,
            Action<long, long>? bytesProgress, CancellationToken ct);

        public sealed class InstalledImprovement
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string ExclusiveGroup { get; set; } = "";
            public DateTime InstalledAt { get; set; }
        }

        public sealed class Registry
        {
            public List<InstalledImprovement> Items { get; set; } = new();
        }

        public sealed class CatalogEntry
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string ExclusiveGroup { get; set; } = "";
            public string Category { get; set; } = "";
            public string BundleUrl { get; set; } = "";
            public string BundleSha256 { get; set; } = "";
            public long BundleSizeBytes { get; set; }
            public List<RequiredContainer> Requires { get; set; } = new();
        }

        public sealed class RequiredContainer
        {
            public string Path { get; set; } = "";
            public string Url { get; set; } = "";
            public string Sha256 { get; set; } = "";
            public long SizeBytes { get; set; }
        }

        public sealed class Result
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public List<string> Notes { get; set; } = new();
        }

        private static string Root       => AppDataRoot.Dir("improvements");
        private static string BundlesDir => AppDataRoot.Dir("improvements", "bundles");
        private static string CleanDir   => AppDataRoot.Dir("improvements", "clean");

        private static string? FindCleanUpdateRpf()
        {
            try
            {
                var dir = AppDataRoot.BackupDir("clean");
                return Directory.EnumerateFiles(dir, "update_*.rpf").FirstOrDefault();
            }
            catch { return null; }
        }
        private static string RegistryPath => Path.Combine(Root, "installed.json");

        public Registry LoadRegistry() => ReadRegistry();

        public static Registry ReadRegistrySnapshot() => ReadRegistry();

        private static Registry ReadRegistry()
        {
            try
            {
                if (!File.Exists(RegistryPath)) return new Registry();
                var text = File.ReadAllText(RegistryPath);
                return string.IsNullOrWhiteSpace(text)
                    ? new Registry()
                    : JsonSerializer.Deserialize<Registry>(text, Json) ?? new Registry();
            }
            catch { return new Registry(); }
        }

        private static void SaveRegistry(Registry r)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(RegistryPath, JsonSerializer.Serialize(r, Json));
        }

        private static string BundleDir(string id) =>
            SafePath.ResolveInside(BundlesDir, id, "Идентификатор улучшения");

        public static string? LocalBundleDir(string id)
        {
            if (!SafePath.TryResolveInside(BundlesDir, id, out var dir, out _)) return null;
            return File.Exists(Path.Combine(dir, "improvement.json")) ? dir : null;
        }

        public static string LocalCatalogPath => Path.Combine(Root, "local.json");

        private sealed class LocalCatalogFile
        {
            public List<CatalogEntry> Improvements { get; set; } = new();
        }

        public static List<CatalogEntry> LoadLocalCatalog()
        {
            try
            {
                if (!File.Exists(LocalCatalogPath)) return new List<CatalogEntry>();
                var text = File.ReadAllText(LocalCatalogPath).TrimStart('﻿');
                return JsonSerializer.Deserialize<LocalCatalogFile>(text, Json)?.Improvements
                       ?? new List<CatalogEntry>();
            }
            catch { return new List<CatalogEntry>(); }
        }

        public static List<CatalogEntry> MergeCatalog(IEnumerable<CatalogEntry> remote)
        {
            var byId = new Dictionary<string, CatalogEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in remote) byId[e.Id] = e;
            foreach (var e in LoadLocalCatalog()) byId[e.Id] = e;
            return byId.Values.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public Task<Result> InstallAsync(string gtaRoot, CatalogEntry entry,
            IReadOnlyList<CatalogEntry> catalog, DownloadAsync download,
            Action<string, int, string?>? progress, Action<string> log, CancellationToken ct)
        {
            var reg = LoadRegistry();

            string GroupOf(InstalledImprovement x)
            {
                var live = catalog.FirstOrDefault(c => c.Id.Equals(x.Id, StringComparison.OrdinalIgnoreCase));
                return live is not null ? (live.ExclusiveGroup ?? "") : x.ExclusiveGroup;
            }

            var want = reg.Items
                .Where(x => !x.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.IsNullOrWhiteSpace(entry.ExclusiveGroup)
                            || !GroupOf(x).Equals(entry.ExclusiveGroup, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id)
                .Append(entry.Id).ToList();
            return ApplySetAsync(gtaRoot, want, catalog, download, progress, log, ct);
        }

        public Task<Result> RemoveAsync(string gtaRoot, string id,
            IReadOnlyList<CatalogEntry> catalog, DownloadAsync download,
            Action<string, int, string?>? progress, Action<string> log, CancellationToken ct)
        {
            var want = LoadRegistry().Items.Select(x => x.Id)
                        .Where(x => !x.Equals(id, StringComparison.OrdinalIgnoreCase)).ToList();
            return ApplySetAsync(gtaRoot, want, catalog, download, progress, log, ct);
        }

        public async Task<Result> ApplySetAsync(string gtaRoot, IReadOnlyList<string> wantIds,
            IReadOnlyList<CatalogEntry> catalog, DownloadAsync download,
            Action<string, int, string?>? progress, Action<string> log, CancellationToken ct)
        {
            var installed = ReadRegistry().Items.Select(x => x.Id).ToList();
            var entries = new List<CatalogEntry>();
            var dropped = new List<string>();
            foreach (var id in wantIds)
            {
                var e = catalog.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (e == null)
                {
                    if (!installed.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)))
                        return Fail(Loc.T("error.improvementNotInCatalogRebuild", ("id", id)));

                    log($"[Улучшения] '{id}' пропал из каталога, но стоит в игре - снимаю вместе с набором");
                    dropped.Add(id);
                    continue;
                }
                entries.Add(e);
            }

            var clash = entries.Where(e => !string.IsNullOrWhiteSpace(e.ExclusiveGroup))
                               .GroupBy(e => e.ExclusiveGroup!, StringComparer.OrdinalIgnoreCase)
                               .FirstOrDefault(g => g.Count() > 1);
            if (clash != null)
                return Fail(Loc.T("error.improvementExclusiveGroup",
                    ("group", clash.Key), ("names", string.Join(", ", clash.Select(x => x.Name)))));

            foreach (var r in entries.SelectMany(e => e.Requires))
                if (!SafePath.TryResolveInside(gtaRoot, r.Path, out _, out var why))
                {
                    log($"[Улучшения] ОТКАЗ: путь контейнера «{r.Path}» вне папки игры - {why}");
                    return Fail(Loc.T("error.improvementWritesOutsideGame", ("path", r.Path)));
                }

            var placed = new List<string>();

            try
            {
                var required = entries.SelectMany(e => e.Requires)
                                      .GroupBy(r => Norm(r.Path), StringComparer.OrdinalIgnoreCase)
                                      .Select(g => g.First()).ToList();
                progress?.Invoke("downloading", 5, Loc.T("install.checkingBaseContainers"));
                foreach (var r in required)
                {
                    ct.ThrowIfCancellationRequested();
                    await EnsureCleanContainerAsync(r, download, progress, log, ct);
                    if (PlaceContainerIfMissing(gtaRoot, r, log) is { } put) placed.Add(put);
                }

                for (int i = 0; i < entries.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    int band = 20 + 50 * i / Math.Max(1, entries.Count);
                    await EnsureBundleAsync(entries[i], download, progress, band, log, ct);
                }

                progress?.Invoke("injecting", 75, Loc.T("install.rebuildingSlots"));
                var dirs = entries.Select(e => BundleDir(e.Id)).ToList();

                var leaving = installed
                    .Where(id => !entries.Any(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                var reset = leaving.SelectMany(ContainersOfCached)
                                   .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (reset.Count > 0)
                    log($"[Улучшения] уходят: {string.Join(", ", leaving)} - возвращаю контейнеры: {string.Join(", ", reset)}");

                var leavingDirs = leaving.Select(LocalBundleDir)
                                         .Where(d => d != null).Cast<string>().ToList();
                var lostLeaving = leaving.Where(id => LocalBundleDir(id) == null).ToList();
                if (lostLeaving.Count > 0)
                    log($"[Улучшения] у уходящих нет кеша бандла (регистрацию не сбросить): {string.Join(", ", lostLeaving)}");

                var svc = new ImprovementInstallService();
                var res = await Task.Run(() => svc.Apply(new ImprovementInstallService.Request
                {
                    GtaRoot = gtaRoot,
                    ImprovementDirs = dirs,
                    ContainersToReset = reset,
                    LeavingImprovementDirs = leavingDirs,
                    CleanContainerProvider = rel =>
                    {
                        if (!SafePath.TryResolveInside(CleanDir, FlatName(rel), out var p, out _)) return null;
                        return File.Exists(p) ? p : null;
                    },
                    CleanUpdateRpfPath = FindCleanUpdateRpf(),
                }, log), ct);

                if (!res.Success)
                {
                    RollbackPlaced(placed, log);
                    progress?.Invoke("error", 75, res.Message);
                    return new Result { Success = false, Message = res.Message, Notes = res.Notes };
                }

                var reg = new Registry
                {
                    Items = entries.Select(e => new InstalledImprovement
                    {
                        Id = e.Id,
                        Name = e.Name,
                        ExclusiveGroup = e.ExclusiveGroup ?? "",
                        InstalledAt = DateTime.Now,
                    }).ToList(),
                };
                SaveRegistry(reg);

                progress?.Invoke("done", 100, null);
                foreach (var id in dropped)
                    res.Notes.Insert(0, Loc.T("error.improvementNotInCatalogRebuild", ("id", id)));

                return new Result
                {
                    Success = true,
                    Message = entries.Count == 0
                        ? Loc.T("misc.improvementsRemovedClean")
                        : Loc.T("misc.improvementsInstalledList",
                            ("names", string.Join(", ", entries.Select(e => e.Name)))),
                    Notes = res.Notes,
                };
            }
            catch (OperationCanceledException) { RollbackPlaced(placed, log); throw; }
            catch (Exception ex)
            {
                RollbackPlaced(placed, log);
                progress?.Invoke("error", 0, ex.Message);
                return Fail($"{ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void RollbackPlaced(List<string> placed, Action<string> log)
        {
            foreach (var p in placed)
            {
                try
                {
                    if (File.Exists(p)) File.Delete(p);
                    log($"[Улучшения] откат: убран {p}");
                }
                catch (Exception ex)
                {
                    log($"[Улучшения] откат: не удалось убрать {p} - {ex.Message}");
                }
            }
            placed.Clear();
        }

        private static IEnumerable<string> ContainersOfCached(string id)
        {
            var dir = LocalBundleDir(id);
            if (dir == null) yield break;

            List<ImprovementSlot> slots;
            try
            {
                var m = JsonSerializer.Deserialize<ImprovementManifest>(
                    File.ReadAllText(Path.Combine(dir, "improvement.json")), Json);
                slots = m?.Slots ?? new();
            }
            catch { yield break; }

            foreach (var s in slots)
                if (!string.IsNullOrWhiteSpace(s.Container))
                    yield return s.Container;
        }

        public static Result WipeAllLocal(string gtaRoot, Action<string> log)
        {
            var res = new Result { Success = true };
            var ids = ReadRegistry().Items.Select(x => x.Id).ToList();

            foreach (var container in ids.SelectMany(ContainersOfCached)
                                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    if (!SafePath.TryResolveInside(gtaRoot, container, out var live, out var why))
                    {
                        log($"[Улучшения] откат: путь «{container}» вне папки игры - {why}");
                        continue;
                    }
                    if (!File.Exists(live)) continue;

                    string clean = CleanCopyPath(container);
                    if (!File.Exists(clean))
                    {
                        log($"[Улучшения] откат: чистой копии {container} нет - остаётся как есть");
                        res.Notes.Add(Loc.T("error.noCleanCopyForContainer", ("container", container)));
                        continue;
                    }

                    File.Copy(clean, live, overwrite: true);
                    log($"[Улучшения] откат: {container} возвращён чистым ({new FileInfo(clean).Length:N0} б)");
                    res.Notes.Add($"{container} - возвращён чистым");
                }
                catch (Exception ex)
                {
                    log($"[Улучшения] откат: {container} не восстановлен - {ex.Message}");
                    res.Notes.Add($"{container} - {ex.Message}");
                }
            }

            try
            {
                if (File.Exists(RegistryPath)) File.Delete(RegistryPath);
                log($"[Улучшения] откат: реестр установленного очищен ({ids.Count} шт)");
            }
            catch (Exception ex)
            {
                log($"[Улучшения] откат: реестр не удалось стереть - {ex.Message}");
                res.Success = false;
                res.Message = ex.Message;
            }
            return res;
        }

        private static async Task EnsureCleanContainerAsync(RequiredContainer r,
            DownloadAsync download, Action<string, int, string?>? progress,
            Action<string> log, CancellationToken ct)
        {
            Directory.CreateDirectory(CleanDir);
            string dst = CleanCopyPath(r.Path);

            if (string.IsNullOrWhiteSpace(r.Sha256))
                throw new InvalidOperationException(
                    Loc.T("error.baseContainerNoSha", ("path", r.Path)));

            if (File.Exists(dst) && new FileInfo(dst).Length == r.SizeBytes &&
                Sha256(dst).Equals(r.Sha256, StringComparison.OrdinalIgnoreCase))
                return;

            if (string.IsNullOrWhiteSpace(r.Url))
                throw new InvalidOperationException(
                    Loc.T("error.baseContainerNoUrl", ("path", r.Path)));

            log($"[Улучшения] качаю чистый контейнер {r.Path} ({r.SizeBytes:N0} б)");
            progress?.Invoke("downloading", 8, Loc.T("install.baseContainer", ("file", Path.GetFileName(r.Path))));
            await download(r.Url, dst, null, ct);

            var got = Sha256(dst);
            if (!got.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(dst); } catch { }
                throw new InvalidOperationException(
                    Loc.T("error.baseContainerShaMismatch",
                        ("file", Path.GetFileName(r.Path)), ("expected", r.Sha256[..16]), ("actual", got[..16])));
            }
        }

        private static string? PlaceContainerIfMissing(string gtaRoot, RequiredContainer r, Action<string> log)
        {
            string live = SafePath.ResolveInside(gtaRoot, r.Path, "Путь базового контейнера улучшения");
            if (File.Exists(live)) return null;

            string src = CleanCopyPath(r.Path);
            if (!File.Exists(src))
                throw new InvalidOperationException(Loc.T("error.noLocalContainerCopy", ("path", r.Path)));

            Directory.CreateDirectory(Path.GetDirectoryName(live)!);
            File.Copy(src, live, overwrite: false);
            log($"[Улучшения] в игре не было {r.Path} - положил чистый ({r.SizeBytes:N0} б)");
            return live;
        }

        private static async Task EnsureBundleAsync(CatalogEntry e, DownloadAsync download,
            Action<string, int, string?>? progress, int band, Action<string> log, CancellationToken ct)
        {
            string dir = BundleDir(e.Id);
            string marker = Path.Combine(dir, ".sha256");

            if (File.Exists(Path.Combine(dir, "improvement.json")) &&
                File.Exists(marker) &&
                File.ReadAllText(marker).Trim().Equals(e.BundleSha256, StringComparison.OrdinalIgnoreCase))
                return;

            Directory.CreateDirectory(BundlesDir);
            string zip = SafePath.ResolveLeafInside(BundlesDir, e.Id + ".zip", "Идентификатор улучшения");

            log($"[Улучшения] качаю {e.Name} ({e.BundleSizeBytes:N0} б)");
            await download(e.BundleUrl, zip,
                (got, total) =>
                {
                    if (total > 0)
                        progress?.Invoke("downloading", band + (int)(20L * got / total),
                            Loc.T("install.downloadingMb",
                                ("name", e.Name), ("done", got / 1048576), ("total", total / 1048576)));
                }, ct);

            if (string.IsNullOrWhiteSpace(e.BundleSha256))
            {
                try { File.Delete(zip); } catch { }
                throw new InvalidOperationException(
                    Loc.T("error.bundleNoSha", ("name", e.Name)));
            }
            var actual = Sha256(zip);
            if (!actual.Equals(e.BundleSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(zip); } catch { }
                throw new InvalidOperationException(
                    Loc.T("error.bundleShaMismatch",
                        ("name", e.Name), ("expected", e.BundleSha256[..16]), ("actual", actual[..16])));
            }

            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
            progress?.Invoke("extracting", band + 20, Loc.T("install.extracting", ("name", e.Name)));
            ZipFile.ExtractToDirectory(zip, dir);
            try { File.Delete(zip); } catch { }

            if (!File.Exists(Path.Combine(dir, "improvement.json")))
                throw new InvalidOperationException(Loc.T("error.bundleNoImprovementJson", ("name", e.Name)));
            File.WriteAllText(marker, e.BundleSha256);
        }

        private static string FlatName(string relPath) => Norm(relPath).Replace('/', '_');

        private static string CleanCopyPath(string relPath) =>
            SafePath.ResolveLeafInside(CleanDir, FlatName(relPath), "Имя локальной копии контейнера");

        private static string Norm(string p) => SafePath.NormalizeRelative(p);

        private static string Sha256(string path)
        {
            using var fs = File.OpenRead(path);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }

        private static Result Fail(string msg) => new() { Success = false, Message = msg };
    }
}
