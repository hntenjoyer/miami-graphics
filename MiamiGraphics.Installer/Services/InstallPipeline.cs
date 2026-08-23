using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;

namespace MiamiGraphics.Installer.Services;

public sealed class InstallPipeline
{
    public string InstallRoot { get; set; }

    private const string PayloadUrl =
        "https://miamigraphicsstorage.uk/releases/MiamiGraphicsPayload_608c6008746846eb_v1.5.5.zip";

    private const string PayloadSha256 =
        "62E15432E673D57FD219BC5F10F89BDF1E166BEFCF2EE09A22E4842F3B453BF7";
    private const long PayloadSizeBytes = 173339860;

    private static readonly string[] PayloadMirrors = BuildPayloadMirrors(PayloadUrl);

    private static string[] BuildPayloadMirrors(string primary)
    {
        try
        {
            var uri = new Uri(primary);
            var path = uri.PathAndQuery;
            return new[]
            {
                primary,
                "https://ru.miamigraphicsstorage.uk" + path,
                "https://cdn.miamigraphicsstorage.uk" + path,
            };
        }
        catch { return new[] { primary }; }
    }

    public bool PreferRu { get; set; }

    private async Task<string[]> ResolveMirrorsAsync()
    {
        if (!PreferRu) return await ProbeAndOrderMirrors(PayloadMirrors);

        var uri = new Uri(PayloadUrl);
        var path = uri.PathAndQuery;
        var ru  = "https://ru.miamigraphicsstorage.uk"  + path;
        var cdn = "https://cdn.miamigraphicsstorage.uk" + path;
        var hub = await TryResolveHubAsync(uri.AbsolutePath).ConfigureAwait(false);
        return hub != null
            ? new[] { hub, ru, cdn, PayloadUrl }
            : new[] { ru, cdn, PayloadUrl };
    }

    private static readonly string[] HubEntries =
    {
        "https://ru.miamigraphicsstorage.uk",
        "https://hnt.miamigraphicsstorage.uk",
    };

    private static readonly string[] TrustedDownloadDomains =
    {
        "miamigraphicsstorage.uk",
        "miami-graphics.com",
    };

    private static bool IsTrustedDownloadUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;

        var host = uri.Host.TrimEnd('.');
        foreach (var domain in TrustedDownloadDomains)
        {
            if (host.Equals(domain, StringComparison.OrdinalIgnoreCase)) return true;
            if (host.Length > domain.Length + 1
                && host[host.Length - domain.Length - 1] == '.'
                && host.EndsWith(domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task<string?> TryResolveHubAsync(string keyPath)
    {
        var key = keyPath.TrimStart('/');
        foreach (var hub in HubEntries)
        {
            try
            {
                using var h = new HttpClient(new FragmentingHttpHandler()) { Timeout = TimeSpan.FromSeconds(8) };
                var s = await h.GetStringAsync(
                    $"{hub}/route?key={Uri.EscapeDataString(key)}&format=json").ConfigureAwait(false);
                using var doc = JsonDocument.Parse(s);
                if (doc.RootElement.TryGetProperty("url", out var u))
                {
                    var url = u.GetString();
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    if (!IsTrustedDownloadUrl(url))
                    {
                        Debug.WriteLine($"[hub] {hub} вернул чужой адрес - игнорирую: {url}");
                        continue;
                    }
                    return url;
                }
            }
            catch {}
        }
        return null;
    }

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StallTimeout   = TimeSpan.FromSeconds(12);

    private const string VelopackSetupUrl =
        "https://cdn.miamigraphicsstorage.uk/velopack/Miami.Graphics-win-Setup.exe";

    private static readonly string VelopackSetupSha256 = "";

    private const bool BridgingMode = false;

    private const string AppName = "Miami Graphics";

    public event Action<string, string>? StepStarted;
    public event Action<string, bool>?   StepCompleted;
    public event Action<double>?         OverallProgress;
    public event Action<string>?         DetailUpdated;

    public InstallPipeline()
    {
        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        InstallRoot = DetectExistingInstallRoot() ?? Path.Combine(lad, AppName);
    }

    private static string? DetectExistingInstallRoot()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MiamiGraphics");
            if (key?.GetValue("InstallLocation") is not string loc
                || string.IsNullOrWhiteSpace(loc)
                || !Directory.Exists(loc))
                return null;
            Debug.WriteLine($"[install-root] reusing existing install location: {loc}");
            return loc;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[install-root] registry probe failed: {ex.Message}");
            return null;
        }
    }

    public async Task RunAsync()
    {
        if (BridgingMode)
        {
            await Step("prep", "Подготовка",           weight: 10, RunBridgingPrep);
            await Step("dl",   "Загрузка установщика", weight: 80, RunBridgingDownload);
            await Step("hand", "Запуск установки",     weight: 10, RunBridgingHandoff);
            return;
        }

        await Step("prep",  "Подготовка",       weight: 5,  RunWipe);
        await Step("dl",    "Загрузка",         weight: 70, RunDownload);
        await Step("ext",   "Распаковка",       weight: 18, RunExtract);
        await Step("final", "Финальный штрих",  weight: 7,  RunFinal);
    }

    private Task RunBridgingPrep(Action<double> progress)
    {
        return Task.Run(() =>
        {
            try
            {
                var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var legacy = Path.Combine(lad, "Miami Graphics");
                if (Directory.Exists(legacy))
                {
                    DetailUpdated?.Invoke("Удаляю старую установку…");
                    KillProcessByName("Miami Graphics");
                    KillProcessByName("Miami Installer");
                    Thread.Sleep(500);

                    try { Directory.Delete(legacy, recursive: true); }
                    catch (Exception ex) { Debug.WriteLine($"[bridge-prep] legacy cleanup: {ex.Message}"); }
                }
                progress(50);

                try
                {
                    using var hkcu = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: true);
                    hkcu?.DeleteSubKeyTree("MiamiGraphics", throwOnMissingSubKey: false);
                }
                catch (Exception ex) { Debug.WriteLine($"[bridge-prep] HKCU uninstall: {ex.Message}"); }
                progress(100);
            }
            catch (Exception ex) { Debug.WriteLine($"[bridge-prep] {ex.Message}"); }
        });
    }

