using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Shell.Services;

public sealed class RendererBootstrapper
{
    private const string RendererZipUrlCdn =
        "https://miamigraphicsstorage.uk/releases/MiamiGraphicsRenderer_6.zip";
    private const string RendererZipUrlRu =
        "https://ru.miamigraphicsstorage.uk/releases/MiamiGraphicsRenderer_6.zip";
    private const string RendererSha256 =
        "A5AF55B98379F614496D7B7C2C1B7CE2323695D31C9FB825C5194FA1E647E652";
    private const string ExpectedRendererVersion = "6";

    private static string GetRendererZipUrl() =>
        ServerRegionStore.Load() == ServerRegion.Ru ? RendererZipUrlRu : RendererZipUrlCdn;

    private static string RendererParentDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiamiGraphics");
    private static string RendererDir =>
        Path.Combine(RendererParentDir, "Renderer");
    public static string RendererDirPath => RendererDir;
    private static string NodeExePath =>
        Path.Combine(RendererDir, "node.exe");
    private static string RenderJsPath =>
        Path.Combine(RendererDir, "render.js");
    private static string VersionTxtPath =>
        Path.Combine(RendererDir, "version.txt");
    public static string PuppeteerCacheDir =>
        Path.Combine(RendererDir, ".cache", "puppeteer");
    private static string PuppeteerInstallScript =>
        Path.Combine(RendererDir, "node_modules", "puppeteer", "install.mjs");
    private static string ChromeHeadlessShellDir =>
        Path.Combine(PuppeteerCacheDir, "chrome-headless-shell");

    public sealed record EnsureResult(
        bool   Success,
        bool   AlreadyInstalled,
        string RendererPath,
        long   DownloadedBytes,
        string? ErrorMessage);

    public async Task<EnsureResult> EnsureInstalledAsync(
        IProgress<(string Phase, double Percent)>? progress = null,
        CancellationToken ct = default)
    {
        if (IsAlreadyInstalled())
            return new EnsureResult(true, true, RendererDir, 0, null);

        if (IsRendererFilesInstalled() && !IsChromiumInstalled())
        {
            var chromeOnly = await EnsureChromiumInstalledAsync(progress, ct);
            if (chromeOnly.Success)
                return new EnsureResult(true, false, RendererDir, 0, null);
            return new EnsureResult(false, false, RendererDir, 0,
                Loc.T("renderer.presentButChromiumMissing", ("reason", chromeOnly.ErrorMessage)));
        }

        progress?.Report(("downloading", 0));
        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var tmpDir = Path.Combine(lad, "MiamiGraphics", "tmp");
        Directory.CreateDirectory(tmpDir);
        var tmpZip = Path.Combine(tmpDir, "Renderer.tmp.zip");

        long downloaded;
        try
        {
            using var http = new HttpClient(new FragmentingHttpHandler(), disposeHandler: true)
            {
                Timeout = TimeSpan.FromMinutes(10),
            };
            var zipUrl = GetRendererZipUrl();
            Debug.WriteLine($"[renderer] downloading from {zipUrl}");
            using var resp = await http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1;

            await using var net = await resp.Content.ReadAsStreamAsync(ct);
            await using (var file = new FileStream(tmpZip, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buf = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await net.ReadAsync(buf, ct)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report(("downloading", done * 100.0 / total));
                }
                downloaded = done;
            }
        }
        catch (Exception ex)
        {
            return new EnsureResult(false, false, RendererDir, 0, $"Download failed: {ex.Message}");
        }

        progress?.Report(("verifying", 0));
        try
        {
            var actual = await Task.Run(() => ComputeSha256(tmpZip), ct);
            if (!string.Equals(actual, RendererSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(tmpZip); } catch { }
                return new EnsureResult(false, false, RendererDir, downloaded,
                    Loc.T("renderer.zipShaMismatch"));
            }
        }
        catch (Exception ex)
        {
            return new EnsureResult(false, false, RendererDir, downloaded, $"SHA-256 check failed: {ex.Message}");
        }

        progress?.Report(("extracting", 0));
        try
        {

            KillRendererProcesses();

            if (Directory.Exists(RendererDir))
            {
                if (!TryDeleteDirWithRetry(RendererDir, attempts: 5, delayMs: 300))
                {
                    Debug.WriteLine($"[renderer] could not clean old Renderer/, extracting on top (overwrite=true should still work)");
                }
            }

            Directory.CreateDirectory(RendererParentDir);
            await Task.Run(() => ZipFile.ExtractToDirectory(tmpZip, RendererParentDir, overwriteFiles: true), ct);
            try { File.Delete(tmpZip); } catch { }
        }
        catch (Exception ex)
        {
            return new EnsureResult(false, false, RendererDir, downloaded, $"Extract failed: {ex.Message}");
        }

        if (!IsAlreadyInstalled())
            return new EnsureResult(false, false, RendererDir, downloaded,
                Loc.T("renderer.extractedButKeyFilesMissing"));

        var chromeResult = await EnsureChromiumInstalledAsync(progress, ct);
        if (!chromeResult.Success)
        {
            return new EnsureResult(false, false, RendererDir, downloaded,
                Loc.T("renderer.extractedButChromiumMissing", ("reason", chromeResult.ErrorMessage)));
        }

        progress?.Report(("done", 100));
        return new EnsureResult(true, false, RendererDir, downloaded, null);
    }

