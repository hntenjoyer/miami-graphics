using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MiamiGraphics.Core.Services
{
    public static class MinimapFontRetarget
    {
        private static readonly string[] FieldVars = { "distanceToSurfaceTF", "distanceToFloorTF" };

        public static byte[] Apply(byte[] gfx, string? fontId, List<string> notes)
        {
            var entry = MinimapFontCatalog.Find(fontId);
            if (entry is null) return gfx;
            var symbol = entry.Symbol;

            if (gfx.Length < 9 || gfx[0] != (byte)'G' || gfx[1] != (byte)'F' || gfx[2] != (byte)'X')
            {
                notes.Add("[Минимапа] Шрифт: файл не несжатый GFX - пропущено.");
                return gfx;
            }

            var body = gfx.Skip(8).ToArray();
            List<Tag> tags;
            try { tags = ParseTags(body); }
            catch (Exception ex) { notes.Add($"[Минимапа] Шрифт: разбор тегов не удался ({ex.Message})."); return gfx; }

            var imported = CollectImportedFonts(body, tags);
            var embedded = CollectEmbeddedCatalogFonts(body, tags);
            byte[]? inject = null;
            if (!imported.TryGetValue(symbol, out int targetId))
            {
                var prev = embedded.FirstOrDefault(kv => string.Equals(kv.Value.Id, entry.Id, StringComparison.Ordinal));
                if (prev.Value is not null)
                {
                    targetId = prev.Key;
                }
                else
                {
                    var blob = MinimapFontCatalog.LoadBlob(entry);
                    if (blob is null || blob.Length < 16)
                    {
                        notes.Add($"[Минимапа] Шрифт {symbol}: этой миникартой не импортирован и вшить нечего - оставлен прежний.");
                        return gfx;
                    }
                    targetId = FreeCharacterId(body, tags);
                    inject = BuildCompactedFontTag(targetId, blob);
                }
            }

            var idToEntry = new Dictionary<int, MinimapFontCatalog.Entry>();
            foreach (var kv in imported)
            {
                var e = MinimapFontCatalog.Available().FirstOrDefault(x =>
                    string.Equals(x.Symbol, kv.Key, StringComparison.Ordinal));
                if (e is not null) idToEntry[kv.Value] = e;
            }
            foreach (var kv in embedded) idToEntry[kv.Key] = kv.Value;

            var edits = FindFieldFontOffsets(body, tags);
            if (edits.Count == 0)
            {
                notes.Add("[Минимапа] Шрифт: поля цифр (distanceToSurfaceTF/FloorTF) не найдены - пропущено.");
                return gfx;
            }

            if (edits.All(e => (body[e.FontIdOff] | (body[e.FontIdOff + 1] << 8)) == targetId))
            {
                notes.Add($"[Минимапа] Шрифт: {symbol} уже стоит (id {targetId}) - повторный прогон пропущен.");
                return gfx;
            }

            int insertAt = inject is null ? 0 : HeaderEnd(tags);
            int shift = inject?.Length ?? 0;

            var result = new List<byte>(gfx.Length + shift + 16);
            result.AddRange(gfx.Take(8));
            if (inject is null)
            {
                result.AddRange(body);
            }
            else
            {
                result.AddRange(body.Take(insertAt));
                result.AddRange(inject);
                result.AddRange(body.Skip(insertAt));
            }

            double hScale = MinimapFontCatalog.HeightScale(entry);
            int skipped = 0;
            foreach (var e in edits)
            {
                int fidAbs = 8 + e.FontIdOff + (e.FontIdOff >= insertAt ? shift : 0);
                int curFont = result[fidAbs] | (result[fidAbs + 1] << 8);
                if (curFont == targetId) { skipped++; continue; }
                result[fidAbs]     = (byte)(targetId & 0xFF);
                result[fidAbs + 1] = (byte)((targetId >> 8) & 0xFF);

                if (e.HeightOff >= 0)
                {
                    double prevScale = idToEntry.TryGetValue(curFont, out var prevEntry)
                        ? MinimapFontCatalog.HeightScale(prevEntry)
                        : 1.0;
                    int hAbs = 8 + e.HeightOff + (e.HeightOff >= insertAt ? shift : 0);
                    int cur = result[hAbs] | (result[hAbs + 1] << 8);
                    int nh  = Math.Clamp((int)Math.Round(cur / prevScale * hScale), 1, 0xFFFF);
                    if (nh != cur)
                    {
                        result[hAbs]     = (byte)(nh & 0xFF);
                        result[hAbs + 1] = (byte)((nh >> 8) & 0xFF);
                    }
                }
            }
            bool rescale = Math.Abs(hScale - 1.0) > 1e-6;

            int total = result.Count;
            result[4] = (byte)(total & 0xFF);
            result[5] = (byte)((total >> 8) & 0xFF);
            result[6] = (byte)((total >> 16) & 0xFF);
            result[7] = (byte)((total >> 24) & 0xFF);

            string capNote = rescale
                ? $", кегль ×{hScale:0.###} под метрики шрифта"
                : "";
            string skipNote = skipped > 0 ? $" (ещё {skipped} уже стояло)" : "";
            notes.Add(inject is null
                ? $"[Минимапа] Шрифт: {symbol} (id {targetId}), полей изменено: {edits.Count - skipped}{skipNote}{capNote}."
                : $"[Минимапа] Шрифт: {symbol} вшит тегом 1005 (id {targetId}, {inject.Length:N0} б), полей изменено: {edits.Count - skipped}{skipNote}{capNote}.");
            return result.ToArray();
        }

        private static int HeaderEnd(List<Tag> tags)
        {
            var headerCodes = new HashSet<int> { 69, 77, 1000, 9, 24, 8 };
            foreach (var t in tags)
                if (!headerCodes.Contains(t.Code))
                    return t.TagStart;
            return tags.Count > 0 ? tags[^1].TagStart : 0;
        }

        private static int FreeCharacterId(byte[] body, List<Tag> tags)
        {
            var used = CollectUsedIds(body, tags);
            int id = 9100;
            while (used.Contains(id)) id++;
            return id;
        }

        private static byte[] BuildCompactedFontTag(int id, byte[] blob)
        {
            int len = blob.Length + 2;
            var tag = new List<byte>(len + 6);
            int cl = (1005 << 6) | 0x3F;
            tag.Add((byte)(cl & 0xFF)); tag.Add((byte)((cl >> 8) & 0xFF));
            tag.Add((byte)(len & 0xFF));
            tag.Add((byte)((len >> 8) & 0xFF));
            tag.Add((byte)((len >> 16) & 0xFF));
            tag.Add((byte)((len >> 24) & 0xFF));
            tag.Add((byte)(id & 0xFF)); tag.Add((byte)((id >> 8) & 0xFF));
            tag.AddRange(blob);
            return tag.ToArray();
        }

        private sealed record Tag(int Code, int TagStart, int DataStart, int Len);

        private readonly record struct FieldEdit(int FontIdOff, int HeightOff);

        private static int SkipRect(byte[] b, int i) => i + ((5 + (b[i] >> 3) * 4 + 7) / 8);

        private static List<Tag> ParseTags(byte[] body)
        {
            var res = new List<Tag>();
            int i = SkipRect(body, 0) + 4;
            while (i + 2 <= body.Length)
            {
                int start = i;
                int cl = body[i] | (body[i + 1] << 8); i += 2;
                int code = cl >> 6, len = cl & 0x3F;
                if (len == 0x3F)
                {
                    len = body[i] | (body[i + 1] << 8) | (body[i + 2] << 16) | (body[i + 3] << 24);
                    i += 4;
                }
                res.Add(new Tag(code, start, i, len));
                i += len;
                if (code == 0) break;
            }
            return res;
        }

        private static Dictionary<string, int> CollectImportedFonts(byte[] body, List<Tag> tags)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var t in tags)
            {
                if (t.Code != 71 || t.Len < 5) continue;
                int o = t.DataStart, end = t.DataStart + t.Len;
                while (o < end && body[o] != 0) o++;
                o += 1 + 2;
                if (o + 2 > end) continue;
                int cnt = body[o] | (body[o + 1] << 8); o += 2;
                for (int k = 0; k < cnt && o + 2 <= end; k++)
                {
                    int cid = body[o] | (body[o + 1] << 8); o += 2;
                    int s = o;
                    while (o < end && body[o] != 0) o++;
                    var name = Encoding.ASCII.GetString(body, s, o - s);
                    o++;
                    if (!string.IsNullOrEmpty(name)) map[name] = cid;
                }
            }
            return map;
        }

        private static Dictionary<int, MinimapFontCatalog.Entry> CollectEmbeddedCatalogFonts(byte[] body, List<Tag> tags)
        {
            var map = new Dictionary<int, MinimapFontCatalog.Entry>();
            foreach (var t in tags)
            {
                if (t.Code != 1005 || t.Len < 4) continue;
                int id = body[t.DataStart] | (body[t.DataStart + 1] << 8);
                foreach (var entry in MinimapFontCatalog.Available())
                {
                    if (string.IsNullOrEmpty(entry.Blob)) continue;
                    var blob = MinimapFontCatalog.LoadBlob(entry);
                    if (blob is null || blob.Length != t.Len - 2) continue;
                    bool same = true;
                    for (int i = 0; i < blob.Length; i++)
                        if (body[t.DataStart + 2 + i] != blob[i]) { same = false; break; }
                    if (same) { map[id] = entry; break; }
                }
            }
            return map;
        }

        private static readonly HashSet<int> DefineTags = new()
        {
            2,4,6,7,10,11,13,14,17,20,21,22,32,33,35,36,37,39,46,48,
            60,63,75,78,83,84,90,91,1003,1005,1006,1008,1010,1011
        };

        private static HashSet<int> CollectUsedIds(byte[] body, List<Tag> tags)
        {
            var ids = new HashSet<int>();
            foreach (var t in tags)
                if (DefineTags.Contains(t.Code) && t.Len >= 2)
                    ids.Add(body[t.DataStart] | (body[t.DataStart + 1] << 8));
            foreach (var kv in CollectImportedFonts(body, tags)) ids.Add(kv.Value);
            return ids;
        }

        private static List<FieldEdit> FindFieldFontOffsets(byte[] body, List<Tag> tags)
        {
            var needles = FieldVars.Select(v => Encoding.ASCII.GetBytes(v)).ToArray();

            var wanted = new HashSet<int>();
            foreach (var t in tags)
            {
                if (t.Code != 39 || t.Len < 4) continue;
                foreach (var inner in ParseRange(body, t.DataStart + 4, t.DataStart + t.Len))
                {
                    if (inner.Code != 26 && inner.Code != 70) continue;
                    if (!needles.Any(n => Contains(body, inner.DataStart, inner.Len, n))) continue;

                    int flagLo = body[inner.DataStart];
                    if ((flagLo & 0x02) == 0) continue;
                    int cidOff = inner.DataStart + (inner.Code == 70 ? 4 : 3);
                    if (cidOff + 2 > inner.DataStart + inner.Len) continue;
                    wanted.Add(body[cidOff] | (body[cidOff + 1] << 8));
                }
            }
            if (wanted.Count == 0) return new List<FieldEdit>();

            var res = new List<FieldEdit>();
            foreach (var t in tags)
            {
                if (t.Code != 37 || t.Len < 8) continue;
                int cid = body[t.DataStart] | (body[t.DataStart + 1] << 8);
                if (!wanted.Contains(cid)) continue;

                int o = SkipRect(body, t.DataStart + 2);
                int flags = body[o] | (body[o + 1] << 8); o += 2;
                if ((flags & 0x0001) == 0) continue;
                int heightOff = (flags & 0x8000) == 0 ? o + 2 : -1;
                res.Add(new FieldEdit(o, heightOff));
            }
            return res;
        }

        private static List<Tag> ParseRange(byte[] body, int from, int to)
        {
            var res = new List<Tag>();
            int i = from;
            while (i + 2 <= to)
            {
                int start = i;
                int cl = body[i] | (body[i + 1] << 8); i += 2;
                int code = cl >> 6, len = cl & 0x3F;
                if (len == 0x3F)
                {
                    if (i + 4 > to) break;
                    len = body[i] | (body[i + 1] << 8) | (body[i + 2] << 16) | (body[i + 3] << 24);
                    i += 4;
                }
                if (i + len > to) break;
                res.Add(new Tag(code, start, i, len));
                i += len;
                if (code == 0) break;
            }
            return res;
        }

        private static bool Contains(byte[] hay, int start, int len, byte[] needle)
        {
            int end = start + len - needle.Length;
            for (int i = start; i <= end; i++)
            {
                int k = 0;
                while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) return true;
            }
            return false;
        }

    }
}