    private async Task RunBridgingDownload(Action<double> progress)
    {
        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var tmpDir = Path.Combine(lad, "MiamiGraphics", "tmp");
        Directory.CreateDirectory(tmpDir);
        var stubPath = Path.Combine(tmpDir, "Miami.Graphics-win-Setup.exe");

        if (string.IsNullOrWhiteSpace(VelopackSetupSha256))
            throw new InvalidOperationException(
                "Режим bridging включён, но в сборку не вшит эталон установщика (SHA-256). " +
                "Установка отменена.");
        if (!IsTrustedDownloadUrl(VelopackSetupUrl))
            throw new InvalidOperationException("Адрес установщика не входит в список доверенных. Установка отменена.");

        using var http = new HttpClient(new FragmentingHttpHandler(), disposeHandler: true)
        {
            Timeout = TimeSpan.FromMinutes(20),
        };
        using var resp = await http.GetAsync(VelopackSetupUrl, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1;

        await using var net = await resp.Content.ReadAsStreamAsync();
        await using var file = new FileStream(stubPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        var buf = new byte[1 << 16];
        long done = 0;
        var sw = Stopwatch.StartNew();
        long lastReportMs = 0;
        int read;
        while ((read = await net.ReadAsync(buf)) > 0)
        {
            await file.WriteAsync(buf.AsMemory(0, read));
            done += read;
            if (sw.ElapsedMilliseconds - lastReportMs > 200)
            {
                double pct = total > 0 ? done * 100.0 / total : 0;
                progress(pct);
                double speedMbps = done / 1024.0 / 1024.0 / Math.Max(0.001, sw.ElapsedMilliseconds / 1000.0);
                DetailUpdated?.Invoke(total > 0 ? $"{done * 100 / total}%  ·  {speedMbps:F1} MB/s" : $"{speedMbps:F1} MB/s");
                lastReportMs = sw.ElapsedMilliseconds;
            }
        }
        DetailUpdated?.Invoke("Проверяю целостность установщика…");
        var actual = await Task.Run(() => ComputeSha256(stubPath));
        if (!string.Equals(actual, VelopackSetupSha256, StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[bridge-dl] sha256 {actual}, ожидался {VelopackSetupSha256}");
            TryDeleteFileWithRetry(stubPath);
            throw new IOException(
                "Скачанный установщик не прошёл проверку подлинности и был удалён. Установка отменена.");
        }

        progress(100);
        _bridgingStubPath = stubPath;
    }

    private string? _bridgingStubPath;

    private Task RunBridgingHandoff(Action<double> progress)
    {
        return Task.Run(() =>
        {
            if (string.IsNullOrEmpty(_bridgingStubPath) || !File.Exists(_bridgingStubPath))
                throw new InvalidOperationException("Velopack stub не был скачан.");

            DetailUpdated?.Invoke("Запускаю установщик Miami Graphics…");
            var psi = new ProcessStartInfo
            {
                FileName = _bridgingStubPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(_bridgingStubPath) ?? "",
            };
            Process.Start(psi);
            progress(100);
        });
    }

    private static void KillProcessByName(string name)
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try { proc.Kill(entireProcessTree: true); proc.WaitForExit(2000); }
                catch (Exception ex) { Debug.WriteLine($"[kill] {name} pid={proc.Id}: {ex.Message}"); }
                finally { proc.Dispose(); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[kill] {name}: {ex.Message}"); }
    }

    private async Task Step(string id, string label, int weight, Func<Action<double>, Task> body)
    {
        StepStarted?.Invoke(id, label);
        var prevTotal = _completedWeight;
        await body(p =>
        {
            var overall = prevTotal + (weight * p / 100.0);
            OverallProgress?.Invoke(overall);
        });
        _completedWeight += weight;
        OverallProgress?.Invoke(_completedWeight);
        StepCompleted?.Invoke(id, true);
    }
    private double _completedWeight;

    private Task RunWipe(Action<double> progress)
    {
        return Task.Run(() =>
        {
            DetailUpdated?.Invoke("Закрываю запущенный лаунчер…");
            KillProcessByName("Miami Graphics");
            Thread.Sleep(500);
            DetailUpdated?.Invoke("");

            var paths = new List<string>();
            var pfx64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var lad   = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var ad    = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var tmp   = Path.GetTempPath();
            foreach (var pf in new[] { pfx64, pfx86 })
            foreach (var name in new[] { "Miami Graphics", "MiamiGraphics" })
                paths.Add(Path.Combine(pf, name));
            foreach (var name in new[] { "MiamiGraphics", "Miami Graphics", "MiamiGraphics" })
            {
                paths.Add(Path.Combine(lad, name));
                paths.Add(Path.Combine(ad, name));
                paths.Add(Path.Combine(tmp, name));
            }

            string? preservedInstalledVersion = null;
            try
            {
                var markerPath = Path.Combine(lad, "MiamiGraphics", "config", "installed_version.txt");
                if (File.Exists(markerPath))
                    preservedInstalledVersion = File.ReadAllText(markerPath).Trim();
            }
            catch (Exception ex) { Debug.WriteLine($"[wipe] preserve marker: {ex.Message}"); }

            var stateDir = Path.Combine(lad, "MiamiGraphics");
            for (int i = 0; i < paths.Count; i++)
            {
                var p = paths[i];
                if (string.Equals(p, InstallRoot, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(p, stateDir,    StringComparison.OrdinalIgnoreCase)) continue;
                if (Directory.Exists(p))
                {
                    try { DeleteDirectoryWithRetry(p); }
                    catch (Exception ex) { Debug.WriteLine($"[wipe] {p}: {ex.Message}"); }
                }
                progress((i + 1) * 100.0 / paths.Count);
            }

            if (preservedInstalledVersion is not null)
            {
                try
                {
                    var dir = Path.Combine(lad, "MiamiGraphics", "config");
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "installed_version.txt"), preservedInstalledVersion);
                    Debug.WriteLine($"[wipe] preserved installed_version = {preservedInstalledVersion}");
                }
                catch (Exception ex) { Debug.WriteLine($"[wipe] restore marker: {ex.Message}"); }
            }
            DetailUpdated?.Invoke("");

            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                hklm.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B5C65F97-79B5-41F2-8F3C-9891146D0632}_is1", throwOnMissingSubKey: false);
            }
            catch (Exception ex) { Debug.WriteLine($"[wipe] reg-key: {ex.Message}"); }
            try
            {
                using var hkcu = Registry.CurrentUser;
                hkcu.DeleteSubKeyTree(@"Software\HunterGraphics", throwOnMissingSubKey: false);
            }
            catch (Exception ex) { Debug.WriteLine($"[wipe] HKCU legacy: {ex.Message}"); }
        });
    }

