using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using Microsoft.Win32;

namespace MiamiGraphics.Installer.Services;

public static class ZapretManager
{
    public const string OurDomain = "miamigraphicsstorage.uk";

    private static readonly string[] OurHosts = { "miamigraphicsstorage.uk", "eu.miamigraphicsstorage.uk" };

    private static readonly string EuVpsIp = System.Environment.GetEnvironmentVariable("MG_EU_VPS_IP") ?? "";

    public const string BundledVersion = "1.9.9d";

    public const string DefaultInstallDir = @"C:\Zapret";

    private static readonly string[] ZipMirrors =
    {
        "https://ru.miamigraphicsstorage.uk/releases/zapret_9adb0a4182f8d185_v1.9.9d.zip",
        "https://miamigraphicsstorage.uk/releases/zapret_9adb0a4182f8d185_v1.9.9d.zip",
        "https://cdn.miamigraphicsstorage.uk/releases/zapret_9adb0a4182f8d185_v1.9.9d.zip",
        "https://github.com/Flowseal/zapret-discord-youtube/releases/download/1.9.9d/zapret-discord-youtube-1.9.9d.zip",
    };

    private static readonly string[] CloudflareRanges =
    {
        "103.21.244.0/22", "103.22.200.0/22", "103.31.4.0/22",
        "104.16.0.0/13",  "104.24.0.0/14",  "108.162.192.0/18",
        "131.0.72.0/22",  "141.101.64.0/18","162.158.0.0/15",
        "172.64.0.0/13",  "173.245.48.0/20","188.114.96.0/20",
        "190.93.240.0/20","197.234.240.0/22","198.41.128.0/17",
    };

    public static async Task EnsureAsync(string? preferredDir, Action<string>? detail, Action<double>? progress = null)
    {
        var root = DetectRootFromRegistry();

        if (root != null)
        {
            bool looksLikeZapret =
                File.Exists(Path.Combine(root, "general.bat")) ||
                File.Exists(Path.Combine(root, "service.bat")) ||
                Directory.Exists(Path.Combine(root, "lists")) ||
                File.Exists(Path.Combine(root, "bin", "winws.exe"));
            if (!looksLikeZapret)
            {
                Debug.WriteLine($"[zapret.detect] WinDivert root '{root}' is not a zapret layout - skipping (foreign DPI tool)");
                progress?.Invoke(100);
                return;
            }

            detail?.Invoke("Проверяю Zapret…");
            RepairPriorDamage(root);
            ApplyOurEntries(root);
            progress?.Invoke(100);
            return;
        }

        var dir = string.IsNullOrWhiteSpace(preferredDir) ? DefaultInstallDir : preferredDir!;
        detail?.Invoke($"Устанавливаю Zapret в {dir}…");
        await FreshInstallAsync(dir, detail, progress).ConfigureAwait(false);
    }

    private static async Task WithCreep(double from, double to, Action<double>? progress, Func<Task> body)
    {
        progress?.Invoke(from);
        using var cts = new CancellationTokenSource();
        var creep = Task.Run(async () =>
        {
            double v = from;
            while (!cts.IsCancellationRequested && v < to - 0.5)
            {
                v += (to - v) * 0.06 + 0.2;
                progress?.Invoke(Math.Min(v, to));
                try { await Task.Delay(500, cts.Token).ConfigureAwait(false); } catch { }
            }
        });
        try { await body().ConfigureAwait(false); }
        finally { cts.Cancel(); try { await creep.ConfigureAwait(false); } catch { } progress?.Invoke(to); }
    }

    private static async Task FreshInstallAsync(string dir, Action<string>? detail, Action<double>? progress)
    {
        Directory.CreateDirectory(dir);
        var zip = Path.Combine(Path.GetTempPath(), "miami_zapret.zip");
        detail?.Invoke("Скачиваю Zapret…");
        if (!await DownloadZipAsync(zip, dp => progress?.Invoke(dp * 0.55), detail).ConfigureAwait(false)) return;
        progress?.Invoke(58);
        detail?.Invoke("Распаковываю Zapret…");
        ExtractInto(zip, dir);
        TryDelete(zip);
        SanitizeBats(dir);
        ApplyOurEntries(dir);
        progress?.Invoke(64);
        detail?.Invoke("Запускаю Zapret…");
        await WithCreep(64, 96, progress, () => Task.Run(() => StartGeneral(dir))).ConfigureAwait(false);
        await Task.Delay(1500).ConfigureAwait(false);
        progress?.Invoke(100);
    }

