using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Core.Injector
{
    public static class ArchiveFixer
    {
        private static string? _cachedExePath;

        public static string ResolveExePath()
            => _cachedExePath ??= ResolveArchiveFixPath();

        public static bool Fix(string rpfPath, string logicalName)
        {
            if (string.Equals(Path.GetFileName(rpfPath), logicalName, StringComparison.OrdinalIgnoreCase))
                return Fix(rpfPath);

            var stageDir = Path.Combine(Path.GetTempPath(), "mg_afix_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stageDir);
            var staged = Path.Combine(stageDir, logicalName);
            try
            {
                File.Copy(rpfPath, staged, overwrite: true);
                bool ok = Fix(staged);
                if (ok) File.Copy(staged, rpfPath, overwrite: true);
                return ok;
            }
            finally { try { Directory.Delete(stageDir, recursive: true); } catch { } }
        }

        public static void FixOrThrow(string rpfPath, string? logicalName = null)
        {
            bool ok = logicalName is null ? Fix(rpfPath) : Fix(rpfPath, logicalName);
            if (!ok)
                throw new InvalidOperationException(Loc.T("error.archiveFixFailed",
                    ("file", logicalName ?? Path.GetFileName(rpfPath))));
        }

        public static bool Fix(string rpfPath)
        {
            var exe = ResolveExePath();
            if (!File.Exists(exe))
            {
                Console.WriteLine($"[ArchiveFix] WARN: ArchiveFix.exe не найден - пропускаю fix-up для {Path.GetFileName(rpfPath)}");
                return false;
            }

            Console.WriteLine($"[ArchiveFix] Запуск исправления хешей: {Path.GetFileName(rpfPath)} (exe: {exe})");

            var keysDir = Path.GetDirectoryName(exe);
            var startInfo = new ProcessStartInfo
            {
                FileName  = "cmd.exe",
                Arguments = $"/c \"\"{exe}\" \"{rpfPath}\" < NUL\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = keysDir
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    Console.WriteLine("[ArchiveFix] ERROR: Process.Start вернул null");
                    return false;
                }

                var output = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.ErrorDataReceived  += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                long sizeBytes = 0;
                try { sizeBytes = new FileInfo(rpfPath).Length; } catch { }
                int timeoutMs = (int)Math.Clamp(sizeBytes / (5L * 1024 * 1024) * 1000, 60_000, 600_000);

                bool exited = process.WaitForExit(timeoutMs);
                if (!exited)
                {
                    Console.WriteLine($"[ArchiveFix] ERROR: timeout {timeoutMs / 1000}s ({sizeBytes / (1024 * 1024)} MB), killing");
                    try { process.Kill(entireProcessTree: true); } catch { }
                    return false;
                }

                string log;
                lock (output) log = output.ToString();

                if (log.IndexOf("cryptokey", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    log.IndexOf("0 cryptokeys", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    log.IndexOf("Error loading crypto keys", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Console.WriteLine($"[ArchiveFix] ERROR: ключи не найдены рядом с exe ({keysDir}). RPF не пофикшен: {Path.GetFileName(rpfPath)}");
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    Console.WriteLine($"[ArchiveFix] ERROR: exit code {process.ExitCode}. Вывод: {log.Trim()}");
                    return false;
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[ArchiveFix] Успешно: {Path.GetFileName(rpfPath)}");
                Console.ResetColor();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ArchiveFix] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }

        private static string ResolveArchiveFixPath()
        {
            var relCandidates = new[]
            {
                Path.Combine("additionals", "keys", "ArchiveFix.exe"),
                Path.Combine("additionals", "Keys", "ArchiveFix.exe"),
                Path.Combine("additionals", "ArchiveFix.exe"),
                Path.Combine("Tools", "ArchiveFix.exe"),
            };

            string[] directCandidates = relCandidates
                .Select(r => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, r))
                .ToArray();
            foreach (var c in directCandidates)
                if (File.Exists(c)) return c;

            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                foreach (var rel in relCandidates)
                {
                    var p = Path.Combine(dir, rel);
                    if (File.Exists(p)) return p;
                }
                dir = Path.GetDirectoryName(dir);
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var rel in relCandidates)
            {
                var p = Path.Combine(localAppData, "MiamiGraphics", rel);
                if (File.Exists(p)) return p;
            }

            return directCandidates[0];
        }
    }
}
