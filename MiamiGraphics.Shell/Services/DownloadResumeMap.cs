using System.Diagnostics;
using System.IO;
using System.Text;

namespace MiamiGraphics.Shell.Services;

internal sealed class DownloadResumeMap
{
    public const int BlockBytes = 8 * 1024 * 1024;

    private const string Magic = "MGPART01";

    private static readonly TimeSpan PersistEvery = TimeSpan.FromSeconds(2);

    private readonly bool[] _done;
    private readonly string _sidecarPath;
    private readonly bool _persist;
    private readonly object _gate = new();
    private long _lastPersistTicks;
    private bool _dirty;

    public long Total { get; }
    public int BlockCount => _done.Length;

    private DownloadResumeMap(string sidecarPath, long total, bool[] done, bool persist)
    {
        _sidecarPath = sidecarPath;
        Total = total;
        _done = done;
        _persist = persist;
    }

    public static string SidecarFor(string destPath) => destPath + ".mgpart";

    public static DownloadResumeMap OpenOrCreate(string destPath, long total, bool allowResume, out bool resumed)
    {
        resumed = false;
        var sidecar = SidecarFor(destPath);
        var blockCount = total <= 0 ? 0 : (int)((total + BlockBytes - 1) / BlockBytes);

        if (allowResume && total > 0 && TryLoad(sidecar, total, blockCount) is { } loaded)
        {
            long len = -1;
            try { len = new FileInfo(destPath).Length; } catch { }
            if (len == total)
            {
                var have = 0;
                foreach (var b in loaded) if (b) have++;
                if (have > 0)
                {
                    resumed = true;
                    DownloadLog.Write("chunk",
                        $"докачка: на диске уже {have * (long)BlockBytes / 1048576} МБ из {total / 1048576} МБ, продолжаю с этого места");
                }
                return new DownloadResumeMap(sidecar, total, loaded, persist: true);
            }
        }

        TryDelete(sidecar);
        return new DownloadResumeMap(sidecar, total, new bool[Math.Max(blockCount, 0)], persist: allowResume);
    }

    private static bool[]? TryLoad(string sidecar, long total, int blockCount)
    {
        try
        {
            if (!File.Exists(sidecar)) return null;
            using var fs = File.OpenRead(sidecar);
            using var br = new BinaryReader(fs, Encoding.ASCII);
            var magic = new string(br.ReadChars(Magic.Length));
            if (!string.Equals(magic, Magic, StringComparison.Ordinal)) return null;
            if (br.ReadInt64() != total) return null;
            if (br.ReadInt32() != BlockBytes) return null;
            if (br.ReadInt32() != blockCount) return null;

            var bytes = br.ReadBytes((blockCount + 7) / 8);
            if (bytes.Length != (blockCount + 7) / 8) return null;
            var done = new bool[blockCount];
            for (var i = 0; i < blockCount; i++)
                done[i] = (bytes[i >> 3] & (1 << (i & 7))) != 0;
            return done;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[resume] sidecar read failed {sidecar}: {ex.Message}");
            return null;
        }
    }

    public bool IsDone(int block)
    {
        lock (_gate) return block >= 0 && block < _done.Length && _done[block];
    }

    public void MarkDone(int block)
    {
        bool persist;
        lock (_gate)
        {
            if (block < 0 || block >= _done.Length || _done[block]) return;
            _done[block] = true;
            _dirty = true;
            var now = Environment.TickCount64;
            persist = now - _lastPersistTicks >= PersistEvery.TotalMilliseconds;
            if (persist) _lastPersistTicks = now;
        }
        if (persist) Flush();
    }

    public long DoneBytes()
    {
        lock (_gate)
        {
            long sum = 0;
            for (var i = 0; i < _done.Length; i++)
            {
                if (!_done[i]) continue;
                var start = (long)i * BlockBytes;
                sum += Math.Min(BlockBytes, Total - start);
            }
            return sum;
        }
    }

    public void Flush()
    {
        byte[] bytes;
        int blockCount;
        if (!_persist) return;
        lock (_gate)
        {
            if (!_dirty) return;
            _dirty = false;
            blockCount = _done.Length;
            bytes = new byte[(blockCount + 7) / 8];
            for (var i = 0; i < blockCount; i++)
                if (_done[i]) bytes[i >> 3] |= (byte)(1 << (i & 7));
        }
        try
        {
            var tmp = _sidecarPath + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(_sidecarPath)!);
            using (var fs = File.Create(tmp))
            using (var bw = new BinaryWriter(fs, Encoding.ASCII))
            {
                bw.Write(Magic.ToCharArray());
                bw.Write(Total);
                bw.Write(BlockBytes);
                bw.Write(blockCount);
                bw.Write(bytes);
            }
            File.Move(tmp, _sidecarPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[resume] sidecar write failed {_sidecarPath}: {ex.Message}");
        }
    }

    public void Discard()
    {
        lock (_gate) _dirty = false;
        TryDelete(_sidecarPath);
    }

    public static void DiscardFor(string destPath) => TryDelete(SidecarFor(destPath));

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        try { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); } catch { }
    }
}
