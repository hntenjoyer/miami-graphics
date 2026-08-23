#nullable disable
using CodeWalker.GameFiles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MiamiGraphics.Core.Services
{

    public static class YddToGltfConverter
    {
        private static void Log(string s) => Console.WriteLine("[YDD→GLB] " + s);

        public static Task<bool> ConvertAsync(string yddPath, string outputPath)
            => Task.Run(() => ConvertCore(yddPath, outputPath));

        public static Task<bool> ConvertBytesAsync(byte[] yddBytes, string outputPath)
            => Task.Run(() => ConvertBytesCore(yddBytes, null, outputPath));

        public static Task<bool> ConvertBytesAsync(byte[] yddBytes, IList<byte[]> ytdBytesList, string outputPath)
            => Task.Run(() => ConvertBytesCore(yddBytes, ytdBytesList, outputPath));

        private static bool ConvertCore(string yddPath, string outputPath)
        {
            try
            {
                Log("Чтение: " + yddPath);
                var data = File.ReadAllBytes(yddPath);
                return ConvertBytesCore(data, null, outputPath);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("YDD→GLB EXCEPTION:");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                return false;
            }
        }

        private static bool ConvertBytesCore(byte[] yddBytes, IList<byte[]> ytdBytesList, string outputPath)
        {
            try
            {
                if (yddBytes == null || yddBytes.Length == 0)
                {
                    Log("ERROR: пустой буфер YDD");
                    return false;
                }

                var ydd = new YddFile();
                ydd.Load(yddBytes);

                var drawables = ydd.Drawables;
                if (drawables == null || drawables.Length == 0)
                {
                    Log("ERROR: YDD не содержит drawables");
                    return false;
                }

                var picked = drawables.FirstOrDefault(d =>
                    d?.DrawableModels?.High != null && d.DrawableModels.High.Length > 0);
                if (picked == null)
                {
                    Log("ERROR: ни один drawable не содержит High LOD моделей");
                    return false;
                }

                Log($"Конвертирую drawable '{picked.Name ?? "<unnamed>"}' (всего в YDD: {drawables.Length})");

                Dictionary<string, byte[]> externalPngs = null;
                if (ytdBytesList != null && ytdBytesList.Count > 0)
                {
                    externalPngs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                    int idx = 0;
                    foreach (var ytdBytes in ytdBytesList)
                    {
                        idx++;
                        if (ytdBytes == null || ytdBytes.Length == 0) continue;
                        try
                        {
                            var ytd = new YtdFile();
                            ytd.Load(ytdBytes);
                            var pngs = YdrToGltfConverter.ExtractTexturesFromDict(ytd.TextureDict);
                            Log($"YTD #{idx}: декодировано {pngs.Count} текстур");
                            foreach (var kv in pngs)
                                externalPngs[kv.Key] = kv.Value;
                        }
                        catch (Exception ex)
                        {
                            Log($"YTD #{idx}: ошибка парсинга - {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                    Log($"Итого внешних текстур: {externalPngs.Count}");
                }

                return YdrToGltfConverter.ConvertDrawableCore(picked, outputPath, externalPngs);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("YDD→GLB EXCEPTION:");
                Console.WriteLine(ex.ToString());
                Console.ResetColor();
                return false;
            }
        }
    }
}
