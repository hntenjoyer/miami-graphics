using System.Diagnostics;
using System.Threading;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Shell.Services;

internal sealed class InstallProgressGate : IDisposable
{
    internal delegate void Sender(string key, string name, string phase, int percent,
                                  string? errorMessage, string? detailMessage);

    private static readonly TimeSpan SilenceBeforeHeartbeat = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan MinEmitInterval = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan AbandonAfter = TimeSpan.FromMinutes(20);

    private static readonly TimeSpan TerminalCooldown = TimeSpan.FromSeconds(15);

    private sealed class Row
    {
        public string  Name          = string.Empty;
        public string  Phase         = string.Empty;
        public int     Percent;
        public string? Detail;
        public long    StepStartedMs;
        public long    LastRealMs;
        public long    LastSentMs;
        public bool    HasSent;
        public bool    Dirty;
    }

    private readonly Dictionary<string, Row> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _terminatedAt = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly Sender _send;
    private readonly Timer  _watchdog;
    private bool _disposed;

    private IDisposable? _quotaHold;

    private IDisposable? _quotaPending;

    public InstallProgressGate(Sender send)
    {
        _send = send;
        _watchdog = new Timer(_ => Tick(), null, (int)TickInterval.TotalMilliseconds,
                                                 (int)TickInterval.TotalMilliseconds);
    }

    private void SyncQuotaHold()
    {
        bool writing = false;
        bool queued  = false;
        foreach (var kv in _rows)
        {
            if (string.Equals(kv.Value.Phase, "queued", StringComparison.OrdinalIgnoreCase)) queued = true;
            else writing = true;
            if (writing && queued) break;
        }

        if (writing)
            _quotaHold ??= MiamiGraphics.Core.System.DataQuota.Hold("установка");
        else if (_quotaHold is not null)
        {
            try { _quotaHold.Dispose(); } catch { }
            _quotaHold = null;
        }

        if (writing || queued)
            _quotaPending ??= MiamiGraphics.Core.System.DataQuota.HoldPending("очередь установок");
        else if (_quotaPending is not null)
        {
            try { _quotaPending.Dispose(); } catch { }
            _quotaPending = null;
        }
    }

    private static long NowMs() => Environment.TickCount64;

    private static bool IsTerminal(string phase)
        => string.Equals(phase, "done",      StringComparison.OrdinalIgnoreCase)
        || string.Equals(phase, "error",     StringComparison.OrdinalIgnoreCase)
        || string.Equals(phase, "cancelled", StringComparison.OrdinalIgnoreCase);

