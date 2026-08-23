using System;
using System.IO;
using System.Text.Json;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.HotSwap
{
    public sealed class HotSwapBinding
    {
        public string? Root { get; set; }
        public int Method { get; set; } = 1;
        public string? BoundAtUtc { get; set; }
    }

    public static class HotSwapStore
    {
        private const string Leaf1 = "MiamiGraphics";
        private const string Leaf2 = "hotswap";

        public static string DefaultRoot(string gtaRoot)
        {
            var vol = Path.GetPathRoot(Path.GetFullPath(gtaRoot))
                      ?? throw new InvalidOperationException(Loc.T("error.gameVolumeUnknown"));
            return Path.Combine(vol, Leaf1, Leaf2);
        }

        public static string RootFor(string gtaRoot, HotSwapMethod method, string? storeRoot)
        {
            var plan = HotSwapPlan.For(method);
            if (plan.Store != HotSwapStoreKind.CustomFolder) return DefaultRoot(gtaRoot);
            if (!string.IsNullOrWhiteSpace(storeRoot))
                return Path.Combine(Path.GetFullPath(storeRoot!), Leaf1, Leaf2);
            return FallbackCustomRoot(gtaRoot, method);
        }

        public static string FallbackCustomRoot(string gtaRoot, HotSwapMethod method)
        {
            if (HotSwapPlan.For(method).RequireSameVolume)
            {
                var vol = Path.GetPathRoot(Path.GetFullPath(gtaRoot))
                          ?? throw new InvalidOperationException(Loc.T("error.gameVolumeUnknown"));
                return Path.Combine(vol, Leaf1 + "Swap", Leaf2);
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Leaf1, "hotswap_store", Leaf2);
        }

        private static string BindingPath(string gtaRoot) => Path.Combine(DefaultRoot(gtaRoot), "store.json");

        private static readonly object Gate = new();
        private static string? _cacheKey;
        private static HotSwapBinding? _cacheVal;
        private static DateTime _cacheAt = DateTime.MinValue;

        public static void InvalidateCache()
        {
            lock (Gate) { _cacheKey = null; _cacheVal = null; _cacheAt = DateTime.MinValue; }
        }

        private static HotSwapBinding? ReadBinding(string gtaRoot)
        {
            lock (Gate)
            {
                var key = gtaRoot.TrimEnd('\\', '/').ToLowerInvariant();
                if (_cacheKey == key && (DateTime.UtcNow - _cacheAt).TotalMilliseconds < 1000)
                    return _cacheVal;

                HotSwapBinding? b = null;
                try
                {
                    var p = BindingPath(gtaRoot);
                    if (File.Exists(p))
                    {
                        b = JsonSerializer.Deserialize<HotSwapBinding>(File.ReadAllText(p));
                        if (b is not null && string.IsNullOrWhiteSpace(b.Root)) b = null;
                    }
                }
                catch { b = null; }

                _cacheKey = key; _cacheVal = b; _cacheAt = DateTime.UtcNow;
                return b;
            }
        }

        public static HotSwapMethod ActiveMethod(string gtaRoot)
        {
            var b = ReadBinding(gtaRoot);
            if (b is not null) return HotSwapPlan.Normalize(b.Method);
            return ConfiguredMethod();
        }

        public static HotSwapMethod ConfiguredMethod()
        {
            try { return HotSwapPlan.Normalize(HotSwapModeStore.Read().Method); }
            catch { return HotSwapMethod.AgentSameFolder; }
        }

        public static string Resolve(string gtaRoot)
        {
            var b = ReadBinding(gtaRoot);
            if (b is not null) return b.Root!;
            var mode = HotSwapModeStore.Read();
            return RootFor(gtaRoot, HotSwapPlan.Normalize(mode.Method), mode.StoreRoot);
        }

        public static string Bind(string gtaRoot, HotSwapMethod method, string? storeRoot)
        {
            var root = RootFor(gtaRoot, method, storeRoot);
            var p = BindingPath(gtaRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            Directory.CreateDirectory(root);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new HotSwapBinding
            {
                Root = root,
                Method = (int)method,
                BoundAtUtc = DateTime.UtcNow.ToString("O"),
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, p, overwrite: true);
            InvalidateCache();
            HotSwapLog.Write("store", $"привязка записана: способ {(int)method}, корень образа {root}");
            return root;
        }

        public static void Unbind(string gtaRoot)
        {
            try { File.Delete(BindingPath(gtaRoot)); } catch { }
            InvalidateCache();
            HotSwapLog.Write("store", "привязка снята (образа больше нет)");
        }

        public static bool IsBound(string gtaRoot) => ReadBinding(gtaRoot) is not null;
    }
}
