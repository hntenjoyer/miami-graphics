using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;

namespace MiamiGraphics.Installer.Services;

public sealed class FragmentingHttpHandler : DelegatingHandler
{
    public FragmentingHttpHandler() : this(fragmentCount: 3, interFragmentDelayMs: 0) { }

    public FragmentingHttpHandler(int fragmentCount, int interFragmentDelayMs)
        : base(BuildInner(fragmentCount, interFragmentDelayMs)) { }

    private static SocketsHttpHandler BuildInner(int fragmentCount, int interFragmentDelayMs)
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(60),
            EnableMultipleHttp2Connections = false,

            ConnectCallback = async (ctx, ct) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
                {
                    NoDelay = true,
                };
                try
                {
                    await socket.ConnectAsync(ctx.DnsEndPoint, ct).ConfigureAwait(false);
                    Debug.WriteLine($"[tls-frag] connected {ctx.DnsEndPoint.Host}:{ctx.DnsEndPoint.Port}");
                    return new FragmentingNetworkStream(
                        new NetworkStream(socket, ownsSocket: true),
                        fragmentCount, interFragmentDelayMs, ctx.DnsEndPoint.Host);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
    }
}

internal sealed class FragmentingNetworkStream : Stream
{
    private readonly Stream _inner;
    private readonly int _fragmentCount;
    private readonly int _interFragmentDelayMs;
    private readonly byte[]? _sniHostBytes;
    private int _writeCount;

    public FragmentingNetworkStream(Stream inner, int fragmentCount = 3, int interFragmentDelayMs = 0, string? sniHost = null)
    {
        _inner = inner;
        _fragmentCount = Math.Max(1, fragmentCount);
        _interFragmentDelayMs = Math.Max(0, interFragmentDelayMs);
        _sniHostBytes = string.IsNullOrEmpty(sniHost)
            ? null
            : System.Text.Encoding.ASCII.GetBytes(sniHost);
    }

    public override void Write(byte[] buffer, int offset, int count)
        => WriteAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        var n = Interlocked.Increment(ref _writeCount);
        if (n == 1 && buffer.Length > 64 && _fragmentCount > 1)
        {
            await FragmentedWriteAsync(buffer, ct).ConfigureAwait(false);
        }
        else
        {
            await _inner.WriteAsync(buffer, ct).ConfigureAwait(false);
        }
    }

    private async ValueTask FragmentedWriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct)
    {
        var cuts = new SortedSet<int>();
        for (int i = 1; i < _fragmentCount; i++)
        {
            int c = i * (buffer.Length / _fragmentCount);
            if (c > 0 && c < buffer.Length) cuts.Add(c);
        }
        int sniSplit = FindSniSplitPoint(buffer.Span);
        if (sniSplit > 0 && sniSplit < buffer.Length) cuts.Add(sniSplit);
        if (cuts.Count == 0) cuts.Add(buffer.Length / 2);

        Debug.WriteLine($"[tls-frag] fragmenting ClientHello: {buffer.Length} bytes → {cuts.Count + 1} parts"
            + (sniSplit > 0 ? $" (SNI split @ {sniSplit})" : " (equal split - SNI not located)")
            + (_interFragmentDelayMs > 0 ? $", {_interFragmentDelayMs}ms delay" : ""));

        int prev = 0;
        foreach (var cut in cuts)
        {
            await _inner.WriteAsync(buffer.Slice(prev, cut - prev), ct).ConfigureAwait(false);
            await _inner.FlushAsync(ct).ConfigureAwait(false);
            if (_interFragmentDelayMs > 0) await Task.Delay(_interFragmentDelayMs, ct).ConfigureAwait(false);
            prev = cut;
        }
        await _inner.WriteAsync(buffer.Slice(prev, buffer.Length - prev), ct).ConfigureAwait(false);
        await _inner.FlushAsync(ct).ConfigureAwait(false);
    }

    private int FindSniSplitPoint(ReadOnlySpan<byte> hello)
    {
        var host = _sniHostBytes;
        if (host is null || host.Length < 4 || hello.Length < host.Length) return -1;
        int idx = hello.IndexOf(host);
        if (idx < 0) return -1;
        return idx + host.Length / 2;
    }

    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);

    public override int Read(byte[] buffer, int offset, int count)
        => _inner.Read(buffer, offset, count);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => _inner.ReadAsync(buffer, offset, count, ct);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        => _inner.ReadAsync(buffer, ct);

    public override bool CanRead  => _inner.CanRead;
    public override bool CanWrite => _inner.CanWrite;
    public override bool CanSeek  => false;
    public override long Length   => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
