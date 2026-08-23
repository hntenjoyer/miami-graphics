using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Parser;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Services
{
    public enum LegitSeverity { Neutral, Visual, Warning, Danger }

    public enum LegitVerdictKind { Safe, Mixed, Danger }

    public sealed class LegitFieldDiff
    {
        public string Owner { get; set; } = "";
        public string Field { get; set; } = "";
        public string CleanValue { get; set; } = "";
        public string ModValue { get; set; } = "";
        public double? DeltaPercent { get; set; }
        public bool IsRed { get; set; }
    }

    public sealed class LegitFileFinding
    {
        public string Path { get; set; } = "";
        public string Change { get; set; } = "changed";
        public LegitSeverity Severity { get; set; } = LegitSeverity.Neutral;
        public string CategoryLabel { get; set; } = "";
        public string Note { get; set; } = "";
        public bool FormatOnly { get; set; }
        public long Size { get; set; }
        public bool WeaponRelated { get; set; }
        public List<LegitFieldDiff> FieldDiffs { get; set; } = new();
    }

    public sealed class LegitReport
    {
        public LegitVerdictKind Verdict { get; set; } = LegitVerdictKind.Safe;
        public string VerdictTitle { get; set; } = "";
        public string VerdictText { get; set; } = "";
        public List<string> VerdictReasons { get; set; } = new();
        public string Source { get; set; } = "";
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
        public int DangerCount { get; set; }
        public int WarningCount { get; set; }
        public int ChangedCount { get; set; }
        public int AddedCount { get; set; }
        public int DeletedCount { get; set; }
        public List<LegitFileFinding> Findings { get; set; } = new();
        public List<string> Unverified { get; set; } = new();
        public int CheckedCount { get; set; }
    }

    public sealed class LegitCheckService
    {

        private static readonly HashSet<string> RedFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "RecoilShakeAmplitude", "RecoilShakeHash", "RecoilShakeHashFirstPerson",
            "RecoilAccuracyMax", "RecoilErrorTime", "RecoilRecoveryRate",
            "MinTimeBetweenRecoilShakes", "IkRecoilDisplacement", "IkRecoilDisplacementScope",
            "IkRecoilDisplacementScaleBackward", "IkRecoilDisplacementScaleVertical",
            "ExplosionShakeAmplitude", "AccuracyOffsetShakeHash",
            "AccuracySpread", "BatchSpread", "BulletsInBatch",
            "AccurateModeAccuracyModifier", "RunAndGunAccuracyModifier", "RunAndGunAccuracyMinOverride",
            "RecoilAccuracyToAllowHeadShotPlayer", "RecoilAccuracyToAllowHeadShotAI",
            "MinHeadShotDistancePlayer", "MaxHeadShotDistancePlayer",
            "MinHeadShotDistanceAI", "MaxHeadShotDistanceAI",
            "LockOnRange", "WeaponRange",
            "Damage", "DamageFallOffRangeMin", "DamageFallOffRangeMax", "DamageFallOffModifier",
            "HeadShotDamageModifierPlayer", "HeadShotDamageModifierAI",
            "NetworkPlayerDamageModifier", "NetworkPedDamageModifier", "NetworkHeadShotPlayerDamageModifier",
            "HitLimbsDamageModifier", "NetworkHitLimbsDamageModifier", "LightlyArmouredDamageModifier",
            "TimeBetweenShots", "ClipSize", "AnimReloadRate",
            "DamageTime", "DamageTimeInVehicle", "DamageTimeInVehicleHeadShot",
            "FirstPersonScopeFov", "FirstPersonScopeOffset", "FirstPersonScopeRotationOffset",
            "FirstPersonScopeAttachmentOffset", "FirstPersonScopeAttachmentRotationOffset",
            "AccuracyModifier", "DamageModifier",
            "AnimFireRateModifier", "AnimBlindFireRateModifier", "AnimWantingToShootFireRateModifier",
            "PLAYER_RECOIL_MODIFIER_MIN", "PLAYER_RECOIL_MODIFIER_MAX", "PLAYER_RECOIL_CROUCHED_MODIFIER",
            "PLAYER_BLIND_FIRE_MODIFIER_MIN", "PLAYER_BLIND_FIRE_MODIFIER_MAX",
            "PLAYER_RECENTLY_DAMAGED_MODIFIER", "AI_GLOBAL_MODIFIER",
            "LockOnRangeModifier", "NoReticuleLockOnRangeModifier", "NoReticuleMaxLockOnRange",
            "LockOnDistanceRejectionModifier",
            "DefaultHealth", "DefaultArmour", "FatiguedHealthThreshold", "InjuredHealthThreshold",
            "DyingHealthThreshold", "HurtHealthThreshold", "Invincible",
            "EndRadius",
        };

        private static readonly HashSet<string> RedFlagTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "CanLockonOnFoot", "CanLockonInVehicle",
        };

        private static string FieldCategory(string field)
        {
            var f = field.ToLowerInvariant();
            if (f.Contains("recoil") || f.Contains("shake")) return Loc.T("legit.catRecoil");
            if (f.Contains("spread") || f.Contains("accuracy")) return Loc.T("legit.catSpread");
            if (f.Contains("headshot") || f.Contains("lockon") || f == "weaponrange") return Loc.T("legit.catAim");
            if (f.Contains("damage") || f == "timebetweenshots" || f == "clipsize" || f.Contains("firerate") || f.Contains("reload")) return Loc.T("legit.catDamage");
            if (f.Contains("firstperson") || f.Contains("fov")) return Loc.T("legit.catViewmodel");
            if (f.Contains("health") || f.Contains("armour") || f == "invincible") return Loc.T("legit.catHpArmor");
            return Loc.T("legit.catShooting");
        }

        private static readonly string[] PresenceFlagLeafs =
        {
            "playertargetting.meta", "healthconfig.meta",
            "pedtargetevaluator.meta", "playerinfo.meta",
        };

        private enum FileClass
        {
            WeaponMeta,
            ComponentsMeta,
            AnimationsMeta,
            PedAccuracy, PedHealth, PedBounds, Explosion,
            TuneBinary,
            CamerasBinary,
            YellowMeta,
            Visual,
            UnknownXml,
            Neutral,
        }

        private static readonly HashSet<string> NonWeaponAiMetas = new(StringComparer.OrdinalIgnoreCase)
        {
            "loadouts.meta", "combatbehaviour.meta", "weapontargetsequences.meta",
            "scenarios.meta", "vehiclelayouts.meta", "taskdata.meta", "relationships.meta",
        };

        private static FileClass ClassifyPath(string innerPath)
        {
            string p = innerPath.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
            string leaf = p.Contains('/') ? p[(p.LastIndexOf('/') + 1)..] : p;

            if (leaf == "minimap.ymt") return FileClass.Visual;

            if (leaf is "playertargetting.ymt" or "pedtargetevaluator.ymt")
                return FileClass.TuneBinary;
            if (leaf == "cameras.ymt") return FileClass.CamerasBinary;
            if (leaf == "pedaccuracy.meta") return FileClass.PedAccuracy;
            if (leaf == "pedhealth.meta" || leaf == "healthconfig.meta") return FileClass.PedHealth;
            if (leaf == "pedbounds.xml") return FileClass.PedBounds;
            if (leaf == "weaponcomponents.meta") return FileClass.ComponentsMeta;
            if (leaf == "weaponanimations.meta") return FileClass.AnimationsMeta;
            if (PresenceFlagLeafs.Contains(leaf)) return FileClass.UnknownXml;
            if (leaf.EndsWith(".ydr.xml")) return FileClass.UnknownXml;

            if (leaf is "playerswitch.meta") return FileClass.Neutral;

            if (leaf is "content.xml" or "setup2.xml") return FileClass.Neutral;
            if (leaf == "mapzoomdata.meta") return FileClass.Visual;
            if (leaf is "explosion.meta" or "explosion.ymt") return FileClass.Explosion;

            if (leaf is "pickups.meta" or "shop_weapon.meta" or "weaponarchetypes.meta")
                return FileClass.YellowMeta;
            if (NonWeaponAiMetas.Contains(leaf)) return FileClass.YellowMeta;

            bool inAiDir = p.Contains("/data/ai/") || p.StartsWith("data/ai/");
            if (inAiDir && leaf.EndsWith(".meta")) return FileClass.WeaponMeta;
            if (leaf.StartsWith("vehicleweapon") && leaf.EndsWith(".meta")) return FileClass.WeaponMeta;

            if (p.Contains("/timecycle/") || p.Contains("/weather/") || p.Contains("cloudhat")
                || leaf is "visualsettings.dat" or "weaponfx.dat" or "clouds.xml" or "cloudkeyframes.xml"
                    or "distant_lights.dat" or "distant_lights_hd.dat" or "water.xml" or "waterreflection.xml")
                return FileClass.Visual;
            if (leaf.EndsWith(".gfx") || leaf.EndsWith(".ytd") || leaf.EndsWith(".ydr")
                || leaf.EndsWith(".ydd") || leaf.EndsWith(".yft") || leaf.EndsWith(".ycd")
                || leaf.EndsWith(".awc") || leaf.EndsWith(".rel") || leaf.EndsWith(".ypt")
                || leaf.EndsWith(".ytyp") || leaf.EndsWith(".ymap") || leaf.EndsWith(".ynv"))
                return FileClass.Visual;

            if (p.StartsWith("x64/data/tune/")) return FileClass.Neutral;

            if (leaf.EndsWith(".meta") || leaf.EndsWith(".xml") || leaf.EndsWith(".ymt"))
                return FileClass.UnknownXml;

            return FileClass.Neutral;
        }

        private static Dictionary<int, string>? _psoHashMap;
        private static readonly object _psoLock = new();

        private static Dictionary<int, string> PsoHashMap()
        {
            if (_psoHashMap != null) return _psoHashMap;
            lock (_psoLock)
            {
                if (_psoHashMap != null) return _psoHashMap;
                var map = new Dictionary<int, string>();
                try
                {
                    var asm = typeof(LegitCheckService).Assembly;
                    foreach (var res in asm.GetManifestResourceNames()
                        .Where(n => n.Contains(".pso.", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                    {
                        using var st = asm.GetManifestResourceStream(res);
                        if (st == null) continue;
                        using var sr = new StreamReader(st);
                        string? line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            if (line.Length == 0) continue;
                            uint h = RageLib.Hash.Jenkins.Hash(line);
                            map.TryAdd((int)h, line);
                        }
                    }
                }
                catch {}
                _psoHashMap = map;
                return map;
            }
        }

        private static byte[]? TryDecodePsoToXml(byte[] bytes)
        {
            if (bytes.Length < 4 || bytes[0] != (byte)'P' || bytes[1] != (byte)'S'
                || bytes[2] != (byte)'I' || bytes[3] != (byte)'N')
                return null;
            try
            {
                using var inMs = new MemoryStream(bytes, false);
                var val = new RageLib.GTA5.PSOWrappers.PsoReader().Read(inMs);
                var exp = new RageLib.GTA5.PSOWrappers.PsoXmlExporter
                {
                    HashMapping = PsoHashMap(),
                    TolerateUnknownHashes = true,
                };
                using var outMs = new MemoryStream();
                exp.Export(val, outMs);
                return outMs.ToArray();
            }
            catch { return null; }
        }

        private static byte[]? NormalizeTuneToXml(byte[] bytes)
        {
            if (TryParseXml(bytes) != null) return bytes;
            return TryDecodePsoToXml(bytes);
        }

        private static RageArchiveWrapper7 OpenArchiveResilient(Func<Stream> openStream, params string?[] nameCandidates)
        {
            Exception? last = null;
            var triedKeys = new HashSet<uint>();
            long len = -1;

            uint? KeyIdx(string name)
            {
                if (len < 0) return null;
                try { return (GTA5Hash.CalculateHash(name) + (uint)len + (101 - 40)) % 0x65; }
                catch { return null; }
            }

            RageArchiveWrapper7? TryOne(string name)
            {
                Stream s;
                try { s = openStream(); } catch (Exception ex) { last = ex; return null; }
                if (len < 0) { try { len = s.Length; } catch { } }
                var idx = KeyIdx(name);
                if (idx.HasValue) triedKeys.Add(idx.Value);
                try { return RageArchiveWrapper7.Open(s, name, false); }
                catch (Exception ex)
                {
                    last = ex;
                    try { s.Dispose(); } catch { }
                    return null;
                }
            }

            foreach (var name in nameCandidates)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                var a = TryOne(name!);
                if (a != null) return a;
            }

            if (GTA5Constants.PC_NG_KEYS != null && GTA5Constants.PC_NG_KEYS.Length > 0)
            {
                for (int i = 0; i < 5000 && triedKeys.Count < 0x65; i++)
                {
                    string fake = "k" + i.ToString(CultureInfo.InvariantCulture);
                    var idx = KeyIdx(fake);
                    if (!idx.HasValue) break;
                    if (triedKeys.Contains(idx.Value)) continue;
                    var a = TryOne(fake);
                    if (a != null) return a;
                }
            }
            throw last ?? new InvalidOperationException("RPF open failed");
        }

        public static byte[]? TryExtractFileResilient(string archivePath, string internalPath)
        {
            try
            {
                using var arc = OpenArchiveResilient(
                    () => new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
                    Path.GetFileName(archivePath), "update.rpf");
                var segs = internalPath.Replace('\\', '/').TrimStart('/')
                    .Split('/', StringSplitOptions.RemoveEmptyEntries);
                return segs.Length == 0 ? null : ExtractInner(arc.Root, segs, 0);
            }
            catch { return null; }
        }

        public static string NormalizeGunBase(string leaf)
        {
            var s = leaf.ToLowerInvariant();
            if (s.EndsWith(".meta")) s = s[..^5];
            foreach (var pref in new[] { "vehicleweapons_", "vehicleweapon_", "weapons_", "weapon_", "vehicle_" })
            {
                if (s.StartsWith(pref)) { s = s[pref.Length..]; break; }
            }
            return s;
        }

        public static (byte[]? bytes, string? matchedName) TryExtractByGunBase(
            string archivePath, string innerDir, string leaf)
        {
            try
            {
                using var arc = OpenArchiveResilient(
                    () => new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete),
                    Path.GetFileName(archivePath), "update.rpf");

                IArchiveDirectory? dir = arc.Root;
                foreach (var seg in innerDir.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    dir = dir?.GetDirectories().FirstOrDefault(d => d.Name.Equals(seg, StringComparison.OrdinalIgnoreCase));
                    if (dir == null) return (null, null);
                }

                var want = NormalizeGunBase(leaf);
                var hit = dir!.GetFiles().FirstOrDefault(x =>
                    x.Name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    && NormalizeGunBase(x.Name) == want);
                return hit == null ? (null, null) : (SafeDecode(hit), hit.Name);
            }
            catch { return (null, null); }
        }

        private static byte[]? ExtractInner(IArchiveDirectory dir, string[] segs, int i)
        {
            if (i == segs.Length - 1)
            {
                var f = dir.GetFiles().FirstOrDefault(x => x.Name.Equals(segs[i], StringComparison.OrdinalIgnoreCase));
                return f == null ? null : SafeDecode(f);
            }
            var sub = dir.GetDirectories().FirstOrDefault(d => d.Name.Equals(segs[i], StringComparison.OrdinalIgnoreCase));
            if (sub != null) return ExtractInner(sub, segs, i + 1);

            var rf = dir.GetFiles().FirstOrDefault(x => x.Name.Equals(segs[i], StringComparison.OrdinalIgnoreCase)
                && x.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase));
            if (rf != null)
            {
                var bytes = SafeDecode(rf);
                if (bytes == null) return null;
                try
                {
                    using var nested = OpenArchiveResilient(() => new MemoryStream(bytes, false), rf.Name);
                    return ExtractInner(nested.Root, segs, i + 1);
                }
                catch { return null; }
            }
            return null;
        }

        public static bool NeedsBytesForFieldDiff(string innerPath)
        {
            return ClassifyPath(innerPath) is FileClass.WeaponMeta or FileClass.ComponentsMeta
                or FileClass.AnimationsMeta or FileClass.PedAccuracy or FileClass.PedHealth
                or FileClass.PedBounds or FileClass.Explosion or FileClass.UnknownXml
                or FileClass.TuneBinary or FileClass.CamerasBinary;
        }

        public LegitReport CheckUpdateRpf(
            string cleanRpfPath, string targetRpfPath, string sourceLabel,
            Action<int, string>? progress, CancellationToken ct)
        {
            var report = new LegitReport { Source = sourceLabel };

            static Func<Stream> FileFactory(string path) => () =>
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            RageArchiveWrapper7 cleanArc, targetArc;
            try
            {
                cleanArc = OpenArchiveResilient(FileFactory(cleanRpfPath), Path.GetFileName(cleanRpfPath), "update.rpf");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    Loc.T("error.legitOpenClean") + " " + ex.Message);
            }
            try
            {
                targetArc = OpenArchiveResilient(FileFactory(targetRpfPath), Path.GetFileName(targetRpfPath), "update.rpf");
            }
            catch (Exception ex)
            {
                cleanArc.Dispose();
                throw new InvalidOperationException(
                    Loc.T("error.legitOpenTarget") + " " + ex.Message);
            }

            using (cleanArc)
            using (targetArc)
            {
                int total = CountFiles(targetArc.Root);
                int done = 0;
                report.CheckedCount = total;
                CompareDirs(cleanArc.Root, targetArc.Root, "", report, ref done, total, progress, ct);
            }

            FinalizeReport(report);
            return report;
        }

        public LegitReport CheckFromManifest(
            DiffManifest manifest,
            Func<PatchAction, byte[]?> fetchModBytes,
            Func<string, (byte[]? bytes, string? where)> resolveVanilla,
            string sourceLabel,
            Action<int, string>? progress, CancellationToken ct)
        {
            var report = new LegitReport { Source = sourceLabel };
            var actions = manifest.Actions ?? new List<PatchAction>();
            report.CheckedCount = actions.Count;
            int done = 0;

            foreach (var a in actions)
            {
                ct.ThrowIfCancellationRequested();
                done++;
                progress?.Invoke(Math.Min(99, done * 100 / Math.Max(1, actions.Count)), a.TargetPath ?? "");

                string path = a.TargetPath ?? "";
                if (string.IsNullOrWhiteSpace(path)) continue;

                if (a.Type == ActionType.Delete)
                {
                    var del = MakeDeletedFinding(path);
                    if (del != null) report.Findings.Add(del);
                    continue;
                }

                bool added = a.Type == ActionType.Import;
                var cls = ClassifyPath(path);

                if (path.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                {
                    byte[]? rpfBytes = null;
                    try { rpfBytes = fetchModBytes(a); } catch { rpfBytes = null; }
                    if (rpfBytes != null)
                    {
                        var (opened, inner) = ScanNestedRpf(rpfBytes, LeafOf(path), path, resolveVanilla, ct);
                        if (!opened)
                        {
                            report.Findings.Add(new LegitFileFinding
                            {
                                Path = path, Change = added ? "added" : "changed", Size = a.Size,
                                Severity = LegitSeverity.Neutral, CategoryLabel = Loc.T("legit.labelArchive"),
                                Note = Loc.T("legit.noteNestedArchiveUnopenable"),
                            });
                        }
                        else if (inner.Count == 0)
                        {
                            report.Findings.Add(new LegitFileFinding
                            {
                                Path = path, Change = added ? "added" : "changed", Size = a.Size,
                                Severity = LegitSeverity.Visual, CategoryLabel = Loc.T("legit.labelCheckedClean"),
                                Note = Loc.T("legit.noteNestedArchiveClean"),
                            });
                        }
                        else
                        {
                            report.Findings.AddRange(inner);
                            bool anyRed = inner.Any(x => x.Severity == LegitSeverity.Danger);
                            report.Findings.Add(new LegitFileFinding
                            {
                                Path = path, Change = added ? "added" : "changed", Size = a.Size,
                                Severity = anyRed ? LegitSeverity.Danger : LegitSeverity.Warning,
                                CategoryLabel = anyRed ? Loc.T("legit.labelWeaponChangedInside") : Loc.T("legit.labelWeaponEditsInside"),
                                WeaponRelated = true,
                                Note = Loc.T("legit.noteArchiveContentsBelow"),
                            });
                        }
                        continue;
                    }
                    report.Findings.Add(new LegitFileFinding
                    {
                        Path = path, Change = added ? "added" : "changed", Size = a.Size,
                        Severity = LegitSeverity.Neutral, CategoryLabel = added ? Loc.T("legit.labelAdded") : Loc.T("legit.labelChanged"),
                        Note = Loc.T("legit.noteNestedArchiveNotChecked"),
                    });
                    continue;
                }

                bool needsFieldDiff = NeedsBytesForFieldDiff(path);

                byte[]? modBytes = null, cleanBytes = null;
                string? vanillaWhere = null;
                if (needsFieldDiff)
                {
                    try { modBytes = fetchModBytes(a); } catch { modBytes = null; }
                    try { var v = resolveVanilla(path); cleanBytes = v.bytes; vanillaWhere = v.where; }
                    catch { cleanBytes = null; }
                }

                var finding = BuildFinding(path, added, cls, cleanBytes, modBytes, a.Size, vanillaWhere);
                if (needsFieldDiff && modBytes == null)
                {
                    report.Unverified.Add(path);
                    if (finding.Severity < LegitSeverity.Warning)
                        finding.Severity = LegitSeverity.Warning;
                    finding.Note = AppendNote(finding.Note, Loc.T("legit.noteFileUnavailableForDiff"));
                }
                report.Findings.Add(finding);
            }

            FinalizeReport(report);
            return report;
        }

        private static int CountFiles(IArchiveDirectory dir)
        {
            int n = dir.GetFiles().Count();
            foreach (var sub in dir.GetDirectories()) n += CountFiles(sub);
            return n;
        }

        private (bool opened, List<LegitFileFinding> findings) ScanNestedRpf(
            byte[] rpfBytes, string rpfLeaf, string displayPrefix,
            Func<string, (byte[]? bytes, string? where)> resolveVanilla, CancellationToken ct)
        {
            var results = new List<LegitFileFinding>();
            try
            {
                using var arc = OpenArchiveResilient(() => new MemoryStream(rpfBytes, false),
                    rpfLeaf, "miami_weapon.rpf", "weapon.rpf", "weapons.rpf");
                ScanDirForCombat(arc.Root, "", displayPrefix, resolveVanilla, results, ct);
                return (true, results);
            }
            catch
            {
                return (false, results);
            }
        }

        private void ScanDirForCombat(
            IArchiveDirectory dir, string inner, string displayPrefix,
            Func<string, (byte[]? bytes, string? where)> resolveVanilla,
            List<LegitFileFinding> results, CancellationToken ct)
        {
            foreach (var fEntry in dir.GetFiles())
            {
                ct.ThrowIfCancellationRequested();
                string innerPath = inner.Length == 0 ? fEntry.Name : inner + "/" + fEntry.Name;
                var cls = ClassifyPath(innerPath);
                bool combat = cls is FileClass.WeaponMeta or FileClass.ComponentsMeta or FileClass.AnimationsMeta
                    or FileClass.PedAccuracy or FileClass.PedHealth or FileClass.PedBounds;
                if (!combat) continue;
                var modBytes = SafeDecode(fEntry);
                if (modBytes == null) continue;
                byte[]? van = null; string? where = null;
                try { var v = resolveVanilla(innerPath); van = v.bytes; where = v.where; } catch { }
                results.Add(BuildFinding(displayPrefix + "/" + innerPath, added: true, cls, van, modBytes, modBytes.Length, where));
            }
            foreach (var sub in dir.GetDirectories())
                ScanDirForCombat(sub, inner.Length == 0 ? sub.Name : inner + "/" + sub.Name,
                    displayPrefix, resolveVanilla, results, ct);
        }

        private void CompareDirs(
            IArchiveDirectory? cleanDir, IArchiveDirectory? targetDir, string prefix,
            LegitReport report, ref int done, int total,
            Action<int, string>? progress, CancellationToken ct)
        {
            var cleanFiles = cleanDir?.GetFiles().ToDictionary(f => f.Name.ToLowerInvariant())
                             ?? new Dictionary<string, IArchiveFile>();
            var targetFiles = targetDir?.GetFiles().ToDictionary(f => f.Name.ToLowerInvariant())
                              ?? new Dictionary<string, IArchiveFile>();

            foreach (var kvp in targetFiles)
            {
                ct.ThrowIfCancellationRequested();
                done++;
                if (done % 25 == 0 || done == total)
                    progress?.Invoke(Math.Min(99, (int)((long)done * 100 / Math.Max(1, total))), prefix + kvp.Key);

                string path = prefix + kvp.Key;
                bool isNestedRpf = kvp.Key.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);

                if (!cleanFiles.TryGetValue(kvp.Key, out var cleanFile))
                {
                    if (isNestedRpf && TryOpenNested(kvp.Value, out var addedArc))
                    {
                        using (addedArc)
                            CollectAllAsAdded(addedArc!.Root, path + "/", report, ct);
                        continue;
                    }
                    byte[]? modBytes = SafeDecode(kvp.Value);
                    var cls = ClassifyPath(path);
                    report.Findings.Add(BuildFinding(path, added: true, cls, null, modBytes,
                        modBytes?.LongLength ?? 0));
                    continue;
                }

                byte[]? rawClean = SafeRaw(cleanFile);
                byte[]? rawTarget = SafeRaw(kvp.Value);
                if (rawClean != null && rawTarget != null && rawClean.AsSpan().SequenceEqual(rawTarget))
                    continue;

                if (isNestedRpf)
                {
                    if (TryOpenNested(cleanFile, out var cArc) && TryOpenNested(kvp.Value, out var tArc))
                    {
                        using (cArc) using (tArc)
                            CompareDirs(cArc!.Root, tArc!.Root, path + "/", report, ref done, total, progress, ct);
                        continue;
                    }
                }

                byte[]? decClean = SafeDecode(cleanFile);
                byte[]? decTarget = SafeDecode(kvp.Value);
                if (decClean != null && decTarget != null && decClean.AsSpan().SequenceEqual(decTarget))
                    continue;

                var cls2 = ClassifyPath(path);
                report.Findings.Add(BuildFinding(path, added: false, cls2, decClean, decTarget,
                    decTarget?.LongLength ?? 0));
            }

            foreach (var kvp in cleanFiles)
            {
                if (!targetFiles.ContainsKey(kvp.Key))
                {
                    var del = MakeDeletedFinding(prefix + kvp.Key);
                    if (del != null) report.Findings.Add(del);
                }
            }

            var cleanSubs = cleanDir?.GetDirectories().ToDictionary(d => d.Name.ToLowerInvariant())
                            ?? new Dictionary<string, IArchiveDirectory>();
            var targetSubs = targetDir?.GetDirectories().ToDictionary(d => d.Name.ToLowerInvariant())
                             ?? new Dictionary<string, IArchiveDirectory>();
            foreach (var sub in targetSubs)
            {
                cleanSubs.TryGetValue(sub.Key, out var matched);
                CompareDirs(matched, sub.Value, prefix + sub.Key + "/", report, ref done, total, progress, ct);
            }
        }

        private void CollectAllAsAdded(IArchiveDirectory dir, string prefix, LegitReport report, CancellationToken ct)
        {
            foreach (var f in dir.GetFiles())
            {
                ct.ThrowIfCancellationRequested();
                string path = prefix + f.Name;
                byte[]? bytes = SafeDecode(f);
                report.Findings.Add(BuildFinding(path, added: true, ClassifyPath(path), null, bytes,
                    bytes?.LongLength ?? 0));
            }
            foreach (var sub in dir.GetDirectories())
                CollectAllAsAdded(sub, prefix + sub.Name + "/", report, ct);
        }

        private static bool TryOpenNested(IArchiveFile file, out RageArchiveWrapper7? arc)
        {
            arc = null;
            var bytes = SafeDecode(file);
            if (bytes == null) return false;
            try
            {
                arc = OpenArchiveResilient(() => new MemoryStream(bytes, false), file.Name);
                return true;
            }
            catch { arc = null; return false; }
        }

        private static byte[]? SafeRaw(IArchiveFile file)
        {
            try
            {
                using var ms = new MemoryStream();
                file.Export(ms);
                return ms.ToArray();
            }
            catch { return null; }
        }

        private static byte[]? SafeDecode(IArchiveFile file)
        {
            try
            {
                if (file is IArchiveBinaryFile binFile)
                {
                    using var ms = new MemoryStream();
                    binFile.Export(ms);
                    byte[] buf = ms.ToArray();

                    if (binFile.IsEncrypted)
                    {
                        var hash = GTA5Hash.CalculateHash(binFile.Name);
                        var keyIdx = (hash + (uint)binFile.UncompressedSize + (101 - 40)) % 0x65;
                        var key = GTA5Constants.PC_NG_KEYS != null && GTA5Constants.PC_NG_KEYS.Length > keyIdx
                            ? GTA5Constants.PC_NG_KEYS[keyIdx] : null;
                        if (key != null && key.Length > 0)
                            buf = GTA5Crypto.Decrypt(buf, key);
                    }
                    if (binFile.IsCompressed)
                    {
                        using var def = new DeflateStream(new MemoryStream(buf), CompressionMode.Decompress);
                        using var outMs = new MemoryStream();
                        def.CopyTo(outMs);
                        return outMs.ToArray();
                    }
                    return buf;
                }
                using var ms2 = new MemoryStream();
                file.Export(ms2);
                byte[] data = ms2.ToArray();
                if (data.Length >= 4 && data[0] == 0x52 && data[1] == 0x53 && data[2] == 0x43 && data[3] == 0x07)
                    data[3] = 0x37;
                return data;
            }
            catch { return null; }
        }

        private LegitFileFinding BuildFinding(
            string path, bool added, FileClass cls, byte[]? cleanBytes, byte[]? modBytes, long size,
            string? vanillaWhere = null)
        {
            var f = new LegitFileFinding
            {
                Path = path,
                Change = added ? "added" : "changed",
                Size = size,
            };
            string origin = vanillaWhere ?? Loc.T("legit.originCleanGame");

            switch (cls)
            {
                case FileClass.TuneBinary:
                case FileClass.CamerasBinary:
                {
                    string cat = cls == FileClass.CamerasBinary ? Loc.T("legit.labelViewmodelCamera") : Loc.T("legit.labelAimTuning");
                    f.CategoryLabel = cat;

                    byte[]? cleanXml = cleanBytes != null ? NormalizeTuneToXml(cleanBytes) : null;
                    byte[]? modXml   = modBytes   != null ? NormalizeTuneToXml(modBytes)   : null;

                    if (cleanXml != null && modXml != null)
                    {
                        var diffs = DiffMetaXml(cleanXml, modXml);
                        if (diffs != null)
                        {
                            foreach (var d in diffs) d.IsRed = true;
                            f.FieldDiffs = diffs;
                            if (diffs.Count == 0)
                            {
                                f.FormatOnly = true;
                                f.Severity = LegitSeverity.Neutral;
                                f.CategoryLabel = Loc.T("legit.labelMatchesOriginal");
                                f.Note = Loc.T("legit.noteResavedValuesMatch");
                                return f;
                            }
                            f.Severity = LegitSeverity.Danger;
                            f.Note = Loc.T("legit.noteValuesChangedDecoded",
                                ("count", diffs.Count),
                                ("kind", cls == FileClass.CamerasBinary ? "cameras" : "playertargetting"));
                            return f;
                        }
                    }

                    if (modXml != null)
                    {
                        var modDoc = TryParseXml(modXml);
                        if (modDoc?.Root != null) AppendAllLeafValues(f, modDoc);
                    }
                    if (added)
                    {
                        f.Severity = LegitSeverity.Warning;
                        f.Note = Loc.T("legit.noteTuningAddedNoOriginal");
                    }
                    else
                    {
                        f.Severity = LegitSeverity.Danger;
                        f.Note = f.FieldDiffs.Count > 0
                            ? Loc.T("legit.noteOriginalUndecodable")
                            : Loc.T("legit.noteBinaryBytesDiffer");
                    }
                    return f;
                }

                case FileClass.WeaponMeta:
                case FileClass.ComponentsMeta:
                case FileClass.AnimationsMeta:
                case FileClass.PedAccuracy:
                case FileClass.PedHealth:
                case FileClass.PedBounds:
                case FileClass.Explosion:
                {
                    if (cleanBytes != null && modBytes != null)
                    {
                        var diffs = DiffMetaXml(cleanBytes, modBytes);
                        if (diffs == null)
                        {
                            f.Severity = cls == FileClass.Explosion ? LegitSeverity.Neutral : LegitSeverity.Warning;
                            f.CategoryLabel = added ? Loc.T("legit.labelAdded") : Loc.T("legit.labelChanged");
                            f.Note = Loc.T("legit.noteBinaryNoValueDiff");
                            return f;
                        }
                        f.FieldDiffs = diffs;
                        if (diffs.Count == 0)
                        {
                            f.FormatOnly = true;
                            f.Severity = LegitSeverity.Neutral;
                            f.CategoryLabel = Loc.T("legit.labelMatchesOriginal");
                            f.Note = added
                                ? Loc.T("legit.noteAddedButValuesMatch", ("origin", origin))
                                : Loc.T("legit.noteBytesDifferValuesMatch");
                            return f;
                        }
                        var redCats = diffs.Where(d => d.IsRed)
                            .Select(d => FieldCategory(d.Field)).Distinct().ToList();
                        if (redCats.Count > 0)
                        {
                            f.Severity = LegitSeverity.Danger;
                            f.CategoryLabel = string.Join(" · ", redCats);
                            f.Note = added
                                ? Loc.T("legit.noteAddedShootingValuesChanged", ("origin", origin))
                                : Loc.T("legit.noteDiffersFromOriginalShooting", ("origin", origin));
                        }
                        else
                        {
                            f.Severity = LegitSeverity.Neutral;
                            f.CategoryLabel = added ? Loc.T("legit.labelWeaponAdded") : Loc.T("legit.labelWeaponChanged");
                            f.WeaponRelated = true;
                            f.Note = Loc.T("legit.noteShootingValuesMatch", ("origin", origin));
                        }
                        return f;
                    }

                    f.CategoryLabel = added ? Loc.T("legit.labelNewWeaponCustom") : Loc.T("legit.labelWeaponChanged");
                    f.WeaponRelated = true;
                    if (modBytes != null) AppendContentScan(f, modBytes);
                    f.Severity = added ? LegitSeverity.Neutral : LegitSeverity.Warning;
                    f.Note = added
                        ? Loc.T("legit.noteNoVanillaCounterpart")
                        : Loc.T("legit.noteOriginalUnavailable");
                    return f;
                }

                case FileClass.YellowMeta:
                    f.Severity = LegitSeverity.Warning;
                    f.CategoryLabel = added ? Loc.T("legit.labelAdded") : Loc.T("legit.labelChanged");
                    if (!added && cleanBytes != null && modBytes != null)
                    {
                        var yd = DiffMetaXml(cleanBytes, modBytes);
                        if (yd != null)
                        {
                            f.FieldDiffs = yd;
                            if (yd.Count == 0)
                            {
                                f.FormatOnly = true;
                                f.Severity = LegitSeverity.Neutral;
                                f.Note = Loc.T("legit.noteFormattingOnly");
                            }
                        }
                    }
                    return f;

                case FileClass.Visual:
                    f.Severity = LegitSeverity.Visual;
                    f.CategoryLabel = Loc.T("legit.labelVisual");
                    return f;

                case FileClass.UnknownXml:
                {
                    if (!added && cleanBytes != null && modBytes != null)
                    {
                        var ud = DiffMetaXml(cleanBytes, modBytes);
                        if (ud != null)
                        {
                            f.FieldDiffs = ud;
                            if (ud.Count == 0)
                            {
                                f.FormatOnly = true;
                                f.Severity = LegitSeverity.Neutral;
                                f.CategoryLabel = Loc.T("legit.labelFormatting");
                                f.Note = Loc.T("legit.noteBytesDifferAllValuesMatch");
                                return f;
                            }
                        }
                    }
                    else if (modBytes != null)
                    {
                        AppendContentScan(f, modBytes);
                    }
                    bool red = f.FieldDiffs.Any(d => d.IsRed);
                    f.Severity = red ? LegitSeverity.Warning : LegitSeverity.Neutral;
                    f.CategoryLabel = added ? Loc.T("legit.labelAdded") : Loc.T("legit.labelChanged");
                    if (!red && f.FieldDiffs.Count == 0)
                        f.Note = AppendNote(f.Note, Loc.T("legit.noteNoDangerousChangesInside"));
                    return f;
                }

                default:
                {
                    f.Severity = LegitSeverity.Neutral;
                    f.CategoryLabel = added ? Loc.T("legit.labelAdded") : Loc.T("legit.labelChanged");
                    if (added && path.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase))
                        f.Note = Loc.T("legit.noteNestedArchiveValuesNotChecked");
                    return f;
                }
            }
        }

        private LegitFileFinding? MakeDeletedFinding(string path)
        {
            var cls = ClassifyPath(path);
            if (cls is FileClass.Visual) return null;
            bool combat = cls is FileClass.WeaponMeta or FileClass.ComponentsMeta or FileClass.AnimationsMeta
                or FileClass.PedAccuracy or FileClass.PedHealth or FileClass.TuneBinary or FileClass.CamerasBinary;
            return new LegitFileFinding
            {
                Path = path,
                Change = "deleted",
                Severity = combat ? LegitSeverity.Danger : LegitSeverity.Neutral,
                CategoryLabel = combat ? Loc.T("legit.labelWeaponFileDeleted") : Loc.T("legit.labelDeleted"),
                WeaponRelated = combat,
                Note = combat ? Loc.T("legit.noteWeaponFileDeleted") : "",
            };
        }

        private static void AppendAllLeafValues(LegitFileFinding f, XDocument doc)
        {
            const int cap = 60;
            string owner = "";
            foreach (var el in doc.Root!.DescendantsAndSelf())
            {
                if (f.FieldDiffs.Count >= cap)
                {
                    f.Note = AppendNote(f.Note, Loc.T("legit.noteFirst60Values"));
                    return;
                }
                var nameChild = el.Elements().FirstOrDefault(c => c.Name.LocalName == "Name" && !c.HasElements);
                if (nameChild != null)
                {
                    var id = nameChild.Value.Trim();
                    if (id.Length > 0) owner = id;
                }
                if (el.HasElements) continue;
                var v = ReadLeafValue(el);
                if (string.IsNullOrWhiteSpace(v)) continue;
                f.FieldDiffs.Add(new LegitFieldDiff
                {
                    Owner = owner,
                    Field = el.Name.LocalName,
                    CleanValue = "-",
                    ModValue = v,
                    IsRed = RedFields.Contains(el.Name.LocalName),
                });
            }
        }

        private static void AppendContentScan(LegitFileFinding f, byte[] bytes)
        {
            const int cap = 60;
            XDocument? doc = TryParseXml(bytes);
            if (doc?.Root == null) return;
            string owner = "";
            foreach (var el in doc.Root.DescendantsAndSelf())
            {
                if (f.FieldDiffs.Count >= cap)
                {
                    f.Note = AppendNote(f.Note, Loc.T("legit.noteFirst60Values"));
                    return;
                }
                var nameChild = el.Elements().FirstOrDefault(c => c.Name.LocalName == "Name");
                if (nameChild != null && !nameChild.HasElements)
                {
                    var id = nameChild.Value.Trim();
                    if (id.Length > 0) owner = id;
                }
                if (RedFields.Contains(el.Name.LocalName))
                {
                    f.FieldDiffs.Add(new LegitFieldDiff
                    {
                        Owner = owner,
                        Field = el.Name.LocalName,
                        CleanValue = "-",
                        ModValue = ReadLeafValue(el),
                        IsRed = true,
                    });
                }
            }
        }

        public static List<LegitFieldDiff>? DiffMetaXml(byte[] cleanBytes, byte[] modBytes)
        {
            XDocument? clean = TryParseXml(cleanBytes);
            XDocument? mod = TryParseXml(modBytes);
            if (clean?.Root == null || mod?.Root == null) return null;

            var diffs = new List<LegitFieldDiff>();
            DiffElements(clean.Root, mod.Root, "", diffs);
            return diffs;
        }

        private static XDocument? TryParseXml(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                return XDocument.Load(ms, LoadOptions.None);
            }
            catch { return null; }
        }

        private static string? IdentityOf(XElement el)
        {
            var nameChild = el.Elements().FirstOrDefault(c => c.Name.LocalName == "Name" && !c.HasElements);
            var id = nameChild?.Value.Trim();
            if (!string.IsNullOrEmpty(id)) return id;
            var keyAttr = el.Attribute("key")?.Value ?? el.Attribute("name")?.Value;
            return string.IsNullOrEmpty(keyAttr) ? null : keyAttr.Trim();
        }

        private static void DiffElements(XElement clean, XElement mod, string owner, List<LegitFieldDiff> diffs)
        {
            if (!clean.HasElements && !mod.HasElements)
            {
                CompareLeaf(clean, mod, owner, diffs);
                return;
            }

            var cleanGroups = clean.Elements().GroupBy(e => e.Name.LocalName).ToDictionary(g => g.Key, g => g.ToList());
            var modGroups = mod.Elements().GroupBy(e => e.Name.LocalName).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var kvp in modGroups)
            {
                cleanGroups.TryGetValue(kvp.Key, out var cleanList);
                cleanList ??= new List<XElement>();
                var modList = kvp.Value;

                var cleanById = new Dictionary<string, XElement>();
                var cleanNoId = new List<XElement>();
                foreach (var ce in cleanList)
                {
                    var id = IdentityOf(ce);
                    if (id != null && !cleanById.ContainsKey(id)) cleanById.Add(id, ce);
                    else cleanNoId.Add(ce);
                }
                var matchedClean = new HashSet<XElement>();
                int noIdCursor = 0;

                foreach (var me in modList)
                {
                    var id = IdentityOf(me);
                    XElement? counterpart = null;
                    if (id != null && cleanById.TryGetValue(id, out var byId)) counterpart = byId;
                    else if (id == null && noIdCursor < cleanNoId.Count) counterpart = cleanNoId[noIdCursor++];

                    if (counterpart == null)
                    {
                        bool redInside = me.DescendantsAndSelf().Any(d => RedFields.Contains(d.Name.LocalName));
                        diffs.Add(new LegitFieldDiff
                        {
                            Owner = id ?? owner,
                            Field = me.Name.LocalName + " (" + Loc.T("legit.fieldSuffixAdded") + ")",
                            CleanValue = "-",
                            ModValue = id ?? Loc.T("legit.valueNewElement"),
                            IsRed = redInside,
                        });
                        continue;
                    }
                    matchedClean.Add(counterpart);
                    DiffElements(counterpart, me, id ?? owner, diffs);
                }

                foreach (var ce in cleanList)
                {
                    if (matchedClean.Contains(ce)) continue;
                    if (IdentityOf(ce) is not { } cid) continue;
                    bool redInside = ce.DescendantsAndSelf().Any(d => RedFields.Contains(d.Name.LocalName));
                    diffs.Add(new LegitFieldDiff
                    {
                        Owner = cid,
                        Field = ce.Name.LocalName + " (" + Loc.T("legit.fieldSuffixDeleted") + ")",
                        CleanValue = cid,
                        ModValue = "-",
                        IsRed = redInside,
                    });
                }
            }

            foreach (var kvp in cleanGroups)
            {
                if (modGroups.ContainsKey(kvp.Key)) continue;
                foreach (var ce in kvp.Value)
                {
                    bool redInside = ce.DescendantsAndSelf().Any(d => RedFields.Contains(d.Name.LocalName));
                    diffs.Add(new LegitFieldDiff
                    {
                        Owner = IdentityOf(ce) ?? owner,
                        Field = ce.Name.LocalName + " (" + Loc.T("legit.fieldSuffixDeleted") + ")",
                        CleanValue = ReadLeafValue(ce),
                        ModValue = "-",
                        IsRed = redInside || RedFields.Contains(ce.Name.LocalName),
                    });
                }
            }
        }

        private static void CompareLeaf(XElement clean, XElement mod, string owner, List<LegitFieldDiff> diffs)
        {
            string field = mod.Name.LocalName;
            bool isRed = RedFields.Contains(field);

            var attrNames = clean.Attributes().Select(a => a.Name.LocalName)
                .Union(mod.Attributes().Select(a => a.Name.LocalName), StringComparer.OrdinalIgnoreCase)
                .Where(n => !n.Equals("type", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var an in attrNames)
            {
                string cv = clean.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(an, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                string mv = mod.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(an, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
                if (ValuesEqual(cv, mv)) continue;
                diffs.Add(new LegitFieldDiff
                {
                    Owner = owner,
                    Field = an.Equals("value", StringComparison.OrdinalIgnoreCase) ? field : field + "." + an,
                    CleanValue = cv, ModValue = mv,
                    DeltaPercent = DeltaPercent(cv, mv),
                    IsRed = isRed,
                });
            }

            string ct = Normalize(clean.Value);
            string mt = Normalize(mod.Value);
            if (ct == mt) return;

            bool looksLikeTokens = (ct.Contains(' ') || mt.Contains(' ')) && !double.TryParse(ct, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
            if (looksLikeTokens)
            {
                var cSet = new HashSet<string>(ct.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                var mSet = new HashSet<string>(mt.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
                var addedTokens = mSet.Except(cSet).ToList();
                var removedTokens = cSet.Except(mSet).ToList();
                if (addedTokens.Count == 0 && removedTokens.Count == 0) return;
                bool redToken = addedTokens.Any(t => RedFlagTokens.Contains(t));
                diffs.Add(new LegitFieldDiff
                {
                    Owner = owner, Field = field,
                    CleanValue = removedTokens.Count > 0 ? Loc.T("legit.tokensRemoved", ("tokens", string.Join(" ", removedTokens))) : "-",
                    ModValue = addedTokens.Count > 0 ? Loc.T("legit.tokensAdded", ("tokens", string.Join(" ", addedTokens))) : "-",
                    IsRed = isRed || redToken,
                });
                return;
            }

            if (ValuesEqual(ct, mt)) return;
            diffs.Add(new LegitFieldDiff
            {
                Owner = owner, Field = field,
                CleanValue = ct, ModValue = mt,
                DeltaPercent = DeltaPercent(ct, mt),
                IsRed = isRed,
            });
        }

        private static string ReadLeafValue(XElement el)
        {
            var v = el.Attribute("value")?.Value;
            if (!string.IsNullOrEmpty(v)) return v;
            var xyz = new[] { "x", "y", "z" }
                .Select(n => el.Attribute(n)?.Value)
                .Where(s => s != null).ToList();
            if (xyz.Count > 0) return string.Join(", ", xyz);
            var t = Normalize(el.Value);
            return t.Length > 64 ? t[..64] + "…" : t;
        }

        private static string Normalize(string s)
            => Regex.Replace(s ?? "", @"\s+", " ").Trim();

        private static bool ValuesEqual(string a, string b)
        {
            a = Normalize(a); b = Normalize(b);
            if (string.Equals(a, b, StringComparison.Ordinal)) return true;
            if (double.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var da)
                && double.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var db))
            {
                return Math.Abs(da - db) <= 1e-6 * Math.Max(1.0, Math.Max(Math.Abs(da), Math.Abs(db)));
            }
            return false;
        }

        private static double? DeltaPercent(string a, string b)
        {
            if (double.TryParse(Normalize(a), NumberStyles.Any, CultureInfo.InvariantCulture, out var da)
                && double.TryParse(Normalize(b), NumberStyles.Any, CultureInfo.InvariantCulture, out var db)
                && Math.Abs(da) > 1e-9)
            {
                return Math.Round((db - da) / Math.Abs(da) * 100.0, 1);
            }
            return null;
        }

        private static string AppendNote(string existing, string extra)
            => string.IsNullOrEmpty(existing) ? extra : existing + "; " + extra;

        private static void FinalizeReport(LegitReport report)
        {
            report.Findings = report.Findings
                .GroupBy(f => f.Change + "|" + f.Path.ToLowerInvariant())
                .Select(g => g.First())
                .OrderByDescending(f => f.Severity)
                .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            report.DangerCount = report.Findings.Count(f => f.Severity == LegitSeverity.Danger);
            report.WarningCount = report.Findings.Count(f => f.Severity == LegitSeverity.Warning);
            report.ChangedCount = report.Findings.Count(f => f.Change == "changed");
            report.AddedCount = report.Findings.Count(f => f.Change == "added");
            report.DeletedCount = report.Findings.Count(f => f.Change == "deleted");

            var reasons = report.VerdictReasons;
            var dangerFiles = report.Findings.Where(f => f.Severity == LegitSeverity.Danger).ToList();

            if (dangerFiles.Count > 0)
            {
                report.Verdict = LegitVerdictKind.Danger;
                report.VerdictTitle = Loc.T("legit.verdictDangerTitle");

                var redDiffs = dangerFiles.SelectMany(f => f.FieldDiffs.Where(d => d.IsRed)).ToList();
                var cats = redDiffs.Select(d => FieldCategory(d.Field)).Distinct().ToList();
                var owners = redDiffs.Where(d => d.Owner.StartsWith("WEAPON_", StringComparison.OrdinalIgnoreCase))
                    .Select(d => d.Owner).Distinct().ToList();

                if (cats.Count > 0)
                    reasons.Add(Loc.T("legit.reasonShootingValuesTouched", ("cats", string.Join(", ", cats))));
                if (owners.Count > 0)
                    reasons.Add(Loc.T("legit.reasonWeaponsChanged",
                        ("count", owners.Count),
                        ("names", string.Join(", ", owners.Take(8)) + (owners.Count > 8 ? "…" : ""))));
                var worst = redDiffs.Where(d => d.DeltaPercent.HasValue)
                    .OrderBy(d => Math.Abs(100 + d.DeltaPercent!.Value) < Math.Abs(d.DeltaPercent.Value) ? 0 : 1)
                    .OrderByDescending(d => Math.Abs(d.DeltaPercent!.Value)).FirstOrDefault();
                if (worst != null)
                    reasons.Add(Loc.T("legit.reasonWorstDeviation",
                        ("owner", worst.Owner), ("field", worst.Field),
                        ("clean", worst.CleanValue), ("mod", worst.ModValue),
                        ("delta", (worst.DeltaPercent > 0 ? "+" : "") + worst.DeltaPercent)));
                foreach (var bf in dangerFiles.Where(f => f.FieldDiffs.Count == 0).Take(4))
                    reasons.Add($"{bf.Path} - {bf.Note}");

                report.VerdictText =
                    Loc.T("legit.verdictDangerTextHead") + " " +
                    (cats.Count > 0 ? Loc.T("legit.verdictDangerTextCats", ("cats", string.Join(", ", cats))) + " " : "") +
                    Loc.T("legit.verdictDangerTextTail");
            }
            else if (report.Findings.Any(f => f.Severity == LegitSeverity.Warning))
            {
                report.Verdict = LegitVerdictKind.Mixed;
                report.VerdictTitle = Loc.T("legit.verdictMixedTitle");

                var warn = report.Findings.Where(f => f.Severity == LegitSeverity.Warning).ToList();
                var combatTouched = warn.Where(f => f.WeaponRelated).ToList();
                if (combatTouched.Count > 0)
                    reasons.Add(Loc.T("legit.reasonWeaponFilesChangedValuesOk", ("count", combatTouched.Count)));
                var strange = warn.Where(f => !f.WeaponRelated).ToList();
                if (strange.Count > 0)
                    reasons.Add(Loc.T("legit.reasonStrangeFiles",
                        ("count", strange.Count),
                        ("names", string.Join(", ", strange.Take(5).Select(s => LeafOf(s.Path))) + (strange.Count > 5 ? "…" : ""))));
                if (report.Unverified.Count > 0)
                    reasons.Add(Loc.T("legit.reasonUnverified", ("count", report.Unverified.Count)));

                report.VerdictText = Loc.T("legit.verdictMixedText");
            }
            else
            {
                report.Verdict = LegitVerdictKind.Safe;
                report.VerdictTitle = Loc.T("legit.verdictSafeTitle");
                int visual = report.Findings.Count(f => f.Severity == LegitSeverity.Visual);
                int fmt = report.Findings.Count(f => f.FormatOnly);
                int gunsOk = report.Findings.Count(f =>
                    f.Severity == LegitSeverity.Neutral && f.WeaponRelated);
                if (visual > 0) reasons.Add(Loc.T("legit.reasonVisualOnly", ("count", visual)));
                if (gunsOk > 0) reasons.Add(Loc.T("legit.reasonWeaponFilesChangedAllValuesOk", ("count", gunsOk)));
                if (fmt > 0) reasons.Add(Loc.T("legit.reasonFormattingOnlyFiles", ("count", fmt)));
                if (report.Findings.Count == 0) reasons.Add(Loc.T("legit.reasonNoDifferencesAtAll"));
                if (gunsOk == 0) reasons.Add(Loc.T("legit.reasonCombatFilesUntouched"));

                report.VerdictText = gunsOk > 0
                    ? Loc.T("legit.verdictSafeTextGuns")
                    : Loc.T("legit.verdictSafeTextVisual");
            }
        }

        private static string LeafOf(string path)
        {
            var p = path.Replace('\\', '/');
            return p.Contains('/') ? p[(p.LastIndexOf('/') + 1)..] : p;
        }
    }
}
