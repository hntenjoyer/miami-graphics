#nullable disable
using SharpDX;

namespace MiamiGraphics.Gunpack.Services;

public static class ExtrudeService
{
    public sealed class Mesh
    {
        public List<Vector3> Positions = new();
        public List<Vector3> Normals = new();
        public List<Vector2> UVs = new();
        public List<ushort> Indices = new();
    }

    public static Mesh Build(byte[] bgra, int tw, int th, float widthM, float depthM)
    {
        const int MaxDim = 72;
        int gw, gh;
        if (tw >= th) { gw = Math.Min(MaxDim, tw); gh = Math.Max(4, th * gw / tw); }
        else { gh = Math.Min(MaxDim, th); gw = Math.Max(4, tw * gh / th); }

        bool[,] mask = new bool[gw, gh];
        int filled = 0;
        for (int y = 0; y < gh; y++)
            for (int x = 0; x < gw; x++)
            {
                int sx = x * tw / gw, sy = y * th / gh;
                byte a = bgra[(sy * tw + sx) * 4 + 3];
                if (a >= 64) { mask[x, y] = true; filled++; }
            }
        if (filled < 8) return null;

        var contour = TraceContour(mask, gw, gh);
        if (contour == null || contour.Count < 3) return null;

        var poly = SimplifyClosed(contour, 1.15f);
        if (poly.Count < 3) return null;
        if (SignedArea(poly) < 0) poly.Reverse();

        var tris = EarClip(poly);
        if (tris == null || tris.Count == 0) return null;

        float minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X);
        float minY = poly.Min(p => p.Y);
        float scale = widthM / Math.Max(1e-3f, maxX - minX);
        float cx = (minX + maxX) / 2;
        float hd = depthM / 2;

        var mesh = new Mesh();
        Vector2 UvOf(Vector2 p) => new(p.X / gw, p.Y / gh);
        Vector3 LocalOf(Vector2 p, float z) => new((p.X - cx) * scale, -(p.Y - minY) * scale, z);

        ushort AddV(Vector2 p, float z, Vector3 n)
        {
            mesh.Positions.Add(LocalOf(p, z));
            mesh.Normals.Add(n);
            mesh.UVs.Add(UvOf(p));
            return (ushort)(mesh.Positions.Count - 1);
        }

        var front = poly.Select(p => AddV(p, +hd, new Vector3(0, 0, 1))).ToArray();
        foreach (var (a, b, c) in tris) mesh.Indices.AddRange(new[] { front[c], front[b], front[a] });

        var back = poly.Select(p => AddV(p, -hd, new Vector3(0, 0, -1))).ToArray();
        foreach (var (a, b, c) in tris) mesh.Indices.AddRange(new[] { back[a], back[b], back[c] });

        for (int i = 0; i < poly.Count; i++)
        {
            var p0 = poly[i];
            var p1 = poly[(i + 1) % poly.Count];
            var e = p1 - p0;
            var n2 = new Vector2(e.Y, -e.X);
            if (n2.LengthSquared() < 1e-9f) continue;
            n2.Normalize();
            var n3 = new Vector3(n2.X, -n2.Y, 0);
            n3.Normalize();

            ushort a0 = AddV(p0, +hd, n3);
            ushort a1 = AddV(p1, +hd, n3);
            ushort b1 = AddV(p1, -hd, n3);
            ushort b0 = AddV(p0, -hd, n3);
            mesh.Indices.AddRange(new[] { a0, a1, b1, a0, b1, b0 });
        }