    public sealed record ChromiumInstallResult(bool Success, bool AlreadyInstalled, string? ErrorMessage);

    public async Task<ChromiumInstallResult> EnsureChromiumInstalledAsync(
        IProgress<(string Phase, double Percent)>? progress = null,
        CancellationToken ct = default)
    {
        if (IsChromiumInstalled())
        {
            Debug.WriteLine($"[renderer] chrome-headless-shell already present at {ChromeHeadlessShellDir}");
            return new ChromiumInstallResult(true, true, null);
        }

        if (!File.Exists(NodeExePath))
            return new ChromiumInstallResult(false, false, "node.exe not on disk");
        if (!File.Exists(PuppeteerInstallScript))
            return new ChromiumInstallResult(false, false, $"puppeteer install.mjs missing at {PuppeteerInstallScript}");

        try { Directory.CreateDirectory(PuppeteerCacheDir); }
        catch (Exception ex) { return new ChromiumInstallResult(false, false, $"create cache dir failed: {ex.Message}"); }

        progress?.Report(("chromium-download", 0));
        Debug.WriteLine($"[renderer] running puppeteer install.mjs -> {PuppeteerCacheDir}");

        var psi = new ProcessStartInfo
        {
            FileName = NodeExePath,
            WorkingDirectory = RendererDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(PuppeteerInstallScript);
        psi.Environment["PUPPETEER_CACHE_DIR"] = PuppeteerCacheDir;
        psi.Environment["PUPPETEER_SKIP_DOWNLOAD"] = "false";

        var stdoutSb = new StringBuilder();
        var stderrSb = new StringBuilder();

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
                return new ChromiumInstallResult(false, false, Loc.T("renderer.processStartNull"));

            var stdoutTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync()) is not null)
                {
                    stdoutSb.AppendLine(line);
                    Debug.WriteLine($"[renderer.install] {line}");
                    if (line.Contains("Downloading", StringComparison.OrdinalIgnoreCase))
                        progress?.Report(("chromium-download", 30));
                    else if (line.Contains("Extracting", StringComparison.OrdinalIgnoreCase))
                        progress?.Report(("chromium-extract", 75));
                }
            });
            var stderrTask = Task.Run(async () =>
            {
                string? line;
                while ((line = await proc.StandardError.ReadLineAsync()) is not null)
                {
                    stderrSb.AppendLine(line);
                    Debug.WriteLine($"[renderer.install/err] {line}");
                }
            });

            var exited = await Task.Run(() => proc.WaitForExit(300_000), ct);
            if (!exited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return new ChromiumInstallResult(false, false, Loc.T("renderer.chromiumTimeout"));
            }
            await Task.WhenAll(stdoutTask, stderrTask);

            if (proc.ExitCode != 0)
            {
                var tail = stderrSb.Length > 800 ? stderrSb.ToString(stderrSb.Length - 800, 800) : stderrSb.ToString();
                return new ChromiumInstallResult(false, false, $"node exit={proc.ExitCode}: {tail.Trim()}");
            }

            if (!IsChromiumInstalled())
                return new ChromiumInstallResult(false, false,
                    Loc.T("renderer.chromeShellNotFoundAfterInstall", ("dir", ChromeHeadlessShellDir)));

            progress?.Report(("chromium-done", 100));
            return new ChromiumInstallResult(true, false, null);
        }
        catch (Exception ex)
        {
            return new ChromiumInstallResult(false, false, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public static bool IsChromiumInstalled()
    {
        if (!Directory.Exists(ChromeHeadlessShellDir)) return false;
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(ChromeHeadlessShellDir))
            {
                var exes = Directory.EnumerateFiles(sub, "chrome-headless-shell.exe", SearchOption.AllDirectories);
                if (exes.Any()) return true;
            }
        }
        catch { }
        return false;
    }

    public static bool IsAlreadyInstalled()
    {
        if (!File.Exists(NodeExePath) || !File.Exists(RenderJsPath)) return false;
        if (!IsChromiumInstalled()) return false;
        try
        {
            var ver = File.Exists(VersionTxtPath)
                ? File.ReadAllText(VersionTxtPath).Trim()
                : "1";
            return string.Equals(ver, ExpectedRendererVersion, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    public static bool IsRendererFilesInstalled()
    {
        if (!File.Exists(NodeExePath) || !File.Exists(RenderJsPath)) return false;
        try
        {
            var ver = File.Exists(VersionTxtPath)
                ? File.ReadAllText(VersionTxtPath).Trim()
                : "1";
            return string.Equals(ver, ExpectedRendererVersion, StringComparison.Ordinal);
        }
        catch { return false; }
    }

    public sealed record ProbeResult(
        string  RendererPath,
        bool    BaseDirExists,
        bool    NodeExeExists,
        bool    RenderJsExists,
        bool    NodeModulesExists,
        long    NodeModulesSizeMb,
        string? NodeVersion,
        string? NodeError,
        bool    ChromiumInstalled,
        bool    IsUsable)
    {
        public string Summary =>
            $"baseDir={BaseDirExists}, node.exe={NodeExeExists}, render.js={RenderJsExists}, " +
            $"node_modules={NodeModulesExists} ({NodeModulesSizeMb} MB), " +
            $"chromium={ChromiumInstalled}, " +
            $"node={NodeVersion ?? "FAIL: " + (NodeError ?? "?")}";

        public string ActionableHint =>
            !BaseDirExists
              ? Loc.T("renderer.hintMissing")
              : !NodeExeExists || !RenderJsExists
                ? Loc.T("renderer.hintCorrupt")
                : !NodeModulesExists || NodeModulesSizeMb < 50
                  ? Loc.T("renderer.hintNodeModulesCorrupt", ("mb", NodeModulesSizeMb))
                  : NodeError is not null
                    ? Loc.T("renderer.hintNodeWontStart", ("reason", NodeError))
                    : !ChromiumInstalled
                      ? Loc.T("renderer.hintChromiumMissing")
                      : Loc.T("renderer.hintReady");
    }

    public static ProbeResult Probe()
    {
        var baseDir = RendererDir;
        var nodeExe = NodeExePath;
        var renderJs = RenderJsPath;
        var nodeModules = Path.Combine(baseDir, "node_modules");

        bool baseDirExists = Directory.Exists(baseDir);
        bool nodeExeExists = File.Exists(nodeExe);
        bool renderJsExists = File.Exists(renderJs);
        bool nodeModulesExists = Directory.Exists(nodeModules);

        long nodeModulesSizeMb = 0;
        if (nodeModulesExists)
        {
            try
            {
                long bytes = 0;
                foreach (var f in Directory.EnumerateFiles(nodeModules, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; } catch { }
                    if (bytes > 300L * 1024 * 1024) break;
                }
                nodeModulesSizeMb = bytes / 1024 / 1024;
            }
            catch { }
        }

        string? nodeVersion = null;
        string? nodeError = null;
        if (nodeExeExists)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = nodeExe,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                if (p is not null)
                {
                    nodeVersion = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(3000);
                    if (p.ExitCode != 0) nodeError = $"exit code {p.ExitCode}";
                }
                else nodeError = "Process.Start returned null";
            }
            catch (Exception ex) { nodeError = ex.GetType().Name + ": " + ex.Message; }
        }
        else nodeError = "node.exe not on disk";

        bool chromiumInstalled = IsChromiumInstalled();

        bool isUsable = baseDirExists && nodeExeExists && renderJsExists
            && nodeModulesExists && nodeModulesSizeMb >= 50
            && nodeError is null && !string.IsNullOrWhiteSpace(nodeVersion)
            && chromiumInstalled;

        return new ProbeResult(baseDir, baseDirExists, nodeExeExists, renderJsExists,
            nodeModulesExists, nodeModulesSizeMb, nodeVersion, nodeError, chromiumInstalled, isUsable);
    }

    public async Task<EnsureResult> ForceReinstallAsync(
        IProgress<(string Phase, double Percent)>? progress = null,
        CancellationToken ct = default)
    {
        if (Directory.Exists(RendererDir))
        {
            progress?.Report(("cleanup", 0));
            KillRendererProcesses();
            TryDeleteDirWithRetry(RendererDir, attempts: 5, delayMs: 300);
        }
        return await EnsureInstalledAsync(progress, ct);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static void KillRendererProcesses()
    {
        try
        {
            foreach (var name in new[] { "node", "chrome" })
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try
                    {
                        var path = proc.MainModule?.FileName;
                        if (path is not null && path.StartsWith(RendererDir, StringComparison.OrdinalIgnoreCase))
                        {
                            Debug.WriteLine($"[renderer] killing leftover {name} pid={proc.Id}");
                            try { proc.Kill(entireProcessTree: true); proc.WaitForExit(2000); }
                            catch (Exception kx) { Debug.WriteLine($"[renderer] kill failed: {kx.Message}"); }
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[renderer] inspect pid={proc.Id}: {ex.Message}"); }
                    finally { proc.Dispose(); }
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[renderer] KillRendererProcesses: {ex.Message}"); }
    }

    private static bool TryDeleteDirWithRetry(string path, int attempts, int delayMs)
    {
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                if (!Directory.Exists(path)) return true;

                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(path, recursive: true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[renderer] delete attempt {i}: {ex.Message}");
                Thread.Sleep(delayMs);
            }
        }
        return false;
    }
}
