using System.Diagnostics;

namespace MiamiGraphics.Shell.Services;

public static class UpdateRpfMutex
{
    private static readonly SemaphoreSlim _mutex = new(1, 1);
    private static readonly TimeSpan AcquireTimeout = TimeSpan.FromMinutes(10);
    private static string? _currentHolder;
    private static readonly object _holderLock = new();

    public static Action<string>? Contended;

    public static Action? GameRunning;

    private static DateTime _lastGameRunningNotice = DateTime.MinValue;
    private static readonly TimeSpan GameRunningNoticeGap = TimeSpan.FromMinutes(1);

    public static Action? Sealing;

    public static async Task<IDisposable> AcquireAsync(string holderName, CancellationToken ct = default, bool silent = false)
    {
        var sw = Stopwatch.StartNew();
        if (!silent && _mutex.CurrentCount == 0)
        {
            try { Contended?.Invoke(holderName); } catch {}
        }
        if (!silent && GameRunning is not null
            && DateTime.UtcNow - _lastGameRunningNotice > GameRunningNoticeGap)
        {
            try
            {
                if (MiamiGraphics.Core.HotSwap.GameProcessWatcher.FindRpfHolderProcess() is not null)
                {
                    _lastGameRunningNotice = DateTime.UtcNow;
                    GameRunning.Invoke();
                }
            }
            catch {}
        }
        var entered = await _mutex.WaitAsync(AcquireTimeout, ct);
        if (!entered)
        {
            string? current;
            lock (_holderLock) current = _currentHolder;
            throw new TimeoutException(
                $"UpdateRpfMutex acquire timeout ({AcquireTimeout.TotalMinutes:F0} min). " +
                $"Current holder: {current ?? "<unknown>"}. Requested by: {holderName}.");
        }
        lock (_holderLock) _currentHolder = holderName;
        Debug.WriteLine($"[update.rpf] mutex ACQUIRED by '{holderName}' (waited {sw.ElapsedMilliseconds}ms)");
        return new Releaser(holderName);
    }

    public static async Task<IDisposable?> TryAcquireAsync(string holderName, TimeSpan wait, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        bool entered;
        try { entered = await _mutex.WaitAsync(wait, ct); }
        catch (OperationCanceledException) { return null; }
        if (!entered)
        {
            string? current;
            lock (_holderLock) current = _currentHolder;
            Debug.WriteLine($"[update.rpf] mutex BUSY ({current ?? "<unknown>"}), '{holderName}' " +
                            $"не стал ждать дольше {wait.TotalSeconds:F1} с");
            return null;
        }
        lock (_holderLock) _currentHolder = holderName;
        Debug.WriteLine($"[update.rpf] mutex ACQUIRED by '{holderName}' (waited {sw.ElapsedMilliseconds}ms, try)");
        return new Releaser(holderName);
    }

    private sealed class Releaser : IDisposable
    {
        private readonly string _holder;
        private int _disposed;

        public Releaser(string holder) { _holder = holder; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try { Sealing?.Invoke(); }
            catch (Exception ex) { Debug.WriteLine($"[update.rpf] seal hook fail: {ex.Message}"); }

            lock (_holderLock) { if (_currentHolder == _holder) _currentHolder = null; }
            try { _mutex.Release(); }
            catch (Exception ex) { Debug.WriteLine($"[update.rpf] mutex Release fail: {ex.Message}"); }
            Debug.WriteLine($"[update.rpf] mutex RELEASED by '{_holder}'");
        }
    }
}
