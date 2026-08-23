using System;
using System.Collections.Generic;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.HotSwap
{
    public enum HotSwapMethod
    {
        AgentSameFolder = 1,
        AgentOtherFolder = 2,
        ManualSameFolder = 3,
        ManualOtherFolder = 4,
        ReplaceXCopy = 5,
    }

    public enum HotSwapTrigger
    {
        Agent = 0,
        Manual = 1,
    }

    public enum HotSwapStoreKind
    {
        GameVolumeDefault = 0,
        CustomFolder = 1,
    }

    public enum HotSwapPrimitive
    {
        AtomicReplace = 0,
        SafeCopy = 1,
    }

    public sealed class HotSwapPlan
    {
        public HotSwapMethod Method { get; }
        public HotSwapTrigger Trigger { get; }
        public HotSwapStoreKind Store { get; }
        public HotSwapPrimitive Primitive { get; }

        public bool RequireSameVolume => Primitive == HotSwapPrimitive.AtomicReplace;

        public bool KillGameBeforeReturn { get; }

        public int PollMs { get; }

        public int DisarmDebounceMs { get; }

        public bool UseReplaceXProcessList { get; }

        public string Title { get; }

        public string LocalizedTitle => Loc.T($"misc.hotSwapMethod{(int)Method}Title");

        private HotSwapPlan(HotSwapMethod method, HotSwapTrigger trigger, HotSwapStoreKind store,
                            HotSwapPrimitive primitive, bool killBeforeReturn, int pollMs,
                            int disarmDebounceMs, bool replaceXList, string title)
        {
            Method = method;
            Trigger = trigger;
            Store = store;
            Primitive = primitive;
            KillGameBeforeReturn = killBeforeReturn;
            PollMs = pollMs;
            DisarmDebounceMs = disarmDebounceMs;
            UseReplaceXProcessList = replaceXList;
            Title = title;
        }

        private static readonly Dictionary<HotSwapMethod, HotSwapPlan> Table = new()
        {
            [HotSwapMethod.AgentSameFolder] = new HotSwapPlan(
                HotSwapMethod.AgentSameFolder, HotSwapTrigger.Agent, HotSwapStoreKind.GameVolumeDefault,
                HotSwapPrimitive.AtomicReplace, false, 250, 3000, false,
                "Агент следит сам, копии на диске игры"),

            [HotSwapMethod.AgentOtherFolder] = new HotSwapPlan(
                HotSwapMethod.AgentOtherFolder, HotSwapTrigger.Agent, HotSwapStoreKind.CustomFolder,
                HotSwapPrimitive.AtomicReplace, false, 250, 3000, false,
                "Агент следит сам, копии в своей папке"),

            [HotSwapMethod.ManualSameFolder] = new HotSwapPlan(
                HotSwapMethod.ManualSameFolder, HotSwapTrigger.Manual, HotSwapStoreKind.GameVolumeDefault,
                HotSwapPrimitive.AtomicReplace, false, 1000, 0, false,
                "Ручной триггер, копии на диске игры"),

            [HotSwapMethod.ManualOtherFolder] = new HotSwapPlan(
                HotSwapMethod.ManualOtherFolder, HotSwapTrigger.Manual, HotSwapStoreKind.CustomFolder,
                HotSwapPrimitive.SafeCopy, false, 1000, 0, false,
                "Ручной триггер, копии в своей папке (копированием)"),

            [HotSwapMethod.ReplaceXCopy] = new HotSwapPlan(
                HotSwapMethod.ReplaceXCopy, HotSwapTrigger.Agent, HotSwapStoreKind.GameVolumeDefault,
                HotSwapPrimitive.SafeCopy, true, 500, 0, true,
                "Как ReplaceX: агент, копирование, гашение игры"),
        };

        public static HotSwapPlan For(HotSwapMethod method) => Table[Normalize((int)method)];

        public static HotSwapPlan For(int method) => Table[Normalize(method)];

        public static HotSwapMethod Normalize(int method) =>
            Table.ContainsKey((HotSwapMethod)method) ? (HotSwapMethod)method : HotSwapMethod.AgentSameFolder;

        public static bool NeedsAgent(HotSwapMethod method) => For(method).Trigger == HotSwapTrigger.Agent;

        public static bool IsManual(HotSwapMethod method) => For(method).Trigger == HotSwapTrigger.Manual;
    }
}
