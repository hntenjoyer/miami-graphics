using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.HotSwap
{
    public static class GameFileSwapper
    {
        public static List<string> ReadSet(string gtaRoot)
        {
            try
            {
                var p = HotSwapPaths.SetPath(gtaRoot);
                if (File.Exists(p))
                    return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(p)) ?? new List<string>();
            }
            catch { }
            return new List<string>();
        }

        private static void WriteSet(string gtaRoot, List<string> set)
        {
            var p = HotSwapPaths.SetPath(gtaRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, JsonSerializer.Serialize(set));
        }

        private static string BaselinePath(string gtaRoot) =>
            Path.Combine(HotSwapPaths.ImageRoot(gtaRoot), "baseline.json");

        private static void WriteBaseline(string gtaRoot, List<string> set)
        {
            try
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rel in set)
                {
                    var gp = HotSwapPaths.GamePath(gtaRoot, rel);
                    map[rel] = HotSwapFileOps.Stamp(gp) + "|" + HotSwapFileOps.ContentSig(gp);
                }
                File.WriteAllText(BaselinePath(gtaRoot), JsonSerializer.Serialize(map));
            }
            catch {}
        }

        private static Dictionary<string, string> ReadBaseline(string gtaRoot)
        {
            try
            {
                var p = BaselinePath(gtaRoot);
                if (File.Exists(p))
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p))
                           ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static string ArmedStampPath(string gtaRoot) =>
            Path.Combine(HotSwapPaths.ImageRoot(gtaRoot), "armed.json");

        private static Dictionary<string, string> ReadArmedStamps(string gtaRoot)
        {
            try
            {
                var p = ArmedStampPath(gtaRoot);
                if (File.Exists(p))
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p))
                           ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static void WriteArmedStamps(string gtaRoot, Dictionary<string, string> map)
        {
            try
            {
                var p = ArmedStampPath(gtaRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                File.WriteAllText(p, JsonSerializer.Serialize(map));
            }
            catch {}
        }

        private static void DropArmedStamps(string gtaRoot)
        {
            try { File.Delete(ArmedStampPath(gtaRoot)); } catch { }
        }

        public static bool DetectRepairWhileArmed(string gtaRoot, out string? rel)
        {
            rel = null;
            var armed = ReadArmedStamps(gtaRoot);
            if (armed.Count == 0) return false;
            foreach (var kv in armed)
            {
                var now = HotSwapFileOps.Stamp(HotSwapPaths.GamePath(gtaRoot, kv.Key));
                if (string.IsNullOrEmpty(now) || string.IsNullOrEmpty(kv.Value)) continue;
                if (string.Equals(now, kv.Value, StringComparison.Ordinal)) continue;
                rel = kv.Key;
                RockstarRepairWatch.Mark(gtaRoot, kv.Key, kv.Value, now,
                    RockstarRepairWatch.Probe(gtaRoot).Describe());
                return true;
            }
            return false;
        }

        public static string? StaleReason(string gtaRoot)
        {
            if (ReadSet(gtaRoot).Count == 0) return null;
            if (RockstarRepairWatch.Read(gtaRoot) is { } mark)
                return Loc.T("error.hotSwapRepairedDuringSession", ("file", mark.Rel ?? "update\\update.rpf"));
            if (GameChangedSinceFreeze(gtaRoot, out var rel))
                return Loc.T("error.hotSwapGameUpdated", ("file", rel));
            return null;
        }

        private static string? _lastChangeLogged;

        public static bool GameChangedSinceFreeze(string gtaRoot, out string? rel)
        {
            rel = null;
            var baseline = ReadBaseline(gtaRoot);
            if (baseline.Count == 0) return false;

            var phase = HotSwapJournal.Read(gtaRoot).Phase;
            if (phase is HotSwapPhase.Arming or HotSwapPhase.Disarming or HotSwapPhase.Freezing)
                return false;
            bool touched = false;
            var eng = SwapEngines.ForActive(gtaRoot);
            foreach (var kv in baseline)
            {
                if (eng.IsArmed(gtaRoot, kv.Key)) continue;

                int bar = kv.Value.IndexOf('|');
                var wasStamp = bar < 0 ? kv.Value : kv.Value.Substring(0, bar);
                var wasSig   = bar < 0 ? "" : kv.Value.Substring(bar + 1);

                var gamePath = HotSwapPaths.GamePath(gtaRoot, kv.Key);
                var now = HotSwapFileOps.Stamp(gamePath);
                if (string.IsNullOrEmpty(now) || string.IsNullOrEmpty(wasStamp)) continue;
                if (string.Equals(now, wasStamp, StringComparison.Ordinal)) continue;

                var nowSig = HotSwapFileOps.ContentSig(gamePath);
                if (wasSig.Length > 0 && nowSig.Length > 0 &&
                    string.Equals(wasSig, nowSig, StringComparison.Ordinal))
                {
                    baseline[kv.Key] = now + "|" + nowSig;
                    touched = true;
                    var sigTouch = $"{kv.Key}|touch|{nowSig}";
                    if (!string.Equals(sigTouch, _lastChangeLogged, StringComparison.Ordinal))
                    {
                        _lastChangeLogged = sigTouch;
                        HotSwapLog.Write("baseline",
                            $"{kv.Key}: время записи изменилось, содержимое то же ({nowSig}) - " +
                            "это переписывание на месте, а не обновление игры; образ годен");
                    }
                    continue;
                }

                static string LenOf(string stamp) { int i = stamp.IndexOf('-'); return i < 0 ? stamp : stamp.Substring(0, i); }
                if ((nowSig.Length == 0 || wasSig.Length == 0)
                    && string.Equals(LenOf(now), LenOf(wasStamp), StringComparison.Ordinal))
                {
                    if (nowSig.Length > 0) { baseline[kv.Key] = now + "|" + nowSig; touched = true; }
                    var sigLocked = $"{kv.Key}|locked|{now}";
                    if (!string.Equals(sigLocked, _lastChangeLogged, StringComparison.Ordinal))
                    {
                        _lastChangeLogged = sigLocked;
                        HotSwapLog.Write("baseline",
                            $"{kv.Key}: время записи изменилось, {(nowSig.Length == 0 ? "содержимое сейчас не прочитать (файл занят)" : "старой подписи нет (образ от прежней сборки)")}, " +
                            "длина прежняя - считаю образ годным до следующей проверки");
                    }
                    continue;
                }

                {
                    rel = kv.Key;
                    var sig = $"{kv.Key}|{kv.Value}|{now}";
                    if (!string.Equals(sig, _lastChangeLogged, StringComparison.Ordinal))
                    {
                        _lastChangeLogged = sig;
                        HotSwapLog.Write("baseline",
                            $"игра обновилась под замороженным режимом: {kv.Key}: " +
                            $"было {wasStamp} / {(wasSig.Length > 0 ? wasSig : "подписи нет")}, " +
                            $"стало {now} / {(nowSig.Length > 0 ? nowSig : "подпись не снялась")}");
                    }
                    return true;
                }
            }
            if (touched)
                try { File.WriteAllText(BaselinePath(gtaRoot), JsonSerializer.Serialize(baseline)); } catch { }
            return false;
        }

        public static bool Arm(string gtaRoot, int? gamePid, out string? error) =>
            Arm(gtaRoot, gamePid, HotSwapStore.ActiveMethod(gtaRoot), out error);

        public static bool Arm(string gtaRoot, int? gamePid, HotSwapMethod method, out string? error)
        {
            error = null;
            var set = ReadSet(gtaRoot);
            HotSwapLog.Write("arm", $"старт: способ {(int)method} ({HotSwapPlan.For(method).Title}), " +
                $"pid игры {(gamePid?.ToString() ?? "нет")}, файлов в наборе {set.Count}");
            if (set.Count == 0)
            {
                error = Loc.T("error.hotSwapImageMissing");
                HotSwapLog.Write("arm", "отказ: набор пуст (swapset.json не найден или пустой) - образ не собран");
                return false;
            }
            if (StaleReason(gtaRoot) is { } stale)
            {
                error = stale + " " + Loc.T("error.hotSwapRebuildHint");
                HotSwapLog.Write("arm", "отказ: образ протух - " + stale);
                return false;
            }
            var env = RockstarRepairWatch.Probe(gtaRoot);
            HotSwapLog.Write("arm", "окружение Rockstar: " + env.Describe());
            if (env.LauncherAlive && !HotSwapPlan.For(method).KillGameBeforeReturn)
                HotSwapLog.Write("arm",
                    "ВНИМАНИЕ: Rockstar Games Launcher запущен во время подмены. Он сверяет файлы игры " +
                    "с манифестом и может заказать перекачку update.rpf. Надёжнее закрыть его перед игрой " +
                    "или переключиться на способ 5 (он гасит процессы Rockstar перед возвратом файлов).");
            if (env.RepairInProgress)
            {
                error = Loc.T("error.hotSwapRepairInProgress");
                HotSwapLog.Write("arm",
                    "отказ: рядом с файлами игры свежие временные файлы лаунчера - " +
                    "подставлять моды под идущую докачку нельзя");
                return false;
            }
            try
            {
                var eng = SwapEngines.For(method);
                var swTotal = Stopwatch.StartNew();
                HotSwapJournal.Write(gtaRoot, HotSwapPhase.Arming, gamePid);
                var prevStamps = ReadArmedStamps(gtaRoot);
                var armedStamps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var rel in set)
                {
                    if (!eng.CanArm(gtaRoot, rel))
                    {
                        HotSwapLog.Write("arm", $"{rel}: пропуск, CanArm=false ({eng.Describe(gtaRoot, rel)})");
                        if (eng.IsArmed(gtaRoot, rel))
                        {
                            var keep = prevStamps.TryGetValue(rel, out var was) && !string.IsNullOrEmpty(was)
                                ? was
                                : HotSwapFileOps.Stamp(HotSwapPaths.GamePath(gtaRoot, rel));
                            if (!string.IsNullOrEmpty(keep)) armedStamps[rel] = keep;
                        }
                        continue;
                    }
                    var sw = Stopwatch.StartNew();
                    eng.ArmOne(gtaRoot, rel);
                    armedStamps[rel] = HotSwapFileOps.Stamp(HotSwapPaths.GamePath(gtaRoot, rel));
                    HotSwapLog.Write("arm", $"{rel}: моды подставлены за {sw.ElapsedMilliseconds} мс");
                }
                WriteArmedStamps(gtaRoot, armedStamps);
                HotSwapJournal.Write(gtaRoot, HotSwapPhase.Armed, gamePid);
                HotSwapLog.Write("arm", $"готово за {swTotal.ElapsedMilliseconds} мс");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                HotSwapLog.Write("arm", "ошибка подстановки", ex);
                return false;
            }
        }

        public static bool Disarm(string gtaRoot, out string? error) =>
            Disarm(gtaRoot, HotSwapStore.ActiveMethod(gtaRoot), out error);

        private static string? _lastDisarmBlocked;

        public static bool Disarm(string gtaRoot, HotSwapMethod method, out string? error)
        {
            error = null;
            var set = ReadSet(gtaRoot);
            if (RockstarRepairWatch.Read(gtaRoot) is { } repaired)
            {
                var sig = repaired.Rel ?? "?";
                if (!string.Equals(sig, _lastDisarmBlocked, StringComparison.Ordinal))
                {
                    _lastDisarmBlocked = sig;
                    HotSwapLog.Write("disarm",
                        $"пропуск целиком: зафиксирован ремонт Rockstar ({sig}) - файлы игры новее образа, не трогаю их");
                }
                return true;
            }
            _lastDisarmBlocked = null;

            var envDisarm = RockstarRepairWatch.Probe(gtaRoot);
            if (envDisarm.RepairInProgress)
            {
                error = Loc.T("error.hotSwapRepairInProgress");
                HotSwapLog.Write("disarm",
                    "отказ: рядом с файлами игры свежие временные файлы лаунчера (" +
                    envDisarm.Describe() + ") - возврат отложен до конца докачки");
                return false;
            }

            HotSwapLog.Write("disarm", $"старт: способ {(int)method} ({HotSwapPlan.For(method).Title}), " +
                $"файлов в наборе {set.Count}");
            try
            {
                var eng = SwapEngines.For(method);
                var armed = ReadArmedStamps(gtaRoot);
                bool foreignSeen = false;
                var swTotal = Stopwatch.StartNew();
                HotSwapJournal.Write(gtaRoot, HotSwapPhase.Disarming);
                foreach (var rel in set)
                {
                    if (armed.TryGetValue(rel, out var wasStamp) && !string.IsNullOrEmpty(wasStamp))
                    {
                        var nowStamp = HotSwapFileOps.Stamp(HotSwapPaths.GamePath(gtaRoot, rel));
                        if (!string.IsNullOrEmpty(nowStamp)
                            && !string.Equals(nowStamp, wasStamp, StringComparison.Ordinal))
                        {
                            foreignSeen = true;
                            RockstarRepairWatch.Mark(gtaRoot, rel, wasStamp, nowStamp,
                                RockstarRepairWatch.Probe(gtaRoot).Describe());
                            HotSwapLog.Write("disarm",
                                $"{rel}: ОТКАЗ возврата - файл игры подменён не нами (был {wasStamp}, стал {nowStamp}). " +
                                "Оставляю файл игры как есть, образ помечен протухшим.");
                            continue;
                        }
                    }
                    if (!eng.CanDisarm(gtaRoot, rel))
                    {
                        HotSwapLog.Write("disarm", $"{rel}: пропуск, CanDisarm=false ({eng.Describe(gtaRoot, rel)})");
                        continue;
                    }
                    var sw = Stopwatch.StartNew();
                    eng.DisarmOne(gtaRoot, rel);
                    HotSwapLog.Write("disarm", $"{rel}: чистый файл возвращён за {sw.ElapsedMilliseconds} мс");
                }
                if (!foreignSeen) DropArmedStamps(gtaRoot);
                HotSwapJournal.Write(gtaRoot, HotSwapPhase.Idle);
                HotSwapLog.Write("disarm", $"готово за {swTotal.ElapsedMilliseconds} мс");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                HotSwapLog.Write("disarm", "ошибка возврата чистых файлов", ex);
                return false;
            }
        }

        public static bool Freeze(string gtaRoot, IDictionary<string, string> cleanSources, out string? error)
        {
            var mode = HotSwapModeStore.Read();
            return Freeze(gtaRoot, cleanSources, HotSwapPlan.Normalize(mode.Method), mode.StoreRoot, out error);
        }

        public static bool Freeze(string gtaRoot, IDictionary<string, string> cleanSources,
                                  HotSwapMethod method, string? storeRoot, out string? error)
        {
            error = null;
            HotSwapLog.Write("freeze", $"старт: способ {(int)method} ({HotSwapPlan.For(method).Title}), " +
                $"gta {gtaRoot}, storeRoot {(string.IsNullOrWhiteSpace(storeRoot) ? "(дефолт)" : storeRoot)}, " +
                $"чистых источников {cleanSources.Count}");
            if (ReadSet(gtaRoot).Count > 0)
            {
                error = Loc.T("error.hotSwapAlreadyFrozen");
                HotSwapLog.Write("freeze", "отказ: образ уже собран (swapset.json не пустой)");
                return false;
            }
            try
            {
                var imageRoot = HotSwapStore.Bind(gtaRoot, method, storeRoot);
                Directory.CreateDirectory(imageRoot);
                HotSwapLog.Write("freeze", $"корень образа: {imageRoot}");
                RockstarRepairWatch.Clear(gtaRoot);
                DropArmedStamps(gtaRoot);

                var eng = SwapEngines.For(method);
                var done = new List<string>();
                var swTotal = Stopwatch.StartNew();
                HotSwapJournal.Write(gtaRoot, HotSwapPhase.Freezing);
                foreach (var rel in HotSwapPaths.ExistingRelPaths(gtaRoot))
                {
                    if (!cleanSources.TryGetValue(rel, out var cleanSrc)
                        || string.IsNullOrWhiteSpace(cleanSrc) || !File.Exists(cleanSrc))
                    {
                        HotSwapLog.Write("freeze", $"{rel}: пропуск - нет чистого источника" +
                            (string.IsNullOrWhiteSpace(cleanSrc) ? "" : $" (файл не найден: {cleanSrc})"));
                        continue;
                    }

                    var game = HotSwapPaths.GamePath(gtaRoot, rel);
                    var gameLen = new FileInfo(game).Length;
                    if (gameLen == new FileInfo(cleanSrc).Length)
                    {
                        HotSwapLog.Write("freeze",
                            $"{rel}: пропуск - не модифицирован (размер совпал с чистым, {gameLen} байт)");
                        continue;
                    }

                    HotSwapLog.Write("freeze", $"{rel}: замораживаю ({gameLen} байт, чистый источник: {cleanSrc})");
                    var sw = Stopwatch.StartNew();
                    eng.FreezeOne(gtaRoot, rel, cleanSrc);
                    HotSwapLog.Write("freeze", $"{rel}: готово за {sw.ElapsedMilliseconds} мс");
                    done.Add(rel);
                    WriteSet(gtaRoot, done);
                }
                if (done.Count == 0)
                {
                    HotSwapJournal.Write(gtaRoot, HotSwapPhase.Idle);
                    HotSwapStore.Unbind(gtaRoot);
                    error = Loc.T("error.hotSwapNothingToFreeze");
                    HotSwapLog.Write("freeze", "отказ: нечего замораживать - ни один файл игры не отличается от чистого");
                    return false;
                }
                WriteSet(gtaRoot, done);
                WriteBaseline(gtaRoot, done);
                HotSwapJournal.Write(gtaRoot, HotSwapPhase.Idle);
                HotSwapLog.Write("freeze",
                    $"готово: {done.Count} файл(ов) [{string.Join(", ", done)}] за {swTotal.ElapsedMilliseconds} мс");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                HotSwapLog.Write("freeze", "ошибка заморозки, зову восстановление", ex);
                try { HotSwapRecovery.EnsureConsistent(gtaRoot, out _); } catch { }
                return false;
            }
        }

        private static bool GameFileIsForeign(string gtaRoot, string rel)
        {
            var now = HotSwapFileOps.Stamp(HotSwapPaths.GamePath(gtaRoot, rel));
            if (string.IsNullOrEmpty(now)) return false;

            var gamePath = HotSwapPaths.GamePath(gtaRoot, rel);
            var moddedPath = HotSwapPaths.ModdedPath(gtaRoot, rel);
            var cleanPath = HotSwapPaths.CleanPath(gtaRoot, rel);
            var modded = HotSwapFileOps.Stamp(moddedPath);
            var cleanCopy = HotSwapFileOps.Stamp(cleanPath);
            bool haveB = ReadBaseline(gtaRoot).TryGetValue(rel, out var b) && !string.IsNullOrEmpty(b);
            var bStamp = ""; var bSig = "";
            if (haveB) { int bar = b!.IndexOf('|'); bStamp = bar < 0 ? b : b.Substring(0, bar); bSig = bar < 0 ? "" : b.Substring(bar + 1); }

            bool SameContentAsAny(params string[] refs)
            {
                var nowSig = HotSwapFileOps.ContentSig(gamePath);
                if (nowSig.Length == 0) return true;
                foreach (var r in refs)
                {
                    if (string.IsNullOrEmpty(r) || !File.Exists(r)) continue;
                    var sig = HotSwapFileOps.ContentSig(r);
                    if (sig.Length > 0 && string.Equals(sig, nowSig, StringComparison.Ordinal)) return true;
                }
                return bSig.Length > 0 && string.Equals(bSig, nowSig, StringComparison.Ordinal);
            }

            if (SwapEngines.ForActive(gtaRoot).IsArmed(gtaRoot, rel))
            {
                if (!ReadArmedStamps(gtaRoot).TryGetValue(rel, out var a) || string.IsNullOrEmpty(a)) return false;
                if (string.Equals(now, a, StringComparison.Ordinal)) return false;

                var moddedSig = HotSwapFileOps.ContentSig(moddedPath);
                if (moddedSig.Length == 0) return false;
                var nowSigArmed = HotSwapFileOps.ContentSig(gamePath);
                if (nowSigArmed.Length == 0) return false;
                return !string.Equals(moddedSig, nowSigArmed, StringComparison.Ordinal);
            }

            if (string.Equals(now, modded, StringComparison.Ordinal)) return false;
            if (string.Equals(now, cleanCopy, StringComparison.Ordinal)) return false;
            if (haveB && string.Equals(now, bStamp, StringComparison.Ordinal)) return false;

            if (!(haveB || modded.Length > 0 || cleanCopy.Length > 0)) return false;
            return !SameContentAsAny(moddedPath, cleanPath);
        }

        private static void DropImageOne(string gtaRoot, string rel)
        {
            HotSwapFileOps.DeleteQuiet(HotSwapPaths.ModdedPath(gtaRoot, rel));
            HotSwapFileOps.DeleteQuiet(HotSwapPaths.CleanPath(gtaRoot, rel));
            HotSwapFileOps.DeleteQuiet(HotSwapFileOps.TempFor(HotSwapPaths.ModdedPath(gtaRoot, rel)));
            HotSwapFileOps.DeleteQuiet(HotSwapFileOps.TempFor(HotSwapPaths.CleanPath(gtaRoot, rel)));
            HotSwapFileOps.DeleteQuiet(HotSwapFileOps.TempFor(HotSwapPaths.GamePath(gtaRoot, rel)));
        }

        public static void DropImage(string gtaRoot)
        {
            foreach (var rel in ReadSet(gtaRoot)) DropImageOne(gtaRoot, rel);
            try { File.Delete(HotSwapPaths.SetPath(gtaRoot)); } catch { }
            try { File.Delete(BaselinePath(gtaRoot)); } catch { }
            DropArmedStamps(gtaRoot);
            RockstarRepairWatch.Clear(gtaRoot);
            HotSwapJournal.Write(gtaRoot, HotSwapPhase.Idle);
            HotSwapStore.Unbind(gtaRoot);
            HotSwapLog.Write("unfreeze", "образ снесён без возврата: игры по замороженному корню больше нет");
        }

        public static bool Unfreeze(string gtaRoot, out string? error) =>
            Unfreeze(gtaRoot, HotSwapStore.ActiveMethod(gtaRoot), out error);

        public static bool Unfreeze(string gtaRoot, HotSwapMethod method, out string? error) =>
            Unfreeze(gtaRoot, method, out error, out _);

        public static bool Unfreeze(string gtaRoot, HotSwapMethod method, out string? error, out List<string> dropped)
        {
            error = null;
            dropped = new List<string>();
            var envUnfreeze = RockstarRepairWatch.Probe(gtaRoot);
            if (envUnfreeze.RepairInProgress)
            {
                error = Loc.T("error.hotSwapRepairInProgress");
                HotSwapLog.Write("unfreeze",
                    "отказ: Rockstar Launcher докачивает файлы игры (" + envUnfreeze.Describe() +
                    ") - выключение режима отложено, повтори после докачки");
                return false;
            }

            try
            {
                var eng = SwapEngines.For(method);
                var set = ReadSet(gtaRoot);
                HotSwapLog.Write("unfreeze", $"старт: способ {(int)method} ({HotSwapPlan.For(method).Title}), " +
                    $"файлов в наборе {set.Count}");
                var swTotal = Stopwatch.StartNew();
                HotSwapJournal.Write(gtaRoot, HotSwapPhase.Arming);
                foreach (var rel in set)
                {
                    if (GameFileIsForeign(gtaRoot, rel))
                    {
                        DropImageOne(gtaRoot, rel);
                        dropped.Add(rel);
                        HotSwapLog.Write("unfreeze",
                            $"{rel}: в игре лежит ЧУЖОЙ файл (перекачан Rockstar Launcher'ом) - " +
                            "оставляю его, копии образа выброшены. Моды для этого файла надо ставить заново.");
                        continue;
                    }
                    var sw = Stopwatch.StartNew();
                    eng.UnfreezeOne(gtaRoot, rel);
                    DropImageOne(gtaRoot, rel);
                    HotSwapLog.Write("unfreeze", $"{rel}: моды возвращены в игру за {sw.ElapsedMilliseconds} мс");
                }
                try { File.Delete(HotSwapPaths.SetPath(gtaRoot)); } catch { }
                try { File.Delete(BaselinePath(gtaRoot)); } catch { }
                DropArmedStamps(gtaRoot);
                RockstarRepairWatch.Clear(gtaRoot);
                HotSwapJournal.Write(gtaRoot, HotSwapPhase.Idle);
                HotSwapStore.Unbind(gtaRoot);
                HotSwapLog.Write("unfreeze", $"готово за {swTotal.ElapsedMilliseconds} мс");
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                HotSwapLog.Write("unfreeze", "ошибка разморозки", ex);
                return false;
            }
        }
    }
}
