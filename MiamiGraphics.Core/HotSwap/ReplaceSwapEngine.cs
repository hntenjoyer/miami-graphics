using System;
using System.IO;

namespace MiamiGraphics.Core.HotSwap
{
    internal sealed class ReplaceSwapEngine : ISwapEngine
    {
        public bool HasImage(string gtaRoot, string rel) =>
            File.Exists(HotSwapPaths.ModdedPath(gtaRoot, rel));

        public bool IsArmed(string gtaRoot, string rel) =>
            File.Exists(HotSwapPaths.CleanPath(gtaRoot, rel));

        public bool CanArm(string gtaRoot, string rel) =>
            File.Exists(HotSwapPaths.ModdedPath(gtaRoot, rel))
            && !File.Exists(HotSwapPaths.CleanPath(gtaRoot, rel));

        public bool CanDisarm(string gtaRoot, string rel) =>
            File.Exists(HotSwapPaths.CleanPath(gtaRoot, rel));

        public void ArmOne(string gtaRoot, string rel) =>
            HotSwapFileOps.ReplaceWithRetry(
                HotSwapPaths.ModdedPath(gtaRoot, rel),
                HotSwapPaths.GamePath(gtaRoot, rel),
                HotSwapPaths.CleanPath(gtaRoot, rel));

        public void DisarmOne(string gtaRoot, string rel) =>
            HotSwapFileOps.ReplaceWithRetry(
                HotSwapPaths.CleanPath(gtaRoot, rel),
                HotSwapPaths.GamePath(gtaRoot, rel),
                HotSwapPaths.ModdedPath(gtaRoot, rel));

        public void FreezeOne(string gtaRoot, string rel, string cleanSource)
        {
            var game = HotSwapPaths.GamePath(gtaRoot, rel);
            var modded = HotSwapPaths.ModdedPath(gtaRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(modded)!);
            File.Move(game, modded);
            try { File.Copy(cleanSource, game, overwrite: false); }
            catch { File.Move(modded, game); throw; }
        }

        public void UnfreezeOne(string gtaRoot, string rel)
        {
            var modded = HotSwapPaths.ModdedPath(gtaRoot, rel);
            var clean = HotSwapPaths.CleanPath(gtaRoot, rel);
            if (!File.Exists(modded)) return;
            if (File.Exists(clean)) File.Delete(clean);
            HotSwapFileOps.ReplaceWithRetry(modded, HotSwapPaths.GamePath(gtaRoot, rel), clean);
            HotSwapFileOps.DeleteQuiet(clean);
        }

        public void RollbackFreezeOne(string gtaRoot, string rel)
        {
            var game = HotSwapPaths.GamePath(gtaRoot, rel);
            var modded = HotSwapPaths.ModdedPath(gtaRoot, rel);
            if (!File.Exists(modded)) return;
            if (File.Exists(game)) File.Delete(game);
            File.Move(modded, game);
        }

        public void SanitizeOne(string gtaRoot, string rel) { }

        public string Describe(string gtaRoot, string rel)
        {
            bool game = File.Exists(HotSwapPaths.GamePath(gtaRoot, rel));
            bool modded = File.Exists(HotSwapPaths.ModdedPath(gtaRoot, rel));
            bool clean = File.Exists(HotSwapPaths.CleanPath(gtaRoot, rel));
            return $"rename-движок: файл игры {(game ? "есть" : "НЕТ")}, " +
                   $"modded-копия {(modded ? "есть" : "нет")}, " +
                   $"clean-копия {(clean ? "есть (= моды в игре)" : "нет (= в игре чистый)")}";
        }
    }
}
