using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace MiamiGraphics.Shell.Services;

public static class SessionLog
{
    private const long MaxBytes = 3 * 1024 * 1024;
    private const int BackupCount = 3;

    private static readonly object _gate = new();

    public static string LogDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiamiGraphics",
        "logs");

    public static string LogFile { get; } = Path.Combine(LogDir, "miami-launcher.log");

    public static void Info(string tag, string message) => Write("INFO", tag, message);
    public static void Warn(string tag, string message) => Write("WARN", tag, message);

    public static void Error(string tag, string message, Exception? ex = null)
    {
        if (ex is null) { Write("ERR ", tag, message); return; }
        Write("ERR ", tag, $"{message} :: {ex.GetType().Name}: {ex.Message}\n{ex}");
    }

    public static void SessionStart(string[]? args = null)
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var infoVer = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var version = string.IsNullOrWhiteSpace(infoVer)
                ? (asm.GetName().Version?.ToString(3) ?? "0.0.0")
                : infoVer;

            var sb = new StringBuilder();
            sb.Append('\n')
              .AppendLine("==================================================================")
              .AppendLine($"  SESSION START   Miami Graphics {version}")
              .AppendLine($"  os        : {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})")
              .AppendLine($"  runtime   : {RuntimeInformation.FrameworkDescription}")
              .AppendLine($"  machine   : {Safe(() => Environment.MachineName)}   user: {Safe(() => Environment.UserName)}")
              .AppendLine($"  elevated  : {IsElevated()}")
              .AppendLine($"  pid       : {Safe(() => Environment.ProcessId.ToString())}")
              .AppendLine($"  args      : {(args is { Length: > 0 } ? string.Join(' ', args) : "(none)")}")
              .Append("==================================================================");
            Write("INFO", "session", sb.ToString());
        }
        catch {}
    }

    public static void SessionEnd(string reason)
        => Write("INFO", "session", $"SESSION END ({reason})");

    private static void Write(string level, string tag, string message)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}  {level}  [{tag}]  {message}";
            lock (_gate)
            {
                Directory.CreateDirectory(LogDir);
                RotateIfNeeded();
                File.AppendAllText(LogFile, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            try { Debug.WriteLine($"[SessionLog] write failed: {tag} {message}"); } catch { }
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var fi = new FileInfo(LogFile);
            if (!fi.Exists || fi.Length < MaxBytes) return;

            var oldest = $"{LogFile}.{BackupCount}";
            if (File.Exists(oldest)) File.Delete(oldest);

            for (var i = BackupCount - 1; i >= 1; i--)
            {
                var src = $"{LogFile}.{i}";
                if (File.Exists(src)) File.Move(src, $"{LogFile}.{i + 1}", overwrite: true);
            }

            File.Move(LogFile, $"{LogFile}.1", overwrite: true);
        }
        catch {}
    }

    private static bool IsElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string Safe(Func<string> f)
    {
        try { return f(); } catch { return "?"; }
    }
}
