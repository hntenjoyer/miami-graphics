#nullable enable
using System;
using System.Collections.Generic;
using System.Management;
using System.Threading;

namespace MiamiGraphics.Core.HotSwap
{
    internal sealed class ProcessStartNotifier : IDisposable
    {
        private readonly AutoResetEvent _hit = new(false);
        private readonly HashSet<string> _names;
        private ManagementEventWatcher? _watcher;

        public bool Active { get; private set; }

        public ProcessStartNotifier(IEnumerable<string> names)
        {
            _names = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        }

        public bool TryStart(out string? error)
        {
            error = null;
            if (!OperatingSystem.IsWindows()) { error = "не Windows"; return false; }
            try
            {
                _watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
                _watcher.EventArrived += OnStart;
                _watcher.Start();
                Active = true;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                try { _watcher?.Dispose(); } catch { }
                _watcher = null;
                Active = false;
                return false;
            }
        }

        private void OnStart(object sender, EventArrivedEventArgs e)
        {
            try
            {
                var raw = e.NewEvent?["ProcessName"] as string;
                if (string.IsNullOrEmpty(raw)) { Wake(); return; }
                var name = raw!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? raw[..^4] : raw;
                if (_names.Contains(name)) Wake();
            }
            catch { Wake(); }
        }

        private void Wake()
        {
            try { _hit.Set(); } catch { }
        }

        public bool Wait(int timeoutMs) => _hit.WaitOne(timeoutMs);

        public void Dispose()
        {
            try { if (_watcher != null) { _watcher.EventArrived -= OnStart; _watcher.Stop(); _watcher.Dispose(); } } catch { }
            _watcher = null;
            Active = false;
            try { _hit.Dispose(); } catch { }
        }
    }
}