    private async Task RunDownload(Action<double> progress)
    {
        Directory.CreateDirectory(InstallRoot);
        var tmpZip = Path.Combine(InstallRoot, "payload.tmp.zip");

        Action<double> payloadProgress = progress;

        DetailUpdated?.Invoke("Выбираю быстрый сервер…");

        var mirrors = (await ResolveMirrorsAsync()).Where(IsTrustedDownloadUrl).ToArray();
        if (mirrors.Length == 0)
            throw new IOException("Не осталось ни одного доверенного адреса загрузки. Установка отменена.");

        Exception? lastError = null;
        bool integrityFailed = false;

        const int passes = 3;
        for (int pass = 0; pass < passes; pass++)
        {
            if (pass > 0)
            {
                DetailUpdated?.Invoke($"Сервера не ответили, пробую ещё раз ({pass + 1}/{passes})…");
                await Task.Delay(TimeSpan.FromSeconds(4 * pass));
            }

            for (int i = 0; i < mirrors.Length; i++)
            {
                var url = mirrors[i];
                bool isFallback = i > 0 || pass > 0;
                try
                {
                    if (isFallback)
                        DetailUpdated?.Invoke("Основной сервер недоступен, переключаюсь на резервный…");
                    else
                        DetailUpdated?.Invoke("Подключаюсь к серверу…");

                    if (await TryDownloadFromMirror(url, tmpZip, payloadProgress, minSpeedFailover: pass == 0))
                    {
                        DetailUpdated?.Invoke("Проверяю целостность архива…");
                        var verdict = await Task.Run(() => VerifyPayload(tmpZip));
                        if (verdict is null)
                            return;
                        Debug.WriteLine($"[download] {url}: архив не совпал с эталоном - {verdict}");

                        integrityFailed = true;
                        TryDeleteFileWithRetry(tmpZip);
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Debug.WriteLine($"[download] mirror {url} failed (pass {pass + 1}): {ex.Message}");
                }
                TryDeleteFileWithRetry(tmpZip);
            }
        }
        if (integrityFailed)
            throw new IOException(
                "Скачанный установочный архив не прошёл проверку подлинности и был удалён. " +
                "Установка отменена. Это бывает при подмене файла в сети (публичный Wi-Fi, " +
                "«ускорители» и антивирусы с перехватом трафика) - попробуйте другую сеть " +
                "или скачайте установщик заново с сайта.", lastError);
        throw new IOException(
            "Не удалось скачать установочные файлы ни с одного сервера. " +
            "Проверьте интернет-соединение и попробуйте снова.", lastError);
    }

    private static string? VerifyPayload(string path)
    {
        try
        {
            var len = new FileInfo(path).Length;
            if (len != PayloadSizeBytes)
                return $"размер {len} B, ожидался {PayloadSizeBytes} B";

            var actual = ComputeSha256(path);
            if (!string.Equals(actual, PayloadSha256, StringComparison.OrdinalIgnoreCase))
                return $"sha256 {actual}, ожидался {PayloadSha256}";

            Debug.WriteLine($"[verify] payload ok: {len} B, sha256 {actual}");
            return null;
        }
        catch (Exception ex)
        {
            return $"проверка не выполнена: {ex.Message}";
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static async Task<string[]> ProbeAndOrderMirrors(string[] mirrors)
    {
        if (mirrors.Length <= 1) return mirrors;
        var speeds = new double[mirrors.Length];
        var tasks = new Task[mirrors.Length];
        for (int i = 0; i < mirrors.Length; i++)
        {
            int idx = i;
            tasks[i] = Task.Run(async () => { speeds[idx] = await ProbeMirrorMbps(mirrors[idx]); });
        }
        try { await Task.WhenAll(tasks); } catch { }

        var order = new List<int>();
        for (int i = 0; i < mirrors.Length; i++) order.Add(i);
        order.Sort((a, b) =>
        {
            bool aok = speeds[a] > 0, bok = speeds[b] > 0;
            if (aok != bok) return aok ? -1 : 1;
            if (aok && bok) return speeds[b].CompareTo(speeds[a]);
            return a.CompareTo(b);
        });
        var result = new string[mirrors.Length];
        for (int i = 0; i < order.Count; i++) result[i] = mirrors[order[i]];
        try
        {
            var hosts = new string[result.Length];
            for (int i = 0; i < result.Length; i++) hosts[i] = new Uri(result[i]).Host;
            Debug.WriteLine("[download.probe] order: " + string.Join(" > ", hosts));
        }
        catch { }
        return result;
    }

    private static async Task<double> ProbeMirrorMbps(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Range = new global::System.Net.Http.Headers.RangeHeaderValue(0, 512 * 1024 - 1);
            var sw = global::System.Diagnostics.Stopwatch.StartNew();
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            if (!resp.IsSuccessStatusCode) return -1;
            var buf = await resp.Content.ReadAsByteArrayAsync();
            sw.Stop();
            return buf.Length / 1024.0 / 1024.0 / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        }
        catch { return -1; }
    }

    private async Task<bool> TryDownloadFromMirror(string url, string tmpZip, Action<double> progress, bool minSpeedFailover = false)
    {
        using var http = new HttpClient(new FragmentingHttpHandler(), disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        HttpResponseMessage resp;
        using (var connectCts = new CancellationTokenSource(ConnectTimeout))
        {
            try
            {
                resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, connectCts.Token);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine($"[download] {url}: connect timeout");
                return false;
            }
        }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[download] {url}: HTTP {(int)resp.StatusCode}");
                return false;
            }
            var total = resp.Content.Headers.ContentLength ?? -1;

            if (total > 0 && total != PayloadSizeBytes)
            {
                Debug.WriteLine($"[download] {url}: размер {total} != эталон {PayloadSizeBytes} - зеркало отдаёт не наш файл");
                return false;
            }

            await using var net = await resp.Content.ReadAsStreamAsync();
            await using var file = new FileStream(tmpZip, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
            var buf = new byte[1 << 16];
            long done = 0;
            var sw = Stopwatch.StartNew();
            long lastReportBytes = 0;
            long lastReportMs = 0;

            const long SpeedWindowMs   = 25_000;
            const long MinWindowBytes  = 50 * 1024 * SpeedWindowMs / 1000;
            long windowStartMs = 0, windowStartBytes = 0;
            bool firstWindow = true;

            using var stallCts = new CancellationTokenSource();
            stallCts.CancelAfter(StallTimeout);
            while (true)
            {
                int read;
                try
                {
                    read = await net.ReadAsync(buf, stallCts.Token);
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"[download] {url}: stalled (no bytes {StallTimeout.TotalSeconds}s) at {done}/{total}");
                    return false;
                }
                if (read <= 0) break;

                await file.WriteAsync(buf.AsMemory(0, read));
                done += read;
                stallCts.CancelAfter(StallTimeout);

                if (minSpeedFailover && total > 0 && done < total
                    && sw.ElapsedMilliseconds - windowStartMs >= SpeedWindowMs)
                {
                    long windowBytes = done - windowStartBytes;
                    if (!firstWindow && windowBytes < MinWindowBytes)
                    {
                        Debug.WriteLine($"[download] {url}: throttled ({windowBytes / 1024} KB per {SpeedWindowMs / 1000}s window) at {done}/{total} - failing over");
                        return false;
                    }
                    firstWindow = false;
                    windowStartMs = sw.ElapsedMilliseconds;
                    windowStartBytes = done;
                }

                if (sw.ElapsedMilliseconds - lastReportMs > 200)
                {
                    double pct = total > 0 ? done * 100.0 / total : 0;
                    progress(pct);
                    double speedMbps = (done - lastReportBytes) / 1024.0 / 1024.0
                                       / Math.Max(0.001, (sw.ElapsedMilliseconds - lastReportMs) / 1000.0);
                    DetailUpdated?.Invoke(
                        total > 0
                            ? $"{done * 100 / total}%  ·  {speedMbps:F1} MB/s"
                            : $"{speedMbps:F1} MB/s");
                    lastReportMs = sw.ElapsedMilliseconds;
                    lastReportBytes = done;
                }
            }

            if (total > 0 && done < total)
            {
                Debug.WriteLine($"[download] {url}: incomplete {done}/{total}");
                return false;
            }

            progress(100);
            return true;
        }
    }

