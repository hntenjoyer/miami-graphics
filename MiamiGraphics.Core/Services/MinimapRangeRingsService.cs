using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MiamiGraphics.Core.Services
{
    public static class MinimapRangeRingsService
    {
        private const string ContainerExport = "HEALTH_ARMOUR_ABILITY_BIG";
        private const string OutlineExport   = "radar_radius_outline_blip";
        private const int RingDepthBase = 1111;

        private static (double sx, double sy) ScaleForMeters(int meters)
        {
            if (meters == 100) return (0.161346435546875, 0.160614013671875);
            if (meters == 125) return (0.191986083984375, 0.1909942626953125);
            double sx = 0.161346435546875 + (meters - 100) * (0.191986083984375 - 0.161346435546875) / 25.0;
            double sy = 0.160614013671875 + (meters - 100) * (0.1909942626953125 - 0.160614013671875) / 25.0;
            return (sx, sy);
        }

        public static bool Apply(string gfxPath, IReadOnlyList<int>? radiiMeters)
        {
            byte[] data;
            try { data = File.ReadAllBytes(gfxPath); }
            catch { return false; }

            if (data.Length < 12 || data[0] != (byte)'G' || data[1] != (byte)'F' || data[2] != (byte)'X')
                return false;

            if (!TryRebuild(data, radiiMeters ?? Array.Empty<int>(), out var outBytes))
                return false;

            var tmp = gfxPath + ".rings.tmp";
            try
            {
                File.WriteAllBytes(tmp, outBytes);
                File.Copy(tmp, gfxPath, overwrite: true);
                return true;
            }
            catch { return false; }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }

        public static bool Detect(string gfxPath)
        {
            try { return Detect(File.ReadAllBytes(gfxPath)); }
            catch { return false; }
        }

        public static bool Detect(byte[] data)
        {
            if (data is null || data.Length < 12
                || data[0] != (byte)'G' || data[1] != (byte)'F' || data[2] != (byte)'X')
                return false;
            try { return HasRings(data); }
            catch { return false; }
        }

        private static bool HasRings(byte[] data)
        {
            int tstart = TagStart(data);
            var top = Walk(data, tstart, data.Length);
            if (top.Count == 0 || top[^1].Code != 0) return false;

            var exp = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var t in top)
            {
                if (t.Code != 56) continue;
                int cnt = U16(data, t.BodyStart), p = t.BodyStart + 2;
                for (int i = 0; i < cnt && p + 2 <= t.BodyEnd; i++)
                {
                    int cid = U16(data, p); p += 2;
                    int s = p;
                    while (p < t.BodyEnd && data[p] != 0) p++;
                    exp[Encoding.UTF8.GetString(data, s, p - s)] = cid;
                    p++;
                }
            }
            if (!exp.TryGetValue(ContainerExport, out int containerId)) return false;
            if (!exp.TryGetValue(OutlineExport, out int outlineId)) return false;

            TopTag? SpriteById(int sid)
            {
                foreach (var t in top)
                    if (t.Code == 39 && U16(data, t.BodyStart) == sid) return t;
                return null;
            }
            var container = SpriteById(containerId);
            var outline = SpriteById(outlineId);
            if (container is null || outline is null) return false;

            int graphicId = -1;
            foreach (var st in Walk(data, outline.Value.BodyStart + 4, outline.Value.BodyEnd))
                if (st.Code == 26 && (data[st.BodyStart] & 0x02) != 0)
                { graphicId = U16(data, st.BodyStart + 3); break; }
            if (graphicId < 0) return false;

            var ringWrappers = new HashSet<int>();
            foreach (var t in top)
            {
                if (t.Code != 39) continue;
                int sid = U16(data, t.BodyStart);
                if (sid == outlineId) continue;
                foreach (var st in Walk(data, t.BodyStart + 4, t.BodyEnd))
                    if (st.Code == 26 && (data[st.BodyStart] & 0x02) != 0
                        && U16(data, st.BodyStart + 3) == graphicId)
                    { ringWrappers.Add(sid); break; }
            }
            if (ringWrappers.Count == 0) return false;

            foreach (var st in Walk(data, container.Value.BodyStart + 4, container.Value.BodyEnd))
                if (st.Code == 26 && (data[st.BodyStart] & 0x02) != 0
                    && ringWrappers.Contains(U16(data, st.BodyStart + 3)))
                    return true;
            return false;
        }

        private readonly struct TopTag
        {
            public readonly int Code, Start, End, BodyStart, BodyEnd;
            public TopTag(int c, int s, int e, int bs, int be) { Code = c; Start = s; End = e; BodyStart = bs; BodyEnd = be; }
        }

        private static int TagStart(byte[] d)
        {
            int nbits = (d[8] >> 3) & 0x1F;
            int rectBytes = (5 + 4 * nbits + 7) / 8;
            return 8 + rectBytes + 2 + 2;
        }

        private static List<TopTag> Walk(byte[] d, int start, int limit)
        {
            var tags = new List<TopTag>();
            int pos = start;
            while (pos + 2 <= limit)
            {
                int rh = d[pos] | (d[pos + 1] << 8);
                int code = rh >> 6;
                int len = rh & 0x3F;
                int hl = 2;
                if (len == 0x3F)
                {
                    len = d[pos + 2] | (d[pos + 3] << 8) | (d[pos + 4] << 16) | (d[pos + 5] << 24);
                    hl = 6;
                }
                int bodyStart = pos + hl;
                int end = bodyStart + len;
                if (end > limit) break;
                tags.Add(new TopTag(code, pos, end, bodyStart, end));
                if (code == 0) break;
                pos = end;
            }
            return tags;
        }

        private static ushort U16(byte[] d, int p) => (ushort)(d[p] | (d[p + 1] << 8));

        private static bool TryRebuild(byte[] data, IReadOnlyList<int> radii, out byte[] result)
        {
            result = Array.Empty<byte>();
            int tstart = TagStart(data);
            var top = Walk(data, tstart, data.Length);
            if (top.Count == 0 || top[^1].Code != 0) return false;

            var exp = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var t in top)
            {
                if (t.Code != 56) continue;
                int cnt = U16(data, t.BodyStart), p = t.BodyStart + 2;
                for (int i = 0; i < cnt && p + 2 <= t.BodyEnd; i++)
                {
                    int cid = U16(data, p); p += 2;
                    int s = p;
                    while (p < t.BodyEnd && data[p] != 0) p++;
                    string nm = Encoding.UTF8.GetString(data, s, p - s);
                    p++;
                    exp[nm] = cid;
                }
            }
            if (!exp.TryGetValue(ContainerExport, out int containerId)) return false;
            if (!exp.TryGetValue(OutlineExport, out int outlineId)) return false;

            TopTag? SpriteById(int sid)
            {
                foreach (var t in top)
                    if (t.Code == 39 && U16(data, t.BodyStart) == sid) return t;
                return null;
            }
            var container = SpriteById(containerId);
            var outline = SpriteById(outlineId);
            if (container is null || outline is null) return false;

            int graphicId = -1;
            foreach (var st in Walk(data, outline.Value.BodyStart + 4, outline.Value.BodyEnd))
            {
                if (st.Code == 26 && (data[st.BodyStart] & 0x02) != 0)
                { graphicId = U16(data, st.BodyStart + 3); break; }
            }
            if (graphicId < 0) return false;

            var ringWrappers = new HashSet<int>();
            foreach (var t in top)
            {
                if (t.Code != 39) continue;
                int sid = U16(data, t.BodyStart);
                if (sid == outlineId) continue;
                foreach (var st in Walk(data, t.BodyStart + 4, t.BodyEnd))
                {
                    if (st.Code == 26 && (data[st.BodyStart] & 0x02) != 0
                        && U16(data, st.BodyStart + 3) == graphicId)
                    { ringWrappers.Add(sid); break; }
                }
            }

            int maxId = 0;
            foreach (var t in top)
            {
                if (t.Code == 39 || IsDefineWithId(t.Code))
                {
                    int id = U16(data, t.BodyStart);
                    if (id < 65535 && id > maxId) maxId = id;
                }
            }
            int newSpriteId = maxId + 1;

            var outBuf = new List<byte>(data.Length + 256);
            outBuf.AddRange(new ArraySegment<byte>(data, 0, tstart));

            for (int ti = 0; ti < top.Count; ti++)
            {
                var t = top[ti];
                if (t.Code == 39 && U16(data, t.BodyStart) == containerId)
                {
                    var body = RebuildContainerBody(data, t, ringWrappers, radii, newSpriteId);
                    outBuf.AddRange(Tag(39, body));
                }
                else if (t.Code == 39 && ringWrappers.Contains(U16(data, t.BodyStart)))
                {
                }
                else if (t.Code == 0)
                {
                    if (radii.Count > 0)
                        outBuf.AddRange(WrapperSprite(newSpriteId, graphicId));
                    outBuf.AddRange(new ArraySegment<byte>(data, t.Start, t.End - t.Start));
                }
                else
                {
                    outBuf.AddRange(new ArraySegment<byte>(data, t.Start, t.End - t.Start));
                }
            }

            var arr = outBuf.ToArray();
            arr[4] = (byte)(arr.Length & 0xFF);
            arr[5] = (byte)((arr.Length >> 8) & 0xFF);
            arr[6] = (byte)((arr.Length >> 16) & 0xFF);
            arr[7] = (byte)((arr.Length >> 24) & 0xFF);
            result = arr;
            return true;
        }

        private static bool IsDefineWithId(int code) => code switch
        {
            2 or 6 or 10 or 13 or 20 or 21 or 22 or 32 or 35 or 36 or 48 or 60 or 75 or 83 or 84 or 90 => true,
            _ => false,
        };

        private static byte[] RebuildContainerBody(byte[] data, TopTag container, HashSet<int> ringWrappers,
            IReadOnlyList<int> radii, int newSpriteId)
        {
            var sub = Walk(data, container.BodyStart + 4, container.BodyEnd);
            var body = new List<byte>();
            body.AddRange(new ArraySegment<byte>(data, container.BodyStart, 4));

            bool inserted = false;
            foreach (var st in sub)
            {
                if (st.Code == 26 && (data[st.BodyStart] & 0x02) != 0
                    && ringWrappers.Contains(U16(data, st.BodyStart + 3)))
                    continue;

                if (!inserted && (st.Code == 1 || st.Code == 0))
                {
                    for (int i = 0; i < radii.Count; i++)
                    {
                        var (sx, sy) = ScaleForMeters(radii[i]);
                        body.AddRange(RingPlacement(RingDepthBase + i, newSpriteId, sx, sy));
                    }
                    inserted = true;
                }
                body.AddRange(new ArraySegment<byte>(data, st.Start, st.End - st.Start));
            }
            return body.ToArray();
        }

        private static byte[] WrapperSprite(int spriteId, int graphicId)
        {
            var inner = PlaceObject2(1, graphicId, IdentityMatrix(), null);
            var body = new List<byte>();
            body.Add((byte)(spriteId & 0xFF)); body.Add((byte)(spriteId >> 8));
            body.Add(0); body.Add(0);
            body.AddRange(inner);
            return Tag(39, body.ToArray());
        }

        private static byte[] RingPlacement(int depth, int spriteId, double sx, double sy)
            => PlaceObject2(depth, spriteId, ScaleMatrix(sx, sy, 1908, 1357), IdentityCxform());

        private static byte[] Tag(int code, byte[] body)
        {
            int n = body.Length;
            if (n < 0x3F)
            {
                var r = new byte[2 + n];
                int rh = (code << 6) | n;
                r[0] = (byte)(rh & 0xFF); r[1] = (byte)(rh >> 8);
                Buffer.BlockCopy(body, 0, r, 2, n);
                return r;
            }
            else
            {
                var r = new byte[6 + n];
                int rh = (code << 6) | 0x3F;
                r[0] = (byte)(rh & 0xFF); r[1] = (byte)(rh >> 8);
                r[2] = (byte)(n & 0xFF); r[3] = (byte)((n >> 8) & 0xFF);
                r[4] = (byte)((n >> 16) & 0xFF); r[5] = (byte)((n >> 24) & 0xFF);
                Buffer.BlockCopy(body, 0, r, 6, n);
                return r;
            }
        }

        private static byte[] PlaceObject2(int depth, int charId, byte[] matrix, byte[]? cxform)
        {
            int flags = (1 << 1) | (1 << 2) | (cxform != null ? (1 << 3) : 0);
            var b = new List<byte>();
            b.Add((byte)flags);
            b.Add((byte)(depth & 0xFF)); b.Add((byte)(depth >> 8));
            b.Add((byte)(charId & 0xFF)); b.Add((byte)(charId >> 8));
            b.AddRange(matrix);
            if (cxform != null) b.AddRange(cxform);
            return Tag(26, b.ToArray());
        }

        private static byte[] IdentityMatrix()
        {
            var w = new BitWriter();
            w.UB(0, 1);
            w.UB(0, 1);
            w.UB(0, 5);
            return w.ToBytes();
        }

        private static byte[] ScaleMatrix(double sx, double sy, int txTwips, int tyTwips)
        {
            var w = new BitWriter();
            const int nScale = 15;
            w.UB(1, 1); w.UB(nScale, 5);
            w.SB(Fixed16(sx), nScale); w.SB(Fixed16(sy), nScale);
            w.UB(1, 1); w.UB(0, 5);
            const int nTr = 12;
            w.UB(nTr, 5); w.SB(txTwips, nTr); w.SB(tyTwips, nTr);
            return w.ToBytes();
        }

        private static byte[] IdentityCxform()
        {
            var w = new BitWriter();
            w.UB(0, 1);
            w.UB(1, 1);
            w.UB(10, 4);
            w.SB(256, 10); w.SB(256, 10); w.SB(256, 10); w.SB(256, 10);
            return w.ToBytes();
        }

        private static int Fixed16(double v) => (int)Math.Round(v * 65536.0, MidpointRounding.AwayFromZero);

        private sealed class BitWriter
        {
            private readonly List<int> _bits = new();
            public void UB(int value, int n) { for (int i = n - 1; i >= 0; i--) _bits.Add((value >> i) & 1); }
            public void SB(int value, int n) { if (value < 0) value = (1 << n) + value; UB(value, n); }
            public byte[] ToBytes()
            {
                while (_bits.Count % 8 != 0) _bits.Add(0);
                var r = new byte[_bits.Count / 8];
                for (int i = 0; i < _bits.Count; i += 8)
                {
                    int x = 0;
                    for (int j = 0; j < 8; j++) x = (x << 1) | _bits[i + j];
                    r[i / 8] = (byte)x;
                }
                return r;
            }
        }
    }
}
