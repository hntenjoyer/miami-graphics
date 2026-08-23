using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MiamiGraphics.Shell.Services;

public static class RangeZipFetcher
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    private const uint EOCD_SIG  = 0x06054b50;
    private const uint CEN_SIG   = 0x02014b50;
    private const uint LOC_SIG   = 0x04034b50;
    private const int  MaxTail   = 256 * 1024;

    public static async Task<Dictionary<string, byte[]>?> TryFetchAsync(
        string url, IEnumerable<string> wantedFileNames,
        IProgress<(long received, long total)>? progress, CancellationToken ct)
    {
        try
        {
            var wanted = new HashSet<string>(
                wantedFileNames.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
            if (wanted.Count == 0) return null;

            long total = await GetLengthAsync(url, ct);
            if (total <= 0) return null;

            int tailLen = (int)Math.Min(total, MaxTail);
            var tail = await RangeAsync(url, total - tailLen, total - 1, ct);
            if (tail == null || tail.Length < 22) return null;
            long tailStart = total - tail.Length;

            int eocd = -1;
            for (int i = tail.Length - 22; i >= 0; i--)
                if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == EOCD_SIG) { eocd = i; break; }
            if (eocd < 0) return null;

            uint cdSize   = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 12));
            uint cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(eocd + 16));
            if (cdOffset == 0xFFFFFFFF || cdSize == 0xFFFFFFFF) return null;

            byte[] cd;
            if (cdOffset >= tailStart)
            {
                int off = (int)(cdOffset - tailStart);
                if (off + cdSize > tail.Length) return null;
                cd = tail.AsSpan(off, (int)cdSize).ToArray();
            }
            else
            {
                cd = await RangeAsync(url, cdOffset, cdOffset + cdSize - 1, ct) ?? Array.Empty<byte>();
                if (cd.Length < cdSize) return null;
            }

            var entries = new List<(string name, ushort method, uint compSize, uint localOffset)>();
            int p = 0;
            while (p + 46 <= cd.Length)
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(p)) != CEN_SIG) break;
                ushort method   = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(p + 10));
                uint compSize   = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(p + 20));
                ushort nameLen  = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(p + 28));
                ushort extraLen = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(p + 30));
                ushort cmtLen   = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(p + 32));
                uint localOff   = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(p + 42));
                string name = System.Text.Encoding.UTF8.GetString(cd, p + 46, nameLen);
                var baseName = Path.GetFileName(name);
                if (!string.IsNullOrEmpty(baseName) && wanted.Contains(baseName)
                    && compSize != 0xFFFFFFFF && localOff != 0xFFFFFFFF)
                    entries.Add((baseName, method, compSize, localOff));
                p += 46 + nameLen + extraLen + cmtLen;
            }
            if (entries.Count == 0) return null;

            long grandTotal = entries.Sum(e => (long)e.compSize) + 1;
            long received = 0;
            progress?.Report((0, grandTotal));

            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                if (e.method != 0 && e.method != 8) return null;

                long blockEnd = e.localOffset + 30 + 512 + e.compSize;
                var block = await RangeAsync(url, e.localOffset, Math.Min(blockEnd, total - 1), ct);
                if (block == null || block.Length < 30
                    || BinaryPrimitives.ReadUInt32LittleEndian(block) != LOC_SIG) return null;

                ushort lNameLen  = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(26));
                ushort lExtraLen = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(28));
                int dataStart = 30 + lNameLen + lExtraLen;

                byte[] comp;
                if (dataStart + e.compSize <= block.Length)
                {
                    comp = block.AsSpan(dataStart, (int)e.compSize).ToArray();
                }
                else
                {
                    long dataOff = e.localOffset + dataStart;
                    comp = await RangeAsync(url, dataOff, dataOff + e.compSize - 1, ct) ?? Array.Empty<byte>();
                    if (comp.Length < e.compSize) return null;
                }

                byte[] data;
                if (e.method == 0)
                {
                    data = comp;
                }
                else
                {
                    using var ms = new MemoryStream(comp);
                    using var ds = new DeflateStream(ms, CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    await ds.CopyToAsync(outMs, ct);
                    data = outMs.ToArray();
                }
                result[e.name] = data;
                received += e.compSize;
                progress?.Report((received, grandTotal));
            }

            return result;
        }
        catch { return null; }
    }

    private static async Task<long> GetLengthAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode != HttpStatusCode.PartialContent) return -1;
        return resp.Content.Headers.ContentRange?.Length ?? -1;
    }

    private static async Task<byte[]?> RangeAsync(string url, long from, long to, CancellationToken ct)
    {
        if (to < from) return Array.Empty<byte>();
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(from, to);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode != HttpStatusCode.PartialContent && resp.StatusCode != HttpStatusCode.OK)
            return null;
        return await resp.Content.ReadAsByteArrayAsync(ct);
    }
}