        if (mesh.Positions.Count > 65000) return null;
        return mesh;
    }

    private static List<Vector2> TraceContour(bool[,] m, int w, int h)
    {
        bool At(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && m[x, y];

        int sx = -1, sy = -1;
        for (int y = 0; y < h && sx < 0; y++)
            for (int x = 0; x < w; x++)
                if (m[x, y]) { sx = x; sy = y; break; }
        if (sx < 0) return null;

        int[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
        int[] dy = { 0, 1, 1, 1, 0, -1, -1, -1 };

        var pts = new List<Vector2>();
        int cx = sx, cy = sy, dir = 6;
        for (int step = 0; step < w * h * 4; step++)
        {
            pts.Add(new Vector2(cx + 0.5f, cy + 0.5f));
            int found = -1;
            for (int i = 0; i < 8; i++)
            {
                int d = (dir + 6 + i) % 8;
                if (At(cx + dx[d], cy + dy[d])) { found = d; break; }
            }
            if (found < 0) break;
            cx += dx[found]; cy += dy[found]; dir = found;
            if (cx == sx && cy == sy && pts.Count > 2) break;
        }
        return pts.Count >= 3 ? pts : null;
    }

    private static List<Vector2> SimplifyClosed(List<Vector2> pts, float eps)
    {
        if (pts.Count < 8) return new List<Vector2>(pts);
        int iA = 0, iB = 0; float best = -1;
        for (int i = 0; i < pts.Count; i += Math.Max(1, pts.Count / 64))
            for (int j = i + 1; j < pts.Count; j += Math.Max(1, pts.Count / 64))
            {
                float d = (pts[i] - pts[j]).LengthSquared();
                if (d > best) { best = d; iA = i; iB = j; }
            }
        var half1 = Slice(pts, iA, iB);
        var half2 = Slice(pts, iB, iA);
        var r = Rdp(half1, eps);
        r.RemoveAt(r.Count - 1);
        r.AddRange(Rdp(half2, eps));
        r.RemoveAt(r.Count - 1);
        return r;
    }
    private static List<Vector2> Slice(List<Vector2> pts, int from, int to)
    {
        var r = new List<Vector2>();
        for (int i = from; ; i = (i + 1) % pts.Count) { r.Add(pts[i]); if (i == to) break; }
        return r;
    }
    private static List<Vector2> Rdp(List<Vector2> pts, float eps)
    {
        if (pts.Count < 3) return new List<Vector2>(pts);
        int idx = -1; float dmax = 0;
        var a = pts[0]; var b = pts[^1];
        for (int i = 1; i < pts.Count - 1; i++)
        {
            float d = PerpDist(pts[i], a, b);
            if (d > dmax) { dmax = d; idx = i; }
        }
        if (dmax <= eps) return new List<Vector2> { a, b };
        var left = Rdp(pts.GetRange(0, idx + 1), eps);
        var right = Rdp(pts.GetRange(idx, pts.Count - idx), eps);
        left.RemoveAt(left.Count - 1);
        left.AddRange(right);
        return left;
    }
    private static float PerpDist(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len = ab.Length();
        if (len < 1e-6f) return (p - a).Length();
        return Math.Abs((p.X - a.X) * ab.Y - (p.Y - a.Y) * ab.X) / len;
    }

    private static float SignedArea(List<Vector2> p)
    {
        float s = 0;
        for (int i = 0; i < p.Count; i++)
        {
            var a = p[i]; var b = p[(i + 1) % p.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return s / 2;
    }

    private static List<(int a, int b, int c)> EarClip(List<Vector2> poly)
    {
        var idx = Enumerable.Range(0, poly.Count).ToList();
        var tris = new List<(int, int, int)>();
        int guard = 0;
        while (idx.Count > 3 && guard++ < 10000)
        {
            bool clipped = false;
            for (int i = 0; i < idx.Count; i++)
            {
                int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                var a = poly[ia]; var b = poly[ib]; var c = poly[ic];
                float cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (cross <= 0) continue;
                bool anyInside = false;
                foreach (var j in idx)
                {
                    if (j == ia || j == ib || j == ic) continue;
                    if (PointInTri(poly[j], a, b, c)) { anyInside = true; break; }
                }
                if (anyInside) continue;
                tris.Add((ia, ib, ic));
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break;
        }
        if (idx.Count == 3) tris.Add((idx[0], idx[1], idx[2]));
        return tris;
    }
    private static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }
    private static float Sign(Vector2 p, Vector2 a, Vector2 b)
        => (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
}
