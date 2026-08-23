using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.Services
{
    public static class MinimapHitBitmapService
    {
        private const double RadX = 6, RadY = 8, RadW = 179, RadH = 114;
        private const double RadCx = RadX + RadW / 2;
        private const double RadCy = RadY + RadH / 2;

        public static byte[]? ReplaceHitArt(
            byte[] gfxBytes,
            IReadOnlyCollection<int> hitSpriteIds,
            int newBitmapId,
            int newShapeId,
            int width,
            int height,
            byte[] rgba,
            out string? error,
            double scalePct = 100,
            double? centerX = null,
            double? centerY = null)
        {
            error = null;
            if (gfxBytes is null || gfxBytes.Length < 32) { error = Loc.T("error.gfxEmpty"); return null; }
            if (!(gfxBytes[0] == 'G' && gfxBytes[1] == 'F' && gfxBytes[2] == 'X'))
            { error = Loc.T("error.gfxExpectedUncompressed"); return null; }
            if (hitSpriteIds is null || hitSpriteIds.Count == 0) { error = Loc.T("error.healthHitMcNotFound"); return null; }
            if (width <= 0 || height <= 0 || rgba is null || rgba.Length != width * height * 4)
            { error = Loc.T("error.imagePixelsInvalid"); return null; }
            if (width > 1024 || height > 1024) { error = Loc.T("error.imageTooBig1024"); return null; }

            var top = MgSwf.WalkTop(gfxBytes, out int headerLen);
            if (top.Count == 0) { error = Loc.T("error.tagStreamEmpty"); return null; }

            var shapeBounds = MgSwf.IndexShapeBounds(gfxBytes, top);
            var spriteTags = MgSwf.IndexSprites(gfxBytes, top);

            var targets = top.Where(t => t.Code == 39 && t.BodyEnd - t.BodyStart >= 4
                                      && hitSpriteIds.Contains(MgSwf.U16(gfxBytes, t.BodyStart)))
                             .OrderBy(t => t.Start).ToList();
            if (targets.Count == 0) { error = Loc.T("error.hitDefineSpriteNotFound"); return null; }

            double k100 = Math.Clamp(scalePct, 10, 400) / 100.0;
            double boxW = RadW * k100, boxH = RadH * k100;
            double fit = Math.Min(boxW / width, boxH / height);
            double drawW = width * fit, drawH = height * fit;
            double dcx = centerX ?? RadCx, dcy = centerY ?? RadCy;
            double dx0 = dcx - drawW / 2, dy0 = dcy - drawH / 2;

            double vx0 = Math.Max(dx0, RadX), vy0 = Math.Max(dy0, RadY);
            double vx1 = Math.Min(dx0 + drawW, RadX + RadW), vy1 = Math.Min(dy0 + drawH, RadY + RadH);
            if (vx1 - vx0 < 0.5 || vy1 - vy0 < 0.5)
            { error = Loc.T("error.hitImageOffMap"); return null; }

            int px0 = Clamp((int)Math.Round((vx0 - dx0) / drawW * width), 0, width - 1);
            int px1 = Clamp((int)Math.Round((vx1 - dx0) / drawW * width), px0 + 1, width);
            int py0 = Clamp((int)Math.Round((vy0 - dy0) / drawH * height), 0, height - 1);
            int py1 = Clamp((int)Math.Round((vy1 - dy0) / drawH * height), py0 + 1, height);
            int cw = px1 - px0, ch = py1 - py0;

            byte[] cropped;
            if (cw == width && ch == height) cropped = rgba;
            else
            {
                cropped = new byte[cw * ch * 4];
                for (int y = 0; y < ch; y++)
                    Array.Copy(rgba, ((py0 + y) * width + px0) * 4, cropped, y * cw * 4, cw * 4);
            }

            var scales = IndexHitPlacementScales(gfxBytes, top, hitSpriteIds);
            var rects = new List<(int X0, int Y0, int X1, int Y1)>();
            var memo = new Dictionary<int, (int, int, int, int)?>();
            foreach (var t in targets)
            {
                var r = MgSwf.SpriteContentRect(gfxBytes, t, shapeBounds, spriteTags, memo, 0)
                    ?? (0, 0, 1280, 1280);
                var (kx, ky) = scales.TryGetValue(t.SpriteId(gfxBytes), out var kk) ? kk : (2.8, 1.8);
                double rcx = (r.Item1 + r.Item3) / 2.0, rcy = (r.Item2 + r.Item4) / 2.0;
                double tx = 20.0 / kx, ty = 20.0 / ky;
                rects.Add(((int)Math.Round(rcx + (vx0 - RadCx) * tx), (int)Math.Round(rcy + (vy0 - RadCy) * ty),
                           (int)Math.Round(rcx + (vx1 - RadCx) * tx), (int)Math.Round(rcy + (vy1 - RadCy) * ty)));
            }

            width = cw; height = ch;
            byte[] bitmapTag = BuildDefineBitsLossless2(newBitmapId, width, height, cropped);

            using var ms = new MemoryStream();
            int cursor = 0;
            bool defsInserted = false;
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                int shapeId = newShapeId + i;
                ms.Write(gfxBytes, cursor, t.Start - cursor);
                if (!defsInserted)
                {
                    ms.Write(bitmapTag, 0, bitmapTag.Length);
                    defsInserted = true;
                }
                var shapeTag = BuildBitmapRectShape(shapeId, newBitmapId, rects[i], width, height);
                ms.Write(shapeTag, 0, shapeTag.Length);
                var body = BuildHitSprite(t.SpriteId(gfxBytes), shapeId);
                ms.Write(body, 0, body.Length);
                cursor = t.End;
            }
            ms.Write(gfxBytes, cursor, gfxBytes.Length - cursor);

            var result = ms.ToArray();
            BitConverter.GetBytes((uint)result.Length).CopyTo(result, 4);
            return result;
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;

        private static Dictionary<int, (double Kx, double Ky)> IndexHitPlacementScales(
            byte[] d, List<MgSwf.Tag> top, IReadOnlyCollection<int> hitSpriteIds)
        {
            var map = new Dictionary<int, (double, double)>();
            void Scan(IEnumerable<MgSwf.Tag> tags)
            {
                foreach (var st in tags)
                {
                    if (st.Code != 26) continue;
                    var po = MgSwf.ParsePlaceObject2(d, st);
                    if (po is null) continue;
                    var p = po.Value;
                    if (p.Name != "healthHitMC" || p.CharId <= 0 || !hitSpriteIds.Contains(p.CharId)) continue;
                    if (Math.Abs(p.Matrix.R0) > 0.01 || Math.Abs(p.Matrix.R1) > 0.01) continue;
                    double kx = Math.Abs(p.Matrix.A), ky = Math.Abs(p.Matrix.D);
                    if (kx > 0.01 && ky > 0.01 && !map.ContainsKey(p.CharId)) map[p.CharId] = (kx, ky);
                }
            }
            Scan(top);
            foreach (var t in top)
                if (t.Code == 39 && t.BodyEnd - t.BodyStart >= 4)
                    Scan(MgSwf.Walk(d, t.BodyStart + 4, t.BodyEnd));
            return map;
        }

        internal static byte[] BuildDefineBitsLossless2(int id, int w, int h, byte[] rgba)
        {
            var argb = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++)
            {
                byte r = rgba[i * 4 + 0], g = rgba[i * 4 + 1], b = rgba[i * 4 + 2], a = rgba[i * 4 + 3];
                argb[i * 4 + 0] = a;
                argb[i * 4 + 1] = (byte)(r * a / 255);
                argb[i * 4 + 2] = (byte)(g * a / 255);
                argb[i * 4 + 3] = (byte)(b * a / 255);
            }
            using var zms = new MemoryStream();
            using (var z = new ZLibStream(zms, CompressionLevel.Optimal, leaveOpen: true))
                z.Write(argb, 0, argb.Length);
            var packed = zms.ToArray();

            using var data = new MemoryStream();
            data.Write(BitConverter.GetBytes((ushort)id));
            data.WriteByte(5);
            data.Write(BitConverter.GetBytes((ushort)w));
            data.Write(BitConverter.GetBytes((ushort)h));
            data.Write(packed, 0, packed.Length);
            var payload = data.ToArray();

            using var tag = new MemoryStream();
            tag.Write(MgSwf.TagHeader(36, payload.Length));
            tag.Write(payload, 0, payload.Length);
            return tag.ToArray();
        }

        internal static byte[] BuildBitmapRectShape(int shapeId, int bitmapId,
            (int X0, int Y0, int X1, int Y1) rect, int w, int h)
        {
            int x0 = rect.X0, y0 = rect.Y0, x1 = rect.X1, y1 = rect.Y1;
            if (x1 <= x0) x1 = x0 + 20;
            if (y1 <= y0) y1 = y0 + 20;
            int rw = x1 - x0, rh = y1 - y0;
            var bw = new MgSwf.BitWriter();

            int rb = new[] { x0, x1, y0, y1 }.Select(MgSwf.SignedBits).Max();
            bw.WriteBits(rb, 5);
            bw.WriteBits(x0, rb); bw.WriteBits(x1, rb);
            bw.WriteBits(y0, rb); bw.WriteBits(y1, rb);
            bw.Align();

            bw.WriteByte(1);
            bw.WriteByte(0x41);
            bw.WriteUInt16((ushort)bitmapId);
            int sx = (int)Math.Round(rw * 65536.0 / w, MidpointRounding.AwayFromZero);
            int sy = (int)Math.Round(rh * 65536.0 / h, MidpointRounding.AwayFromZero);
            bw.WriteBits(1, 1);
            int sb = Math.Max(MgSwf.SignedBits(sx), MgSwf.SignedBits(sy));
            bw.WriteBits(sb, 5);
            bw.WriteBits(sx, sb);
            bw.WriteBits(sy, sb);
            bw.WriteBits(0, 1);
            int tb = Math.Max(MgSwf.SignedBits(x0), MgSwf.SignedBits(y0));
            bw.WriteBits(tb, 5);
            bw.WriteBits(x0, tb);
            bw.WriteBits(y0, tb);
            bw.Align();

            bw.WriteByte(0);

            bw.WriteBits(1, 4);
            bw.WriteBits(0, 4);
            bw.WriteBits(0, 1);
            bw.WriteBits(0b00101, 5);
            int mb = Math.Max(MgSwf.SignedBits(x0), MgSwf.SignedBits(y0));
            bw.WriteBits(mb, 5);
            bw.WriteBits(x0, mb);
            bw.WriteBits(y0, mb);
            bw.WriteBits(1, 1);
            WriteStraightEdge(bw, rw, 0);
            WriteStraightEdge(bw, 0, rh);
            WriteStraightEdge(bw, -rw, 0);
            WriteStraightEdge(bw, 0, -rh);
            bw.WriteBits(0, 6);
            bw.Align();

            var payload = new MemoryStream();
            payload.Write(BitConverter.GetBytes((ushort)shapeId));
            var body = bw.ToArray();
            payload.Write(body, 0, body.Length);
            var pb = payload.ToArray();

            using var tag = new MemoryStream();
            tag.Write(MgSwf.TagHeader(2, pb.Length));
            tag.Write(pb, 0, pb.Length);
            return tag.ToArray();
        }

        private static void WriteStraightEdge(MgSwf.BitWriter bw, int dx, int dy)
        {
            bw.WriteBits(1, 1);
            bw.WriteBits(1, 1);
            int nb = Math.Max(MgSwf.SignedBits(dx), Math.Max(MgSwf.SignedBits(dy), 2));
            bw.WriteBits(nb - 2, 4);
            bool general = dx != 0 && dy != 0;
            bw.WriteBits(general ? 1 : 0, 1);
            if (general) { bw.WriteBits(dx, nb); bw.WriteBits(dy, nb); }
            else
            {
                bw.WriteBits(dx == 0 ? 1 : 0, 1);
                bw.WriteBits(dx == 0 ? dy : dx, nb);
            }
        }

        internal static byte[] BuildHitSprite(int spriteId, int shapeId)
        {
            using var po = new MemoryStream();
            po.WriteByte(0x06);
            po.Write(BitConverter.GetBytes((ushort)1));
            po.Write(BitConverter.GetBytes((ushort)shapeId));
            po.WriteByte(0x00);
            var poB = po.ToArray();

            using var body = new MemoryStream();
            body.Write(BitConverter.GetBytes((ushort)spriteId));
            body.Write(BitConverter.GetBytes((ushort)1));
            body.Write(MgSwf.TagHeader(26, poB.Length));
            body.Write(poB, 0, poB.Length);
            body.Write(new byte[] { 0x40, 0x00 });
            body.Write(new byte[] { 0x00, 0x00 });
            var bb = body.ToArray();

            using var tag = new MemoryStream();
            tag.Write(MgSwf.TagHeader(39, bb.Length));
            tag.Write(bb, 0, bb.Length);
            return tag.ToArray();
        }
    }

    public static class MinimapBarShadowService
    {
        public const string ShadowName = "mgShadowMC";

        public static byte[]? WrapShadow(byte[] gfxBytes, out string? error, out int wrapped)
        {
            error = null;
            wrapped = 0;
            if (gfxBytes is null || gfxBytes.Length < 32) { error = Loc.T("error.gfxEmpty"); return null; }
            if (!(gfxBytes[0] == 'G' && gfxBytes[1] == 'F' && gfxBytes[2] == 'X'))
            { error = Loc.T("error.gfxExpectedUncompressed"); return null; }

            var top = MgSwf.WalkTop(gfxBytes, out _);
            if (top.Count == 0) { error = Loc.T("error.tagStreamEmpty"); return null; }
            var shapeBounds = MgSwf.IndexShapeBounds(gfxBytes, top);

            int maxId = 0;
            foreach (var t in top)
            {
                if (t.Code == 39 || MgSwf.IsDefineWithId(t.Code))
                {
                    int id = MgSwf.U16(gfxBytes, t.BodyStart);
                    if (id < 65535 && id > maxId) maxId = id;
                }
            }

            var plans = new List<(MgSwf.Tag Container, MgSwf.Tag Placement, int ShapeId, int WrapperId)>();
            foreach (var t in top)
            {
                if (t.Code != 39) continue;
                var subs = MgSwf.Walk(gfxBytes, t.BodyStart + 4, t.BodyEnd);
                bool alreadyWrapped = false;
                double? barTy = null;
                var candidates = new List<(MgSwf.Tag Sub, int ShapeId, (int X0, int Y0, int X1, int Y1) R)>();
                foreach (var st in subs)
                {
                    if (st.Code != 26) continue;
                    var po = MgSwf.ParsePlaceObject2(gfxBytes, st);
                    if (po is null) continue;
                    var p = po.Value;
                    if (p.Name == "bar_bg") barTy = p.Matrix.Ty;
                    if (p.Name == ShadowName) alreadyWrapped = true;
                    if (p.Name is null && p.CharId > 0 && (p.Flags & 0xC0) == 0
                        && shapeBounds.TryGetValue(p.CharId, out var b))
                        candidates.Add((st, p.CharId, MgSwf.TransformRect(b, p.Matrix)));
                }
                if (barTy is null || alreadyWrapped || candidates.Count == 0) continue;

                var best = candidates
                    .Where(x =>
                    {
                        double w = x.R.X1 - x.R.X0, h = x.R.Y1 - x.R.Y0;
                        double yc = (x.R.Y0 + x.R.Y1) / 2.0;
                        return h > 0 && h <= 400 && w >= 1000 && w >= 4 * h
                               && Math.Abs(yc - barTy.Value) <= 400;
                    })
                    .OrderByDescending(x => (x.R.X1 - x.R.X0) * (long)(x.R.Y1 - x.R.Y0))
                    .Cast<(MgSwf.Tag Sub, int ShapeId, (int X0, int Y0, int X1, int Y1) R)?>()
                    .FirstOrDefault();
                if (best is null) continue;

                plans.Add((t, best.Value.Sub, best.Value.ShapeId, maxId + 1 + plans.Count));
            }

            if (plans.Count == 0) return gfxBytes;

            using var ms = new MemoryStream();
            int cursor = 0;
            foreach (var plan in plans.OrderBy(p => p.Container.Start))
            {
                ms.Write(gfxBytes, cursor, plan.Container.Start - cursor);
                var wrapper = BuildWrapperSprite(plan.WrapperId, plan.ShapeId);
                ms.Write(wrapper, 0, wrapper.Length);
                var newBody = RebuildContainerBody(gfxBytes, plan.Container, plan.Placement, plan.WrapperId);
                var tag = MgSwf.TagHeader(39, newBody.Length);
                ms.Write(tag, 0, tag.Length);
                ms.Write(newBody, 0, newBody.Length);
                cursor = plan.Container.End;
            }
            ms.Write(gfxBytes, cursor, gfxBytes.Length - cursor);

            var result = ms.ToArray();
            BitConverter.GetBytes((uint)result.Length).CopyTo(result, 4);
            wrapped = plans.Count;
            return result;
        }

        private static byte[] BuildWrapperSprite(int spriteId, int shapeId)
        {
            using var po = new MemoryStream();
            po.WriteByte(0x06);
            po.Write(BitConverter.GetBytes((ushort)1));
            po.Write(BitConverter.GetBytes((ushort)shapeId));
            po.WriteByte(0x00);
            var poB = po.ToArray();

            using var body = new MemoryStream();
            body.Write(BitConverter.GetBytes((ushort)spriteId));
            body.Write(BitConverter.GetBytes((ushort)0));
            body.Write(MgSwf.TagHeader(26, poB.Length));
            body.Write(poB, 0, poB.Length);
            var bb = body.ToArray();

            using var tag = new MemoryStream();
            tag.Write(MgSwf.TagHeader(39, bb.Length));
            tag.Write(bb, 0, bb.Length);
            return tag.ToArray();
        }

        private static byte[] RebuildContainerBody(byte[] d, MgSwf.Tag container, MgSwf.Tag placement, int wrapperId)
        {
            using var body = new MemoryStream();
            body.Write(d, container.BodyStart, 4);
            var subs = MgSwf.Walk(d, container.BodyStart + 4, container.BodyEnd);
            foreach (var st in subs)
            {
                if (st.Start != placement.Start)
                {
                    body.Write(d, st.Start, st.End - st.Start);
                    continue;
                }
                int flags = d[st.BodyStart];
                using var nb = new MemoryStream();
                nb.WriteByte((byte)(flags | 0x20));
                nb.Write(d, st.BodyStart + 1, 2);
                nb.Write(BitConverter.GetBytes((ushort)wrapperId));
                int rest = st.BodyStart + 5;
                nb.Write(d, rest, st.BodyEnd - rest);
                var nameBytes = global::System.Text.Encoding.ASCII.GetBytes(ShadowName);
                nb.Write(nameBytes, 0, nameBytes.Length);
                nb.WriteByte(0);
                var nbB = nb.ToArray();
                var hdr = MgSwf.TagHeader(26, nbB.Length);
                body.Write(hdr, 0, hdr.Length);
                body.Write(nbB, 0, nbB.Length);
            }
            return body.ToArray();
        }
    }

    public static class MinimapNorthBlipService
    {
        public static byte[]? HideNorth(byte[] gfxBytes, out string? error, out bool found)
        {
            error = null; found = false;
            try
            {
                var d = (byte[])gfxBytes.Clone();
                var top = MgSwf.WalkTop(d, out _);

                int northId = -1;
                foreach (var t in top)
                {
                    if (t.Code != 56) continue;
                    int p = t.BodyStart;
                    int cnt = MgSwf.U16(d, p); p += 2;
                    for (int i = 0; i < cnt && p < t.BodyEnd; i++)
                    {
                        int id = MgSwf.U16(d, p); p += 2;
                        int s0 = p;
                        while (p < t.BodyEnd && d[p] != 0) p++;
                        var nm = global::System.Text.Encoding.ASCII.GetString(d, s0, p - s0);
                        p++;
                        if (nm.Equals("radar_north", StringComparison.OrdinalIgnoreCase)) { northId = id; break; }
                    }
                    if (northId >= 0) break;
                }
                if (northId < 0) { error = Loc.T("error.radarNorthExportNotFound"); return d; }

                foreach (var t in top)
                {
                    if (t.Code != 39) continue;
                    if (t.SpriteId(d) != northId) continue;
                    int frameCount = MgSwf.U16(d, t.BodyStart + 2);
                    var body = new List<byte>
                    {
                        d[t.BodyStart], d[t.BodyStart + 1],
                        d[t.BodyStart + 2], d[t.BodyStart + 3],
                    };
                    for (int i = 0; i < Math.Max(1, frameCount); i++) { body.Add(1 << 6); body.Add(0); }
                    body.Add(0); body.Add(0);
                    var outB = new List<byte>(d.Length);
                    outB.AddRange(new ArraySegment<byte>(d, 0, t.Start));
                    outB.AddRange(MgSwf.TagHeader(39, body.Count));
                    outB.AddRange(body);
                    outB.AddRange(new ArraySegment<byte>(d, t.End, d.Length - t.End));
                    var res = outB.ToArray();
                    BitConverter.GetBytes(res.Length).CopyTo(res, 4);
                    found = true;
                    return res;
                }
                error = Loc.T("error.radarNorthSpriteNotFound");
                return d;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }
    }

    public static class MinimapBlipArtService
    {
        public static readonly string[] PlayerArrowExports = { "radar_centre_stroke", "radar_centre" };
        public static readonly string[] GpsBlipExports = { "radar_waypoint_stroke", "radar_waypoint" };

        public static byte[]? ReplaceBlipArt(
            byte[] gfxBytes,
            IReadOnlyList<string> exportNames,
            int width,
            int height,
            byte[] rgba,
            out string? error,
            out int replaced)
        {
            error = null;
            replaced = 0;
            if (gfxBytes is null || gfxBytes.Length < 32) { error = Loc.T("error.gfxEmpty"); return null; }
            if (!(gfxBytes[0] == 'G' && gfxBytes[1] == 'F' && gfxBytes[2] == 'X'))
            { error = Loc.T("error.gfxExpectedUncompressed"); return null; }
            if (width <= 0 || height <= 0 || rgba is null || rgba.Length != width * height * 4)
            { error = Loc.T("error.imagePixelsInvalid"); return null; }
            if (width > 1024 || height > 1024) { error = Loc.T("error.imageTooBig1024Lower"); return gfxBytes; }

            try
            {
                var top = MgSwf.WalkTop(gfxBytes, out _);
                if (top.Count == 0) { error = Loc.T("error.tagStreamEmpty"); return null; }

                var wantedIds = new HashSet<int>();
                foreach (var t in top)
                {
                    if (t.Code != 56) continue;
                    int p = t.BodyStart;
                    int cnt = MgSwf.U16(gfxBytes, p); p += 2;
                    for (int i = 0; i < cnt && p < t.BodyEnd; i++)
                    {
                        int id = MgSwf.U16(gfxBytes, p); p += 2;
                        int s0 = p;
                        while (p < t.BodyEnd && gfxBytes[p] != 0) p++;
                        var nm = global::System.Text.Encoding.ASCII.GetString(gfxBytes, s0, p - s0);
                        p++;
                        foreach (var want in exportNames)
                            if (nm.Equals(want, StringComparison.OrdinalIgnoreCase)) { wantedIds.Add(id); break; }
                    }
                }
                if (wantedIds.Count == 0)
                { error = Loc.T("error.linkageNotFound", ("names", string.Join("/", exportNames))); return gfxBytes; }

                var targets = top.Where(t => t.Code == 39 && t.BodyEnd - t.BodyStart >= 4
                                          && wantedIds.Contains(t.SpriteId(gfxBytes)))
                                 .OrderBy(t => t.Start).ToList();
                if (targets.Count == 0)
                { error = Loc.T("error.linkageWithoutSprite"); return gfxBytes; }

                int maxId = 0;
                foreach (var t in top)
                    if (t.Code == 39 || MgSwf.IsDefineWithId(t.Code))
                    {
                        int id = MgSwf.U16(gfxBytes, t.BodyStart);
                        if (id < 65535 && id > maxId) maxId = id;
                    }

                var shapeBounds = MgSwf.IndexShapeBounds(gfxBytes, top);
                var spriteTags = MgSwf.IndexSprites(gfxBytes, top);
                var memo = new Dictionary<int, (int, int, int, int)?>();

                int newBitmapId = maxId + 1;
                byte[] bitmapTag = MinimapHitBitmapService.BuildDefineBitsLossless2(newBitmapId, width, height, rgba);

                using var ms = new MemoryStream();
                int cursor = 0;
                bool bitmapInserted = false;
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    var r = MgSwf.SpriteContentRect(gfxBytes, t, shapeBounds, spriteTags, memo, 0)
                        ?? (-160, -160, 160, 160);
                    double cx = (r.Item1 + r.Item3) / 2.0, cy = (r.Item2 + r.Item4) / 2.0;
                    double rw = Math.Max(20, r.Item3 - r.Item1), rh = Math.Max(20, r.Item4 - r.Item2);
                    double s = Math.Min(rw / width, rh / height);
                    double hw = width * s / 2.0, hh = height * s / 2.0;
                    var rect = ((int)Math.Round(cx - hw), (int)Math.Round(cy - hh),
                                (int)Math.Round(cx + hw), (int)Math.Round(cy + hh));

                    ms.Write(gfxBytes, cursor, t.Start - cursor);
                    if (!bitmapInserted) { ms.Write(bitmapTag, 0, bitmapTag.Length); bitmapInserted = true; }
                    int shapeId = newBitmapId + 1 + i;
                    var shapeTag = MinimapHitBitmapService.BuildBitmapRectShape(shapeId, newBitmapId, rect, width, height);
                    ms.Write(shapeTag, 0, shapeTag.Length);
                    var body = MinimapHitBitmapService.BuildHitSprite(t.SpriteId(gfxBytes), shapeId);
                    ms.Write(body, 0, body.Length);
                    cursor = t.End;
                }
                ms.Write(gfxBytes, cursor, gfxBytes.Length - cursor);

                var result = ms.ToArray();
                BitConverter.GetBytes((uint)result.Length).CopyTo(result, 4);
                replaced = targets.Count;
                return result;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }
    }

    public static class MinimapBarFillAlphaService
    {
        public static byte[]? RaiseFillAlpha(byte[] gfxBytes, bool hpBar, bool armourBar, out string? error, out int patched)
            => RaiseFillAlpha(gfxBytes, hpBar, armourBar, hpBar, armourBar, out error, out patched);

        public static byte[]? RaiseFillAlpha(byte[] gfxBytes, bool hpFill, bool armourFill,
            bool hpTrough, bool armourTrough, out string? error, out int patched)
        {
            error = null;
            patched = 0;
            if (gfxBytes is null || gfxBytes.Length < 32) { error = Loc.T("error.gfxEmpty"); return null; }
            if (!(gfxBytes[0] == 'G' && gfxBytes[1] == 'F' && gfxBytes[2] == 'X'))
            { error = Loc.T("error.gfxExpectedUncompressed"); return null; }
            if (!hpFill && !armourFill && !hpTrough && !armourTrough) return gfxBytes;

            try
            {
                var d = (byte[])gfxBytes.Clone();
                var top = MgSwf.WalkTop(d, out _);
                if (top.Count == 0) { error = Loc.T("error.tagStreamEmpty"); return null; }

                var shapes = new Dictionary<int, MgSwf.Tag>();
                foreach (var t in top)
                    if (t.Code is 2 or 22 or 32 or 83 && t.BodyEnd - t.BodyStart >= 3)
                        shapes[MgSwf.U16(d, t.BodyStart)] = t;
                var sprites = MgSwf.IndexSprites(d, top);

                var wanted = new HashSet<string>(StringComparer.Ordinal);
                if (hpFill) wanted.Add("healthBar");
                if (hpTrough) wanted.Add("healthTrough");
                if (armourFill) wanted.Add("armourBar");
                if (armourTrough) wanted.Add("armourTrough");

                var roots = new HashSet<int>();
                void Scan(IEnumerable<MgSwf.Tag> tags)
                {
                    foreach (var st in tags)
                    {
                        if (st.Code != 26) continue;
                        var po = MgSwf.ParsePlaceObject2(d, st);
                        if (po is { } p && p.Name is not null && p.CharId > 0 && wanted.Contains(p.Name))
                            roots.Add(p.CharId);
                    }
                }
                Scan(top);
                foreach (var t in top)
                    if (t.Code == 39 && t.BodyEnd - t.BodyStart >= 4)
                        Scan(MgSwf.Walk(d, t.BodyStart + 4, t.BodyEnd));
                if (roots.Count == 0) { error = Loc.T("error.barClipsNotFound"); return d; }

                var targetShapes = new HashSet<int>();
                var queue = new Queue<(int Id, int Depth)>();
                foreach (var r in roots) queue.Enqueue((r, 0));
                var seen = new HashSet<int>();
                while (queue.Count > 0)
                {
                    var (id, depth) = queue.Dequeue();
                    if (!seen.Add(id) || depth > 4) continue;
                    if (shapes.ContainsKey(id)) { targetShapes.Add(id); continue; }
                    if (!sprites.TryGetValue(id, out var sp)) continue;
                    foreach (var st in MgSwf.Walk(d, sp.BodyStart + 4, sp.BodyEnd))
                    {
                        if (st.Code != 26) continue;
                        var po = MgSwf.ParsePlaceObject2(d, st);
                        if (po is { } p && p.CharId > 0) queue.Enqueue((p.CharId, depth + 1));
                    }
                }
                if (targetShapes.Count == 0) { error = Loc.T("error.barClipsNoDefineShape"); return d; }

                foreach (var id in targetShapes)
                {
                    var t = shapes[id];
                    if (t.Code is 2 or 22) continue;
                    if (!PatchFillAlphas(d, t, ref patched))
                        error = Loc.T("error.defineShapeNonStandard", ("id", id));
                }
                return d;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        private static bool PatchFillAlphas(byte[] d, MgSwf.Tag t, ref int patched)
        {
            try
            {
                int p = t.BodyStart + 2;
                var br = new MgSwf.BitReader(d, p);
                int nb = br.ReadUB(5);
                br.ReadSB(nb); br.ReadSB(nb); br.ReadSB(nb); br.ReadSB(nb);
                p = br.AlignedPos;
                if (t.Code == 83)
                {
                    br = new MgSwf.BitReader(d, p);
                    nb = br.ReadUB(5);
                    br.ReadSB(nb); br.ReadSB(nb); br.ReadSB(nb); br.ReadSB(nb);
                    p = br.AlignedPos + 1;
                }
                if (p >= t.BodyEnd) return false;
                int count = d[p++];
                if (count == 0xFF) { count = MgSwf.U16(d, p); p += 2; }

                var offsets = new List<int>();
                for (int i = 0; i < count; i++)
                {
                    if (p >= t.BodyEnd) return false;
                    int type = d[p++];
                    if (type == 0x00)
                    {
                        if (p + 4 > t.BodyEnd) return false;
                        offsets.Add(p + 3);
                        p += 4;
                    }
                    else if (type is 0x10 or 0x12 or 0x13)
                    {
                        p = SkipMatrix(d, p);
                        if (p >= t.BodyEnd) return false;
                        int grads = d[p++] & 0x0F;
                        for (int g = 0; g < grads; g++)
                        {
                            if (p + 5 > t.BodyEnd) return false;
                            offsets.Add(p + 4);
                            p += 5;
                        }
                        if (type == 0x13) p += 2;
                    }
                    else if (type is 0x40 or 0x41 or 0x42 or 0x43)
                    {
                        p = SkipMatrix(d, p + 2);
                    }
                    else return false;
                    if (p > t.BodyEnd) return false;
                }

                foreach (var off in offsets)
                {
                    if (d[off] > 0 && d[off] < 255) { d[off] = 255; patched++; }
                }
                return true;
            }
            catch { return false; }
        }

        private static int SkipMatrix(byte[] d, int p)
        {
            var br = new MgSwf.BitReader(d, p);
            if (br.ReadUB(1) == 1) { int n = br.ReadUB(5); br.ReadSB(n); br.ReadSB(n); }
            if (br.ReadUB(1) == 1) { int n = br.ReadUB(5); br.ReadSB(n); br.ReadSB(n); }
            int nt = br.ReadUB(5);
            br.ReadSB(nt); br.ReadSB(nt);
            return br.AlignedPos;
        }
    }

    internal static class MgSwf
    {
        internal readonly struct Tag
        {
            public readonly int Code, Start, End, BodyStart, BodyEnd;
            public Tag(int c, int s, int e, int bs, int be) { Code = c; Start = s; End = e; BodyStart = bs; BodyEnd = be; }
            public int SpriteId(byte[] d) => U16(d, BodyStart);
        }

        internal readonly struct Po2Info
        {
            public readonly int Flags, Depth, CharId;
            public readonly (double A, double R0, double R1, double D, int Tx, int Ty) Matrix;
            public readonly string? Name;
            public Po2Info(int flags, int depth, int charId,
                (double, double, double, double, int, int) matrix, string? name)
            { Flags = flags; Depth = depth; CharId = charId; Matrix = matrix; Name = name; }
        }

        internal static ushort U16(byte[] d, int p) => (ushort)(d[p] | (d[p + 1] << 8));

        internal static bool IsDefineWithId(int code) => code switch
        {
            2 or 6 or 10 or 13 or 20 or 21 or 22 or 32 or 35 or 36 or 37 or 46 or 48 or 60 or 75 or 83 or 84 or 90 => true,
            _ => false,
        };

        internal static byte[] TagHeader(int code, int length)
        {
            var b = new byte[6];
            ushort hdr = (ushort)((code << 6) | 0x3F);
            b[0] = (byte)hdr; b[1] = (byte)(hdr >> 8);
            BitConverter.GetBytes(length).CopyTo(b, 2);
            return b;
        }

        internal static List<Tag> WalkTop(byte[] d, out int tagStart)
        {
            int nbits = d[8] >> 3;
            int rectBytes = (5 + nbits * 4 + 7) / 8;
            tagStart = 8 + rectBytes + 4;
            return Walk(d, tagStart, d.Length);
        }

        internal static List<Tag> Walk(byte[] d, int start, int limit)
        {
            var tags = new List<Tag>();
            int pos = start;
            while (pos + 2 <= limit)
            {
                int rh = d[pos] | (d[pos + 1] << 8);
                int code = rh >> 6;
                int len = rh & 0x3F;
                int hl = 2;
                if (len == 0x3F)
                {
                    if (pos + 6 > limit) break;
                    len = d[pos + 2] | (d[pos + 3] << 8) | (d[pos + 4] << 16) | (d[pos + 5] << 24);
                    hl = 6;
                }
                int bodyStart = pos + hl;
                int end = bodyStart + len;
                if (end > limit || len < 0) break;
                tags.Add(new Tag(code, pos, end, bodyStart, end));
                if (code == 0) break;
                pos = end;
            }
            return tags;
        }

        internal static Dictionary<int, (int X0, int Y0, int X1, int Y1)> IndexShapeBounds(byte[] d, List<Tag> top)
        {
            var map = new Dictionary<int, (int, int, int, int)>();
            foreach (var t in top)
            {
                if (t.Code is not (2 or 22 or 32 or 83)) continue;
                if (t.BodyEnd - t.BodyStart < 3) continue;
                int id = U16(d, t.BodyStart);
                var br = new BitReader(d, t.BodyStart + 2);
                int nb = br.ReadUB(5);
                int x0 = br.ReadSB(nb), x1 = br.ReadSB(nb), y0 = br.ReadSB(nb), y1 = br.ReadSB(nb);
                map[id] = (x0, y0, x1, y1);
            }
            return map;
        }

        internal static Dictionary<int, Tag> IndexSprites(byte[] d, List<Tag> top)
        {
            var map = new Dictionary<int, Tag>();
            foreach (var t in top)
                if (t.Code == 39 && t.BodyEnd - t.BodyStart >= 4)
                    map[U16(d, t.BodyStart)] = t;
            return map;
        }

        internal static (int, int, int, int)? SpriteContentRect(
            byte[] d, Tag sprite,
            Dictionary<int, (int X0, int Y0, int X1, int Y1)> shapeBounds,
            Dictionary<int, Tag> sprites,
            Dictionary<int, (int, int, int, int)?> memo,
            int depth)
        {
            if (depth > 4) return null;
            (int, int, int, int)? union = null;
            foreach (var st in Walk(d, sprite.BodyStart + 4, sprite.BodyEnd))
            {
                if (st.Code != 26) continue;
                var po = ParsePlaceObject2(d, st);
                if (po is null || po.Value.CharId <= 0) continue;
                var p = po.Value;
                (int, int, int, int)? child = null;
                if (shapeBounds.TryGetValue(p.CharId, out var b)) child = b;
                else if (sprites.TryGetValue(p.CharId, out var sub))
                {
                    if (!memo.TryGetValue(p.CharId, out child))
                    {
                        memo[p.CharId] = null;
                        child = SpriteContentRect(d, sub, shapeBounds, sprites, memo, depth + 1);
                        memo[p.CharId] = child;
                    }
                }
                if (child is null) continue;
                var tr = TransformRect((child.Value.Item1, child.Value.Item2, child.Value.Item3, child.Value.Item4), p.Matrix);
                union = union is null
                    ? tr
                    : (Math.Min(union.Value.Item1, tr.X0), Math.Min(union.Value.Item2, tr.Y0),
                       Math.Max(union.Value.Item3, tr.X1), Math.Max(union.Value.Item4, tr.Y1));
            }
            return union;
        }

        internal static (int X0, int Y0, int X1, int Y1) TransformRect(
            (int X0, int Y0, int X1, int Y1) r,
            (double A, double R0, double R1, double D, int Tx, int Ty) m)
        {
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var (x, y) in new[] { (r.X0, r.Y0), (r.X1, r.Y0), (r.X0, r.Y1), (r.X1, r.Y1) })
            {
                double nx = m.A * x + m.R1 * y + m.Tx;
                double ny = m.R0 * x + m.D * y + m.Ty;
                minX = Math.Min(minX, nx); maxX = Math.Max(maxX, nx);
                minY = Math.Min(minY, ny); maxY = Math.Max(maxY, ny);
            }
            return ((int)Math.Floor(minX), (int)Math.Floor(minY), (int)Math.Ceiling(maxX), (int)Math.Ceiling(maxY));
        }

        internal static Po2Info? ParsePlaceObject2(byte[] d, Tag t)
        {
            try
            {
                if (t.BodyEnd - t.BodyStart < 3) return null;
                int flags = d[t.BodyStart];
                int depth = U16(d, t.BodyStart + 1);
                int p = t.BodyStart + 3;
                int charId = -1;
                if ((flags & 0x02) != 0) { charId = U16(d, p); p += 2; }
                (double, double, double, double, int, int) matrix = (1, 0, 0, 1, 0, 0);
                if ((flags & 0x04) != 0)
                {
                    var br = new BitReader(d, p);
                    double a = 1, sd = 1, r0 = 0, r1 = 0;
                    if (br.ReadUB(1) == 1)
                    {
                        int n = br.ReadUB(5);
                        a = br.ReadSB(n) / 65536.0;
                        sd = br.ReadSB(n) / 65536.0;
                    }
                    if (br.ReadUB(1) == 1)
                    {
                        int n = br.ReadUB(5);
                        r0 = br.ReadSB(n) / 65536.0;
                        r1 = br.ReadSB(n) / 65536.0;
                    }
                    int nt = br.ReadUB(5);
                    int tx = br.ReadSB(nt), ty = br.ReadSB(nt);
                    matrix = (a, r0, r1, sd, tx, ty);
                    p = br.AlignedPos;
                }
                if ((flags & 0x08) != 0)
                {
                    var br = new BitReader(d, p);
                    int hasAdd = br.ReadUB(1), hasMult = br.ReadUB(1);
                    int n = br.ReadUB(4);
                    int terms = (hasMult == 1 ? 4 : 0) + (hasAdd == 1 ? 4 : 0);
                    for (int i = 0; i < terms; i++) br.ReadSB(n);
                    p = br.AlignedPos;
                }
                if ((flags & 0x10) != 0) p += 2;
                string? name = null;
                if ((flags & 0x20) != 0)
                {
                    int s = p;
                    while (p < t.BodyEnd && d[p] != 0) p++;
                    name = global::System.Text.Encoding.ASCII.GetString(d, s, p - s);
                    p++;
                }
                return new Po2Info(flags, depth, charId, matrix, name);
            }
            catch { return null; }
        }

        internal static int SignedBits(int v)
        {
            if (v == 0) return 2;
            int n = 1;
            while (!(-(1L << (n - 1)) <= v && v <= (1L << (n - 1)) - 1)) n++;
            return Math.Max(n, 2);
        }

        internal sealed class BitReader
        {
            private readonly byte[] _d;
            private int _bytePos;
            private int _bitPos;

            public BitReader(byte[] d, int pos) { _d = d; _bytePos = pos; }

            public int ReadUB(int n)
            {
                int v = 0;
                for (int i = 0; i < n; i++)
                {
                    v = (v << 1) | ((_d[_bytePos] >> (7 - _bitPos)) & 1);
                    _bitPos++;
                    if (_bitPos == 8) { _bitPos = 0; _bytePos++; }
                }
                return v;
            }

            public int ReadSB(int n)
            {
                if (n == 0) return 0;
                int v = ReadUB(n);
                if ((v & (1 << (n - 1))) != 0) v -= 1 << n;
                return v;
            }

            public int AlignedPos => _bitPos == 0 ? _bytePos : _bytePos + 1;
        }

        internal sealed class BitWriter
        {
            private readonly List<byte> _bytes = new();
            private int _bitPos;

            public void WriteBits(int value, int count)
            {
                for (int i = count - 1; i >= 0; i--)
                {
                    if (_bitPos == 0) _bytes.Add(0);
                    if (((value >> i) & 1) != 0)
                        _bytes[^1] |= (byte)(0x80 >> _bitPos);
                    _bitPos = (_bitPos + 1) & 7;
                }
            }

            public void Align() => _bitPos = 0;

            public void WriteByte(byte b) { Align(); _bytes.Add(b); _bitPos = 0; }

            public void WriteUInt16(ushort v) { Align(); _bytes.Add((byte)v); _bytes.Add((byte)(v >> 8)); }

            public byte[] ToArray() { return _bytes.ToArray(); }
        }
    }
}
