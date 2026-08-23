using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace MiamiGraphics.Core.HotSwap
{
    public static class HotSwapAgentLoop
    {
        public static void Run(CancellationToken ct = default)
        {
            HotSwapLog.Write("агент", $"старт цикла: pid {Environment.ProcessId}, exe {Environment.ProcessPath}");
            bool wasGame = false;
            DateTime lastBeat = DateTime.MinValue;
            DateTime gameGoneAt = DateTime.MaxValue;
            bool loggedIdle = false;
            HotSwapMethod? loggedMethod = null;
            string? lastLoopError = null;
            DateTime lastLoopErrorAt = DateTime.MinValue;
            DateTime lastRepairCheck = DateTime.MinValue;
            bool repairShouted = false;
            DateTime armRetryAt = DateTime.MinValue;
            string? lastArmError = null;
            const int ArmRetryPauseSec = 10;

            using var starts = new ProcessStartNotifier(
                GameProcessWatcher.ArmProcesses
                    .Concat(GameProcessWatcher.ReplaceXArmProcesses)
                    .Concat(new[] { "Launcher", "RockstarService", "PlayGTAV" }));
            if (starts.TryStart(out var notifyErr))
                HotSwapLog.Write("агент", "подписка на старт процессов поднята - опрос переведён на редкий шаг");
            else
                HotSwapLog.Write("агент", $"подписка на старт процессов не поднялась ({notifyErr}) - опрос частый, как раньше");

            while (!ct.IsCancellationRequested)
            {
                int sleepMs = 250;
                try
                {
                    var mode = HotSwapModeStore.Read();
                    if (!mode.Enabled || string.IsNullOrWhiteSpace(mode.GtaRoot) || !Directory.Exists(mode.GtaRoot))
                    {
                        if (!loggedIdle)
                        {
                            loggedIdle = true;
                            HotSwapLog.Write("агент", "режим выключен или GtaRoot не задан/не существует " +
                                $"(Enabled={mode.Enabled}, GtaRoot={mode.GtaRoot ?? "(нет)"}) - сплю по 3 с");
                        }
                        Thread.Sleep(3000);
                        continue;
                    }
                    if (loggedIdle)
                    {
                        loggedIdle = false;
                        HotSwapLog.Write("агент", $"режим включён, gta {mode.GtaRoot}");
                    }
                    var gta = mode.GtaRoot!;
                    var plan = HotSwapPlan.For(HotSwapStore.ActiveMethod(gta));
                    using var procs = GameProcessWatcher.ProcSnapshot.Take();

                    sleepMs = !starts.Active && GameProcessWatcher.RockstarLauncherRunning(procs)
                        ? Math.Min(plan.PollMs, 30)
                        : plan.PollMs;
                    if (loggedMethod != plan.Method)
                    {
                        loggedMethod = plan.Method;
                        var names = plan.UseReplaceXProcessList
                            ? GameProcessWatcher.ReplaceXArmProcesses
                            : GameProcessWatcher.ArmProcesses;
                        HotSwapLog.Write("агент", $"план: способ {(int)plan.Method} ({plan.Title}), " +
                            $"опрос {plan.PollMs} мс, триггер {(plan.Trigger == HotSwapTrigger.Manual ? "ручной" : "агент")}, " +
                            $"пауза перед возвратом {plan.DisarmDebounceMs} мс, " +
                            $"гашение процессов перед возвратом: {(plan.KillGameBeforeReturn ? "да" : "нет")}, " +
                            $"слежу за процессами [{string.Join(", ", names)}]" +
                            (plan.UseReplaceXProcessList ? " (список ReplaceX)" : " (боевой список)"));
                    }

                    if ((DateTime.UtcNow - lastBeat).TotalSeconds > 10)
                    {
                        HotSwapRecovery.EnsureConsistent(gta, out _);
                        Heartbeat(gta, plan.Trigger == HotSwapTrigger.Manual ? "manual"
                                       : wasGame ? "armed" : "watching");
                        lastBeat = DateTime.UtcNow;
                    }

                    if (plan.Trigger == HotSwapTrigger.Manual)
                    {
                        wasGame = false;
                        gameGoneAt = DateTime.MaxValue;
                        Sleep(starts, sleepMs);
                        continue;
                    }

                    var found = plan.UseReplaceXProcessList
                        ? GameProcessWatcher.FindGameProcessInfo(GameProcessWatcher.ReplaceXArmProcesses, gta, procs)
                        : GameProcessWatcher.FindGameProcessInfo(GameProcessWatcher.ArmProcesses, null, procs);
                    int? pid = found?.Pid;

                    if (pid is null && lastArmError is not null)
                    {
                        lastArmError = null;
                        armRetryAt = DateTime.MinValue;
                    }

                    if (wasGame && (DateTime.UtcNow - lastRepairCheck).TotalSeconds > 5)
                    {
                        lastRepairCheck = DateTime.UtcNow;
                        if (GameFileSwapper.DetectRepairWhileArmed(gta, out var brokenRel) && !repairShouted)
                        {
                            repairShouted = true;
                            HotSwapLog.Write("агент",
                                $"ремонт Rockstar прямо во время сессии: {brokenRel} больше не наш. " +
                                "Моды в этой сессии уже не действуют, образ помечен протухшим.");
                            Heartbeat(gta, "armed", "Rockstar перекачал файлы игры - моды слетели, нужна пересборка образа");
                        }
                    }

                    if (pid is not null && !wasGame && DateTime.UtcNow >= armRetryAt)
                    {
                        if (lastArmError is null)
                            HotSwapLog.Write("агент", $"обнаружена игра: {found!.Value.Name} (pid {pid}) - решение: армить");
                        if (GameFileSwapper.Arm(gta, pid, plan.Method, out var err))
                        {
                            wasGame = true;
                            repairShouted = false;
                            lastArmError = null;
                            armRetryAt = DateTime.MinValue;
                            lastRepairCheck = DateTime.UtcNow;
                            gameGoneAt = DateTime.MaxValue;
                            Heartbeat(gta, "armed");
                        }
                        else
                        {
                            var text = err ?? "без причины";
                            if (!string.Equals(text, lastArmError, StringComparison.Ordinal))
                                HotSwapLog.Write("агент",
                                    $"arm не удался: {text} - повторю через {ArmRetryPauseSec} с, пока игра открыта");
                            lastArmError = text;
                            armRetryAt = DateTime.UtcNow.AddSeconds(ArmRetryPauseSec);
                            Heartbeat(gta, "watching", "arm: " + err);
                        }
                    }
                    else if (pid is null && wasGame)
                    {
                        if (gameGoneAt == DateTime.MaxValue)
                        {
                            gameGoneAt = DateTime.UtcNow;
                            HotSwapLog.Write("агент",
                                $"игра пропала из процессов - жду {plan.DisarmDebounceMs} мс тишины перед возвратом");
                        }
                        if ((DateTime.UtcNow - gameGoneAt).TotalMilliseconds >= plan.DisarmDebounceMs)
                        {
                            HotSwapLog.Write("агент", "решение: дизармить (тишина выдержана)");
                            if (plan.KillGameBeforeReturn)
                            {
                                int killed = GameProcessWatcher.KillReturnBlockers(gta);
                                HotSwapLog.Write("агент", $"погашено процессов-блокеров: {killed}, жду 1 с");
                                Thread.Sleep(1000);
                            }
                            if (GameFileSwapper.Disarm(gta, plan.Method, out var err))
                            {
                                wasGame = false;
                                Heartbeat(gta, "watching");
                            }
                            else
                            {
                                HotSwapLog.Write("агент", "disarm не удался: " + (err ?? "без причины"));
                                Heartbeat(gta, "armed", "disarm: " + err);
                            }
                        }
                    }
                    else if (pid is not null)
                    {
                        if (wasGame && gameGoneAt != DateTime.MaxValue)
                            HotSwapLog.Write("агент",
                                $"игра снова видна ({found!.Value.Name}, pid {pid}) - возврат отменён, сессия продолжается");
                        gameGoneAt = DateTime.MaxValue;
                    }
                }
                catch (Exception ex)
                {
                    if (!string.Equals(ex.Message, lastLoopError, StringComparison.Ordinal)
                        || (DateTime.UtcNow - lastLoopErrorAt).TotalSeconds > 60)
                    {
                        lastLoopError = ex.Message;
                        lastLoopErrorAt = DateTime.UtcNow;
                        HotSwapLog.Write("агент", "ошибка цикла (цикл живёт)", ex);
                    }
                }
                Sleep(starts, sleepMs);
            }
            HotSwapLog.Write("агент", "цикл остановлен (отмена)");
        }

        private static void Sleep(ProcessStartNotifier starts, int ms)
        {
            if (starts.Active) starts.Wait(ms);
            else Thread.Sleep(ms);
        }

        private static string? _lastBeatError;

        private static void Heartbeat(string gtaRoot, string status, string? note = null)
        {
            try
            {
                var p = HotSwapPaths.AgentStatePath(gtaRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                var tmp = p + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(new
                {
                    alive = true,
                    status,
                    note,
                    pid = Environment.ProcessId,
                    atUtc = DateTime.UtcNow.ToString("O"),
                }));
                File.Move(tmp, p, overwrite: true);
                _lastBeatError = null;
            }
            catch (Exception ex)
            {
                if (!string.Equals(ex.Message, _lastBeatError, StringComparison.Ordinal))
                {
                    _lastBeatError = ex.Message;
                    HotSwapLog.Write("агент", "heartbeat не записался", ex);
                }
            }
        }
    }
}
