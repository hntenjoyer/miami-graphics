using System;
using System.IO;
using System.Text.Json;

namespace MiamiGraphics.Core.HotSwap
{
    public sealed class HotSwapMode
    {
        public bool Enabled { get; set; }
        public string? GtaRoot { get; set; }
        public string? EnabledAtUtc { get; set; }

        public int Method { get; set; } = 1;

        public string? StoreRoot { get; set; }
    }

    public static class HotSwapModeStore
    {
        public static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MiamiGraphics", "config", "hotswap_mode.json");

        public static HotSwapMode Read()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return new HotSwapMode();
                var m = JsonSerializer.Deserialize<HotSwapMode>(File.ReadAllText(ConfigPath)) ?? new HotSwapMode();
                m.Method = (int)HotSwapPlan.Normalize(m.Method);
                return m;
            }
            catch { return new HotSwapMode(); }
        }

        public static void Update(Action<HotSwapMode> mutate)
        {
            var mode = Read();
            mutate(mode);
            Write(mode);
        }

        public static void Write(HotSwapMode mode)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(mode, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, ConfigPath, overwrite: true);
            HotSwapStore.InvalidateCache();
        }
    }
}
