using System;
using System.IO;
using System.Threading;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.HotSwap
{
    public static class HotSwapManual
    {
        private static string? ConfiguredRoot() => HotSwapModeStore.Read().GtaRoot;

        public static bool ArmNow(out string? error) => ArmNow(ConfiguredRoot() ?? "", out error);

        public static bool DisarmNow(out string? error) => DisarmNow(ConfiguredRoot() ?? "", out error);

        public static bool ArmNow(string gtaRoot, out string? error)
        {
            HotSwapLog.Write("manual", "кнопка «Захожу в игру»: запрос подмены");
            if (!Precheck(gtaRoot, out var method, out error))
            {
                HotSwapLog.Write("manual", "отказ проверки: " + error);
                return false;
            }

            if (GameProcessWatcher.FindRpfHolderProcess() is not null)
            {
                error = Loc.T("error.gtaAlreadyRunningArm");
                HotSwapLog.Write("manual", "отказ: игра уже запущена");
                return false;
            }

            HotSwapRecovery.EnsureConsistent(gtaRoot, out _);
            var ok = GameFileSwapper.Arm(gtaRoot, null, method, out error);
            HotSwapLog.Write("manual", ok ? "моды подставлены" : "не удалось подставить: " + error);
            return ok;
        }

        public static bool DisarmNow(string gtaRoot, out string? error)
        {
            HotSwapLog.Write("manual", "кнопка «Вышел из игры»: запрос возврата чистых файлов");
            if (!Precheck(gtaRoot, out var method, out error))
            {
                HotSwapLog.Write("manual", "отказ проверки: " + error);
                return false;
            }

            if (GameProcessWatcher.FindRpfHolderProcess() is not null)
            {
                error = Loc.T("error.gtaStillRunningDisarm");
                HotSwapLog.Write("manual", "отказ: игра ещё запущена");
                return false;
            }

            var plan = HotSwapPlan.For(method);
            if (plan.KillGameBeforeReturn)
            {
                int killed = GameProcessWatcher.KillReturnBlockers(gtaRoot);
                HotSwapLog.Write("manual", $"погашено блокеров возврата: {killed}, жду 1 с");
                Thread.Sleep(1000);
            }
            var ok = GameFileSwapper.Disarm(gtaRoot, method, out error);
            HotSwapLog.Write("manual", ok ? "чистые файлы возвращены" : "не удалось вернуть: " + error);
            return ok;
        }

        public static bool IsManualMode(string gtaRoot) =>
            HotSwapPlan.IsManual(HotSwapStore.ActiveMethod(gtaRoot));

        private static bool Precheck(string gtaRoot, out HotSwapMethod method, out string? error)
        {
            method = HotSwapMethod.AgentSameFolder;
            error = null;

            if (string.IsNullOrWhiteSpace(gtaRoot) || !Directory.Exists(gtaRoot))
            {
                error = Loc.T("error.gtaNotFoundShort");
                return false;
            }
            var mode = HotSwapModeStore.Read();
            if (!mode.Enabled)
            {
                error = Loc.T("error.rockstarModeOff");
                return false;
            }
            method = HotSwapStore.ActiveMethod(gtaRoot);
            if (!HotSwapPlan.IsManual(method))
            {
                error = Loc.T("error.hotSwapMethodIsAutomatic", ("method", HotSwapPlan.For(method).LocalizedTitle));
                return false;
            }
            if (GameFileSwapper.ReadSet(gtaRoot).Count == 0)
            {
                error = Loc.T("error.hotSwapImageMissingReenable");
                return false;
            }
            return true;
        }
    }
}
