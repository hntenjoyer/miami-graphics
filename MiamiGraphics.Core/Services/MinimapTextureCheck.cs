using System;
using System.Collections.Generic;

namespace MiamiGraphics.Core.Services
{
    public static class MinimapTextureCheck
    {
        private static readonly HashSet<int> EmbeddedBitmapTags = new() { 6, 20, 21, 35, 36, 90 };

        private static readonly HashSet<int> ExternalImageTags = new() { 1001, 1009 };

        public readonly record struct Result(int Embedded, int External)
        {
            public bool HasOwnArt => Embedded > 0;
        }

        public static Result Inspect(byte[] gfx)
        {
            if (gfx is null || gfx.Length < 16) return default;
            if (gfx[0] != (byte)'G' || gfx[1] != (byte)'F' || gfx[2] != (byte)'X') return default;

            int embedded = 0, external = 0;
            try
            {
                var body = new ReadOnlySpan<byte>(gfx, 8, gfx.Length - 8);
                int i = ((5 + (body[0] >> 3) * 4 + 7) / 8) + 4;
                while (i + 2 <= body.Length)
                {
                    int cl = body[i] | (body[i + 1] << 8); i += 2;
                    int code = cl >> 6, len = cl & 0x3F;
                    if (len == 0x3F)
                    {
                        if (i + 4 > body.Length) break;
                        len = body[i] | (body[i + 1] << 8) | (body[i + 2] << 16) | (body[i + 3] << 24);
                        i += 4;
                    }
                    if (len < 0 || i + len > body.Length) break;
                    if (EmbeddedBitmapTags.Contains(code)) embedded++;
                    else if (ExternalImageTags.Contains(code)) external++;
                    i += len;
                    if (code == 0) break;
                }
            }
            catch { return default; }

            return new Result(embedded, external);
        }

        public static string Describe(byte[] gfx)
        {
            var r = Inspect(gfx);
            return r.HasOwnArt
                ? $"своих растров: {r.Embedded}, внешних ссылок: {r.External}"
                : $"своей графики нет (только стоковые элементы), внешних ссылок: {r.External}";
        }
    }
}
