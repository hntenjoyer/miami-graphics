#nullable enable
using System;
using System.Linq;

namespace MiamiGraphics.Core.Services;

public static class RpfEntrySanity
{
    public static readonly string[] ResourceExtensions =
        { ".ydr", ".yft", ".ytd", ".ymap", ".ytyp", ".ycd", ".yld", ".ypt", ".ybn", ".yed", ".ynv" };

    public static bool IsRsc7(byte[]? d) =>
        d is { Length: >= 4 } && d[0] == 0x52 && d[1] == 0x53 && d[2] == 0x43 && d[3] == 0x37;

    public static bool IsRpf7(byte[]? d) =>
        d is { Length: >= 4 } && d[0] == 0x37 && d[1] == 0x46 && d[2] == 0x50 && d[3] == 0x52;

    public static bool NameSaysResource(string? name) =>
        !string.IsNullOrEmpty(name) &&
        ResourceExtensions.Any(e => name!.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static bool NameSaysNestedRpf(string? name) =>
        !string.IsNullOrEmpty(name) && name!.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);

    public static bool NameIsGarbage(string? name) =>
        string.IsNullOrEmpty(name) || name!.Any(ch => ch < 32);

    public static bool NameIsNonAscii(string? name) =>
        !string.IsNullOrEmpty(name) && name!.Any(ch => ch > 126);

    public static string? RejectReason(string? name, byte[]? bytes)
    {
        if (NameIsGarbage(name))
            return "непечатное имя (мусор обфускации)";

        if (NameSaysNestedRpf(name))
            return IsRpf7(bytes) ? null : $"вложенный rpf без заголовка RPF7 ({Magic(bytes)})";

        if (!NameSaysResource(name)) return null;

        if (!IsRsc7(bytes))
            return $"ресурсное имя, а содержимое не RSC7 ({Magic(bytes)})";

        return RpfResourceBudget.RejectReasonBySize(name, bytes);
    }

    public static string Magic(byte[]? d)
    {
        if (d is null || d.Length == 0) return "пусто";
        return "magic='" + new string(d.Take(4).Select(b => b >= 32 && b < 127 ? (char)b : '.').ToArray()) + "'";
    }
}
