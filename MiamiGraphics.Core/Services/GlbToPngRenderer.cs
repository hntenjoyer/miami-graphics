#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace MiamiGraphics.Core.Services
{

    public static class GlbToPngRenderer
    {

        private static string FindRendererDir()
        {
            var pinned = Environment.GetEnvironmentVariable("MIAMI_RENDERER_DIR");
            if (!string.IsNullOrWhiteSpace(pinned))
            {
                try
                {
                    var full = Path.GetFullPath(pinned);
                    if (Directory.Exists(full) && File.Exists(Path.Combine(full, "render.js")))
                        return full;
                }
                catch { }
            }

            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";

            string[] candidates =
            {

                Path.Combine(asmDir, "Renderer"),

                Path.Combine(asmDir, "..", "..", "..", "..", "MiamiGraphics.Core", "Renderer"),
                Path.Combine(asmDir, "..", "..", "..", "MiamiGraphics.Core", "Renderer"),

                Path.Combine(asmDir, "..", "..", "..", "Renderer"),
                Path.Combine(asmDir, "..", "..", "..", "..", "Renderer"),

                Path.Combine(Directory.GetCurrentDirectory(), "Renderer"),
                Path.Combine(Directory.GetCurrentDirectory(), "MiamiGraphics.Core", "Renderer"),
            };
            foreach (var c in candidates)
            {
                string full;
                try { full = Path.GetFullPath(c); }
                catch { continue; }
                if (Directory.Exists(full) && File.Exists(Path.Combine(full, "render.js")))
                    return full;
            }
            return null;
        }

        private static string ResolveNodeExecutable(string rendererDir)
        {
            string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? ".";
            string[] candidates =
            {
                Path.Combine(rendererDir, "node.exe"),
                Path.Combine(asmDir, "Renderer", "node.exe"),
                Path.Combine(asmDir, "node", "node.exe"),
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    var full = Path.GetFullPath(candidate);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }

            return "node";
        }

        private static bool CheckNodeAvailable(string rendererDir, out string version, out string nodeExe)
        {
            version = null;
            nodeExe = ResolveNodeExecutable(rendererDir);
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = nodeExe,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                version = p.StandardOutput.ReadToEnd().Trim();
                p.WaitForExit(3000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        public static bool IsAvailable()
        {
            var dir = FindRendererDir();
            if (dir == null) return false;
            if (!Directory.Exists(Path.Combine(dir, "node_modules"))) return false;
            return CheckNodeAvailable(dir, out _, out _);
        }

        public static async Task<bool> RenderAsync(string glbPath, string pngPath,
            int width = 1024, int height = 512, string view = null)
        {
            if (!File.Exists(glbPath))
            {
                Console.WriteLine("[Renderer] GLB не найден: " + glbPath);
                return false;
            }

            string rendererDir = FindRendererDir();
            if (rendererDir == null)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Renderer] Папка Renderer/ не найдена.");
                Console.WriteLine("           Эта функция доступна только в DEV-режиме (исходники проекта).");
                Console.ResetColor();
                return false;
            }

            if (!CheckNodeAvailable(rendererDir, out string nodeVersion, out string nodeExe))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Renderer] Node.js не найден в PATH.");
                Console.WriteLine("           Установите Node.js 18+ с https://nodejs.org");
                Console.WriteLine("           Затем перезапустите программу.");
                Console.ResetColor();
                return false;
            }

            string nodeModules = Path.Combine(rendererDir, "node_modules");
            if (!Directory.Exists(nodeModules))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Renderer] Зависимости рендерера не установлены.");
                Console.WriteLine("           В папке: " + rendererDir);
                Console.WriteLine("           выполните:  npm install");
                Console.WriteLine("           (скачает Puppeteer + Chromium ~150 МБ, делается один раз)");
                Console.ResetColor();
                return false;
            }

            string scriptPath = Path.Combine(rendererDir, "render.js");
            string absoluteGlb = Path.GetFullPath(glbPath);
            string absolutePng = Path.GetFullPath(pngPath);

            Console.WriteLine("[Renderer] Node.js:  " + nodeVersion);
            Console.WriteLine("[Renderer] Renderer: " + rendererDir);
            Console.WriteLine("[Renderer] Input:    " + absoluteGlb);
            Console.WriteLine("[Renderer] Output:   " + absolutePng);

            var psi = new ProcessStartInfo
            {
                FileName = nodeExe,
                WorkingDirectory = rendererDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add(absoluteGlb);
            psi.ArgumentList.Add(absolutePng);
            psi.ArgumentList.Add(width.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add(height.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(view))
                psi.ArgumentList.Add(view);
            psi.Environment["PUPPETEER_CACHE_DIR"] = Path.Combine(rendererDir, ".cache", "puppeteer");

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    Console.WriteLine("[Renderer] Не удалось запустить node");
                    return false;
                }

                var stdoutTask = Task.Run(() =>
                {
                    string line;
                    while ((line = proc.StandardOutput.ReadLine()) != null)
                        Console.WriteLine("[Renderer] " + line);
                });
                var stderrTask = Task.Run(() =>
                {
                    string line;
                    while ((line = proc.StandardError.ReadLine()) != null)
                        if (!string.IsNullOrWhiteSpace(line))
                            Console.WriteLine("[Renderer/err] " + line);
                });

                await Task.Run(() => proc.WaitForExit());
                await stdoutTask;
                await stderrTask;

                if (proc.ExitCode != 0)
                {
                    Console.WriteLine("[Renderer] Node вернул код " + proc.ExitCode);
                    return false;
                }
                if (!File.Exists(absolutePng))
                {
                    Console.WriteLine("[Renderer] PNG не создан: " + absolutePng);
                    return false;
                }
                var info = new FileInfo(absolutePng);
                Console.WriteLine("[Renderer] ✓ PNG готов: " + absolutePng + " (" + info.Length + " байт)");
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Renderer] EXCEPTION: " + ex.Message);
                Console.ResetColor();
                return false;
            }
        }

        public static async Task<List<string>> RenderVariantsAsync(
            string glbPath, string outDir, IReadOnlyList<string> views,
            int width = 1024, int height = 1024)
        {
            var produced = new List<string>();
            if (!File.Exists(glbPath)) { Console.WriteLine("[Renderer] GLB не найден: " + glbPath); return produced; }
            if (views == null || views.Count == 0) return produced;

            string rendererDir = FindRendererDir();
            if (rendererDir == null) { Console.WriteLine("[Renderer] Папка Renderer/ не найдена."); return produced; }
            if (!CheckNodeAvailable(rendererDir, out string nodeVersion, out string nodeExe))
            {
                Console.WriteLine("[Renderer] Node.js не найден.");
                return produced;
            }
            if (!Directory.Exists(Path.Combine(rendererDir, "node_modules")))
            {
                Console.WriteLine("[Renderer] node_modules рендерера не установлены.");
                return produced;
            }

            string scriptPath  = Path.Combine(rendererDir, "render.js");
            string absoluteGlb = Path.GetFullPath(glbPath);
            string absoluteOut = Path.GetFullPath(outDir);
            Directory.CreateDirectory(absoluteOut);

            Console.WriteLine("[Renderer] Node.js:  " + nodeVersion);
            Console.WriteLine("[Renderer] Variants: " + string.Join(",", views) + " → " + absoluteOut);

            var psi = new ProcessStartInfo
            {
                FileName = nodeExe,
                WorkingDirectory = rendererDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(scriptPath);
            psi.ArgumentList.Add(absoluteGlb);
            psi.ArgumentList.Add(absoluteOut);
            psi.ArgumentList.Add(width.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add(height.ToString(CultureInfo.InvariantCulture));
            psi.ArgumentList.Add("--views=" + string.Join(",", views));
            psi.Environment["PUPPETEER_CACHE_DIR"] = Path.Combine(rendererDir, ".cache", "puppeteer");

            try
            {
                using var proc = Process.Start(psi);
                if (proc == null) { Console.WriteLine("[Renderer] Не удалось запустить node"); return produced; }

                var stdoutTask = Task.Run(() =>
                {
                    string line;
                    while ((line = proc.StandardOutput.ReadLine()) != null)
                        Console.WriteLine("[Renderer] " + line);
                });
                var stderrTask = Task.Run(() =>
                {
                    string line;
                    while ((line = proc.StandardError.ReadLine()) != null)
                        if (!string.IsNullOrWhiteSpace(line))
                            Console.WriteLine("[Renderer/err] " + line);
                });

                await Task.Run(() => proc.WaitForExit());
                await stdoutTask;
                await stderrTask;

                if (proc.ExitCode != 0)
                {
                    Console.WriteLine("[Renderer] Node вернул код " + proc.ExitCode);
                    return produced;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Renderer] EXCEPTION: " + ex.Message);
                return produced;
            }

            foreach (var v in views)
            {
                var f = Path.Combine(absoluteOut, v + ".webp");
                if (File.Exists(f)) produced.Add(f);
            }
            return produced;
        }
    }
}