    private Task RunExtract(Action<double> progress)
    {
        return Task.Run(() =>
        {
            var appDir = Path.Combine(InstallRoot, "app");
            var zipToCheck = Path.Combine(InstallRoot, "payload.tmp.zip");
            var verdict = VerifyPayload(zipToCheck);
            if (verdict is not null)
            {
                Debug.WriteLine($"[extract] payload не совпал с эталоном перед распаковкой - {verdict}");
                TryDeleteFileWithRetry(zipToCheck);
                throw new IOException(
                    "Установочный архив не прошёл проверку подлинности перед распаковкой и был удалён. " +
                    "Установка отменена - ничего не записано. Запустите установщик ещё раз.");
            }

            if (Directory.Exists(appDir)) DeleteDirectoryWithRetry(appDir);
            Directory.CreateDirectory(appDir);
            var tmpZip = Path.Combine(InstallRoot, "payload.tmp.zip");

            using var zip = ZipFile.OpenRead(tmpZip);
            var entries = zip.Entries;
            var appRoot = Path.GetFullPath(appDir);
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                var resolved = Path.GetFullPath(Path.Combine(appDir, e.FullName));
                if (resolved != appRoot &&
                    !resolved.StartsWith(appRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new IOException($"Unsafe zip entry escapes target directory: {e.FullName}");
                if (string.IsNullOrEmpty(e.Name))
                {
                    Directory.CreateDirectory(resolved);
                    continue;
                }
                var dest = resolved;
                var parent = Path.GetDirectoryName(dest);
                if (parent is not null) Directory.CreateDirectory(parent);
                try { e.ExtractToFile(dest, overwrite: true); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    Debug.WriteLine($"[extract] locked {dest}: {ex.Message} - evicting");
                    EvictLockedFile(dest);
                    e.ExtractToFile(dest, overwrite: true);
                }
                if (i % 50 == 0)
                {
                    progress(i * 100.0 / entries.Count);
                    DetailUpdated?.Invoke($"{i * 100 / entries.Count}%");
                }
            }
            TryDeleteFileWithRetry(tmpZip);
            progress(100);
        });
    }

    private static void DeleteDirectoryWithRetry(string dir)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try { Directory.Delete(dir, recursive: true); return; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[extract] delete {dir} attempt {attempt}: {ex.Message}");
                KillProcessByName("Miami Graphics");
                KillProcessesRunningFrom(dir);
                ClearReadOnlyAttributes(dir);
                Thread.Sleep(300);
            }
        }

