#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace MiamiGraphics.Core.Services
{
    public static class GraphicsYtdMerger
    {
        public sealed class MergeResult
        {
            public byte[] Bytes { get; init; }
            public List<string> Applied { get; init; } = new();
            public List<string> Missing { get; init; } = new();
            public string Error { get; init; }
            public bool Success => Bytes != null && Error == null;
        }

        public static MergeResult Overlay(byte[] baseYtd, byte[] overlayYtd, IReadOnlyList<string> textureNames)
        {
            if (baseYtd == null || baseYtd.Length == 0) return new MergeResult { Error = "base ytd empty" };
            if (overlayYtd == null || overlayYtd.Length == 0) return new MergeResult { Error = "overlay ytd empty" };

            YtdFile baseFile, overlayFile;
            try { baseFile = new YtdFile(); baseFile.Load(baseYtd); }
            catch (Exception ex) { return new MergeResult { Error = $"base parse: {ex.Message}" }; }
            try { overlayFile = new YtdFile(); overlayFile.Load(overlayYtd); }
            catch (Exception ex) { return new MergeResult { Error = $"overlay parse: {ex.Message}" }; }

            var baseItems = baseFile.TextureDict?.Textures?.data_items?.Where(t => t != null).ToList();
            if (baseItems == null || baseItems.Count == 0) return new MergeResult { Error = "base has no textures" };
            var overlayItems = overlayFile.TextureDict?.Textures?.data_items?.Where(t => t != null).ToArray()
                               ?? Array.Empty<Texture>();

            var applied = new List<string>();
            var missing = new List<string>();
            foreach (var name in textureNames)
            {
                var src = overlayItems.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                if (src == null) { missing.Add(name); continue; }
                var idx = baseItems.FindIndex(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) baseItems[idx] = src;
                else baseItems.Add(src);
                applied.Add(name);
            }

            if (applied.Count == 0)
                return new MergeResult { Error = $"no overlay textures matched ({string.Join(",", missing)})", Missing = missing };

            try { baseFile.TextureDict.BuildFromTextureList(baseItems); }
            catch (Exception ex) { return new MergeResult { Error = $"rebuild: {ex.Message}", Applied = applied, Missing = missing }; }

            byte[] outBytes;
            try { outBytes = baseFile.Save(); }
            catch (Exception ex) { return new MergeResult { Error = $"save: {ex.Message}", Applied = applied, Missing = missing }; }
            if (outBytes == null || outBytes.Length == 0)
                return new MergeResult { Error = "save produced empty bytes", Applied = applied, Missing = missing };

            return new MergeResult { Bytes = outBytes, Applied = applied, Missing = missing };
        }
    }
}
