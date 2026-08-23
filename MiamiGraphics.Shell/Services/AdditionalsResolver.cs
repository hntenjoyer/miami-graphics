using System.Diagnostics;
using System.IO;

namespace MiamiGraphics.Shell.Services;

public static class AdditionalsResolver
{
    private static string? _additionalsCache;
    private static string? _templatesCache;

    public static string? FindAdditionalsRoot() => _additionalsCache ??= ProbeAdditionals();

    public static string? FindTemplatesRoot() => _templatesCache ??= ProbeTemplates();

    public static string? AdditionalsPath(params string[] segments)
    {
        var root = FindAdditionalsRoot();
        if (root is null) return null;
        var p = Path.Combine(new[] { root }.Concat(segments).ToArray());
        return File.Exists(p) || Directory.Exists(p) ? p : null;
    }

    public static string? TemplatesPath(params string[] segments)
    {
        var root = FindTemplatesRoot();
        if (root is null) return null;
        var p = Path.Combine(new[] { root }.Concat(segments).ToArray());
        return File.Exists(p) || Directory.Exists(p) ? p : null;
    }

    private static string? ProbeAdditionals()
    {

        const string marker = @"Keys\gtav_ng_key.dat";

        foreach (var candidate in CandidateRoots("additionals"))
        {
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, marker)))
            {
                Debug.WriteLine($"[AdditionalsResolver] additionals → {candidate}");
                return candidate;
            }
        }
        Debug.WriteLine($"[AdditionalsResolver] additionals NOT FOUND. Tried: {string.Join(", ", CandidateRoots("additionals"))}");
        return null;
    }

    private static string? ProbeTemplates()
    {

        string[] markers = { "miami_empty.rpf", "hntgraph_empty.rpf" };

        foreach (var candidate in CandidateRoots("templates"))
        {
            if (!Directory.Exists(candidate)) continue;
            foreach (var marker in markers)
            {
                if (File.Exists(Path.Combine(candidate, marker)))
                {
                    Debug.WriteLine($"[AdditionalsResolver] templates → {candidate} (marker {marker})");
                    return candidate;
                }
            }
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var coreTemplates = Path.Combine(dir.FullName, "MiamiGraphics.Core", "templates");
            if (!Directory.Exists(coreTemplates)) continue;
            foreach (var marker in markers)
            {
                if (File.Exists(Path.Combine(coreTemplates, marker)))
                {
                    Debug.WriteLine($"[AdditionalsResolver] templates → {coreTemplates} (Core dev path, marker {marker})");
                    return coreTemplates;
                }
            }
        }

        Debug.WriteLine($"[AdditionalsResolver] templates NOT FOUND.");
        return null;
    }

    private static IEnumerable<string> CandidateRoots(string bundleName)
    {

        yield return Path.Combine(AppContext.BaseDirectory, bundleName);

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(localAppData, "MiamiGraphics", bundleName);

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir?.Parent is not null; i++)
        {
            dir = dir.Parent;
            yield return Path.Combine(dir!.FullName, bundleName);
        }
    }
}
