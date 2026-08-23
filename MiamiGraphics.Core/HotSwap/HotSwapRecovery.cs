using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MiamiGraphics.Core.HotSwap
{
    public static class HotSwapRecovery
    {
        private static string? _lastDecision;

        private static void LogDecision(string msg)
        {
            if (string.Equals(msg, _lastDecision, StringComparison.Ordinal)) return;
            _lastDecision = msg;
            HotSwapLog.Write("recovery", msg);
        }

        public static bool EnsureConsistent(string gtaRoot, out string msg)
        {
            msg = "ок";
            try
            {
                var method = HotSwapStore.ActiveMethod(gtaRoot);
                var eng = SwapEngines.For(method);
                var j = HotSwapJournal.Read(gtaRoot);
                var set = GameFileSwapper.ReadSet(gtaRoot);
                bool needsReturn = set.Any(r => eng.CanDisarm(gtaRoot, r));
                var entry = $"вход: фаза {j.Phase}, способ {(int)method}, набор {set.Count}, " +
                            $"есть что возвращать: {(needsReturn ? "да" : "нет")}";

                if (RockstarRepairWatch.Read(gtaRoot) is { } mark)
                {
                    LogDecision(entry + $" -> зафиксирован ремонт Rockstar ({mark.Rel}): файлы игры не трогаю, жду пересборки образа");
                    if (j.Phase != HotSwapPhase.Idle) HotSwapJournal.Write(gtaRoot, HotSwapPhase.Idle);
                    msg = "ремонт Rockstar: образ протух, файлы игры оставлены как есть";
                    return true;
                }

                switch (j.Phase)
                {
                    case HotSwapPhase.Freezing:
                        {
                            LogDecision(entry + " -> прерванная заморозка: откатываю уехавшие файлы обратно в игру");
                            int back = 0;
                            foreach (var rel in HotSwapPaths.RelPaths)
                            {
                                if (!eng.HasImage(gtaRoot, rel)) continue;
                                eng.RollbackFreezeOne(gtaRoot, rel);
                                HotSwapLog.Write("recovery", $"{rel}: мод возвращён в игру (откат заморозки)");
                                back++;
                            }
                            try { File.Delete(HotSwapPaths.SetPath(gtaRoot)); } catch { }
                            HotSwapJournal.Write(gtaRoot, HotSwapPhase.Idle);
                            HotSwapStore.Unbind(gtaRoot);
                            msg = back > 0
                                ? $"вернул {back} файл(ов) игры после прерванной заморозки"
                                : "прерванная заморозка: файлы игры целы";
                            LogDecision("итог отката заморозки: " + msg);
                            return true;
                        }

                    case HotSwapPhase.Armed:
                    case HotSwapPhase.Arming:
                        if (HotSwapPlan.IsManual(method))
                        {
                            msg = "ручной способ: состояние оставлено как есть";
                            LogDecision(entry + " -> ручной способ: не трогаю (решение за человеком)");
                            return true;
                        }

                        if (FindGameForMethod(method, gtaRoot) is null)
                        {
                            SanitizeAll(eng, gtaRoot, set);
                            if (needsReturn)
                            {
                                LogDecision(entry + " -> игры нет, моды в игре: возвращаю чистый файл");
                                if (GameFileSwapper.Disarm(gtaRoot, method, out var e))
                                { msg = "вернул чистый файл после незавершённой сессии"; LogDecision("итог: " + msg); return true; }
                                msg = "disarm не удался: " + e;
                                LogDecision("итог: " + msg);
                                return false;
                            }
                            LogDecision(entry + " -> игры нет, возвращать нечего: фаза сброшена в Idle");
                            HotSwapJournal.Write(gtaRoot, HotSwapPhase.Idle);
                            return true;
                        }
                        LogDecision(entry + " -> игра запущена: не трогаю");
                        return true;

                    case HotSwapPhase.Disarming:
                        if (needsReturn && FindGameForMethod(method, gtaRoot) is null)
                        {
                            LogDecision(entry + " -> прерванный возврат: довершаю disarm");
                            SanitizeAll(eng, gtaRoot, set);
                            if (GameFileSwapper.Disarm(gtaRoot, method, out var e2))
                            { msg = "довершил возврат чистого файла"; LogDecision("итог: " + msg); return true; }
                            msg = "довершение не удалось: " + e2;
                            LogDecision("итог: " + msg);
                            return false;
                        }
                        LogDecision(entry + " -> возврат уже довершён (или игра бежит): фаза сброшена в Idle");
                        HotSwapJournal.Write(gtaRoot, HotSwapPhase.Idle);
                        return true;

                    default:
                        LogDecision(entry + " -> штатно, ничего не делаю");
                        if (HotSwapPlan.For(method).Primitive == HotSwapPrimitive.SafeCopy
                            && FindGameForMethod(method, gtaRoot) is null)
                            SanitizeAll(eng, gtaRoot, set);
                        return true;
                }
            }
            catch (Exception ex)
            {
                msg = ex.Message;
                HotSwapLog.Write("recovery", "ошибка EnsureConsistent", ex);
                return false;
            }
        }

        private static int? FindGameForMethod(HotSwapMethod method, string gtaRoot)
        {
            var plan = HotSwapPlan.For(method);
            return plan.UseReplaceXProcessList
                ? GameProcessWatcher.FindGameProcess(GameProcessWatcher.ReplaceXArmProcesses, gtaRoot)
                : GameProcessWatcher.FindGameProcess();
        }

        private static void SanitizeAll(ISwapEngine eng, string gtaRoot, List<string> set)
        {
            foreach (var rel in set.Count > 0 ? set : HotSwapPaths.RelPaths.ToList())
            {
                try { eng.SanitizeOne(gtaRoot, rel); } catch { }
            }
        }
    }
}
