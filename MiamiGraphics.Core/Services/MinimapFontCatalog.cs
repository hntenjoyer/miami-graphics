using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.Services
{
    public static class MinimapFontCatalog
    {
        public sealed record Entry(string Id, string Title, string Symbol, string Face, string? Blob = null, int Cap = StockCap);

        public const string StockId = "stock";

        public const int StockCap = 892;

        public static double HeightScale(Entry entry) =>
            entry.Cap > 0 ? (double)StockCap / entry.Cap : 1.0;

        private static readonly Entry[] All =
        {
            new("chalet",    "minimap.fontChalet",    "$Font2",             "Chalet-LondonNineteenSixty", Cap: 980),
            new("fixednum",  "minimap.fontFixedNum",  "$FixedWidthNumbers", "ChaletLondonNineteenSixtyNumbers", "FixedWidthNumbers", 980),

            new("pricedown", "minimap.fontPricedown",  "$gtaCash",           "PricedownGTAVInt",       "gtaCash", 808),
            new("script",    "minimap.fontScript",     "$Font5",             "SignPainter-HouseScript", "Font5", 780),
            new("tag",       "minimap.fontTag",        "$RockstarTAG",       "RockstarTAG",             "RockstarTAG", 892),
        };

        public static IReadOnlyList<Entry> Available()
            => All.Select(e => e with { Title = Loc.T(e.Title) }).ToList();

        public static Entry? Find(string? id)
        {
            if (string.IsNullOrWhiteSpace(id) ||
                string.Equals(id, StockId, StringComparison.OrdinalIgnoreCase))
                return null;
            return All.FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static string? ResolveSymbol(string? id) => Find(id)?.Symbol;

        private static readonly ConcurrentDictionary<string, byte[]?> BlobCache = new(StringComparer.Ordinal);

        public static byte[]? LoadBlob(Entry entry)
        {
            if (string.IsNullOrEmpty(entry.Blob)) return null;
            return BlobCache.GetOrAdd(entry.Blob!, static name =>
            {
                var asm = typeof(MinimapFontCatalog).Assembly;
                var res = asm.GetManifestResourceNames()
                             .FirstOrDefault(n => n.EndsWith("minimapfonts." + name + ".bin",
                                                             StringComparison.OrdinalIgnoreCase));
                if (res is null) return null;
                using var s = asm.GetManifestResourceStream(res);
                if (s is null) return null;
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            });
        }
    }
}
