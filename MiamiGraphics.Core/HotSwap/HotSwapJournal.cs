using System;
using System.IO;
using System.Text.Json;

namespace MiamiGraphics.Core.HotSwap
{
    public enum HotSwapPhase
    {
        Idle = 0,
        Arming = 1,
        Armed = 2,
        Disarming = 3,
        Freezing = 4,
    }

    public sealed class HotSwapJournalState
    {
        public HotSwapPhase Phase { get; set; } = HotSwapPhase.Idle;
        public string? UpdatedAtUtc { get; set; }
        public int? GamePid { get; set; }
    }

    public static class HotSwapJournal
    {
        public static HotSwapJournalState Read(string gtaRoot)
        {
            try
            {
                var p = HotSwapPaths.JournalPath(gtaRoot);
                if (!File.Exists(p)) return new HotSwapJournalState();
                return JsonSerializer.Deserialize<HotSwapJournalState>(File.ReadAllText(p))
                       ?? new HotSwapJournalState();
            }
            catch { return new HotSwapJournalState(); }
        }

        public static void Write(string gtaRoot, HotSwapPhase phase, int? gamePid = null)
        {
            var old = Read(gtaRoot).Phase;
            var p = HotSwapPaths.JournalPath(gtaRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new HotSwapJournalState
            {
                Phase = phase,
                UpdatedAtUtc = DateTime.UtcNow.ToString("O"),
                GamePid = gamePid,
            }));
            File.Move(tmp, p, overwrite: true);
            HotSwapLog.Write("journal", $"фаза {old} -> {phase}" +
                (gamePid is int pid ? $" (pid игры {pid})" : ""));
        }
    }
}
