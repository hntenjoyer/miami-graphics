#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MiamiGraphics.Core.Services;

namespace MiamiGraphics.Shell.Services;

public static class MajesticCrashWatch
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private const int MaxLogs = 5;

    private const long MaxLogBytes = 32L * 1024 * 1024;

    private static string StateFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiamiGraphics", "config", "majestic_crashes_seen.json");

    public static void ScanInBackground()
    {
        _ = Task.Run(() =>
        {
            try { Scan(); }
            catch (Exception ex) { SessionLog.Warn("crash-watch", $"разбор логов Majestic не удался: {ex.Message}"); }
        });
    }

    private static void Scan()
    {
        var dir = LogDir();
        if (dir == null) return;

        var seen = LoadSeen();
        var fresh = new List<FileInfo>();
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("client_*.log"))
        {
            if (DateTime.Now - f.LastWriteTime > MaxAge) continue;
            if (seen.TryGetValue(f.Name, out var len) && len == f.Length) continue;
            fresh.Add(f);
        }
        if (fresh.Count == 0) return;

        var installed = InstalledSummary();
        foreach (var f in fresh.OrderByDescending(x => x.LastWriteTime).Take(MaxLogs))
        {
            seen[f.Name] = f.Length;
            if (f.Length > MaxLogBytes) continue;

            MajesticCrashLog.Crash? crash;
            try { crash = MajesticCrashLog.Parse(ReadLines(f.FullName)); }
            catch (Exception ex) { SessionLog.Warn("crash-watch", $"{f.Name}: {ex.Message}"); continue; }
            if (crash == null) continue;

            SessionLog.Warn("crash-watch", $"{f.Name}: {crash.Describe()}");
            SessionLog.Warn("crash-watch", $"{f.Name}: у нас стояло - {installed}");
        }
        SaveSeen(seen);
    }

    private static IEnumerable<string> ReadLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (sr.ReadLine() is { } line) yield return line;
    }

    private static string? LogDir()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(roaming, "majestic-launcher", "Multiplayer", "logs");
        return Directory.Exists(dir) ? dir : null;
    }

    private static string InstalledSummary()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MiamiGraphics", "install_state.json");
        if (!File.Exists(path)) return "состояние установки не найдено";

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            var parts = new List<string>();

            void Add(string label, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value) && value != "default") parts.Add($"{label}={value}");
            }

            Add("редукс", Str(r, "ReduxId"));
            Add("броня", Str(r, "CurrentArmorName") ?? Str(r, "CurrentArmorId"));
            Add("звуки", Str(r, "CurrentSoundPackName"));

            if (r.TryGetProperty("CustomizationDraft", out var d) && d.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "Bloodfx", "Crosshair", "Timecycle", "Armor", "Arena" })
                    if (d.TryGetProperty(key, out var sub) && sub.ValueKind == JsonValueKind.Object)
                        Add(key.ToLowerInvariant(), Str(sub, "Kind"));

                if (d.TryGetProperty("Minimap", out var mm) && Bool(mm, "Enabled")) parts.Add("миникарта");
                if (d.TryGetProperty("Tracers", out var tr))
                {
                    var kind = Str(tr, "SourceKind");
                    if (!string.IsNullOrWhiteSpace(kind) && kind != "default") parts.Add($"трейсеры={kind}");
                }
                foreach (var flag in new[] { "ZalazyEnabled", "SmokeEnabled", "NoTracerEnabled",
                                             "GreenZoneEnabled", "NoBackpackEnabled", "BigMapEnabled", "CarLogosEnabled" })
                    if (Bool(d, flag)) parts.Add(flag.Replace("Enabled", "").ToLowerInvariant());
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "ничего из наших модов";
        }
        catch (Exception ex) { return $"состояние прочитать не вышло: {ex.Message}"; }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static Dictionary<string, long> LoadSeen()
    {
        try
        {
            if (File.Exists(StateFile))
                return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(StateFile))
                       ?? new Dictionary<string, long>();
        }
        catch { }
        return new Dictionary<string, long>();
    }

    private static void SaveSeen(Dictionary<string, long> seen)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            var trimmed = seen.OrderByDescending(kv => kv.Key, StringComparer.Ordinal).Take(50)
                              .ToDictionary(kv => kv.Key, kv => kv.Value);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(trimmed));
        }
        catch { }
    }
}