    public int Admit(string key, string name, string phase, int percent,
                     string? errorMessage, string? detailMessage)
    {
        key ??= string.Empty;
        phase ??= string.Empty;
        bool terminal = IsTerminal(phase);
        int outPercent;
        long now = NowMs();
        bool emit = true;

        lock (_gate)
        {
            if (terminal)
            {
                _rows.Remove(key);
                _terminatedAt[key] = now;
                outPercent = percent;
            }
            else
            {
                bool opener = percent <= 0
                    && (string.Equals(phase, "starting", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(phase, "queued",   StringComparison.OrdinalIgnoreCase));
                if (_terminatedAt.TryGetValue(key, out var terminatedMs))
                {
                    if (!opener && now - terminatedMs < TerminalCooldown.TotalMilliseconds)
                    {
                        Debug.WriteLine(
                            $"[progress.tail] key='{key}' phase='{phase}' {percent}% пришёл после " +
                            "терминала - глотаю, иначе строка воскреснет и повиснет навсегда.");
                        return -1;
                    }
                    _terminatedAt.Remove(key);
                }

                if (!_rows.TryGetValue(key, out var row))
                {
                    row = new Row { StepStartedMs = now };
                    _rows[key] = row;
                }

                int wanted = percent < 0 ? row.Percent : percent;

                if (wanted < row.Percent)
                {
                    Debug.WriteLine(
                        $"[progress.regress] key='{key}' phase='{phase}' пришёл {wanted}% " +
                        $"после {row.Percent}% - полоса заморожена до {row.Percent}%. " +
                        "Вложенный шаг обязан получать полосу (from..to) от вызывающего.");
                    wanted = row.Percent;
                }

                bool phaseChanged  = !string.Equals(row.Phase,  phase,         StringComparison.Ordinal);
                bool detailChanged = !string.Equals(row.Detail, detailMessage, StringComparison.Ordinal);
                bool stepChanged   = phaseChanged || detailChanged;
                bool changed       = stepChanged || wanted != row.Percent;

                row.Name       = name ?? string.Empty;
                row.Phase      = phase;
                row.Percent    = wanted;
                row.Detail     = detailMessage;
                row.LastRealMs = now;
                if (stepChanged) row.StepStartedMs = now;

                emit = !row.HasSent
                    || phaseChanged
                    || errorMessage is not null
                    || (changed && now - row.LastSentMs >= MinEmitInterval.TotalMilliseconds);

                if (emit)
                {
                    row.HasSent    = true;
                    row.LastSentMs = now;
                    row.Dirty      = false;
                }
                else if (changed) row.Dirty = true;

                outPercent = wanted;
            }

            SyncQuotaHold();
        }

        if (emit) _send(key, name ?? string.Empty, phase, outPercent, errorMessage, detailMessage);
        return outPercent;
    }

    private void Tick()
    {
        if (_disposed) return;
        long now = NowMs();

        lock (_gate)
        {
            if (_terminatedAt.Count > 0)
            {
                List<string>? cooled = null;
                foreach (var kv in _terminatedAt)
                    if (now - kv.Value >= TerminalCooldown.TotalMilliseconds)
                        (cooled ??= new List<string>()).Add(kv.Key);
                if (cooled is not null)
                    foreach (var k in cooled) _terminatedAt.Remove(k);
            }

            List<string>? abandoned = null;
            foreach (var kv in _rows)
            {
                var row = kv.Value;
                long silentMs = now - row.LastRealMs;

                if (silentMs > AbandonAfter.TotalMilliseconds)
                {
                    (abandoned ??= new List<string>()).Add(kv.Key);
                    continue;
                }

                if (row.Dirty && now - row.LastSentMs >= MinEmitInterval.TotalMilliseconds)
                {
                    row.Dirty      = false;
                    row.HasSent    = true;
                    row.LastSentMs = now;
                    try { _send(kv.Key, row.Name, row.Phase, row.Percent, null, row.Detail); }
                    catch (Exception ex) { Debug.WriteLine($"[progress.gate] flush FAIL: {ex.Message}"); }
                    continue;
                }

                if (silentMs < SilenceBeforeHeartbeat.TotalMilliseconds) continue;
                if (string.Equals(row.Phase, "queued", StringComparison.OrdinalIgnoreCase)) continue;
                if (now - row.LastSentMs < HeartbeatInterval.TotalMilliseconds) continue;

                row.LastSentMs = now;
                try
                {
                    _send(kv.Key, row.Name, row.Phase, row.Percent, null,
                        WithElapsed(row.Detail ?? PhaseText(row.Phase), now - row.StepStartedMs));
                }
                catch (Exception ex) { Debug.WriteLine($"[progress.gate] heartbeat FAIL: {ex.Message}"); }
            }
            if (abandoned is not null)
            {
                foreach (var k in abandoned)
                {
                    Debug.WriteLine($"[progress.gate] строка '{k}' молчит дольше {AbandonAfter.TotalMinutes:F0} мин - снимаю сторожа.");
                    _rows.Remove(k);
                }
                SyncQuotaHold();
            }
        }
    }

    private static string PhaseText(string phase) => phase switch
    {
        "starting"              => Loc.T("phase.starting"),
        "downloading"           => Loc.T("phase.downloading"),
        "downloading_new"       => Loc.T("phase.downloading"),
        "downloading_pack"      => Loc.T("phase.downloadingPack"),
        "downloading_packs"     => Loc.T("phase.downloadingPacks"),
        "downloading_template"  => Loc.T("phase.downloadingTemplate"),
        "downloading_empty_template" => Loc.T("phase.downloadingTemplate"),
        "verifying"             => Loc.T("phase.verifying"),
        "extracting"            => Loc.T("phase.extracting"),
        "computing_diff"        => Loc.T("phase.computingDiff"),
        "restoring_old_state"   => Loc.T("phase.restoringOldState"),
        "installing_new"        => Loc.T("phase.installingNew"),
        "applying_user_changes" => Loc.T("phase.applyingUserChanges"),
        "injecting"             => Loc.T("phase.injecting"),
        "installing"            => Loc.T("phase.installing"),
        "registering"           => Loc.T("phase.registering"),
        "merging_guns"          => Loc.T("phase.mergingGuns"),
        "building_rpf"          => Loc.T("phase.buildingRpf"),
        "removing_from_dlc"     => Loc.T("phase.removingFromDlc"),
        "restoring"             => Loc.T("phase.restoring"),
        "preparing"             => Loc.T("phase.preparing"),
        "resolving_version"     => Loc.T("phase.resolvingVersion"),
        "updating_state"        => Loc.T("phase.updatingState"),
        "weaponmeta"            => Loc.T("phase.weaponMeta"),
        _                       => Loc.T("phase.working"),
    };

    internal static string WithElapsed(string? detail, long elapsedMs)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, elapsedMs));
        var clock = t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
        var text = string.IsNullOrWhiteSpace(detail) ? Loc.T("phase.working") : detail!.TrimEnd();
        return text.EndsWith("...", StringComparison.Ordinal) || text.EndsWith("…", StringComparison.Ordinal)
            ? $"{text} {clock}"
            : $"{text} - {clock}";
    }

    public void Dispose()
    {
        _disposed = true;
        try { _watchdog.Dispose(); } catch { }
        lock (_gate) { _rows.Clear(); _terminatedAt.Clear(); SyncQuotaHold(); }
    }
}
