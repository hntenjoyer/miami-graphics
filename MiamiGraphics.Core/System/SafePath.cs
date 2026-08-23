using System;
using System.IO;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.System
{
    public static class SafePath
    {
        public static string NormalizeRelative(string? p) => (p ?? "").Replace('\\', '/').Trim('/');

        public static bool IsSafeRelative(string? relative) => Validate(relative) is null;

        private static string? Validate(string? relative)
        {
            if (string.IsNullOrWhiteSpace(relative)) return Loc.T("error.pathEmpty");
            if (relative!.IndexOf('\0') >= 0) return Loc.T("error.pathNullByte");
            if (relative.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return Loc.T("error.pathInvalidChars");

            if (relative.IndexOf(':') >= 0)
                return Loc.T("error.pathHasColon");

            string norm = relative.Replace('\\', '/');
            if (norm.StartsWith("/", StringComparison.Ordinal))
                return Loc.T("error.pathStartsWithSeparator");
            if (Path.IsPathRooted(relative) || Path.IsPathFullyQualified(relative))
                return Loc.T("error.pathAbsolute");

            foreach (var seg in norm.Split('/'))
            {
                if (seg == "..") return Loc.T("error.pathHasDotDot");
                if (seg.Length > 1 && seg != "." && (seg[^1] == ' ' || seg[^1] == '.'))
                    return Loc.T("error.pathSegmentTrailingDotOrSpace", ("segment", seg));
            }
            return null;
        }

        public static bool TryResolveInside(string? baseDir, string? relative,
            out string fullPath, out string? error)
        {
            fullPath = "";
            if (string.IsNullOrWhiteSpace(baseDir)) { error = Loc.T("error.pathNoBaseDir"); return false; }

            error = Validate(relative);
            if (error != null) return false;

            try
            {
                string root = Path.GetFullPath(baseDir!);
                if (!root.EndsWith(Path.DirectorySeparatorChar))
                    root += Path.DirectorySeparatorChar;

                string full = Path.GetFullPath(
                    Path.Combine(root, relative!.Replace('/', Path.DirectorySeparatorChar)));

                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    error = Loc.T("error.pathEscapesBaseDir");
                    return false;
                }

                fullPath = full;
                return true;
            }
            catch (Exception ex)
            {
                error = Loc.T("error.pathCannotResolve", ("detail", ex.GetType().Name + ": " + ex.Message));
                return false;
            }
        }

        public static string ResolveInside(string? baseDir, string? relative, string what)
        {
            if (TryResolveInside(baseDir, relative, out var full, out var error)) return full;
            throw new InvalidOperationException(
                Loc.T("error.pathRejected", ("what", what), ("path", relative), ("why", error)));
        }

        public static string ResolveLeafInside(string? baseDir, string? leafName, string what)
        {
            if (string.IsNullOrWhiteSpace(leafName) ||
                leafName!.IndexOf('/') >= 0 || leafName.IndexOf('\\') >= 0)
                throw new InvalidOperationException(
                    Loc.T("error.pathExpectedLeafName", ("what", what), ("name", leafName)));
            return ResolveInside(baseDir, leafName, what);
        }

        public static string? ResolveInsideOrNull(string? baseDir, string? relative, out string? error)
            => TryResolveInside(baseDir, relative, out var full, out error) ? full : null;
    }
}
