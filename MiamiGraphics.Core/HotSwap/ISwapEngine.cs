using System;

namespace MiamiGraphics.Core.HotSwap
{
    internal interface ISwapEngine
    {
        bool HasImage(string gtaRoot, string rel);

        bool IsArmed(string gtaRoot, string rel);

        bool CanArm(string gtaRoot, string rel);

        bool CanDisarm(string gtaRoot, string rel);

        void ArmOne(string gtaRoot, string rel);

        void DisarmOne(string gtaRoot, string rel);

        void FreezeOne(string gtaRoot, string rel, string cleanSource);

        void UnfreezeOne(string gtaRoot, string rel);

        void RollbackFreezeOne(string gtaRoot, string rel);

        void SanitizeOne(string gtaRoot, string rel);

        string Describe(string gtaRoot, string rel);
    }

    internal static class SwapEngines
    {
        private static readonly ISwapEngine ReplaceEngine = new ReplaceSwapEngine();
        private static readonly ISwapEngine CopyEngine = new CopySwapEngine();

        public static ISwapEngine For(HotSwapMethod method) =>
            HotSwapPlan.For(method).Primitive == HotSwapPrimitive.SafeCopy ? CopyEngine : ReplaceEngine;

        public static ISwapEngine ForActive(string gtaRoot) => For(HotSwapStore.ActiveMethod(gtaRoot));
    }
}
