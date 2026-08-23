#nullable disable
using System;
using System.Linq;
using CodeWalker.GameFiles;

namespace MiamiGraphics.Core.Services
{

    public static class YtdRenamer
    {

        public static YtdRenameResult RenameInnerTexture(
            byte[] ytdBytes,
            string oldName,
            string newName)
        {
            if (ytdBytes == null || ytdBytes.Length == 0)
                return YtdRenameResult.Fail("empty input bytes");
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
                return YtdRenameResult.Fail("oldName / newName required");
            if (string.Equals(oldName, newName, StringComparison.Ordinal))
                return YtdRenameResult.Skip(ytdBytes, "old==new - nothing to rename");

            YtdFile ytd;
            try
            {
                ytd = new YtdFile();
                ytd.Load(ytdBytes);
            }
            catch (Exception ex)
            {
                return YtdRenameResult.Fail($"YTD parse failed: {ex.GetType().Name}: {ex.Message}");
            }

            var items = ytd.TextureDict?.Textures?.data_items;
            if (items == null || items.Length == 0)
                return YtdRenameResult.Fail("YTD has no textures");

            Texture target = null;
            foreach (var t in items)
            {
                if (t == null) continue;
                if (string.Equals(t.Name, oldName, StringComparison.OrdinalIgnoreCase))
                {
                    target = t;
                    break;
                }
            }
            if (target == null)
            {
                var available = string.Join(", ", items.Where(t => t != null).Select(t => t.Name));
                return YtdRenameResult.Fail(
                    $"texture '{oldName}' not found inside YTD. Available: {available}");
            }

            target.Name = newName;
            target.NameHash = JenkHash.GenHash(newName.ToLowerInvariant());

            try
            {
                ytd.TextureDict.BuildFromTextureList(items.ToList());
            }
            catch (Exception ex)
            {
                return YtdRenameResult.Fail($"BuildFromTextureList failed: {ex.GetType().Name}: {ex.Message}");
            }

            byte[] outBytes;
            try
            {
                outBytes = ytd.Save();
            }
            catch (Exception ex)
            {
                return YtdRenameResult.Fail($"YtdFile.Save failed: {ex.GetType().Name}: {ex.Message}");
            }
            if (outBytes == null || outBytes.Length == 0)
                return YtdRenameResult.Fail("YtdFile.Save produced empty bytes");

            return YtdRenameResult.Ok(outBytes);
        }
    }

    public sealed class YtdRenameResult
    {
        public bool   Success      { get; private set; }
        public byte[] OutputBytes  { get; private set; }
        public string ErrorMessage { get; private set; }
        public bool   WasNoOp      { get; private set; }

        public static YtdRenameResult Ok(byte[] bytes)
            => new YtdRenameResult { Success = true, OutputBytes = bytes };
        public static YtdRenameResult Fail(string msg)
            => new YtdRenameResult { Success = false, ErrorMessage = msg };
        public static YtdRenameResult Skip(byte[] bytes, string msg)
            => new YtdRenameResult { Success = true, OutputBytes = bytes, WasNoOp = true, ErrorMessage = msg };
    }
}
