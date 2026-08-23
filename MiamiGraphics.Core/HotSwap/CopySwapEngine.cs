using System;
using System.IO;

namespace MiamiGraphics.Core.HotSwap
{
    internal sealed class CopySwapEngine : ISwapEngine
    {
        private enum Slot
        {
            Missing,
            Armed,
            Clean,
            Unknown,
        }

        private static Slot Probe(string gtaRoot, string rel)
        {
            var game = HotSwapPaths.GamePath(gtaRoot, rel);
            if (!File.Exists(game)) return Slot.Missing;
            if (HotSwapFileOps.StampEq(game, HotSwapPaths.ModdedPath(gtaRoot, rel))) return Slot.Armed;
            if (HotSwapFileOps.StampEq(game, HotSwapPaths.CleanPath(gtaRoot, rel))) return Slot.Clean;
            return Slot.Unknown;
        }

        public bool HasImage(string gtaRoot, string rel) =>
            File.Exists(HotSwapPaths.ModdedPath(gtaRoot, rel));

        public bool IsArmed(string gtaRoot, string rel) => Probe(gtaRoot, rel) == Slot.Armed;

        public bool CanArm(string gtaRoot, string rel) =>
            File.Exists(HotSwapPaths.ModdedPath(gtaRoot, rel)) && Probe(gtaRoot, rel) != Slot.Armed;

        public bool CanDisarm(string gtaRoot, string rel) =>
            File.Exists(HotSwapPaths.CleanPath(gtaRoot, rel)) && Probe(gtaRoot, rel) == Slot.Armed;

        public void ArmOne(string gtaRoot, string rel)
        {
            var game = HotSwapPaths.GamePath(gtaRoot, rel);
            var modded = HotSwapPaths.ModdedPath(gtaRoot, rel);
            var clean = HotSwapPaths.CleanPath(gtaRoot, rel);
            if (!File.Exists(modded)) return;
            if (Probe(gtaRoot, rel) == Slot.Armed) return;

            if (!File.Exists(clean) && File.Exists(game))
                HotSwapFileOps.CopyThroughTemp(game, clean);

            HotSwapFileOps.CopyThroughTemp(modded, game);
        }

        public void DisarmOne(string gtaRoot, string rel)
        {
            var clean = HotSwapPaths.CleanPath(gtaRoot, rel);
            if (!File.Exists(clean)) return;
            if (Probe(gtaRoot, rel) == Slot.Clean) return;
            HotSwapFileOps.CopyThroughTemp(clean, HotSwapPaths.GamePath(gtaRoot, rel));
        }

        public void FreezeOne(string gtaRoot, string rel, string cleanSource)
        {
            var game = HotSwapPaths.GamePath(gtaRoot, rel);
            HotSwapFileOps.CopyThroughTemp(game, HotSwapPaths.ModdedPath(gtaRoot, rel));
            HotSwapFileOps.CopyThroughTemp(cleanSource, HotSwapPaths.CleanPath(gtaRoot, rel));
            HotSwapFileOps.CopyThroughTemp(cleanSource, game);
        }

        public void UnfreezeOne(string gtaRoot, string rel) => ModsBackAndDropImage(gtaRoot, rel);

        public void RollbackFreezeOne(string gtaRoot, string rel) => ModsBackAndDropImage(gtaRoot, rel);

        private static void ModsBackAndDropImage(string gtaRoot, string rel)
        {
            var game = HotSwapPaths.GamePath(gtaRoot, rel);
            var modded = HotSwapPaths.ModdedPath(gtaRoot, rel);
            var clean = HotSwapPaths.CleanPath(gtaRoot, rel);
            if (!File.Exists(modded)) return;
            if (Probe(gtaRoot, rel) != Slot.Armed)
                HotSwapFileOps.CopyThroughTemp(modded, game);
            HotSwapFileOps.DeleteQuiet(modded);
            HotSwapFileOps.DeleteQuiet(clean);
            DropTemps(gtaRoot, rel);
        }

        public void SanitizeOne(string gtaRoot, string rel) => DropTemps(gtaRoot, rel);

        public string Describe(string gtaRoot, string rel)
        {
            bool modded = File.Exists(HotSwapPaths.ModdedPath(gtaRoot, rel));
            bool clean = File.Exists(HotSwapPaths.CleanPath(gtaRoot, rel));
            var slot = Probe(gtaRoot, rel);
            var slotRu = slot switch
            {
                Slot.Missing => "Missing (файла игры нет)",
                Slot.Armed => "Armed (в игре моды)",
                Slot.Clean => "Clean (в игре чистый)",
                _ => "Unknown (файл игры не совпал ни с одной копией - похоже, игру обновил RGL)",
            };
            return $"copy-движок: Probe={slotRu}, modded-копия {(modded ? "есть" : "нет")}, " +
                   $"clean-копия {(clean ? "есть" : "нет")}";
        }

        private static void DropTemps(string gtaRoot, string rel)
        {
            HotSwapFileOps.SanitizeTemp(HotSwapPaths.GamePath(gtaRoot, rel));
            HotSwapFileOps.SanitizeTemp(HotSwapPaths.ModdedPath(gtaRoot, rel));
            HotSwapFileOps.SanitizeTemp(HotSwapPaths.CleanPath(gtaRoot, rel));
        }
    }
}
