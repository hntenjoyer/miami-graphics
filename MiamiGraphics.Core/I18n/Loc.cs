using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MiamiGraphics.Core.I18n;

public static class Loc
{
    public const string FallbackLanguage = "ru";

    public static readonly IReadOnlyList<string> SupportedLanguages = new[] { "ru", "en", "pl" };

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Catalogs = LoadAll();

    private static readonly IReadOnlyDictionary<string, string> Fallback =
        Catalogs.TryGetValue(FallbackLanguage, out var ru)
            ? ru
            : new Dictionary<string, string>(StringComparer.Ordinal);

    private static volatile IReadOnlyDictionary<string, string> _current = Fallback;
    private static volatile string _currentLanguage = FallbackLanguage;

    private static readonly ConcurrentDictionary<string, byte> MissingReported = new(StringComparer.Ordinal);

    public static string Language => _currentLanguage;

    public static void SetLanguage(string? lang)
    {
        var normalized = Normalize(lang);
        if (string.Equals(normalized, _currentLanguage, StringComparison.Ordinal))
            return;

        _current = Catalogs.TryGetValue(normalized, out var catalog) ? catalog : Fallback;
        _currentLanguage = normalized;
        Debug.WriteLine($"[Loc] язык интерфейса: {normalized} ({_current.Count} ключей)");
    }

    public static string T(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        var current = _current;
        if (current.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
            return value;

        if (!ReferenceEquals(current, Fallback) &&
            Fallback.TryGetValue(key, out var russian) && !string.IsNullOrEmpty(russian))
            return russian;

        ReportMissing(key);
        return key;
    }

    public static string T(string key, params (string Name, object? Value)[] args)
        => Format(T(key), args);

    public static bool Has(string key)
        => !string.IsNullOrEmpty(key) && (_current.ContainsKey(key) || Fallback.ContainsKey(key));

    private static string Normalize(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
            return FallbackLanguage;

        var s = lang.Trim();
        var cut = s.IndexOfAny(new[] { '-', '_' });
        if (cut > 0)
            s = s.Substring(0, cut);
        s = s.ToLowerInvariant();

        foreach (var supported in SupportedLanguages)
            if (string.Equals(s, supported, StringComparison.Ordinal))
                return supported;

        return FallbackLanguage;
    }

    private static string Format(string template, (string Name, object? Value)[] args)
    {
        if (args is null || args.Length == 0 || template.IndexOf('{') < 0)
            return template;

        var sb = new StringBuilder(template.Length + 16);
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '{')
            {
                sb.Append(template[i]);
                continue;
            }

            var end = template.IndexOf('}', i + 1);
            if (end < 0)
            {
                sb.Append(template, i, template.Length - i);
                break;
            }

            var name = template.Substring(i + 1, end - i - 1);
            var matched = false;
            foreach (var (argName, argValue) in args)
            {
                if (!string.Equals(argName, name, StringComparison.Ordinal))
                    continue;
                sb.Append(Stringify(argValue));
                matched = true;
                break;
            }

            if (!matched)
                sb.Append(template, i, end - i + 1);

            i = end;
        }

        return sb.ToString();
    }

    private static string Stringify(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static void ReportMissing(string key)
    {
        if (MissingReported.TryAdd(key, 0))
            Debug.WriteLine($"[Loc] нет перевода для ключа '{key}' (язык {_currentLanguage}) - отдан сам ключ");
    }

    private static Dictionary<string, IReadOnlyDictionary<string, string>> LoadAll()
    {
        var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        var asm = typeof(Loc).Assembly;

        foreach (var lang in SupportedLanguages)
            result[lang] = LoadOne(asm, lang);

        return result;
    }

    private static IReadOnlyDictionary<string, string> LoadOne(Assembly asm, string lang)
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        var suffix = $"i18n.{lang}.json";

        try
        {
            var name = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                Debug.WriteLine($"[Loc] встроенный словарь {suffix} не найден");
                return empty;
            }

            using var stream = asm.GetManifestResourceStream(name);
            if (stream is null)
            {
                Debug.WriteLine($"[Loc] не удалось открыть встроенный словарь {name}");
                return empty;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String)
                    continue;
                if (prop.Name.Length > 0 && prop.Name[0] == '_')
                    continue;
                var text = prop.Value.GetString();
                if (!string.IsNullOrEmpty(text))
                    dict[prop.Name] = text;
            }
            return dict;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Loc] словарь {lang} не прочитан: {ex.Message}");
            return empty;
        }
    }
}
