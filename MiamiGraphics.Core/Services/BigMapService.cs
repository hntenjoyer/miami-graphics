using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MiamiGraphics.Core.I18n;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.Core.Services
{
    public static class BigMapService
    {
        private static readonly string[] MarkerPaths =
        {
            BigMapAnalyzer.MapRpfTarget,
            BigMapAnalyzer.SpexRpfTarget,
        };

        public sealed record MutateResult(
            bool Ok,
            string? Error,
            IReadOnlyCollection<string> TouchedNestedRpfNames);

        public static bool IsInstalled(string updateRpfPath)
        {
            try
            {
                using var fs = new FileStream(updateRpfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var arc = RageArchiveWrapper7.Open(fs, "update.rpf", leaveOpen: true);
                return MarkerPaths.Any(p => FindPlainFile(arc.Root, p) is not null);
            }
            catch { return false; }
        }

        public static MutateResult Apply(string updateRpfPath, string packageDir, string backupDir, string? cleanRpfPath)
        {
            if (!File.Exists(updateRpfPath))
                return new MutateResult(false, Loc.T("error.updateRpfNotFoundAt", ("path", updateRpfPath)), Array.Empty<string>());
            var entries = BigMapAnalyzer.ReadManifest(packageDir);
            if (entries.Count == 0)
                return new MutateResult(false, Loc.T("error.mapPackageManifestEmpty"), Array.Empty<string>());

            entries = entries
                .Select(e => string.Equals(e.TargetPath, BigMapAnalyzer.LegacyImagesMetaTarget, StringComparison.OrdinalIgnoreCase)
                    ? e with { TargetPath = BigMapAnalyzer.ImagesMetaTarget }
                    : e)
                .ToList();

            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var backup = BackupState.Load(backupDir);

                using (var arc = PatchCustomizationSupport.OpenLiveArchiveWithRetry(updateRpfPath))
                {
                    bool installed = MarkerPaths.Any(p => FindPlainFile(arc.Root, p) is not null);
                    if (installed && backup.Entries.Count == 0)
                    {
                        if (string.IsNullOrWhiteSpace(cleanRpfPath) || !File.Exists(cleanRpfPath))
                            return new MutateResult(false,
                                Loc.T("error.foreignMapNoCleanRpfInstall"),
                                Array.Empty<string>());
                        SeedBackupFromClean(backup, cleanRpfPath, entries.Select(e => e.TargetPath));
                    }
                    else if (backup.Entries.Count > 0)
                    {
                        RestoreEntries(arc.Root, backup, touched);
                    }

                    foreach (var e in entries)
                    {
                        if (backup.Entries.ContainsKey(Norm(e.TargetPath))) continue;
                        backup.Entries[Norm(e.TargetPath)] = CaptureEntry(arc.Root, e.TargetPath, backupDir);
                    }
                    backup.Save(backupDir);

                    arc.Flush();
                }

                var writes = new List<KeyValuePair<string, byte[]>>(entries.Count);
                foreach (var e in entries)
                {
                    var src = MiamiGraphics.Core.System.SafePath.ResolveInside(
                        packageDir, e.SourceRel, "Файл пакета карты");
                    writes.Add(new KeyValuePair<string, byte[]>(e.TargetPath, File.ReadAllBytes(src)));
                    foreach (var part in e.TargetPath.Replace('\\', '/').Split('/').SkipLast(1))
                        if (part.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                            touched.Add(part);
                }

                bool wrote = PatchCustomizationSupport.ReplaceFilesInLiveArchive(
                    updateRpfPath, writes, out int applied, out var skipped,
                    addMissingPlainPaths: true);

                if (!wrote || applied != writes.Count)
                    return new MutateResult(false,
                        Loc.T("error.mapPartialWrite",
                            ("applied", applied.ToString()),
                            ("total", writes.Count.ToString()),
                            ("skipped", skipped.Count == 0 ? "-" : string.Join(", ", skipped))),
                        touched);

                DisableReduxVectorMaps(updateRpfPath, backupDir);

                return new MutateResult(true, null, touched);
            }
            catch (Exception ex)
            {
                return new MutateResult(false, $"{ex.GetType().Name}: {ex.Message}", touched);
            }
        }

        public static MutateResult Remove(string updateRpfPath, string backupDir, string? cleanRpfPath)
        {
            if (!File.Exists(updateRpfPath))
                return new MutateResult(false, Loc.T("error.updateRpfNotFoundAt", ("path", updateRpfPath)), Array.Empty<string>());

            var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var backup = BackupState.Load(backupDir);

                using (var arc = PatchCustomizationSupport.OpenLiveArchiveWithRetry(updateRpfPath))
                {
                    bool installed = MarkerPaths.Any(p => FindPlainFile(arc.Root, p) is not null);
                    if (!installed && backup.Entries.Count == 0)
                        return new MutateResult(true, null, touched);

                    if (backup.Entries.Count == 0)
                    {
                        if (string.IsNullOrWhiteSpace(cleanRpfPath) || !File.Exists(cleanRpfPath))
                            return new MutateResult(false,
                                Loc.T("error.foreignMapNoCleanRpfRemove"),
                                Array.Empty<string>());
                        SeedBackupFromClean(backup, cleanRpfPath, AllCanonicalTargets());
                    }

                    RestoreEntries(arc.Root, backup, touched);
                    arc.Flush();
                }

                ReEnableReduxVectorMaps(updateRpfPath, backupDir);

                using (var fs = new FileStream(updateRpfPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var arc2 = RageArchiveWrapper7.Open(fs, "update.rpf", leaveOpen: true))
                {
                    var leftover = MarkerPaths.FirstOrDefault(p => FindPlainFile(arc2.Root, p) is not null);
                    if (leftover != null)
                        return new MutateResult(false, Loc.T("error.cannotRemoveFromUpdateRpf", ("file", leftover)), touched);
                }

                BackupState.Delete(backupDir);
                return new MutateResult(true, null, touched);
            }
            catch (Exception ex)
            {
                return new MutateResult(false, $"{ex.GetType().Name}: {ex.Message}", touched);
            }
        }

        private static void DisableReduxVectorMaps(string updateRpfPath, string backupDir)
        {
            try
            {
                var hosts = FindStreamedRpfsWithMinimap(updateRpfPath);
                global::System.Diagnostics.Trace.WriteLine(
                    $"[bigmap.redux-override] архивов с картой: {hosts.Count} ({string.Join(", ", hosts)})");
                if (hosts.Count == 0) return;

                byte[]? xml;
                using (var arc = PatchCustomizationSupport.OpenLiveArchiveWithRetry(updateRpfPath))
                    xml = ReadEntryBytes(arc.Root, ContentXmlTarget);
                if (xml is null) return;

                var text = Encoding.UTF8.GetString(xml);
                var patched = text;
                var removed = new List<string>();
                foreach (var host in hosts)
                {
                    var next = RemoveFromFilesToEnable(patched, host);
                    if (next == patched) continue;
                    patched = next;
                    removed.Add(host);
                }
                if (removed.Count == 0)
                {
                    global::System.Diagnostics.Trace.WriteLine(
                        "[bigmap.redux-override] строки в filesToEnable не найдены - content.xml не тронут");
                    return;
                }

                var known = LoadReduxHosts(backupDir);
                foreach (var h in removed)
                    if (!known.Contains(h, StringComparer.OrdinalIgnoreCase))
                        known.Add(h);
                SaveReduxHosts(backupDir, known);

                PatchCustomizationSupport.ReplaceFilesInLiveArchive(
                    updateRpfPath,
                    new[] { new KeyValuePair<string, byte[]>(ContentXmlTarget, new UTF8Encoding(false).GetBytes(patched)) },
                    out int applied, out _, addMissingPlainPaths: false);
                global::System.Diagnostics.Trace.WriteLine(
                    $"[bigmap.redux-override] карта редукса выключена, content.xml переписан: {applied}");
            }
            catch (Exception ex)
            {
                global::System.Diagnostics.Trace.WriteLine($"[bigmap.redux-override] {ex}");
            }
        }

        private const string ContentXmlTarget = "content.xml";

        private static string RemoveFromFilesToEnable(string xml, string hostPath)
        {
            const char LF = (char)10;
            var needle = $"update:/{hostPath}";
            var kept = xml.Split(LF).Where(line =>
                !(line.Contains("<Item>", StringComparison.OrdinalIgnoreCase) &&
                  line.Contains(needle, StringComparison.OrdinalIgnoreCase)));
            return string.Join(LF.ToString(), kept);
        }

        private static void ReEnableReduxVectorMaps(string updateRpfPath, string backupDir)
        {
            try
            {
                var hosts = LoadReduxHosts(backupDir);
                if (hosts.Count == 0) return;

                byte[]? xml;
                using (var arc = PatchCustomizationSupport.OpenLiveArchiveWithRetry(updateRpfPath))
                    xml = ReadEntryBytes(arc.Root, ContentXmlTarget);
                if (xml is null) return;

                var text = Encoding.UTF8.GetString(xml);
                var patched = text;
                foreach (var host in hosts)
                    patched = AddToStreamingFilesToEnable(patched, host);
                if (patched == text)
                {
                    global::System.Diagnostics.Trace.WriteLine(
                        "[bigmap.redux-override] возвращать нечего - строки уже на месте");
                    return;
                }

                PatchCustomizationSupport.ReplaceFilesInLiveArchive(
                    updateRpfPath,
                    new[] { new KeyValuePair<string, byte[]>(ContentXmlTarget, new UTF8Encoding(false).GetBytes(patched)) },
                    out int applied, out _, addMissingPlainPaths: false);
                global::System.Diagnostics.Trace.WriteLine(
                    $"[bigmap.redux-override] карта редукса включена обратно ({hosts.Count}), content.xml переписан: {applied}");
            }
            catch (Exception ex)
            {
                global::System.Diagnostics.Trace.WriteLine($"[bigmap.redux-override] {ex}");
            }
        }

        private static string AddToStreamingFilesToEnable(string xml, string hostPath)
        {
            const char LF = (char)10;
            var needle = $"update:/{hostPath}";
            var lines = xml.Split(LF).ToList();

            if (lines.Any(l => l.Contains("<Item>", StringComparison.OrdinalIgnoreCase) &&
                               l.Contains(needle, StringComparison.OrdinalIgnoreCase)))
                return xml;

            int cs = lines.FindIndex(l => l.Contains(StreamingChangeSet, StringComparison.OrdinalIgnoreCase));
            if (cs < 0) return xml;
            int close = lines.FindIndex(cs, l => l.Contains("</filesToEnable>", StringComparison.OrdinalIgnoreCase));
            if (close < 0) return xml;

            if (lines[close].Contains("<filesToEnable>", StringComparison.OrdinalIgnoreCase))
            {
                var line = lines[close];
                int at = line.IndexOf("</filesToEnable>", StringComparison.OrdinalIgnoreCase);
                lines[close] = line[..at];
                lines.Insert(close + 1, line[at..]);
                close++;
            }

            var closing = lines[close];
            var indent = new string(' ', closing.Length - closing.TrimStart().Length + 4);
            var cr = closing.EndsWith("\r", StringComparison.Ordinal) ? "\r" : "";
            lines.Insert(close, $"{indent}<Item>{needle}</Item>{cr}");
            return string.Join(LF.ToString(), lines);
        }

        private const string StreamingChangeSet = "CCS_TITLE_UPDATE_STREAMING";

        private const string ReduxHostsFile = "redux_map_hosts.json";

        private static List<string> LoadReduxHosts(string backupDir)
        {
            try
            {
                var path = Path.Combine(backupDir, ReduxHostsFile);
                if (!File.Exists(path)) return new List<string>();
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))
                       ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        private static void SaveReduxHosts(string backupDir, IEnumerable<string> hosts)
        {
            Directory.CreateDirectory(backupDir);
            File.WriteAllText(Path.Combine(backupDir, ReduxHostsFile),
                JsonSerializer.Serialize(hosts, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }

        private static List<string> FindStreamedRpfsWithMinimap(string updateRpfPath)
        {
            var found = new List<string>();

            using var fs = new FileStream(updateRpfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var arc = RageArchiveWrapper7.Open(fs, "update.rpf", leaveOpen: true);

            var xml = ReadEntryBytes(arc.Root, "content.xml");
            if (xml is null) return found;

            var text = Encoding.UTF8.GetString(xml);
            foreach (var raw in ExtractUpdateRpfPaths(text))
            {
                if (raw.Contains("%PLATFORM%", StringComparison.OrdinalIgnoreCase)) continue;
                if (raw.StartsWith("x64/", StringComparison.OrdinalIgnoreCase)) continue;
                if (ReadEntryBytes(arc.Root, $"{raw}/{BigMapAnalyzer.CanonTiles[0]}") is not null)
                    found.Add(raw);
            }
            return found;
        }

        private static byte[]? ReadEntryBytes(IArchiveDirectory root, string path)
        {
            byte[]? result = null;
            WalkTo(root, path.Split('/'), 0, (dir, name, _) =>
            {
                var f = dir.GetFiles().FirstOrDefault(x =>
                    x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (f is null) return;
                using var ms = new MemoryStream();
                if (f is IArchiveBinaryFile bin)
                {
                    using var raw = bin.GetStream();
                    raw.CopyTo(ms);
                }
                else f.Export(ms);
                result = ms.ToArray();
            }, null, write: false);
            return result;
        }

        private static IEnumerable<string> ExtractUpdateRpfPaths(string contentXml)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const string marker = "update:/";
            int i = 0;
            while ((i = contentXml.IndexOf(marker, i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int start = i + marker.Length;
                int end = contentXml.IndexOfAny(new[] { '<', '"', ' ', '\r', '\n' }, start);
                if (end < 0) break;
                var path = contentXml[start..end].Trim().Replace('\\', '/');
                i = end;
                if (path.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && seen.Add(path))
                    yield return path;
            }
        }

        private static void WriteNestedBlob(string archivePath, string internalPath, byte[] bytes)
        {
            using var arc = RageArchiveWrapper7.Open(archivePath);
            WalkTo(arc.Root, internalPath.Split('/'), 0, (dir, name, _) =>
            {
                var f = dir.GetFiles().FirstOrDefault(x =>
                    x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (f is not IArchiveBinaryFile bin) return;
                bin.Import(new MemoryStream(bytes));
                bin.IsCompressed = false;
                bin.IsEncrypted = false;
                bin.UncompressedSize = bytes.LongLength;
            }, null, write: true);
            arc.Flush();
        }

        private static List<string> RootFileNames(string archivePath)
        {
            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var arc = RageArchiveWrapper7.Open(fs, Path.GetFileName(archivePath), leaveOpen: true);
            return arc.Root.GetFiles().Select(f => f.Name).ToList();
        }

        private static Dictionary<string, byte[]> ReadPackMapRpfEntries(
            string packageDir, IReadOnlyList<BigMapAnalyzer.PlanEntry> entries)
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var mapEntry = entries.FirstOrDefault(e =>
                e.TargetPath.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase));
            if (mapEntry is null) return result;

            var path = MiamiGraphics.Core.System.SafePath.ResolveInside(
                packageDir, mapEntry.SourceRel, "Файл пакета карты");
            if (!File.Exists(path)) return result;

            string? opened = Injector.NgContainer.MakeReadableCopy(path);
            var readPath = opened ?? path;
            try
            {
            using var fs = new FileStream(readPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var arc = RageArchiveWrapper7.Open(fs, Path.GetFileName(readPath), leaveOpen: true);
            foreach (var f in arc.Root.GetFiles())
            {
                try
                {
                    using var ms = new MemoryStream();
                    f.Export(ms);
                    result[f.Name] = ms.ToArray();
                }
                catch {}
            }
            return result;
            }
            finally { Injector.NgContainer.DropCopy(opened); }
        }

        private static IEnumerable<string> AllCanonicalTargets()
        {
            yield return BigMapAnalyzer.MapRpfTarget;
            yield return BigMapAnalyzer.SpexRpfTarget;
            yield return BigMapAnalyzer.ImagesMetaTarget;
            yield return BigMapAnalyzer.LegacyImagesMetaTarget;
            yield return BigMapAnalyzer.YmtTarget;
            yield return BigMapAnalyzer.ZoomTarget;
            foreach (var t in BigMapAnalyzer.CanonTiles)
                yield return $"{BigMapAnalyzer.GenericRpfTarget}/{t}";
            yield return $"{BigMapAnalyzer.MinimapRpfDir}/minimap_main_map.gfx";
            yield return $"{BigMapAnalyzer.MinimapRpfDir}/int3232302352.gfx";
        }

        private sealed class BackupEntry
        {
            public bool Existed { get; set; }
            public string Kind { get; set; } = "binary";
            public bool IsCompressed { get; set; }
            public bool IsEncrypted { get; set; }
            public uint UncompressedSize { get; set; }
            public string? BlobFile { get; set; }
        }

        private sealed class BackupState
        {
            public Dictionary<string, BackupEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);
            public string? PendingDir { get; private set; }

            public static BackupState Load(string dir)
            {
                var s = new BackupState { PendingDir = dir };
                var manifest = Path.Combine(dir, "backup.json");
                if (!File.Exists(manifest)) return s;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
                    foreach (var p in doc.RootElement.GetProperty("entries").EnumerateObject())
                    {
                        var v = p.Value;
                        s.Entries[p.Name] = new BackupEntry
                        {
                            Existed = v.GetProperty("existed").GetBoolean(),
                            Kind = v.GetProperty("kind").GetString() ?? "binary",
                            IsCompressed = v.GetProperty("isCompressed").GetBoolean(),
                            IsEncrypted = v.GetProperty("isEncrypted").GetBoolean(),
                            UncompressedSize = v.GetProperty("uncompressedSize").GetUInt32(),
                            BlobFile = v.TryGetProperty("blobFile", out var b) ? b.GetString() : null,
                        };
                    }
                }
                catch { s.Entries.Clear(); }

                s.Entries.Remove(Norm(ContentXmlTarget));
                return s;
            }

            public void Save(string dir)
            {
                Directory.CreateDirectory(dir);
                var obj = new
                {
                    schema = 1,
                    entries = Entries.ToDictionary(kv => kv.Key, kv => new
                    {
                        existed = kv.Value.Existed,
                        kind = kv.Value.Kind,
                        isCompressed = kv.Value.IsCompressed,
                        isEncrypted = kv.Value.IsEncrypted,
                        uncompressedSize = kv.Value.UncompressedSize,
                        blobFile = kv.Value.BlobFile,
                    }),
                };
                File.WriteAllText(Path.Combine(dir, "backup.json"),
                    JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));
            }

            public static void Delete(string dir)
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        private static BackupEntry CaptureEntry(IArchiveDirectory root, string targetPath, string backupDir)
        {
            var result = new BackupEntry { Existed = false };
            WalkTo(root, Norm(targetPath).Split('/'), 0,
                callback: (dir, name, _) =>
                {
                    var found = dir.GetFiles().FirstOrDefault(f =>
                        f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (found is null) return;

                    Directory.CreateDirectory(backupDir);
                    var blobName = "blob_" + Norm(targetPath).Replace('/', '_') + ".bin";
                    using var ms = new MemoryStream();
                    if (found is IArchiveBinaryFile rawBin)
                    {
                        using var raw = rawBin.GetStream();
                        raw.CopyTo(ms);
                    }
                    else found.Export(ms);
                    File.WriteAllBytes(Path.Combine(backupDir, blobName), ms.ToArray());

                    result.Existed = true;
                    result.BlobFile = blobName;
                    if (found is IArchiveBinaryFile bin)
                    {
                        result.Kind = "binary";
                        result.IsCompressed = bin.IsCompressed;
                        result.IsEncrypted = bin.IsEncrypted;
                        result.UncompressedSize = (uint)bin.UncompressedSize;
                    }
                    else result.Kind = "resource";
                },
                touched: null, write: false);
            return result;
        }

        private static void SeedBackupFromClean(BackupState backup, string cleanRpfPath, IEnumerable<string> targets)
        {
            using var fs = new FileStream(cleanRpfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var clean = RageArchiveWrapper7.Open(fs, "update.rpf", leaveOpen: true);
            string backupDir = backup.PendingDir ?? throw new InvalidOperationException("backup dir not set");
            foreach (var t in targets)
            {
                if (backup.Entries.ContainsKey(Norm(t))) continue;
                backup.Entries[Norm(t)] = CaptureEntry(clean.Root, t, backupDir);
            }
        }

        private static string Norm(string p) => p.Replace('\\', '/').TrimStart('/').ToLowerInvariant();

        private static IArchiveFile? FindPlainFile(IArchiveDirectory root, string path)
        {
            var parts = Norm(path).Split('/');
            var dir = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                dir = dir?.GetDirectories().FirstOrDefault(d =>
                    d.Name.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
                if (dir is null) return null;
            }
            return dir!.GetFiles().FirstOrDefault(f =>
                f.Name.Equals(parts[^1], StringComparison.OrdinalIgnoreCase));
        }

        private static void UpsertFile(IArchiveDirectory root, string path, byte[] bytes, ISet<string> touched)
        {
            WalkTo(root, Norm(path).Split('/'), 0,
                callback: (dir, name, _) => UpsertInDirectory(dir, name, bytes),
                touched: touched, write: true);

            var last = Norm(path).Split('/')[^1];
            if (last.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && IsOpenRpf(bytes))
                touched.Add(last);
        }

        private static bool IsOpenRpf(byte[] b)
        {
            if (b.Length < 16) return false;
            uint enc = BitConverter.ToUInt32(b, 12);
            return enc == 0 || enc == 0x4E45504F;
        }

        private static void RestoreEntries(IArchiveDirectory root, BackupState backup, ISet<string> touched)
        {
            foreach (var (target, e) in backup.Entries)
            {
                if (e.Existed)
                {
                    var blob = File.ReadAllBytes(Path.Combine(backup.PendingDir!, e.BlobFile!));
                    WalkTo(root, target.Split('/'), 0,
                        callback: (dir, name, _) => RestoreInDirectory(dir, name, blob, e),
                        touched: touched, write: true);
                }
                else
                {
                    WalkTo(root, target.Split('/'), 0,
                        callback: (dir, name, _) =>
                        {
                            var f = dir.GetFiles().FirstOrDefault(x =>
                                x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                            if (f is not null) dir.DeleteFile(f);
                        },
                        touched: touched, write: true);
                }
            }
        }

        private static void WalkTo(
            IArchiveDirectory dir, string[] parts, int index,
            Action<IArchiveDirectory, string, bool> callback, ISet<string>? touched,
            bool write, bool insideNested = false)
        {
            string part = parts[index];

            if (index == parts.Length - 1)
            {
                callback(dir, part, insideNested);
                return;
            }

            var sub = dir.GetDirectories().FirstOrDefault(d =>
                d.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (sub is not null)
            {
                WalkTo(sub, parts, index + 1, callback, touched, write, insideNested);
                return;
            }

            if (part.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
            {
                var nestedEntry = dir.GetFiles().FirstOrDefault(f =>
                    f.Name.Equals(part, StringComparison.OrdinalIgnoreCase)) as IArchiveBinaryFile;
                if (nestedEntry is null)
                {
                    if (!write) return;
                    throw new FileNotFoundException(Loc.T("error.nestedRpfNotFound", ("name", part)));
                }

                var stream = nestedEntry.GetStream();
                using var nested = RageArchiveWrapper7.Open(stream, nestedEntry.Name, leaveOpen: true);
                WalkTo(nested.Root, parts, index + 1, callback, touched, write, insideNested: true);
                if (write)
                {
                    nested.Flush();
                    touched?.Add(nestedEntry.Name);
                }
                return;
            }

            if (!write) return;

            var created = dir.CreateDirectory();
            created.Name = part;
            WalkTo(created, parts, index + 1, callback, touched, write, insideNested);
        }

        private static void UpsertInDirectory(IArchiveDirectory dir, string name, byte[] bytes)
        {
            bool isRsc7 = bytes.Length >= 4 && bytes[0] == 0x52 && bytes[1] == 0x53 && bytes[2] == 0x43;
            var existing = dir.GetFiles().FirstOrDefault(f =>
                f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (existing is IArchiveResourceFile rf)
            {
                rf.Import(new MemoryStream(bytes));
                return;
            }
            if (existing is IArchiveBinaryFile bf)
            {
                bf.Import(new MemoryStream(bytes));
                bf.IsCompressed = false;
                bf.IsEncrypted = false;
                bf.UncompressedSize = (uint)bytes.Length;
                return;
            }

            if (isRsc7)
            {
                var nf = dir.CreateResourceFile();
                nf.Name = name;
                nf.Import(new MemoryStream(bytes));
            }
            else
            {
                var nf = dir.CreateBinaryFile();
                nf.Name = name;
                nf.Import(new MemoryStream(bytes));
                nf.IsCompressed = false;
                nf.IsEncrypted = false;
                nf.UncompressedSize = (uint)bytes.Length;
            }
        }

        private static void RestoreInDirectory(IArchiveDirectory dir, string name, byte[] rawBytes, BackupEntry meta)
        {
            var existing = dir.GetFiles().FirstOrDefault(f =>
                f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (meta.Kind == "resource")
            {
                if (existing is IArchiveResourceFile rf) { rf.Import(new MemoryStream(rawBytes)); return; }
                if (existing is not null) dir.DeleteFile(existing);
                var nf = dir.CreateResourceFile();
                nf.Name = name;
                nf.Import(new MemoryStream(rawBytes));
                return;
            }

            if (existing is not null && existing is not IArchiveBinaryFile)
            {
                dir.DeleteFile(existing);
                existing = null;
            }
            var b = existing as IArchiveBinaryFile ?? dir.CreateBinaryFile();
            if (existing is null) b.Name = name;
            b.Import(new MemoryStream(rawBytes));
            b.IsCompressed = meta.IsCompressed;
            b.IsEncrypted = meta.IsEncrypted;
            b.UncompressedSize = meta.UncompressedSize;
        }
    }
}