        EvictLockedFiles(dir);
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception ex)
        {
            Debug.WriteLine($"[extract] {dir} не снесён полностью: {ex.Message} - продолжаем (extract перезапишет)");
        }
    }

    private static void KillProcessesRunningFrom(string dir)
    {
        string root;
        try { root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; }
        catch (Exception ex) { Debug.WriteLine($"[unlock] bad dir {dir}: {ex.Message}"); return; }

        var self = Environment.ProcessId;
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.Id == self) continue;
                var image = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(image)) continue;
                if (!image.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                Debug.WriteLine($"[unlock] kill {proc.ProcessName} pid={proc.Id} ({image})");
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(2000);
            }
            catch {}
            finally { proc.Dispose(); }
        }
    }

    private static void ClearReadOnlyAttributes(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if ((File.GetAttributes(f) & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                        File.SetAttributes(f, FileAttributes.Normal);
                }
                catch {}
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[unlock] attrs {dir}: {ex.Message}"); }
    }

    private static void EvictLockedFiles(string dir)
    {
        List<string> files;
        try { files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList(); }
        catch (Exception ex) { Debug.WriteLine($"[unlock] enum {dir}: {ex.Message}"); return; }

        foreach (var f in files)
        {
            try
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                File.Delete(f);
            }
            catch { EvictLockedFile(f); }
        }
    }

    private static void EvictLockedFile(string path)
    {
        if (!File.Exists(path)) return;
        try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
        try { File.Delete(path); return; } catch { }

        try
        {
            var aside = $"{path}.old{Environment.TickCount64:x}";
            File.Move(path, aside);
            try { File.Delete(aside); }
            catch { MoveFileEx(aside, null, MoveFileFlags.DelayUntilReboot); }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[unlock] evict {path}: {ex.Message}");
            try { MoveFileEx(path, null, MoveFileFlags.DelayUntilReboot); } catch { }
        }
    }

    private static void TryDeleteFileWithRetry(string path)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!File.Exists(path)) return;
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                return;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[delete] {path} attempt {attempt}: {ex.Message}");
                Thread.Sleep(200);
            }
        }
        try
        {
            MoveFileEx(path, null, MoveFileFlags.DelayUntilReboot);
        }
        catch (Exception ex) { Debug.WriteLine($"[delete] MoveFileEx: {ex.Message}"); }
    }

    [Flags]
    private enum MoveFileFlags : uint { DelayUntilReboot = 0x4 }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string lpExistingFileName, string? lpNewFileName, MoveFileFlags dwFlags);

    private async Task RunFinal(Action<double> progress)
    {
        TryDeleteFileWithRetry(Path.Combine(InstallRoot, "payload.tmp.zip"));
        progress(5);

        await WriteInstalledVersionMarkerFromServerAsync();
        progress(20);
        await Task.Run(() =>
        {
            var exe = Path.Combine(InstallRoot, "app", "Miami Graphics.exe");

            try
            {
                if (File.Exists(exe))
                {
                    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                    try { CreateShortcut(Path.Combine(desktop, "Miami Graphics.lnk"), exe); } catch { }
                    try { CreateShortcut(Path.Combine(startMenu, "Programs", "Miami Graphics.lnk"), exe); } catch { }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[final] shortcuts: {ex.Message}"); }
            progress(50);

            try
            {
                var setupCopy = Path.Combine(InstallRoot, "Miami Setup.exe");
                var setup = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(setup) && File.Exists(setup))
                {
                    try { File.Copy(setup, setupCopy, overwrite: true); }
                    catch (Exception ex) { Debug.WriteLine($"[final] copy setup: {ex.Message}"); }
                }

                using var hkcu = Registry.CurrentUser;
                using var key = hkcu.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MiamiGraphics");
                key?.SetValue("DisplayName",     "Miami Graphics");
                key?.SetValue("DisplayVersion",  GetInstallerVersion());
                key?.SetValue("Publisher",       "Miami Graphics");
                key?.SetValue("InstallLocation", InstallRoot);
                if (File.Exists(exe))   key?.SetValue("DisplayIcon", exe);
                if (File.Exists(setupCopy)) key?.SetValue("UninstallString", $"\"{setupCopy}\" --uninstall");
                key?.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key?.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            catch (Exception ex) { Debug.WriteLine($"[final] register: {ex.Message}"); }
            progress(100);
        });
    }

    private static string GetInstallerVersion()
    {
        try
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "1.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch { return "1.0.0"; }
    }

    private static void CreateShortcut(string lnkPath, string targetExe)
    {
        var dir = Path.GetDirectoryName(lnkPath);
        if (dir is not null) Directory.CreateDirectory(dir);
        var t = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell COM unavailable");
        dynamic shell = Activator.CreateInstance(t)!;
        var shortcut = shell.CreateShortcut(lnkPath);
        shortcut.TargetPath = targetExe;
        shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
        shortcut.IconLocation = targetExe + ",0";
        shortcut.Save();
    }

    private static async Task WriteInstalledVersionMarkerFromServerAsync()
    {
        const string supabaseUrl =
            "https://api.miamigraphicsstorage.uk/rest/v1/app_versions?select=version&is_active=eq.true&limit=1";

        string version = "1.0.0";
        try
        {
            using var http = new HttpClient(new FragmentingHttpHandler(), disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(10),
            };
            using var req = new HttpRequestMessage(HttpMethod.Get, supabaseUrl);
            using var resp = await http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                var m = System.Text.RegularExpressions.Regex.Match(body, "\"version\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success) version = m.Groups[1].Value;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[final] supabase version probe failed: {ex.Message}");
        }

        try
        {
            var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(lad, "MiamiGraphics", "config");
            Directory.CreateDirectory(dir);
            var tmp = Path.Combine(dir, "installed_version.txt.tmp");
            var final = Path.Combine(dir, "installed_version.txt");
            File.WriteAllText(tmp, version);
            File.Move(tmp, final, overwrite: true);
            Debug.WriteLine($"[final] installed_version marker = {version}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[final] write marker: {ex.Message}");
        }
    }
}
