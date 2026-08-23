using System.IO;
using System.Reflection;
using System.Text.Json;

namespace MiamiGraphics.Core.System;

public sealed record VersionReference(string ExeVersion, long UpdateRpfSize, string UpdateRpfSha256);

public sealed class GtaVersionsRegistry
{
    private readonly Dictionary<string, VersionReference> _byVersion;

    public GtaVersionsRegistry()
    {
        _byVersion = LoadEmbedded();
    }

    public VersionReference? GetReferenceForVersion(string exeVersion)
    {
        return _byVersion.TryGetValue(exeVersion, out var r) ? r : null;
    }

    public bool IsVersionSupported(string exeVersion) => _byVersion.ContainsKey(exeVersion);

    private static Dictionary<string, VersionReference> LoadEmbedded()
    {
        var asm = typeof(GtaVersionsRegistry).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("gta_versions.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded resource gta_versions.json not found");

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Failed to open embedded gta_versions.json");
        using var reader = new StreamReader(stream);
        var raw = reader.ReadToEnd();

        var doc = JsonDocument.Parse(raw);
        var dict = new Dictionary<string, VersionReference>(StringComparer.Ordinal);
        foreach (var v in doc.RootElement.GetProperty("supportedVersions").EnumerateArray())
        {
            var exeVer = v.GetProperty("exeVersion").GetString()!;
            var updateRpf = v.GetProperty("updateRpf");
            var size = updateRpf.GetProperty("size").GetInt64();
            var sha = updateRpf.GetProperty("sha256").GetString()!;
            dict[exeVer] = new VersionReference(exeVer, size, sha);
        }
        return dict;
    }
}
