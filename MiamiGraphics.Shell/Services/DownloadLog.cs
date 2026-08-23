using System;
using System.IO;
using System.Text;

namespace MiamiGraphics.Shell.Services;

public static class DownloadLog
{
    private const long MaxBytes = 5L * 1024 * 1024;

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly object Gate = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiamiGraphics", "logs", "downloads.log");

    public static string PrevLogPath => Path.Combine(
        Path.GetDirectoryName(LogPath)!, "downloads.prev.log");

    public static void Write(string scope, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{scope}] {message}";
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                RotateIfNeeded();
                using var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var sw = new StreamWriter(fs, Utf8NoBom);
                sw.WriteLine(line);
            }
        }
        catch {}
    }

    public static void Write(string scope, string context, Exception ex)
    {
        try { Write(scope, $"{context}: {ex.GetType().Name}: {ex.Message}"); }
        catch { }
    }

    public static string ReadTail(int tailKb = 64)
    {
        try
        {
            if (!File.Exists(LogPath)) return "";
            var max = Math.Max(1, tailKb) * 1024L;
            lock (Gate)
            {
                using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read,
                                              FileShare.ReadWrite | FileShare.Delete);
                bool cut = fs.Length > max;
                if (cut) fs.Seek(-max, SeekOrigin.End);
                using var sr = new StreamReader(fs, Encoding.UTF8);
                var text = sr.ReadToEnd();
                if (cut)
                {
                    var nl = text.IndexOf('\n');
                    if (nl >= 0 && nl + 1 < text.Length) text = text[(nl + 1)..];
                }
                return text;
            }
        }
        catch { return ""; }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var fi = new FileInfo(LogPath);
            if (!fi.Exists || fi.Length <= MaxBytes) return;
            try { if (File.Exists(PrevLogPath)) File.Delete(PrevLogPath); } catch { }
            File.Move(LogPath, PrevLogPath);
        }
        catch { }
    }
}
