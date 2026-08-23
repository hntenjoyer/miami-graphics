#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiamiGraphics.Core.Services;

public sealed class OptimizationBaseline
{
    public sealed class Snapshot
    {
        [JsonPropertyName("takenAt")]    public DateTimeOffset TakenAt { get; set; }
        [JsonPropertyName("sourcePath")] public string SourcePath { get; set; } = "";
        [JsonPropertyName("values")]     public Dictionary<string, string> Values { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly string _path;
    private Snapshot? _cache;

    public OptimizationBaseline(string? path = null)
        => _path = path ?? DefaultPath();

    public static string DefaultPath()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MiamiGraphics");
        return Path.Combine(root, "optimization_baseline.json");
    }

    public string Path_ => _path;

    public bool Exists => File.Exists(_path);

    public Snapshot? Load()
    {
        if (_cache is not null) return _cache;
        if (!File.Exists(_path)) return null;
        try
        {
            _cache = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(_path), Json);
            return _cache;
        }
        catch
        {
            return null;
        }
    }

    public bool EnsureCaptured(GtaSettingsModel current, string sourcePath)
    {
        if (Load() is not null) return false;

        var snap = new Snapshot
        {
            TakenAt = DateTimeOffset.Now,
            SourcePath = sourcePath,
        };
        foreach (var key in GtaSettingsKeyMap.KnownKeys)
        {
            var v = GtaSettingsKeyMap.Read(current, key);
            if (v is not null) snap.Values[key] = v;
        }

        var dir = global::System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(snap, Json));
        if (File.Exists(_path)) File.Replace(tmp, _path, null);
        else File.Move(tmp, _path);

        _cache = snap;
        return true;
    }

    public string? ValueOf(string key)
        => Load() is { } s && s.Values.TryGetValue(key, out var v) ? v : null;
}
