using System;
using System.Collections.Generic;
using System.Linq;

namespace MiamiGraphics.Core.Parser
{
    public static class MinimapFontLinker
    {
        private const string MinimapComponentKey = "minimap";
        private const string FontContainerRpf    = "scaleform_platform_pc.rpf";
        private const string FontFilePrefix       = "font_lib_";
        private const string FontFileExtension    = ".gfx";

        public static List<string> AttachCustomFonts(ResolvedComponentMap componentMap, IEnumerable<PatchAction> actions)
        {
            var attached = new List<string>();

            if (componentMap?.Components == null ||
                !componentMap.Components.TryGetValue(MinimapComponentKey, out ComponentInfo minimap) ||
                minimap == null || !minimap.IsFound)
            {
                return attached;
            }

            if (actions == null) return attached;

            foreach (var action in actions)
            {
                if (action == null) continue;
                if (action.Type != ActionType.Replace && action.Type != ActionType.Import) continue;

                string target = action.TargetPath?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(target)) continue;
                if (!IsCustomFontPath(target)) continue;

                attached.Add(target);
            }

            if (attached.Count == 0) return attached;

            minimap.InternalPaths ??= new List<string>();
            minimap.InternalPaths.AddRange(attached);
            minimap.InternalPaths = minimap.InternalPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return attached
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool IsCustomFontPath(string normalizedTargetPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedTargetPath)) return false;
            if (normalizedTargetPath.IndexOf(FontContainerRpf, StringComparison.OrdinalIgnoreCase) < 0) return false;

            string fileName = normalizedTargetPath.Substring(normalizedTargetPath.LastIndexOf('/') + 1);
            return fileName.StartsWith(FontFilePrefix, StringComparison.OrdinalIgnoreCase) &&
                   fileName.EndsWith(FontFileExtension, StringComparison.OrdinalIgnoreCase);
        }
    }
}