    private static async Task UpdateInPlaceAsync(string root, Action<string>? detail, Action<double>? progress)
    {
        var listsDir = Path.Combine(root, "lists");
        var oldUserList = ReadLinesSafe(Path.Combine(listsDir, "list-general-user.txt"));
        var oldIpset    = ReadLinesSafe(Path.Combine(listsDir, "ipset-all.txt"));

        var zip = Path.Combine(Path.GetTempPath(), "miami_zapret.zip");
        detail?.Invoke("Скачиваю новую версию Zapret…");
        if (!await DownloadZipAsync(zip, dp => progress?.Invoke(dp * 0.5), detail).ConfigureAwait(false)) return;
        progress?.Invoke(55);
        detail?.Invoke("Обновляю Zapret в вашей папке…");
        ExtractInto(zip, root);
        TryDelete(zip);
        SanitizeBats(root);

        detail?.Invoke("Возвращаю ваши домены и адреса…");
        MergeUnion(Path.Combine(listsDir, "list-general-user.txt"), oldUserList);
        MergeUnion(Path.Combine(listsDir, "ipset-all.txt"), oldIpset);
        ApplyOurEntries(root);
        progress?.Invoke(60);

        detail?.Invoke("Перезапускаю Zapret… (до 30 сек)");
        await WithCreep(60, 96, progress, () => Task.Run(() => RestartZapret(root, detail))).ConfigureAwait(false);
        progress?.Invoke(100);
    }

