using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.Services
{
    public sealed record ReticleBuildSpec(
        bool Dot,
        double DotSize,
        double Gap,
        double Length,
        double Thickness,
        double Tilt,
        bool Outline,
        double OutlineWidth,
        int Opacity,
        int Scale,
        string ColorMain,
        string ColorAds,
        bool Permanent,
        double HipfireSeconds,
        bool Ring = false,
        double RingRadius = 10,
        double RingThickness = 1.5);

    public class ReticleBuilderService
    {
        public static readonly string[] DefaultTargetPaths =
        {
            "x64/patch/data/cdimages/scaleform_generic.rpf/hud_reticle.gfx",
            "x64/data/cdimages/scaleform_generic.rpf/hud_reticle.gfx",
        };

        private static readonly string[] ReticleFrameMarkers =
        {
            "WEAPON_PISTOL", "WEAPONTYPE_SMG", "WEAPONTYPE_RIFLE", "WEAPONTYPE_SHOTGUN",
            "WEAPONTYPE_SNIPER", "WEAPONTYPE_HEAVY", "WEAPONTYPE_THROWN", "WEAPONTYPE_MELEE",
            "SIMPLE_RETICLE", "SIMPLE_MP_RETICLE",
        };

        private const int ReticleMarkerThreshold = 4;

        public static bool LooksLikeReticleGfx(byte[]? raw)
        {
            var body = TryGetSwfBodyText(raw);
            if (body is null) return false;
            int hits = 0;
            foreach (var m in ReticleFrameMarkers)
            {
                if (body.IndexOf(m, StringComparison.Ordinal) >= 0 && ++hits >= ReticleMarkerThreshold)
                    return true;
            }
            return false;
        }

        private static string? TryGetSwfBodyText(byte[]? raw)
        {
            if (raw is null || raw.Length < 64 || raw.Length > 32 * 1024 * 1024) return null;
            byte c0 = raw[0], c1 = raw[1], c2 = raw[2];
            bool plain      = (c0 == 'F' && c1 == 'W' && c2 == 'S') || (c0 == 'G' && c1 == 'F' && c2 == 'X');
            bool compressed = (c0 == 'C' && c1 == 'W' && c2 == 'S') || (c0 == 'C' && c1 == 'F' && c2 == 'X');
            if (!plain && !compressed) return null;
            try
            {
                if (plain)
                    return Encoding.Latin1.GetString(raw);

                using var src = new MemoryStream(raw, 8, raw.Length - 8, writable: false);
                using var inf = new global::System.IO.Compression.ZLibStream(
                    src, global::System.IO.Compression.CompressionMode.Decompress);
                using var dst = new MemoryStream();
                inf.CopyTo(dst, 81920);
                return Encoding.Latin1.GetString(dst.GetBuffer(), 0, (int)dst.Length);
            }
            catch { return null; }
        }

        private static readonly string[] HipFireFrames =
        {
            "WEAPON_PISTOL", "WEAPONTYPE_SMG",
            "WEAPONTYPE_RIFLE", "WEAPONTYPE_SHOTGUN",
            "SIMPLE_RETICLE", "SIMPLE_MP_RETICLE",
        };

        public static readonly IReadOnlyDictionary<string, string> WeaponGroupFrames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pistol"]  = "WEAPON_PISTOL",
                ["smg"]     = "WEAPONTYPE_SMG",
                ["rifle"]   = "WEAPONTYPE_RIFLE",
                ["shotgun"] = "WEAPONTYPE_SHOTGUN",
            };

        public byte[]? Build(
            byte[] baseGfxBytes,
            ReticleBuildSpec spec,
            string javaExe,
            string ffdecJar,
            out string? error)
            => Build(baseGfxBytes, spec, weaponSpecs: null, javaExe, ffdecJar, out error);

        public byte[]? Build(
            byte[] baseGfxBytes,
            ReticleBuildSpec spec,
            IReadOnlyDictionary<string, ReticleBuildSpec>? weaponSpecs,
            string javaExe,
            string ffdecJar,
            out string? error)
        {
            error = null;
            string tempDir = Path.Combine(Path.GetTempPath(), "ReticleBuild_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                spec = Clamp(spec);

                var overriddenFrames = new HashSet<string>(StringComparer.Ordinal);
                var groups = new List<(ReticleBuildSpec Spec, string[] Frames)>();
                if (weaponSpecs != null)
                {
                    foreach (var kv in weaponSpecs)
                    {
                        if (!WeaponGroupFrames.TryGetValue(kv.Key, out var frame)) continue;
                        if (!overriddenFrames.Add(frame)) continue;
                        groups.Add((Clamp(kv.Value), new[] { frame }));
                    }
                }
                var baseFrames = HipFireFrames.Where(f => !overriddenFrames.Contains(f)).ToArray();
                groups.Insert(0, (spec, baseFrames));

                string tSwf = Path.Combine(tempDir, "t.swf");
                File.WriteAllBytes(tSwf, baseGfxBytes);
                SwapGfxHeader(tSwf, toSwf: true);

                string shapedSwf = tSwf;
                if (groups.Any(g => HasGeometry(g.Spec) && g.Frames.Length > 0))
                {
                    string tXml = Path.Combine(tempDir, "t.xml");
                    RunProcess(javaExe, $"-jar \"{ffdecJar}\" -swf2xml \"{tSwf}\" \"{tXml}\"");
                    if (!File.Exists(tXml)) { error = Loc.T("error.ffdecSwf2XmlNoXml"); return null; }

                    string xml = File.ReadAllText(tXml);
                    string? synthErr = SynthesizeShapes(ref xml, groups);
                    if (synthErr != null) { error = synthErr; return null; }

                    string genXml = Path.Combine(tempDir, "gen.xml");
                    File.WriteAllText(genXml, xml);
                    shapedSwf = Path.Combine(tempDir, "shaped.swf");
                    RunProcess(javaExe, $"-jar \"{ffdecJar}\" -xml2swf \"{genXml}\" \"{shapedSwf}\"");
                    if (!File.Exists(shapedSwf)) { error = Loc.T("error.ffdecXml2SwfNoSwf"); return null; }
                }

                string scriptsDir = Path.Combine(tempDir, "scripts");
                Directory.CreateDirectory(scriptsDir);
                RunProcess(javaExe, $"-jar \"{ffdecJar}\" -export script \"{scriptsDir}\" \"{shapedSwf}\"");

                string? asPath = Directory.GetFiles(scriptsDir, "HUD_RETICLE.as", SearchOption.AllDirectories).FirstOrDefault();
                if (asPath is null) { error = Loc.T("error.hudReticleAsNotFound"); return null; }

                string asText = File.ReadAllText(asPath);
                string? asErr = ApplyScriptEdits(ref asText, spec, weaponSpecs);
                if (asErr != null) { error = asErr; return null; }
                File.WriteAllText(asPath, asText);

                string finalSwf = Path.Combine(tempDir, "final.swf");
                RunProcess(javaExe, $"-jar \"{ffdecJar}\" -importScript \"{shapedSwf}\" \"{finalSwf}\" \"{scriptsDir}\"");
                if (!File.Exists(finalSwf)) { error = Loc.T("error.ffdecImportScriptNoFileAs"); return null; }

                SwapGfxHeader(finalSwf, toSwf: false);
                var bytes = File.ReadAllBytes(finalSwf);
                if (bytes.Length < 10_000 || bytes[0] != (byte)'G' || bytes[1] != (byte)'F' || bytes[2] != (byte)'X')
                {
                    error = Loc.T("error.builtGfxFailedCheck", ("len", bytes.Length));
                    return null;
                }
                return bytes;
            }
            catch (Exception ex)
            {
                error = $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        private static ReticleBuildSpec Clamp(ReticleBuildSpec s) => s with
        {
            DotSize        = Math.Clamp(s.DotSize, 0, 10),
            Gap            = Math.Clamp(s.Gap, 0, 40),
            Length         = Math.Clamp(s.Length, 0, 50),
            Thickness      = Math.Clamp(s.Thickness, 0.5, 14),
            Tilt           = Math.Clamp(s.Tilt, 0, 45),
            OutlineWidth   = Math.Clamp(s.OutlineWidth, 0, 6),
            Opacity        = Math.Clamp(s.Opacity, 0, 100),
            Scale          = Math.Clamp(s.Scale, 50, 200),
            HipfireSeconds = Math.Clamp(s.HipfireSeconds, 0, 30),
            RingRadius     = Math.Clamp(s.RingRadius, 3, 40),
            RingThickness  = Math.Clamp(s.RingThickness, 0.5, 8),
        };

        private static bool HasGeometry(ReticleBuildSpec s) =>
            (s.Length > 0 && s.Thickness > 0) ||
            (s.Dot && s.DotSize > 0) ||
            (s.Ring && s.RingRadius > 0 && s.RingThickness > 0);

        private static string? SynthesizeShapes(ref string xml,
            List<(ReticleBuildSpec Spec, string[] Frames)> groups)
        {
            var spriteMatches = Regex.Matches(xml, "<item type=\"DefineSpriteTag\"[^>]*spriteId=\"(\\d+)\"[^>]*>");
            int parentStart = -1;
            for (int i = 0; i < spriteMatches.Count; i++)
            {
                int start = spriteMatches[i].Index;
                int end = i + 1 < spriteMatches.Count ? spriteMatches[i + 1].Index : xml.Length;
                int labels = Regex.Matches(xml.Substring(start, end - start), "FrameLabelTag[^>]*name=\"WEAPON").Count;
                if (labels >= 5) { parentStart = start; break; }
            }
            if (parentStart < 0)
                return Loc.T("error.weaponMarkedSpriteNotFound");

            int maxId = 0;
            foreach (Match m in Regex.Matches(xml, "(?:spriteId|shapeId|characterID|characterId|fontId)=\"(\\d+)\""))
                maxId = Math.Max(maxId, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));

            int nextIdBase = maxId + 10;
            var genAll = new StringBuilder();
            var repoints = new List<(string Frame, int Container)>();
            foreach (var (gSpec, frames) in groups)
            {
                if (frames.Length == 0 || !HasGeometry(gSpec)) continue;
                bool hasArms = gSpec.Length > 0 && gSpec.Thickness > 0;
                bool hasDot = gSpec.Dot && gSpec.DotSize > 0;
                bool hasRing = gSpec.Ring && gSpec.RingRadius > 0 && gSpec.RingThickness > 0;
                int armShape = nextIdBase, armSprite = nextIdBase + 1,
                    dotShape = nextIdBase + 2, dotSprite = nextIdBase + 3,
                    ringShape = nextIdBase + 4, ringSprite = nextIdBase + 5,
                    container = nextIdBase + 6;
                nextIdBase += 10;

                genAll.Append(BuildReticleCharactersXml(gSpec, hasArms, hasDot, hasRing,
                    armShape, armSprite, dotShape, dotSprite, ringShape, ringSprite, container));
                genAll.Append("\n    ");
                foreach (var f in frames) repoints.Add((f, container));
            }
            if (genAll.Length == 0)
                return Loc.T("error.noGeometryToSynthesize");

            xml = xml.Substring(0, parentStart) + genAll + xml.Substring(parentStart);

            int pointed = 0;
            foreach (var (label, container) in repoints)
            {
                var re = new Regex(
                    "(<item type=\"FrameLabelTag\"[^>]*name=\"" + Regex.Escape(label) +
                    "\"[^>]*/>\\s*<item type=\"PlaceObject2Tag\" characterId=\")(\\d+)(\")");
                string before = xml;
                xml = re.Replace(xml, m => m.Groups[1].Value + container + m.Groups[3].Value, 1);
                if (!ReferenceEquals(before, xml) && before != xml) pointed++;
            }
            if (pointed == 0)
                return Loc.T("error.noWeaponFrameRedirected");

            Debug.WriteLine($"[ReticleBuilder] shapes synthesized, groups={groups.Count}, frames re-pointed={pointed}/{repoints.Count}");
            return null;
        }

        private static string BuildReticleCharactersXml(
            ReticleBuildSpec spec, bool hasArms, bool hasDot, bool hasRing,
            int armShapeId, int armSpriteId, int dotShapeId, int dotSpriteId,
            int ringShapeId, int ringSpriteId, int containerId)
        {
            int tw(double px) => (int)Math.Round(px * 20);
            int th = tw(spec.Thickness), len = tw(spec.Length);
            int ow = spec.Outline ? tw(spec.OutlineWidth) : 0;
            int gap = tw(spec.Gap);
            double dr = tw(spec.DotSize) / 2.0;

            var parts = new List<string>();

            if (hasArms)
            {
                var fills = new List<(int r, int g, int b, int a)>();
                var rec = new StringBuilder();
                if (ow > 0)
                {
                    fills.Add((0, 0, 0, 127));
                    rec.Append(RectPath(-(th / 2 + ow), 0, th / 2 + ow, len + 2 * ow, 1));
                    fills.Add((255, 255, 255, 255));
                    rec.Append(RectPath(-th / 2, ow, th / 2, ow + len, 2));
                }
                else
                {
                    fills.Add((255, 255, 255, 255));
                    rec.Append(RectPath(-th / 2, 0, th / 2, len, 1));
                }
                parts.Add(DefineShape3(armShapeId, -(th / 2 + ow), th / 2 + ow, 0, len + 2 * ow, fills, rec.ToString()));
                parts.Add(DefineSprite(armSpriteId, Place(armShapeId, 2, null, Identity())));
            }

            if (hasDot)
            {
                var fills = new List<(int r, int g, int b, int a)>();
                var rec = new StringBuilder();
                if (ow > 0)
                {
                    fills.Add((0, 0, 0, 127));
                    rec.Append(CirclePath(dr + ow, 1));
                    fills.Add((255, 255, 255, 255));
                    rec.Append(CirclePath(dr, 2));
                }
                else
                {
                    fills.Add((255, 255, 255, 255));
                    rec.Append(CirclePath(dr, 1));
                }
                int R = (int)Math.Round(dr + ow);
                parts.Add(DefineShape3(dotShapeId, -R, R, -R, R, fills, rec.ToString()));
                parts.Add(DefineSprite(dotSpriteId, Place(dotShapeId, 2, null, Identity())));
            }

            if (hasRing)
            {
                double mid = tw(spec.RingRadius);
                double rth = tw(spec.RingThickness);
                var fills = new List<(int r, int g, int b, int a)>();
                var rec = new StringBuilder();
                if (ow > 0)
                {
                    fills.Add((0, 0, 0, 127));
                    rec.Append(RingPath(mid + rth / 2 + ow, mid - rth / 2 - ow, 1));
                    fills.Add((255, 255, 255, 255));
                    rec.Append(RingPath(mid + rth / 2, mid - rth / 2, 2));
                }
                else
                {
                    fills.Add((255, 255, 255, 255));
                    rec.Append(RingPath(mid + rth / 2, mid - rth / 2, 1));
                }
                int R = (int)Math.Round(mid + rth / 2 + ow);
                parts.Add(DefineShape3(ringShapeId, -R, R, -R, R, fills, rec.ToString()));
                parts.Add(DefineSprite(ringSpriteId, Place(ringShapeId, 2, null, Identity())));
            }

            double rad = spec.Tilt * Math.PI / 180.0, c = Math.Cos(rad), s = Math.Sin(rad);
            var bases = new (double sx, double sy, double r0, double r1, double tx, double ty)[]
            {
                (-1, -1,  0,  0,    0, -gap),
                ( 0,  0, -1,  1,  gap,    0),
                ( 1,  1,  0,  0,    0,  gap),
                ( 0,  0,  1, -1, -gap,    0),
            };

            var inner = new StringBuilder();
            inner.Append("<item type=\"DoActionTag\" actionBytes=\"0700\" forceWriteAsLong=\"true\"/>");
            if (hasArms)
            {
                for (int i = 0; i < 4; i++)
                {
                    var b = bases[i];
                    var m = (
                        sx: c * b.sx - s * b.r0,
                        sy: s * b.r1 + c * b.sy,
                        r0: s * b.sx + c * b.r0,
                        r1: c * b.r1 - s * b.sy,
                        tx: c * b.tx - s * b.ty,
                        ty: s * b.tx + c * b.ty);
                    inner.Append(Place(armSpriteId, 1 + i * 3, null, m));
                }
            }
            if (hasRing)
                inner.Append(Place(ringSpriteId, 15, null, Identity()));
            if (hasDot)
                inner.Append(Place(dotSpriteId, 20, "reticleCenterMC", Identity()));

            parts.Add(
                $"<item type=\"DefineSpriteTag\" forceWriteAsLong=\"true\" frameCount=\"1\" hasEndTag=\"true\" spriteId=\"{containerId}\">\n" +
                $"      <subTags>{inner}<item type=\"ShowFrameTag\" forceWriteAsLong=\"false\"/></subTags>\n" +
                "    </item>");
            return string.Join("\n    ", parts);
        }

        private static int SignedBits(int v)
        {
            if (v == 0) return 2;
            int n = 1;
            while (!(-(1L << (n - 1)) <= v && v <= (1L << (n - 1)) - 1)) n++;
            return Math.Max(n, 2);
        }
        private static int MaxBits(params int[] vals) => vals.Max(SignedBits);

        private static string StyleChange(int? moveX, int? moveY, int? fill)
        {
            bool hasMove = moveX.HasValue;
            int mb = hasMove ? Math.Max(SignedBits(moveX!.Value), SignedBits(moveY!.Value)) : 0;
            return "<item type=\"StyleChangeRecord\" fillStyle0=\"0\" fillStyle1=\"" + (fill ?? 0) +
                "\" lineStyle=\"0\" moveBits=\"" + mb +
                "\" moveDeltaX=\"" + (moveX ?? 0) + "\" moveDeltaY=\"" + (moveY ?? 0) +
                "\" numFillBits=\"0\" numLineBits=\"0\" stateFillStyle0=\"false\" stateFillStyle1=\"" +
                (fill != null).ToString().ToLowerInvariant() +
                "\" stateLineStyle=\"false\" stateMoveTo=\"" + hasMove.ToString().ToLowerInvariant() +
                "\" stateNewStyles=\"false\"/>";
        }

        private static string Straight(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return "";
            int nb = Math.Max(MaxBits(dx, dy) - 2, 0);
            bool general = dx != 0 && dy != 0;
            return "<item type=\"StraightEdgeRecord\" deltaX=\"" + dx + "\" deltaY=\"" + dy +
                "\" generalLineFlag=\"" + general.ToString().ToLowerInvariant() +
                "\" numBits=\"" + nb + "\" vertLineFlag=\"" + (!general && dx == 0).ToString().ToLowerInvariant() + "\"/>";
        }

        private static string Curved(int cdx, int cdy, int adx, int ady)
        {
            int nb = Math.Max(MaxBits(cdx, cdy, adx, ady) - 2, 0);
            return "<item type=\"CurvedEdgeRecord\" anchorDeltaX=\"" + adx + "\" anchorDeltaY=\"" + ady +
                "\" controlDeltaX=\"" + cdx + "\" controlDeltaY=\"" + cdy + "\" numBits=\"" + nb + "\"/>";
        }

        private static string RectPath(int x0, int y0, int x1, int y1, int fill) =>
            StyleChange(x0, y0, fill) +
            Straight(x1 - x0, 0) + Straight(0, y1 - y0) + Straight(x0 - x1, 0) + Straight(0, y0 - y1);

        private static string CirclePath(double r, int fill) => CirclePath(r, fill, +1);

        private static string CirclePath(double r, int? fill, int dir)
        {
            double C = r / Math.Cos(Math.PI / 8);
            var sb = new StringBuilder(StyleChange((int)Math.Round(r), 0, fill));
            int cx = (int)Math.Round(r), cy = 0;
            for (int k = 0; k < 8; k++)
            {
                double ca = dir * (45 * k + 22.5) * Math.PI / 180, pa = dir * 45.0 * (k + 1) * Math.PI / 180;
                int cdx = (int)Math.Round(C * Math.Cos(ca)) - cx, cdy = (int)Math.Round(C * Math.Sin(ca)) - cy;
                cx += cdx; cy += cdy;
                int px = k == 7 ? (int)Math.Round(r) : (int)Math.Round(r * Math.Cos(pa));
                int py = k == 7 ? 0 : (int)Math.Round(r * Math.Sin(pa));
                int adx = px - cx, ady = py - cy;
                cx += adx; cy += ady;
                sb.Append(Curved(cdx, cdy, adx, ady));
            }
            return sb.ToString();
        }

        private static string RingPath(double outerR, double innerR, int fill)
        {
            if (innerR < 1) return CirclePath(outerR, fill);
            return CirclePath(outerR, fill, +1) + CirclePath(innerR, null, -1);
        }

        private static string DefineShape3(int id, int xmin, int xmax, int ymin, int ymax,
            List<(int r, int g, int b, int a)> fills, string records)
        {
            int nb = MaxBits(xmin, xmax, ymin, ymax);
            var fillItems = string.Concat(fills.Select(f =>
                "<item type=\"FILLSTYLE\" bitmapId=\"0\" fillStyleType=\"0\"><color type=\"RGBA\" alpha=\"" + f.a +
                "\" blue=\"" + f.b + "\" green=\"" + f.g + "\" red=\"" + f.r + "\"/></item>"));
            int numFillBits = Math.Max(1, (int)Math.Ceiling(Math.Log2(fills.Count + 1)));
            return
                $"<item type=\"DefineShape3Tag\" forceWriteAsLong=\"true\" shapeId=\"{id}\">\n" +
                $"      <shapeBounds type=\"RECT\" Xmax=\"{xmax}\" Xmin=\"{xmin}\" Ymax=\"{ymax}\" Ymin=\"{ymin}\" nbits=\"{nb}\"/>\n" +
                $"      <shapes type=\"SHAPEWITHSTYLE\" numFillBits=\"{numFillBits}\" numLineBits=\"0\">\n" +
                $"        <fillStyles type=\"FILLSTYLEARRAY\"><fillStyles>{fillItems}</fillStyles></fillStyles>\n" +
                "        <lineStyles type=\"LINESTYLEARRAY\"><lineStyles/><lineStyles2/></lineStyles>\n" +
                $"        <shapeRecords>{records}<item type=\"EndShapeRecord\" endOfShape=\"0\"/></shapeRecords>\n" +
                "      </shapes>\n" +
                "    </item>";
        }

        private static (double sx, double sy, double r0, double r1, double tx, double ty) Identity()
            => (1, 1, 0, 0, 0, 0);

        private static string MatrixXml((double sx, double sy, double r0, double r1, double tx, double ty) m)
        {
            bool hasScale = !(Math.Abs(m.sx - 1) < 1e-9 && Math.Abs(m.sy - 1) < 1e-9);
            bool hasRotate = !(Math.Abs(m.r0) < 1e-9 && Math.Abs(m.r1) < 1e-9);
            string F(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);
            return "<matrix type=\"MATRIX\" hasRotate=\"" + hasRotate.ToString().ToLowerInvariant() +
                "\" hasScale=\"" + hasScale.ToString().ToLowerInvariant() +
                "\" nRotateBits=\"0\" nScaleBits=\"0\" nTranslateBits=\"0\" rotateSkew0=\"" + (hasRotate ? F(m.r0) : "0.0") +
                "\" rotateSkew1=\"" + (hasRotate ? F(m.r1) : "0.0") +
                "\" scaleX=\"" + (hasScale ? F(m.sx) : "0.0") + "\" scaleY=\"" + (hasScale ? F(m.sy) : "0.0") +
                "\" translateX=\"" + (int)Math.Round(m.tx) + "\" translateY=\"" + (int)Math.Round(m.ty) + "\"/>";
        }

        private static string Place(int charId, int depth, string? name,
            (double sx, double sy, double r0, double r1, double tx, double ty) m)
        {
            return "<item type=\"PlaceObject2Tag\" characterId=\"" + charId +
                "\" clipDepth=\"0\" depth=\"" + depth + "\" forceWriteAsLong=\"true\" " +
                (name != null ? "name=\"" + name + "\" " : "") +
                "placeFlagHasCharacter=\"true\" placeFlagHasClipActions=\"false\" placeFlagHasClipDepth=\"false\" " +
                "placeFlagHasColorTransform=\"false\" placeFlagHasMatrix=\"true\" placeFlagHasName=\"" +
                (name != null).ToString().ToLowerInvariant() +
                "\" placeFlagHasRatio=\"false\" placeFlagMove=\"false\" ratio=\"0\">\n      " +
                MatrixXml(m) + "\n    </item>";
        }

        private static string DefineSprite(int id, string inner) =>
            $"<item type=\"DefineSpriteTag\" forceWriteAsLong=\"true\" frameCount=\"1\" hasEndTag=\"true\" spriteId=\"{id}\">\n" +
            $"      <subTags>{inner}<item type=\"ShowFrameTag\" forceWriteAsLong=\"false\"/></subTags>\n" +
            "    </item>";

        private static readonly IReadOnlyDictionary<string, string> WeaponGroupIds =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["pistol"]  = "453432689",
                ["smg"]     = "1358501004",
                ["rifle"]   = "-1105352287",
                ["shotgun"] = "1234424921",
            };

        private static string? ApplyScriptEdits(ref string s, ReticleBuildSpec spec,
            IReadOnlyDictionary<string, ReticleBuildSpec>? weaponSpecs = null)
        {
            var (mainR, mainG, mainB) = ParseHex(spec.ColorMain, (255, 255, 255));
            int alpha = spec.Opacity;
            string main = $"{mainR},{mainG},{mainB}";

            bool colourDone = false;
            s = new Regex("this\\.DEFAULT_COLOUR = \\[\\d+,\\d+,\\d+,\\d+\\];").Replace(s, _ =>
            {
                colourDone = true;
                return $"this.DEFAULT_COLOUR = [{main},{alpha}];";
            }, 1);
            if (!colourDone) return Loc.T("error.defaultColourNotFound");

            var (adsR, adsG, adsB) = ParseHex(spec.ColorAds, (255, 28, 33));
            string ads = $"{adsR},{adsG},{adsB}";

            s = Regex.Replace(s,
                @"(this\.reticleMC\.reticle\.gotoAndStop\(2\);\s*[\r\n]+\s*com\.rockstargames\.ui\.utils\.Colour\.Colourise\(this\.reticleMC\.reticle,)\d+,\d+,\d+,\d+(\))",
                m => m.Groups[1].Value + ads + "," + alpha + m.Groups[2].Value);

            var stylesM = Regex.Match(s, @"this\.STYLES = \[([^\]]*)\];");
            if (stylesM.Success)
            {
                var parts = stylesM.Groups[1].Value.Split(',');
                if (parts.Length > 1)
                {
                    var styleVar = parts[1].Trim();
                    if (Regex.IsMatch(styleVar, @"^_loc\d+_$"))
                        s = new Regex(@"(var\s+" + Regex.Escape(styleVar) + @"\s*=\s*\[)\d+,\d+,\d+,\d+(\];)")
                            .Replace(s, m => m.Groups[1].Value + ads + "," + alpha + m.Groups[2].Value, 1);
                }
            }

            int remoteIdx = s.IndexOf("if(this.IsUsingRemotePlay)", StringComparison.Ordinal);
            var dotRe = new Regex(@"this\.dotScaler = \d+ \* (_loc\d+_);");
            bool dotDone = false;
            if (remoteIdx >= 0)
            {
                int windowEnd = Math.Min(s.Length, remoteIdx + 400);
                var window = s.Substring(remoteIdx, windowEnd - remoteIdx);
                var hits = dotRe.Matches(window);
                if (hits.Count >= 2)
                {
                    int remoteScaled = (int)Math.Round(spec.Scale * 1.5);
                    var patched = window;
                    patched = patched.Remove(hits[1].Index, hits[1].Length)
                                     .Insert(hits[1].Index, $"this.dotScaler = {spec.Scale} * {hits[1].Groups[1].Value};");
                    patched = patched.Remove(hits[0].Index, hits[0].Length)
                                     .Insert(hits[0].Index, $"this.dotScaler = {remoteScaled} * {hits[0].Groups[1].Value};");
                    s = s.Substring(0, remoteIdx) + patched + s.Substring(windowEnd);
                    dotDone = true;
                }
            }
            if (!dotDone)
            {
                s = dotRe.Replace(s, m => $"this.dotScaler = {spec.Scale} * {m.Groups[1].Value};");
            }

            int fnStart = s.IndexOf("function SET_RETICLE_VISIBLE(params)", StringComparison.Ordinal);
            if (fnStart < 0) return Loc.T("error.setReticleVisibleNotFound");
            int bodyStart = s.IndexOf('{', fnStart);
            if (bodyStart < 0) return Loc.T("error.setReticleVisibleBodyNotFound");
            int depth = 0, i = bodyStart;
            for (; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}') { depth--; if (depth == 0) break; }
            }
            if (depth != 0) return Loc.T("error.setReticleVisibleUnbalanced");

            string replacement = spec.Permanent
                ? "function SET_RETICLE_VISIBLE(params)\n   {\n" +
                  "      var _loc2_ = params[0];\n" +
                  "      this.CONTENT._visible = true;\n" +
                  "      this.hitmarker.gotoAndStop(0);\n" +
                  "      this.reticleMC.hitmarkerMC.gotoAndStop(0);\n   }"
                : "function SET_RETICLE_VISIBLE(params)\n   {\n" +
                  "      var _loc2_ = params[0];\n" +
                  "      this.CONTENT._visible = _loc2_;\n" +
                  "      this.hitmarker.gotoAndStop(0);\n" +
                  "      this.reticleMC.hitmarkerMC.gotoAndStop(0);\n   }";
            s = s.Substring(0, fnStart) + replacement + s.Substring(i + 1);

            if (spec.Permanent)
            {
                s = ForceVisibleTrueIn(s, "function READY(id)");
                s = ForceVisibleTrueIn(s, "function LOAD_RETICLE()");
                s = ForceVisibleTrueIn(s, "function HUD_RETICLE()");
                s = ForceVisibleTrueIn(s, "function RESET_RETICLE()");
                s = ForceVisibleTrueIn(s, "function RESET_RETICLE_EXTERNAL()");
                s = ForceVisibleTrueIn(s, "function REMOVE()");
            }
            else
            {
                s = SetVisibleFalseIn(s, "function READY(id)");
                s = SetVisibleFalseIn(s, "function LOAD_RETICLE()");
                s = StripForcedVisibleIn(s, "function HUD_RETICLE()");
                s = StripForcedVisibleIn(s, "function RESET_RETICLE()");
                s = StripForcedVisibleIn(s, "function RESET_RETICLE_EXTERNAL()");
                s = StripForcedVisibleIn(s, "function REMOVE()");
            }

            var groupColours = new List<string>();
            if (weaponSpecs != null)
            {
                foreach (var kv in weaponSpecs)
                {
                    if (!WeaponGroupIds.TryGetValue(kv.Key, out var wid)) continue;
                    if (string.IsNullOrWhiteSpace(kv.Value.ColorMain)) continue;
                    if (string.Equals(kv.Value.ColorMain, spec.ColorMain, StringComparison.OrdinalIgnoreCase)) continue;
                    var (r, g, b) = ParseHex(kv.Value.ColorMain, (mainR, mainG, mainB));
                    groupColours.Add($"      this.MG_GROUP_COLOURS[\"{wid}\"] = [{r},{g},{b},{alpha}];");
                }
            }

            const string mgMarker = "function MG_APPLY_GROUP_COLOUR()";
            int mgOld = s.IndexOf(mgMarker, StringComparison.Ordinal);
            if (mgOld >= 0)
            {
                var (ob, oe) = FindFnBody(s, mgMarker);
                if (ob > 0) s = s.Substring(0, mgOld) + s.Substring(oe + 1);
            }
            s = new Regex(@"\r?\n\s*this\.MG_APPLY_GROUP_COLOUR\(\);").Replace(s, "");
            s = new Regex(@"\r?\n\s*this\.MG_GROUP_COLOURS = \{\};(\r?\n\s*this\.MG_GROUP_COLOURS\[""-?\d+""\] = \[[^\]]*\];)*").Replace(s, "");
            s = new Regex(@"\r?\n\s*this\.MG_BASE_COLOUR = \[[^\]]*\];").Replace(s, "");

            string mgFn =
                "function MG_APPLY_GROUP_COLOUR()\n   {\n" +
                "      var _mgc = this.MG_GROUP_COLOURS[String(this.weaponID)];\n" +
                "      if(_mgc == undefined)\n      {\n" +
                "         _mgc = this.MG_BASE_COLOUR;\n      }\n" +
                "      if(_mgc != undefined)\n      {\n" +
                "         this.DEFAULT_COLOUR = _mgc;\n" +
                "         if(typeof this.reticleMC.reticle == \"movieclip\")\n" +
                "         {\n" +
                "            com.rockstargames.ui.utils.Colour.Colourise(this.reticleMC.reticle,_mgc[0],_mgc[1],_mgc[2],_mgc[3]);\n" +
                "         }\n      }\n   }\n   ";

            int typeFn = s.IndexOf("function SET_RETICLE_TYPE(params)", StringComparison.Ordinal);
            if (typeFn < 0)
            {
                if (groupColours.Count == 0) return null;
                return Loc.T("error.setReticleTypeNotFound");
            }
            s = s.Substring(0, typeFn) + mgFn + s.Substring(typeFn);

            string table = $"\n      this.MG_BASE_COLOUR = [{main},{alpha}];"
                         + "\n      this.MG_GROUP_COLOURS = {};"
                         + (groupColours.Count > 0 ? "\n" + string.Join("\n", groupColours) : "");
            var ctorAnchor = new Regex(Regex.Escape($"this.DEFAULT_COLOUR = [{main},{alpha}];"));
            s = ctorAnchor.Replace(s, m => m.Value + table, 1);

            var (typeBodyStart, typeBodyEnd) = FindFnBody(s, "function SET_RETICLE_TYPE(params)");
            if (typeBodyStart < 0)
            {
                if (groupColours.Count == 0) return null;
                return Loc.T("error.setReticleTypeBodyNotFound");
            }
            var typeBody = s.Substring(typeBodyStart, typeBodyEnd - typeBodyStart + 1);
            var gotoRe = new Regex(@"this\.reticleMC\.gotoAndStop\([^)]*\);");
            var gm = gotoRe.Match(typeBody);
            if (!gm.Success)
            {
                if (groupColours.Count == 0) return null;
                return Loc.T("error.weaponFrameSwitchNotFound");
            }
            int insertAt = typeBodyStart + gm.Index + gm.Length;
            s = s.Substring(0, insertAt)
              + "\n      this.MG_APPLY_GROUP_COLOUR();"
              + s.Substring(insertAt);

            var (lockStart, lockEnd) = FindFnBody(s, "function SET_RETICLE_LOCKON_TYPE(params)");
            if (lockStart >= 0)
            {
                var lockBody = s.Substring(lockStart, lockEnd - lockStart + 1);
                var whiteRe = new Regex(@"com\.rockstargames\.ui\.utils\.Colour\.Colourise\(this\.reticleMC\.reticle,255,255,255,\d+\);");
                var patchedLock = whiteRe.Replace(lockBody,
                    m => m.Value + "\n            this.MG_APPLY_GROUP_COLOUR();");
                if (!ReferenceEquals(patchedLock, lockBody))
                    s = s.Substring(0, lockStart) + patchedLock + s.Substring(lockEnd + 1);
            }

            return null;
        }

        private static string SetVisibleFalseIn(string s, string fnSignature)
        {
            var (bodyStart, bodyEnd) = FindFnBody(s, fnSignature);
            if (bodyStart < 0) return s;
            string body = s.Substring(bodyStart, bodyEnd - bodyStart + 1)
                .Replace("this.CONTENT._visible = true;", "this.CONTENT._visible = false;");
            return s.Substring(0, bodyStart) + body + s.Substring(bodyEnd + 1);
        }

        private static string StripForcedVisibleIn(string s, string fnSignature)
        {
            var (bodyStart, bodyEnd) = FindFnBody(s, fnSignature);
            if (bodyStart < 0) return s;
            string body = s.Substring(bodyStart, bodyEnd - bodyStart + 1);
            string fixedBody = Regex.Replace(body, "[ \\t]*this\\.CONTENT\\._visible = true;\\r?\\n?", "");
            return s.Substring(0, bodyStart) + fixedBody + s.Substring(bodyEnd + 1);
        }

        private static (int start, int end) FindFnBody(string s, string fnSignature)
        {
            int fnStart = s.IndexOf(fnSignature, StringComparison.Ordinal);
            if (fnStart < 0) return (-1, -1);
            int bodyStart = s.IndexOf('{', fnStart);
            if (bodyStart < 0) return (-1, -1);
            int depth = 0, i = bodyStart;
            for (; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}') { depth--; if (depth == 0) break; }
            }
            return depth == 0 ? (bodyStart, i) : (-1, -1);
        }

        private static string ForceVisibleTrueIn(string s, string fnSignature)
        {
            int fnStart = s.IndexOf(fnSignature, StringComparison.Ordinal);
            if (fnStart < 0) return s;
            int bodyStart = s.IndexOf('{', fnStart);
            if (bodyStart < 0) return s;
            int depth = 0, i = bodyStart;
            for (; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}') { depth--; if (depth == 0) break; }
            }
            if (depth != 0) return s;
            string body = s.Substring(bodyStart, i - bodyStart + 1);
            string fixedBody = body.Contains("this.CONTENT._visible")
                ? body.Replace("this.CONTENT._visible = false;", "this.CONTENT._visible = true;")
                : body.Substring(0, body.Length - 1) + "   this.CONTENT._visible = true;\n   }";
            return s.Substring(0, bodyStart) + fixedBody + s.Substring(i + 1);
        }

        private static (int r, int g, int b) ParseHex(string? hex, (int r, int g, int b) fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var h = hex.TrimStart('#');
            if (h.Length != 6) return fallback;
            try
            {
                return (Convert.ToInt32(h.Substring(0, 2), 16),
                        Convert.ToInt32(h.Substring(2, 2), 16),
                        Convert.ToInt32(h.Substring(4, 2), 16));
            }
            catch { return fallback; }
        }

        private static void SwapGfxHeader(string filePath, bool toSwf)
        {
            if (!File.Exists(filePath)) return;
            byte[] header = new byte[3];
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);
            if (stream.Length < 3) return;
            stream.Read(header, 0, 3);
            if (toSwf)
            {
                if (header[0] == 0x43 && header[1] == 0x46 && header[2] == 0x58)
                { stream.Position = 1; stream.WriteByte(0x57); stream.WriteByte(0x53); }
                else if (header[0] == 0x47 && header[1] == 0x46 && header[2] == 0x58)
                { stream.Position = 0; stream.WriteByte(0x46); stream.WriteByte(0x57); stream.WriteByte(0x53); }
            }
            else
            {
                if (header[0] == 0x43 && header[1] == 0x57 && header[2] == 0x53)
                { stream.Position = 1; stream.WriteByte(0x46); stream.WriteByte(0x58); }
                else if (header[0] == 0x46 && header[1] == 0x57 && header[2] == 0x53)
                { stream.Position = 0; stream.WriteByte(0x47); stream.WriteByte(0x46); stream.WriteByte(0x58); }
            }
        }

        private static void RunProcess(string fileName, string arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                }
            };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Console.WriteLine($"   [ReticleTool] {e.Data}"); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Console.WriteLine($"   [ReticleTool Err] {e.Data}"); };
            process.Start();
            try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(180_000))
            {
                Console.WriteLine("[ReticleBuilder] tool timed out after 180s - killing.");
                try { process.Kill(true); } catch { }
            }
        }
    }
}
