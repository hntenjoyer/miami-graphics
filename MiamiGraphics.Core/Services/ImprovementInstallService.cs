using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Injector;
using MiamiGraphics.Core.System;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{

    public sealed class ImprovementSlotFile
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public string Sha256 { get; set; } = "";
    }

    public sealed class ImprovementSlot
    {
        public string Container { get; set; } = "";
        public string Entry { get; set; } = "";
        public string Mode { get; set; } = "replace";
        public string? File { get; set; }
        public string? Dir { get; set; }
        public List<ImprovementSlotFile> Files { get; set; } = new();
    }

    public sealed class ImprovementUpdateFile
    {
        public string Target { get; set; } = "";
        public string File { get; set; } = "";
    }

    public sealed class ImprovementRegistration
    {
        public string Pack { get; set; } = "";
        public string Device { get; set; } = "";
        public string ChangeSet { get; set; } = "";
        public int SubPackCount { get; set; } = 1;
        public List<string> Rpf { get; set; } = new();
        public List<string> Ityp { get; set; } = new();
        public string ContentBaseFile { get; set; } = "";
        public string Setup2BaseFile { get; set; } = "";
    }

    public sealed class ImprovementManifest
    {
        public string Schema { get; set; } = "";
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string? Note { get; set; }
        public string? ExclusiveGroup { get; set; }
        public List<ImprovementSlot> Slots { get; set; } = new();
        public List<ImprovementUpdateFile> UpdateFiles { get; set; } = new();
        public List<ImprovementRegistration> Registration { get; set; } = new();

        [JsonIgnore] public string Dir { get; set; } = "";
    }

    public sealed class ImprovementApplyResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public List<string> Notes { get; set; } = new();
    }

    public sealed class ImprovementInstallService
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        public sealed class Request
        {
            public string GtaRoot { get; set; } = "";
            public IReadOnlyList<string> ImprovementDirs { get; set; } = Array.Empty<string>();
            public Func<string, string?> CleanContainerProvider { get; set; } = _ => null;
            public string? CleanUpdateRpfPath { get; set; }
            public IReadOnlyList<string> ContainersToReset { get; set; } = Array.Empty<string>();

            public IReadOnlyList<string> LeavingImprovementDirs { get; set; } = Array.Empty<string>();
        }

        public ImprovementApplyResult Apply(Request req, Action<string> log)
        {
            var result = new ImprovementApplyResult();
            var manifests = new List<ImprovementManifest>();

            foreach (var dir in req.ImprovementDirs)
            {
                string path = Path.Combine(dir, "improvement.json");
                if (!File.Exists(path))
                    return Fail(result, Loc.T("error.improvementJsonMissing", ("dir", dir)));
                var m = JsonSerializer.Deserialize<ImprovementManifest>(File.ReadAllText(path), Json);
                if (m == null) return Fail(result, Loc.T("error.improvementManifestUnreadable", ("path", path)));
                m.Dir = dir;
                manifests.Add(m);
                log($"[Улучшения] {m.Name} ({m.Id})");
            }

            var leavingManifests = new List<ImprovementManifest>();
            foreach (var dir in req.LeavingImprovementDirs)
            {
                try
                {
                    string path = Path.Combine(dir, "improvement.json");
                    if (!File.Exists(path)) continue;
                    var m = JsonSerializer.Deserialize<ImprovementManifest>(File.ReadAllText(path), Json);
                    if (m == null) continue;
                    m.Dir = dir;
                    leavingManifests.Add(m);
                }
                catch (Exception ex)
                {
                    log($"[Улучшения] манифест уходящего в '{dir}' не прочитался: {ex.Message}");
                }
            }

            var groups = manifests.Where(m => !string.IsNullOrWhiteSpace(m.ExclusiveGroup))
                                  .GroupBy(m => m.ExclusiveGroup!, StringComparer.OrdinalIgnoreCase)
                                  .Where(g => g.Count() > 1).ToList();
            if (groups.Count > 0)
            {
                var g = groups[0];
                return Fail(result, Loc.T("error.improvementExclusiveGroup",
                    ("group", g.Key), ("names", string.Join(", ", g.Select(x => x.Name)))));
            }

            var containers = manifests.SelectMany(m => m.Slots).Select(s => Norm(s.Container))
                                      .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var resetOnly = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var extra in req.ContainersToReset.Select(Norm))
                if (!containers.Contains(extra, StringComparer.OrdinalIgnoreCase))
                {
                    containers.Add(extra);
                    resetOnly.Add(extra);
                }

            foreach (var m in manifests)
            {
                foreach (var s in m.Slots)
                {
                    if (!SafePath.TryResolveInside(req.GtaRoot, s.Container, out _, out var whyC))
                        return Fail(result, Loc.T("error.improvementContainerOutsideGame",
                            ("name", m.Name), ("container", s.Container), ("why", whyC)));
                    if (!SafePath.IsSafeRelative(s.Entry) || s.Entry.IndexOfAny(new[] { '/', '\\' }) >= 0)
                        return Fail(result, Loc.T("error.improvementSlotNameBad", ("name", m.Name), ("entry", s.Entry)));
                    if (!string.IsNullOrEmpty(s.File) && !SafePath.IsSafeRelative(s.File))
                        return Fail(result, Loc.T("error.improvementSlotBlobOutside", ("name", m.Name), ("file", s.File)));
                    if (!string.IsNullOrEmpty(s.Dir) && !SafePath.IsSafeRelative(s.Dir))
                        return Fail(result, Loc.T("error.improvementOwnFilesOutside", ("name", m.Name), ("dir", s.Dir)));
                }
                foreach (var uf in m.UpdateFiles)
                {
                    if (!SafePath.IsSafeRelative(uf.File))
                        return Fail(result, Loc.T("error.improvementFileOutside", ("name", m.Name), ("file", uf.File)));
                    if (!SafePath.IsSafeRelative(uf.Target))
                        return Fail(result, Loc.T("error.improvementUpdateTargetBad", ("name", m.Name), ("target", uf.Target)));
                }
                foreach (var r in m.Registration)
                {
                    if (!SafePath.IsSafeRelative(r.ContentBaseFile) || !SafePath.IsSafeRelative(r.Setup2BaseFile))
                        return Fail(result, Loc.T("error.improvementRegistrationBaseOutside", ("name", m.Name)));
                    if (!SafePath.IsSafeRelative(r.Pack) || r.Pack.IndexOfAny(new[] { '/', '\\' }) >= 0)
                        return Fail(result, Loc.T("error.improvementPackNameBad", ("name", m.Name), ("pack", r.Pack)));
                }
            }

            foreach (var extra in resetOnly)
                if (!SafePath.TryResolveInside(req.GtaRoot, extra, out _, out var whyX))
                    return Fail(result, Loc.T("error.improvementContainerOutsideGame",
                        ("name", extra), ("container", extra), ("why", whyX)));

            foreach (var container in containers)
            {
                string live = SafePath.ResolveInside(req.GtaRoot, container, "Путь контейнера улучшения");

                if (!File.Exists(live))
                {
                    if (resetOnly.Contains(container)) continue;
                    return Fail(result, Loc.T("error.containerNotFound", ("path", live)));
                }

                string? clean = req.CleanContainerProvider(container);
                if (clean == null || !File.Exists(clean))
                {
                    if (resetOnly.Contains(container))
                    {
                        log($"[Улучшения] {container}: чистой копии нет - контейнер остаётся как есть");
                        result.Notes.Add(Loc.T("error.noCleanCopyForContainer", ("container", container)));
                        continue;
                    }
                    return Fail(result, Loc.T("error.noCleanCopyForContainer", ("container", container)));
                }

                log($"[Улучшения] {container}: восстановление чистого ({new FileInfo(clean).Length:N0} б)");
                File.Copy(clean, live, overwrite: true);

                if (resetOnly.Contains(container))
                {
                    result.Notes.Add($"{container} - возвращён чистым");
                    continue;
                }

                string? work = NgContainer.MakeReadableCopy(live);
                string editTarget = work ?? live;
                if (work != null) log($"[Улучшения] {container}: залочен, работаю на раскрытой копии");

                var byEntry = manifests
                    .SelectMany(m => m.Slots.Where(s => Norm(s.Container).Equals(container, StringComparison.OrdinalIgnoreCase))
                                            .Select(s => (Manifest: m, Slot: s)))
                    .GroupBy(x => x.Slot.Entry, StringComparer.OrdinalIgnoreCase);

                foreach (var entryGroup in byEntry)
                {
                    string entry = entryGroup.Key;
                    var items = entryGroup.ToList();

                    var replacers = items.Where(x => x.Slot.Mode.Equals("replace", StringComparison.OrdinalIgnoreCase)).ToList();
                    var mergers = items.Where(x => x.Slot.Mode.Equals("merge", StringComparison.OrdinalIgnoreCase)).ToList();

                    if (replacers.Count > 1)
                        return Fail(result, Loc.T("error.slotClaimedByMany",
                            ("container", container), ("entry", entry), ("count", replacers.Count)));
                    if (replacers.Count == 1 && mergers.Count > 0)
                        return Fail(result, Loc.T("error.slotClaimedWholeCannotMerge",
                            ("container", container), ("entry", entry),
                            ("owner", replacers[0].Manifest.Name),
                            ("others", string.Join(", ", mergers.Select(x => x.Manifest.Name)))));

                    byte[] blob;
                    if (replacers.Count == 1)
                    {
                        string f = SafePath.ResolveInside(replacers[0].Manifest.Dir,
                            replacers[0].Slot.File, "Блоб слота улучшения");
                        if (!File.Exists(f)) return Fail(result, Loc.T("error.slotBlobMissing", ("path", f)));
                        blob = File.ReadAllBytes(f);
                        log($"[Улучшения] {entry}: целиком от '{replacers[0].Manifest.Name}' ({blob.Length:N0} б)");
                        blob = SanitizeSlotBlob(blob, entry, replacers[0].Manifest.Name, log, out var poisoned);
                        if (poisoned != null) return Fail(result, poisoned);
                    }
                    else
                    {
                        var own = new List<(string Name, byte[] Bytes)>();
                        foreach (var (m, slot) in mergers)
                        {
                            string root = SafePath.ResolveInside(m.Dir, slot.Dir, "Папка своих файлов улучшения");
                            if (!Directory.Exists(root)) return Fail(result, Loc.T("error.ownFilesDirMissing", ("path", root)));
                            foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                                own.Add((f.Substring(root.Length + 1).Replace('\\', '/'), File.ReadAllBytes(f)));
                            log($"[Улучшения] {entry}: + '{m.Name}', своих файлов {Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length}");
                        }

                        var dup = own.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
                        if (dup != null)
                            return Fail(result, Loc.T("error.slotDuplicateFile", ("entry", entry), ("file", dup.Key)));

                        byte[]? baseBlob;
                        using (var look = new RpfSlotReader(editTarget)) baseBlob = look.Read(entry);
                        if (baseBlob == null) return Fail(result, Loc.T("error.slotMissingInClean", ("container", container), ("entry", entry)));

                        blob = BuildMergedSlot(baseBlob, entry, own, log);
                    }

                    ReplaceRootEntry(editTarget, entry, blob);
                    result.Notes.Add($"{container}:/{entry} - {blob.Length:N0} б");
                }

                string finalName = Path.GetFileName(live);
                ArchiveFixer.FixOrThrow(editTarget, finalName);

                if (work != null)
                {
                    File.Copy(work, live, overwrite: true);
                    NgContainer.DropCopy(work);
                }
                log($"[Улучшения] {container}: записан ({new FileInfo(live).Length:N0} б)");
            }

            var updateReplacements = new List<KeyValuePair<string, byte[]>>();
            var vanillaSkipped = new List<string>();
            var vanilla = VanillaLookup.Open(req.CleanUpdateRpfPath);
            using (vanilla)
            {
                foreach (var m in manifests)
                    foreach (var uf in m.UpdateFiles)
                    {
                        string f = SafePath.ResolveInside(m.Dir, uf.File, "Файл улучшения для update.rpf");
                        if (!File.Exists(f)) return Fail(result, Loc.T("error.improvementFileMissing", ("path", f)));
                        var bytes = File.ReadAllBytes(f);
                        if (vanilla.IsSameAsVanilla(uf.Target, bytes))
                        {
                            vanillaSkipped.Add(uf.Target);
                            continue;
                        }
                        updateReplacements.Add(new KeyValuePair<string, byte[]>(uf.Target, bytes));
                    }
            }
            if (vanillaSkipped.Count > 0)
            {
                log($"[Улучшения] пропущено {vanillaSkipped.Count} целей, идентичных ванильным " +
                    $"(перезапись только снесла бы чужие моды): {string.Join(", ", vanillaSkipped.Take(8))}" +
                    (vanillaSkipped.Count > 8 ? $" и ещё {vanillaSkipped.Count - 8}" : ""));
                result.Notes.Add($"пропущено ванильных целей: {vanillaSkipped.Count}");
            }

            var improvementOwned = CollectImprovementOwnedNames(
                req.ImprovementDirs, manifests.Concat(leavingManifests).ToList());

            foreach (var pack in manifests.SelectMany(m => m.Registration.Select(r => (m, r)))
                                          .GroupBy(x => x.r.Pack, StringComparer.OrdinalIgnoreCase))
            {
                var reqs = pack.Select(x => x.r).ToList();
                var owner = pack.First();

                string contentBase = SafePath.ResolveInside(owner.m.Dir, owner.r.ContentBaseFile, "База content.xml");
                string setupBase   = SafePath.ResolveInside(owner.m.Dir, owner.r.Setup2BaseFile, "База setup2.xml");
                if (!File.Exists(contentBase)) return Fail(result, Loc.T("error.baseContentXmlMissing", ("path", contentBase)));
                if (!File.Exists(setupBase))   return Fail(result, Loc.T("error.baseSetup2XmlMissing", ("path", setupBase)));

                var rpfs = reqs.SelectMany(r => r.Rpf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var ityps = reqs.SelectMany(r => r.Ityp).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                int subPacks = reqs.Max(r => Math.Max(1, r.SubPackCount));

                string content;
                try
                {
                    var vanillaBase = File.ReadAllText(contentBase);
                    var liveXml = ReadLivePackContentXml(req.GtaRoot, pack.Key);
                    var foreign = ForeignEnableItems(liveXml, vanillaBase, improvementOwned, owner.r.Device, log);
                    if (foreign.Count > 0)
                        log($"[Улучшения] {pack.Key}: переношу чужие записи ({foreign.Count}): {string.Join(", ", foreign)}");
                    content = BuildContentXml(vanillaBase,
                        owner.r.Device, owner.r.ChangeSet, rpfs, ityps, foreign);
                }
                catch (Exception ex)
                {
                    return Fail(result, Loc.T("error.contentXmlBuildFailed", ("pack", pack.Key), ("reason", ex.Message)));
                }
                string setup = SetSubPackCount(File.ReadAllText(setupBase), subPacks);

                updateReplacements.Add(new(
                    $"dlc_patch/{pack.Key}/content.xml", new global::System.Text.UTF8Encoding(false).GetBytes(content)));
                updateReplacements.Add(new(
                    $"dlc_patch/{pack.Key}/setup2.xml", new global::System.Text.UTF8Encoding(false).GetBytes(setup)));

                log($"[Улучшения] регистрация {pack.Key}: rpf {string.Join(", ", rpfs)}; " +
                    $"ityp {(ityps.Count == 0 ? "-" : string.Join(", ", ityps))}; subPackCount={subPacks}");
            }

            var stillRegistered = new HashSet<string>(
                manifests.SelectMany(m => m.Registration).Select(r => r.Pack),
                StringComparer.OrdinalIgnoreCase);
            foreach (var pack in leavingManifests.SelectMany(m => m.Registration.Select(r => (m, r)))
                                                 .GroupBy(x => x.r.Pack, StringComparer.OrdinalIgnoreCase))
            {
                if (stillRegistered.Contains(pack.Key)) continue;

                var owner = pack.First();
                string contentBase, setupBase;
                try
                {
                    contentBase = SafePath.ResolveInside(owner.m.Dir, owner.r.ContentBaseFile, "База content.xml");
                    setupBase   = SafePath.ResolveInside(owner.m.Dir, owner.r.Setup2BaseFile, "База setup2.xml");
                }
                catch (Exception ex)
                {
                    log($"[Улучшения] {pack.Key}: базы уходящего не читаются ({ex.Message}) - регистрация остаётся как есть");
                    result.Notes.Add($"{pack.Key}: не удалось сбросить регистрацию (нет баз)");
                    continue;
                }
                if (!File.Exists(contentBase) || !File.Exists(setupBase))
                {
                    log($"[Улучшения] {pack.Key}: баз уходящего нет на диске - регистрация остаётся как есть");
                    result.Notes.Add($"{pack.Key}: не удалось сбросить регистрацию (нет баз)");
                    continue;
                }

                var liveXml = ReadLivePackContentXml(req.GtaRoot, pack.Key);
                if (liveXml is null) continue;

                string content;
                try
                {
                    var vanillaBase = File.ReadAllText(contentBase);
                    var foreign = ForeignEnableItems(liveXml, vanillaBase, improvementOwned, owner.r.Device, log);
                    if (foreign.Count > 0)
                        log($"[Улучшения] {pack.Key}: сброс, переношу чужие записи ({foreign.Count}): {string.Join(", ", foreign)}");
                    content = foreign.Count > 0
                        ? BuildContentXml(vanillaBase, owner.r.Device, owner.r.ChangeSet,
                            Array.Empty<string>(), Array.Empty<string>(), foreign)
                        : vanillaBase;
                }
                catch (Exception ex)
                {
                    return Fail(result, Loc.T("error.contentXmlBuildFailed", ("pack", pack.Key), ("reason", ex.Message)));
                }

                if (string.Equals(liveXml.TrimStart('﻿').Trim(), content.TrimStart('﻿').Trim(), StringComparison.Ordinal))
                    continue;

                updateReplacements.Add(new(
                    $"dlc_patch/{pack.Key}/content.xml", new global::System.Text.UTF8Encoding(false).GetBytes(content)));
                updateReplacements.Add(new(
                    $"dlc_patch/{pack.Key}/setup2.xml", new global::System.Text.UTF8Encoding(false).GetBytes(File.ReadAllText(setupBase))));

                log($"[Улучшения] {pack.Key}: регистрация сброшена к ванильной базе");
                result.Notes.Add($"{pack.Key} - регистрация сброшена");
            }

            var poisonDeletions = new List<string>();
            if (!string.IsNullOrWhiteSpace(req.CleanUpdateRpfPath) && File.Exists(req.CleanUpdateRpfPath))
            {
                var handledPacks = new HashSet<string>(
                    manifests.Concat(leavingManifests).SelectMany(m => m.Registration).Select(r => r.Pack),
                    StringComparer.OrdinalIgnoreCase);
                string liveUpdate = Path.Combine(req.GtaRoot, "update", "update.rpf");

                foreach (var (target, device, changeSet) in PoisonedNgRegistrations)
                {
                    try
                    {
                        var pack = target.Split('/')[1];
                        if (handledPacks.Contains(pack)) continue;

                        var liveBytes = PatchCustomizationSupport.GetBytesFromArchiveExactPath(liveUpdate, target);
                        if (liveBytes is null) continue;
                        var vanillaBytes = PatchCustomizationSupport.GetBytesFromArchiveExactPath(req.CleanUpdateRpfPath, target);
                        if (vanillaBytes is null)
                        {
                            poisonDeletions.Add(target);
                            continue;
                        }

                        var wanted = vanillaBytes;
                        if (target.EndsWith("content.xml", StringComparison.OrdinalIgnoreCase) && device is not null)
                        {
                            var vanillaXml = new UTF8Encoding(false).GetString(vanillaBytes);
                            var liveXml = new UTF8Encoding(false).GetString(liveBytes);
                            var foreign = ForeignEnableItems(liveXml, vanillaXml, improvementOwned, device, log);
                            if (foreign.Count > 0)
                            {
                                log($"[Улучшения] санация {pack}: переношу чужие записи ({foreign.Count}): {string.Join(", ", foreign)}");
                                wanted = new UTF8Encoding(false).GetBytes(BuildContentXml(
                                    vanillaXml, device, changeSet!, Array.Empty<string>(), Array.Empty<string>(), foreign));
                            }
                        }

                        if (liveBytes.AsSpan().SequenceEqual(wanted)) continue;
                        updateReplacements.Add(new(target, wanted));
                        log($"[Улучшения] санация: {target} возвращён к ванильному ({liveBytes.Length:N0} → {wanted.Length:N0} б)");
                        result.Notes.Add($"санация: {target}");
                    }
                    catch (Exception ex)
                    {
                        log($"[Улучшения] санация {target}: пропущена ({ex.Message})");
                    }
                }
            }

            if (updateReplacements.Count > 0)
            {
                string updateRpf = Path.Combine(req.GtaRoot, "update", "update.rpf");
                if (!File.Exists(updateRpf)) return Fail(result, Loc.T("error.pathNotFound", ("path", updateRpf)));
                log($"[Улучшения] update.rpf: замен {updateReplacements.Count}");
                bool ok = PatchCustomizationSupport.ReplaceFilesInLiveArchive(
                    updateRpf, updateReplacements, out int applied, out var skipped,
                    addMissingPlainPaths: true);
                result.Notes.Add($"update.rpf - применено {applied}, пропущено {skipped.Count}");
                foreach (var s in skipped) result.Notes.Add($"  пропущен (нет в архиве): {s}");
                if (!ok) return Fail(result, Loc.T("error.updateRpfReplaceFailed"));
            }

            if (poisonDeletions.Count > 0)
            {
                string updateRpf = Path.Combine(req.GtaRoot, "update", "update.rpf");
                try
                {
                    int removed = DeletePlainPaths(updateRpf, poisonDeletions, log);
                    if (removed > 0) result.Notes.Add($"санация: удалено ядовитых регистраций {removed}");
                }
                catch (Exception ex)
                {
                    log($"[Улучшения] санация: удаление не удалось ({ex.Message}) - оставлено как есть");
                }
            }

            result.Success = true;
            result.Message = manifests.Count == 0
                ? Loc.T("misc.improvementsRemovedClean")
                : Loc.T("misc.improvementsApplied", ("count", manifests.Count));
            log("[Улучшения] " + result.Message);
            return result;
        }

        private static int DeletePlainPaths(string archivePath, IReadOnlyList<string> targets, Action<string> log)
        {
            using var arc = RageArchiveWrapper7.Open(archivePath);
            int removed = 0;
            foreach (var target in targets)
            {
                var parts = Norm(target).Split('/', StringSplitOptions.RemoveEmptyEntries);
                IArchiveDirectory? dir = arc.Root;
                for (int i = 0; i < parts.Length - 1 && dir != null; i++)
                    dir = dir.GetDirectories().FirstOrDefault(d =>
                        d.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                var file = dir?.GetFiles().FirstOrDefault(f =>
                    f.Name.Equals(parts[^1], StringComparison.OrdinalIgnoreCase));
                if (dir is null || file is null) continue;
                try
                {
                    dir.DeleteFile(file);
                    removed++;
                    log($"[Улучшения] санация: удалён {target} (в ванили его нет)");
                }
                catch (Exception ex)
                {
                    log($"[Улучшения] санация: {target} не удалился ({ex.Message})");
                }
            }
            if (removed > 0) arc.Flush();
            return removed;
        }

        private static void ReplaceRootEntry(string containerPath, string entryName, byte[] bytes)
        {
            using var arc = RageArchiveWrapper7.Open(containerPath);
            var f = arc.Root.GetFiles().FirstOrDefault(x => x.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase));
            if (f is not IArchiveBinaryFile bin)
                throw new InvalidOperationException(Loc.T("error.containerEntryMissing", ("entry", entryName)));

            bin.Import(new MemoryStream(bytes));
            bin.IsCompressed = false;
            bin.IsEncrypted = false;
            bin.UncompressedSize = bytes.LongLength;

            arc.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
            arc.Flush();
        }

        private static byte[] BuildMergedSlot(byte[] baseBlob, string entryName,
            IReadOnlyList<(string Name, byte[] Bytes)> own, Action<string> log)
        {
            string tmpDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics.Slot", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            string tmp = SafePath.ResolveLeafInside(tmpDir, entryName, "Имя слота улучшения");
            try
            {
                File.WriteAllBytes(tmp, baseBlob);

                string? open = NgContainer.MakeReadableCopy(tmp);
                if (open != null)
                {
                    File.Copy(open, tmp, overwrite: true);
                    NgContainer.DropCopy(open);
                }

                using (var arc = RageArchiveWrapper7.Open(tmp))
                {
                    DropFiller(arc.Root, entryName, log);
                    foreach (var (name, bytes) in own)
                        WriteInto(arc.Root, name, bytes);
                    arc.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                    arc.Flush();
                }

                ArchiveFixer.FixOrThrow(tmp, entryName);
                var result = File.ReadAllBytes(tmp);
                log($"[Улучшения] {entryName}: собран ({baseBlob.Length:N0} → {result.Length:N0} б, добавлено {own.Count})");
                return result;
            }
            finally
            {
                try { Directory.Delete(tmpDir, recursive: true); } catch { }
            }
        }

        private static byte[] SanitizeSlotBlob(byte[] blob, string entryName, string improvementName,
                                               Action<string> log, out string? poisoned)
        {
            poisoned = null;
            int dropped = 0;
            string tmpDir = Path.Combine(Path.GetTempPath(), "MiamiGraphics.Slot", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            string tmp = SafePath.ResolveLeafInside(tmpDir, entryName, "Имя слота улучшения");
            try
            {
                File.WriteAllBytes(tmp, blob);

                string? open = NgContainer.MakeReadableCopy(tmp);
                if (open != null)
                {
                    File.Copy(open, tmp, overwrite: true);
                    NgContainer.DropCopy(open);
                }

                using (var arc = RageArchiveWrapper7.Open(tmp))
                {
                    dropped = DropFiller(arc.Root, entryName, log);
                    if (dropped == 0) return blob;
                    arc.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;
                    arc.Flush();
                }

                ArchiveFixer.FixOrThrow(tmp, entryName);
                var cleaned = File.ReadAllBytes(tmp);
                log($"[Улучшения] {entryName}: блоб пересобран после санации " +
                    $"({blob.Length:N0} → {cleaned.Length:N0} б, выброшено записей: {dropped})");
                return cleaned;
            }
            catch (Exception ex)
            {
                if (dropped > 0)
                {
                    poisoned = Loc.T("error.slotBlobPoisoned",
                        ("name", improvementName), ("entry", entryName),
                        ("count", dropped.ToString()), ("detail", $"{ex.GetType().Name}: {ex.Message}"));
                    log($"[Улучшения] {entryName}: {poisoned}");
                    return blob;
                }

                log($"[Улучшения] {entryName}: санация блоба не удалась " +
                    $"({ex.GetType().Name}: {ex.Message}) - ставим как есть");
                return blob;
            }
            finally
            {
                try { Directory.Delete(tmpDir, recursive: true); } catch { }
            }
        }

        private static string BuildContentXml(string vanilla, string device, string changeSet,
            IReadOnlyList<string> rpfs, IReadOnlyList<string> ityps, IReadOnlyList<string> foreign)
        {
            var items = new StringBuilder();
            foreach (var r in rpfs)
                items.Append($@"
        <Item>
            <filename>{device}:/{r}</filename>
            <fileType>RPF_FILE</fileType>
            <locked value=""true""/>
            <disabled value=""true""/>
            <persistent value=""true""/>
            <overlay value=""true""/>
        </Item>");
            foreach (var i in ityps)
                items.Append($@"
        <Item>
            <filename>{device}:/{i}</filename>
            <fileType>DLC_ITYP_REQUEST</fileType>
            <overlay value=""false"" />
            <disabled value=""true"" />
            <persistent value=""false"" />
            <contents>CONTENTS_PROPS</contents>
        </Item>");

            int close = vanilla.LastIndexOf("</dataFiles>", StringComparison.OrdinalIgnoreCase);
            if (close < 0) throw new InvalidOperationException(Loc.T("error.baseNoDataFiles"));
            string xml = vanilla.Insert(close, items.ToString());

            var enable = new StringBuilder();
            foreach (var n in rpfs.Concat(ityps))
                enable.Append($"\n                <Item>{device}:/{n}</Item>");
            foreach (var f in foreign)
                enable.Append($"\n                <Item>{f}</Item>");
            return AddToChangeSet(xml, changeSet, enable.ToString());
        }

        private static List<string> ForeignEnableItems(
            string? live, string vanillaBase, IReadOnlySet<string> improvementOwned,
            string packDevice, Action<string>? log = null)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(live)) return result;

            var re = new global::System.Text.RegularExpressions.Regex(
                @"<Item>\s*([^<\s][^<]*?)\s*</Item>", global::System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var inVanilla = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (global::System.Text.RegularExpressions.Match m in re.Matches(vanillaBase))
                inVanilla.Add(m.Groups[1].Value);

            var droppedOwnDevice = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (global::System.Text.RegularExpressions.Match m in re.Matches(live))
            {
                var item = m.Groups[1].Value;
                if (item.Length == 0 || item.Contains('<')) continue;
                if (inVanilla.Contains(item)) continue;

                int slash = item.IndexOf(":/", StringComparison.Ordinal);
                var device = slash >= 0 ? item[..slash] : "";
                var name = slash >= 0 ? item[(slash + 2)..] : item;
                if (improvementOwned.Contains(name)) continue;

                if (device.Equals(packDevice, StringComparison.OrdinalIgnoreCase))
                {
                    if (droppedOwnDevice.Count < 16) droppedOwnDevice.Add(item);
                    continue;
                }

                if (seen.Add(item)) result.Add(item);
            }
            if (droppedOwnDevice.Count > 0)
                log?.Invoke($"[Улучшения] выброшены осколки чужого инсталлера на устройстве {packDevice} " +
                            $"({droppedOwnDevice.Count}): {string.Join(", ", droppedOwnDevice)}");
            return result;
        }

        private static IReadOnlySet<string> CollectImprovementOwnedNames(
            IReadOnlyList<string> dirs, IReadOnlyList<ImprovementManifest> manifests)
        {
            var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in manifests)
                foreach (var r in m.Registration)
                {
                    foreach (var x in r.Rpf) owned.Add(x);
                    foreach (var x in r.Ityp) owned.Add(x);
                }

            foreach (var d in dirs)
            {
                var parent = Path.GetDirectoryName(d);
                if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) continue;
                foreach (var sibling in Directory.EnumerateDirectories(parent))
                {
                    var mf = Path.Combine(sibling, "improvement.json");
                    if (!File.Exists(mf)) continue;
                    try
                    {
                        var man = JsonSerializer.Deserialize<ImprovementManifest>(File.ReadAllText(mf), Json);
                        if (man?.Registration is null) continue;
                        foreach (var r in man.Registration)
                        {
                            foreach (var x in r.Rpf) owned.Add(x);
                            foreach (var x in r.Ityp) owned.Add(x);
                        }
                    }
                    catch {}
                }
            }
            return owned;
        }

        private static string? ReadLivePackContentXml(string gtaRoot, string pack)
        {
            try
            {
                var updateRpf = Path.Combine(gtaRoot, "update", "update.rpf");
                if (!File.Exists(updateRpf)) return null;
                using var fs = new FileStream(updateRpf, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var arc = RageArchiveWrapper7.Open(fs, "update.rpf", leaveOpen: true);

                var dir = arc.Root.GetDirectories()
                    .FirstOrDefault(d => d.Name.Equals("dlc_patch", StringComparison.OrdinalIgnoreCase));
                dir = dir?.GetDirectories()
                    .FirstOrDefault(d => d.Name.Equals(pack, StringComparison.OrdinalIgnoreCase));
                var f = dir?.GetFiles()
                    .FirstOrDefault(x => x.Name.Equals("content.xml", StringComparison.OrdinalIgnoreCase));
                if (f is null) return null;
                return new UTF8Encoding(false).GetString(MiamiGraphics.Core.Parser.RpfRealBytes.Get(f));
            }
            catch { return null; }
        }

        private static string AddToChangeSet(string xml, string changeSet, string items)
        {
            int anchor = xml.IndexOf($"<changeSetName>{changeSet}</changeSetName>", StringComparison.OrdinalIgnoreCase);
            if (anchor < 0) throw new InvalidOperationException(Loc.T("error.noChangeSet", ("changeSet", changeSet)));

            int selfClosing = xml.IndexOf("<filesToEnable />", anchor, StringComparison.OrdinalIgnoreCase);
            int open = xml.IndexOf("<filesToEnable>", anchor, StringComparison.OrdinalIgnoreCase);
            if (selfClosing >= 0 && (open < 0 || selfClosing < open))
                return xml.Remove(selfClosing, "<filesToEnable />".Length)
                          .Insert(selfClosing, $"<filesToEnable>{items}</filesToEnable>");

            if (open < 0) throw new InvalidOperationException(Loc.T("error.changeSetNoFilesToEnable", ("changeSet", changeSet)));
            int close = xml.IndexOf("</filesToEnable>", open, StringComparison.OrdinalIgnoreCase);
            return xml.Insert(close, items);
        }

        private static string SetSubPackCount(string setupXml, int n)
        {
            var re = new global::System.Text.RegularExpressions.Regex(@"<subPackCount\s+value=""\d+""\s*/>");
            if (!re.IsMatch(setupXml))
                throw new InvalidOperationException(Loc.T("error.baseSetup2NoSubPackCount"));
            return re.Replace(setupXml, $@"<subPackCount value=""{n}"" />", 1);
        }

        private static readonly (string Target, string? Device, string? ChangeSet)[] PoisonedNgRegistrations =
        {
            ("dlc_patch/mphalloween/content.xml",    null, null),
            ("dlc_patch/mphalloween/setup2.xml",     null, null),
            ("dlc_patch/mpspecialraces/content.xml", null, null),
            ("dlc_patch/mpspecialraces/setup2.xml",  null, null),
            ("dlc_patch/patchday5ng/content.xml",    null, null),
            ("dlc_patch/patchday5ng/setup2.xml",     null, null),
            ("dlc_patch/mpheist/setup2.xml",         null, null),
            ("dlc_patch/mpvalentines2/content.xml",  null, null),
            ("dlc_patch/mpvalentines2/setup2.xml",   null, null),
            ("dlc_patch/patchday17ng/content.xml",   "dlc_PATCHDAY17NG", "CCS_PATCHDAY17_NG_STREAMING"),
            ("dlc_patch/patchday17ng/setup2.xml",    null, null),
        };

        private static int DropFiller(IArchiveDirectory root, string slotName, Action<string> log)
        {
            var doomed = new List<IArchiveFile>();
            foreach (var f in root.GetFiles())
            {
                if (f is not IArchiveBinaryFile bin) continue;
                var name = f.Name ?? "";

                if (RpfEntrySanity.NameIsGarbage(name) || RpfEntrySanity.NameIsNonAscii(name))
                {
                    log($"[Улучшения] {slotName}: выброшен мусор обфускации (непечатное имя, {SafeLen(bin):N0} б)");
                    doomed.Add(f);
                    continue;
                }

                bool resourceName = RpfEntrySanity.NameSaysResource(name);
                bool nestedRpfName = RpfEntrySanity.NameSaysNestedRpf(name);
                if (!resourceName && !nestedRpfName) continue;

                byte[] head;
                try
                {
                    using var ms = new MemoryStream();
                    bin.Export(ms);
                    head = ms.ToArray();
                }
                catch { continue; }

                if (nestedRpfName)
                {
                    if (IsRpf7(head)) continue;
                    log($"[Улучшения] {slotName}: выброшен фейковый вложенный rpf '{name}' ({head.Length:N0} б, не RPF7)");
                    doomed.Add(f);
                    continue;
                }

                if (IsRsc7(head)) continue;

                string magic = new string(head.Take(4)
                    .Select(b => b >= 32 && b < 127 ? (char)b : '.').ToArray());
                log($"[Улучшения] {slotName}: выброшена пустышка '{f.Name}' " +
                    $"({head.Length:N0} б в архиве, magic='{magic}' вместо RSC7)");
                doomed.Add(f);
            }

            int dropped = doomed.Count;
            foreach (var f in doomed) root.DeleteFile(f);

            var doomedDirs = root.GetDirectories()
                .Where(d => string.IsNullOrEmpty(d.Name) || d.Name.Any(ch => ch < 32 || ch > 126))
                .ToList();
            foreach (var d in doomedDirs)
            {
                try
                {
                    root.DeleteDirectory(d);
                    dropped++;
                    log($"[Улучшения] {slotName}: выброшен мусорный каталог обфускации");
                }
                catch (Exception ex)
                {
                    log($"[Улучшения] {slotName}: мусорный каталог не удалился ({ex.Message}) - оставлен");
                }
            }

            return dropped;
        }

        private static long SafeLen(IArchiveBinaryFile bin)
        {
            try { return bin.Size; } catch { return 0; }
        }

        private static void WriteInto(IArchiveDirectory root, string relPath, byte[] bytes)
        {
            var parts = relPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var dir = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var sub = dir.GetDirectories().FirstOrDefault(d => d.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (sub == null) { sub = dir.CreateDirectory(); sub.Name = parts[i]; }
                dir = sub;
            }

            string leaf = parts[^1];
            var existing = dir.GetFiles().FirstOrDefault(f => f.Name.Equals(leaf, StringComparison.OrdinalIgnoreCase));
            if (existing is IArchiveBinaryFile eb && !IsRsc7(bytes))
            {
                eb.Import(new MemoryStream(bytes));
                eb.IsCompressed = false;
                eb.IsEncrypted = false;
                eb.UncompressedSize = bytes.LongLength;
                return;
            }
            if (existing is IArchiveResourceFile er && IsRsc7(bytes))
            {
                er.Import(new MemoryStream(bytes));
                return;
            }

            if (IsRsc7(bytes))
            {
                var rf = dir.CreateResourceFile();
                rf.Name = leaf;
                rf.Import(new MemoryStream(bytes));
            }
            else
            {
                var bf = dir.CreateBinaryFile();
                bf.Name = leaf;
                bf.IsEncrypted = false;
                bf.IsCompressed = false;
                bf.UncompressedSize = bytes.LongLength;
                bf.Import(new MemoryStream(bytes));
            }
        }

        private static bool IsRsc7(byte[] d) => RpfEntrySanity.IsRsc7(d);

        private static bool IsRpf7(byte[] d) => RpfEntrySanity.IsRpf7(d);

        private static string Norm(string p) => SafePath.NormalizeRelative(p);

        private static ImprovementApplyResult Fail(ImprovementApplyResult r, string msg)
        {
            r.Success = false;
            r.Message = msg;
            return r;
        }

        private sealed class VanillaLookup : IDisposable
        {
            private readonly RageArchiveWrapper7? _arc;
            private readonly Stream? _fs;

            private VanillaLookup(RageArchiveWrapper7? arc, Stream? fs) { _arc = arc; _fs = fs; }

            public static VanillaLookup Open(string? cleanUpdateRpf)
            {
                if (string.IsNullOrWhiteSpace(cleanUpdateRpf) || !File.Exists(cleanUpdateRpf))
                    return new VanillaLookup(null, null);
                try
                {
                    var fs = new FileStream(cleanUpdateRpf, FileMode.Open, FileAccess.Read, FileShare.Read);
                    return new VanillaLookup(RageArchiveWrapper7.Open(fs, "update.rpf", leaveOpen: true), fs);
                }
                catch { return new VanillaLookup(null, null); }
            }

            public bool IsSameAsVanilla(string target, byte[] bytes)
            {
                if (_arc is null) return false;
                try
                {
                    var parts = Norm(target).Split('/');
                    IArchiveDirectory dir = _arc.Root;
                    for (int i = 0; i < parts.Length - 1; i++)
                    {
                        var next = dir.GetDirectories()
                            .FirstOrDefault(d => d.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                        if (next is null) return false;
                        dir = next;
                    }
                    var f = dir.GetFiles()
                        .FirstOrDefault(x => x.Name.Equals(parts[^1], StringComparison.OrdinalIgnoreCase));
                    if (f is null) return false;
                    var van = MiamiGraphics.Core.Parser.RpfRealBytes.Get(f);
                    return van.Length == bytes.Length && van.AsSpan().SequenceEqual(bytes);
                }
                catch { return false; }
            }

            public void Dispose()
            {
                try { _arc?.Dispose(); } catch { }
                try { _fs?.Dispose(); } catch { }
            }
        }

        private sealed class RpfSlotReader : IDisposable
        {
            private readonly RageArchiveWrapper7 _arc;
            public RpfSlotReader(string path) => _arc = RageArchiveWrapper7.OpenRead(path);

            public byte[]? Read(string entryName)
            {
                var f = _arc.Root.GetFiles().FirstOrDefault(x => x.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase));
                if (f == null) return null;
                using var ms = new MemoryStream();
                f.Export(ms);
                return ms.ToArray();
            }

            public void Dispose() { try { _arc.Dispose(); } catch { } }
        }
    }
}