    public static string? DetectRootFromRegistry()
    {
        string[] names = { "WinDivert", "WinDivert14", "WinDivert1.4", "windivert" };
        foreach (var svc in names)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{svc}");
                if (key?.GetValue("ImagePath") is string img)
                {
                    var r = RootFromImagePath(img);
                    if (r != null) return r;
                }
            }
            catch { }
        }
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services != null)
            {
                foreach (var name in services.GetSubKeyNames())
                {
                    try
                    {
                        using var k = services.OpenSubKey(name);
                        if (k?.GetValue("ImagePath") is not string img) continue;
                        var low = img.ToLowerInvariant();
                        if (low.Contains("winws") || low.Contains("windivert"))
                        {
                            var r = RootFromImagePath(img);
                            if (r != null) return r;
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[zapret.detect] scan: {ex.Message}"); }
        return null;
    }

    private static string? RootFromImagePath(string? img)
    {
        if (string.IsNullOrWhiteSpace(img)) return null;
        var s = img.Trim();
        if (s.StartsWith(@"\??\", StringComparison.Ordinal)) s = s.Substring(4);

        string exe;
        if (s.StartsWith("\""))
        {
            int end = s.IndexOf('"', 1);
            exe = end > 0 ? s.Substring(1, end - 1) : s.Trim('"');
        }
        else
        {
            int cut = -1;
            foreach (var ext in new[] { ".exe", ".sys" })
            {
                var m = s.IndexOf(ext + " ", StringComparison.OrdinalIgnoreCase);
                if (m >= 0) { cut = m + ext.Length; break; }
            }
            exe = cut > 0 ? s.Substring(0, cut) : s;
        }

        try
        {
            var binDir = Path.GetDirectoryName(exe);
            if (string.IsNullOrEmpty(binDir)) return null;
            var root = string.Equals(Path.GetFileName(binDir), "bin", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(binDir) : binDir;
            return !string.IsNullOrEmpty(root) && Directory.Exists(root) ? root : null;
        }
        catch { return null; }
    }

    public static string? ReadInstalledVersion(string root)
    {
        try
        {
            var vf = Path.Combine(root, ".service", "version.txt");
            if (!File.Exists(vf)) return null;
            var v = File.ReadAllText(vf).Trim();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch { return null; }
    }

    public static int CompareVersions(string left, string right)
    {
        (int[] nums, string suf) Parse(string s)
        {
            var main = new string(s.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            var suf = s.Substring(main.Length);
            var nums = main.Split('.', StringSplitOptions.RemoveEmptyEntries)
                           .Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
            return (nums, suf);
        }
        var a = Parse(left); var b = Parse(right);
        int len = Math.Max(a.nums.Length, b.nums.Length);
        for (int i = 0; i < len; i++)
        {
            int av = i < a.nums.Length ? a.nums[i] : 0;
            int bv = i < b.nums.Length ? b.nums[i] : 0;
            if (av != bv) return av.CompareTo(bv);
        }
        return string.Compare(a.suf, b.suf, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> DownloadZipAsync(string dest, Action<double>? progress, Action<string>? detail)
    {
        for (int mi = 0; mi < ZipMirrors.Length; mi++)
        {
            var url = ZipMirrors[mi];
            try
            {
                using var http = new HttpClient(new FragmentingHttpHandler(), disposeHandler: true)
                {
                    Timeout = TimeSpan.FromSeconds(60),
                };
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MiamiGraphicsInstaller");
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) continue;

                var total = resp.Content.Headers.ContentLength ?? -1;
                await using (var nets = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                await using (var f = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
                {
                    var buf = new byte[1 << 16];
                    long done = 0; int read;
                    var sw = Stopwatch.StartNew(); long lastMs = 0;
                    while ((read = await nets.ReadAsync(buf).ConfigureAwait(false)) > 0)
                    {
                        await f.WriteAsync(buf.AsMemory(0, read)).ConfigureAwait(false);
                        done += read;
                        if (sw.ElapsedMilliseconds - lastMs > 150)
                        {
                            double mbps = done / 1024.0 / 1024.0 / Math.Max(0.001, sw.ElapsedMilliseconds / 1000.0);
                            if (total > 0)
                            {
                                double pct = done * 100.0 / total;
                                progress?.Invoke(pct);
                                detail?.Invoke($"Скачиваю Zapret… {(int)pct}%  ·  {mbps:F1} MB/s");
                            }
                            else detail?.Invoke($"Скачиваю Zapret… {done / 1024} KB  ·  {mbps:F1} MB/s");
                            lastMs = sw.ElapsedMilliseconds;
                        }
                    }
                    await f.FlushAsync().ConfigureAwait(false);
                }

                var gotLen = new FileInfo(dest).Length;
                if (total > 0 && gotLen != total)
                {
                    Debug.WriteLine($"[zapret.dl] {url}: усечён ({gotLen}/{total}) - следующее зеркало");
                    if (mi + 1 < ZipMirrors.Length) detail?.Invoke("Битый архив Zapret - резервный сервер…");
                    continue;
                }
                if (gotLen <= 100_000)
                {
                    Debug.WriteLine($"[zapret.dl] {url}: слишком мал ({gotLen} B)");
                    continue;
                }
                if (!IsValidZip(dest))
                {
                    Debug.WriteLine($"[zapret.dl] {url}: не открывается как zip - следующее зеркало");
                    if (mi + 1 < ZipMirrors.Length) detail?.Invoke("Битый архив Zapret - резервный сервер…");
                    continue;
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[zapret.dl] {url}: {ex.Message}");
                if (mi + 1 < ZipMirrors.Length) detail?.Invoke("Переключаюсь на резервный сервер Zapret…");
            }
        }
        return false;
    }

    private static bool IsValidZip(string path)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.Entries.Count > 0;
        }
        catch { return false; }
    }

    private static void ExtractInto(string zipPath, string targetDir)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        string? top = null; bool single = true;
        foreach (var e in zip.Entries)
        {
            var seg = e.FullName.Replace('\\', '/').Split('/')[0];
            if (top == null) top = seg;
            else if (!string.Equals(top, seg, StringComparison.OrdinalIgnoreCase)) { single = false; break; }
        }
        var strip = single && !string.IsNullOrEmpty(top) ? top + "/" : null;
        var rootFull = Path.GetFullPath(targetDir);

        foreach (var e in zip.Entries)
        {
            var rel = e.FullName.Replace('\\', '/');
            if (strip != null && rel.StartsWith(strip, StringComparison.OrdinalIgnoreCase))
                rel = rel.Substring(strip.Length);
            if (string.IsNullOrEmpty(rel)) continue;
            var dest = Path.GetFullPath(Path.Combine(targetDir, rel));
            if (dest != rootFull && !dest.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Запись архива выходит за пределы каталога распаковки: {e.FullName}");
            if (rel.EndsWith("/"))
            {
                Directory.CreateDirectory(dest);
                continue;
            }
            var parent = Path.GetDirectoryName(dest);
            if (parent != null) Directory.CreateDirectory(parent);
            try { e.ExtractToFile(dest, overwrite: true); }
            catch (Exception ex) { Debug.WriteLine($"[zapret.extract] {rel}: {ex.Message}"); }
        }
    }

    private static void SanitizeBats(string root)
    {
        try
        {
            foreach (var bat in Directory.EnumerateFiles(root, "*.bat", SearchOption.AllDirectories))
            {
                try
                {
                    var lines = File.ReadAllLines(bat);
                    bool changed = false;
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var raw = lines[i];
                        var low = raw.TrimStart().ToLowerInvariant();
                        if (low.StartsWith("rem") || low.StartsWith("::")) continue;

                        bool opensBrowser =
                            (low.Contains("start ") || low.Contains("start\"") || low.Contains("explorer"))
                            && (low.Contains("http://") || low.Contains("https://"));

                        if (opensBrowser)
                        {
                            lines[i] = "rem [miami-silent] " + raw;
                            changed = true;
                        }
                    }
                    if (changed) File.WriteAllLines(bat, lines);
                }
                catch (Exception ex) { Debug.WriteLine($"[zapret.sanitize] {bat}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[zapret.sanitize] {ex.Message}"); }
    }

    private static void RepairPriorDamage(string root)
    {
        try
        {
            if (new DirectoryInfo(root).Parent is null)
            {
                Debug.WriteLine($"[zapret.repair] root '{root}' is a drive root - skipping recursive scan");
                return;
            }
            var scanBudget = Stopwatch.StartNew();
            int scanned = 0;
            foreach (var bat in Directory.EnumerateFiles(root, "*.bat", SearchOption.AllDirectories))
            {
                if (++scanned > 2000 || scanBudget.Elapsed > TimeSpan.FromSeconds(10))
                {
                    Debug.WriteLine($"[zapret.repair] scan budget exceeded ({scanned} files, {scanBudget.Elapsed.TotalSeconds:F0}s) - aborting repair scan");
                    break;
                }
                try
                {
                    var text = File.ReadAllText(bat);
                    if (!text.Contains("[miami-silent]")) continue;
                    var fixedText = System.Text.RegularExpressions.Regex.Replace(
                        text, @"(?m)^(\s*)rem \[miami-silent\] ", "$1");
                    File.WriteAllText(bat, fixedText);
                    Debug.WriteLine($"[zapret.repair] un-silenced {bat}");
                }
                catch (Exception ex) { Debug.WriteLine($"[zapret.repair] {bat}: {ex.Message}"); }
            }

            var ipset = Path.Combine(root, "lists", "ipset-all.txt");
            if (File.Exists(ipset))
            {
                var lines = File.ReadAllLines(ipset);
                var cleaned = lines.Where(l => !System.Text.RegularExpressions.Regex.IsMatch(l.Trim(), @"^\d{1,3}(\.\d{1,3}){3}$")).ToArray();
                if (cleaned.Length != lines.Length)
                {
                    File.WriteAllLines(ipset, cleaned);
                    Debug.WriteLine("[zapret.repair] dropped malformed bare-IP from ipset-all.txt");
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[zapret.repair] {ex.Message}"); }
    }

    private static void ApplyOurEntries(string root)
    {
        try
        {
            var listsDir = Path.Combine(root, "lists");
            if (!Directory.Exists(listsDir)) Directory.CreateDirectory(listsDir);
            var userList = Path.Combine(listsDir, "list-general-user.txt");
            foreach (var h in OurHosts) EnsureLine(userList, h);
            var ipset = Path.Combine(listsDir, "ipset-all.txt");
            EnsureLines(ipset, CloudflareRanges);
            if (!string.IsNullOrWhiteSpace(EuVpsIp)) EnsureLine(ipset, EuVpsIp);
        }
        catch (Exception ex) { Debug.WriteLine($"[zapret.apply] {ex.Message}"); }
    }

    private static void MergeUnion(string path, string[] oldLines)
    {
        if (oldLines.Length == 0) return;
        try
        {
            var have = new HashSet<string>(ReadLinesSafe(path).Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase);
            var add = oldLines.Where(l => !string.IsNullOrWhiteSpace(l) && !have.Contains(l.Trim())).ToArray();
            if (add.Length == 0) return;
            var prefix = File.Exists(path) && new FileInfo(path).Length > 0 && !File.ReadAllText(path).EndsWith("\n")
                ? Environment.NewLine : "";
            File.AppendAllText(path, prefix + string.Join(Environment.NewLine, add) + Environment.NewLine);
        }
        catch (Exception ex) { Debug.WriteLine($"[zapret.merge] {path}: {ex.Message}"); }
    }

    private static string[] ReadLinesSafe(string path)
    {
        try { return File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private static void EnsureLine(string path, string line)
    {
        var ex = ReadLinesSafe(path);
        if (ex.Any(l => string.Equals(l.Trim(), line, StringComparison.OrdinalIgnoreCase))) return;
        var prefix = ex.Length > 0 && File.Exists(path) && !File.ReadAllText(path).EndsWith("\n") ? Environment.NewLine : "";
        File.AppendAllText(path, prefix + line + Environment.NewLine);
    }

    private static void EnsureLines(string path, string[] lines)
    {
        var have = new HashSet<string>(ReadLinesSafe(path).Select(l => l.Trim()), StringComparer.OrdinalIgnoreCase);
        var add = lines.Where(l => !have.Contains(l.Trim())).ToArray();
        if (add.Length == 0) return;
        var prefix = File.Exists(path) && new FileInfo(path).Length > 0 && !File.ReadAllText(path).EndsWith("\n") ? Environment.NewLine : "";
        File.AppendAllText(path, prefix + string.Join(Environment.NewLine, add) + Environment.NewLine);
    }

    private static void InstallService(string root, Action<string>? detail)
    {
        var bat = Path.Combine(root, "service.bat");
        if (!File.Exists(bat)) { StartGeneral(root); return; }
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{bat}\"\"",
                WorkingDirectory = root,
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            var p = Process.Start(psi);
            if (p == null) { StartGeneral(root); return; }
            var si = p.StandardInput;
            si.WriteLine("1");
            si.WriteLine("3");
            si.WriteLine("");
            si.WriteLine("");
            si.WriteLine("0");
            try { si.Close(); } catch { }
            if (!p.WaitForExit(40_000)) { try { p.Kill(true); } catch { } }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[zapret.service] {ex.Message}");
            StartGeneral(root);
        }
    }

    private static void RestartZapret(string root, Action<string>? detail)
    {
        var svc = FindZapretServiceName(root);
        if (svc != null)
        {
            try
            {
                RunHidden("cmd.exe", $"/c net stop \"{svc}\" & net start \"{svc}\"", 20_000);
                Debug.WriteLine($"[zapret.restart] service {svc} restarted");
                return;
            }
            catch (Exception ex) { Debug.WriteLine($"[zapret.restart] svc {svc}: {ex.Message}"); }
        }
        KillWinws();
        StartGeneral(root);
    }

    private static string? FindZapretServiceName(string root)
    {
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services == null) return null;
            var rootLow = root.ToLowerInvariant();
            foreach (var name in services.GetSubKeyNames())
            {
                if (name.StartsWith("WinDivert", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using var k = services.OpenSubKey(name);
                    if (k?.GetValue("ImagePath") is not string img) continue;
                    var low = img.ToLowerInvariant();
                    if (low.Contains(rootLow) || low.Contains("winws"))
                        return name;
                }
                catch { }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[zapret.findsvc] {ex.Message}"); }
        return null;
    }

    private static void StartGeneral(string root)
    {
        var bat = Path.Combine(root, "general.bat");
        if (!File.Exists(bat)) return;
        try
        {
            RunHidden("cmd.exe", $"/c \"\"{bat}\"\"", 12_000, root);
        }
        catch (Exception ex) { Debug.WriteLine($"[zapret.general] {ex.Message}"); }
    }

    private static void KillWinws()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("winws"))
            {
                try { p.Kill(true); p.WaitForExit(2000); } catch { } finally { p.Dispose(); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[zapret.kill] {ex.Message}"); }
    }

    private static void RunHidden(string file, string args, int waitMs, string? cwd = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        if (cwd != null) psi.WorkingDirectory = cwd;
        var p = Process.Start(psi);
        if (p != null && waitMs > 0)
        {
            if (!p.WaitForExit(waitMs)) { try { p.Kill(true); } catch { } }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
