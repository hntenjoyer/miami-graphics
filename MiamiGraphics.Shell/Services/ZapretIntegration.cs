using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MiamiGraphics.Core.I18n;
using Microsoft.Win32;

namespace MiamiGraphics.Shell.Services;

public static class ZapretIntegration
{
    public const string OurDomain = "miamigraphicsstorage.uk";

    public const string DbHost  = "eu.miamigraphicsstorage.uk";
    public static readonly string DbVpsIp = System.Environment.GetEnvironmentVariable("MG_DB_VPS_IP") ?? "";

    public static readonly string[] CloudflareRanges =
    {
        "103.21.244.0/22",
        "103.22.200.0/22",
        "103.31.4.0/22",
        "104.16.0.0/13",
        "104.24.0.0/14",
        "108.162.192.0/18",
        "131.0.72.0/22",
        "141.101.64.0/18",
        "162.158.0.0/15",
        "172.64.0.0/13",
        "173.245.48.0/20",
        "188.114.96.0/20",
        "190.93.240.0/20",
        "197.234.240.0/22",
        "198.41.128.0/17",
    };

    public sealed record ApplyResult(
        bool Success,
        string? ErrorMessage,
        int DomainLinesAdded,
        int IpsetLinesAdded,
        string? ListsDir);

    public static ApplyResult ApplyWhitelist(string zapretRootPath)
    {
        if (string.IsNullOrWhiteSpace(zapretRootPath))
            return new ApplyResult(false, Loc.T("zapret.pathNotSet"), 0, 0, null);
        if (!Directory.Exists(zapretRootPath))
            return new ApplyResult(false, Loc.T("zapret.folderNotFound", ("path", zapretRootPath)), 0, 0, null);

        var listsDir = Path.Combine(zapretRootPath, "lists");
        if (!Directory.Exists(listsDir))
            return new ApplyResult(false, Loc.T("zapret.listsDirMissing", ("path", zapretRootPath)), 0, 0, null);

        try
        {
            var userListPath = Path.Combine(listsDir, "list-general-user.txt");
            var ipsetPath = Path.Combine(listsDir, "ipset-all.txt");

            int domainAdded = EnsureLineInFile(userListPath, OurDomain);
            domainAdded += EnsureLineInFile(userListPath, DbHost);
            int ipsetAdded = EnsureLinesInFile(ipsetPath, CloudflareRanges);
            if (!string.IsNullOrWhiteSpace(DbVpsIp)) ipsetAdded += EnsureLineInFile(ipsetPath, DbVpsIp);

            return new ApplyResult(true, null, domainAdded, ipsetAdded, listsDir);
        }
        catch (Exception ex)
        {
            return new ApplyResult(false, Loc.T("zapret.writeError", ("reason", ex.Message)), 0, 0, listsDir);
        }
    }

    public static bool IsInstalledAt(string? zapretRootPath)
    {
        if (string.IsNullOrWhiteSpace(zapretRootPath)) return false;
        try
        {
            var listsDir = Path.Combine(zapretRootPath, "lists");
            return File.Exists(Path.Combine(listsDir, "list-general-user.txt"));
        }
        catch { return false; }
    }

    public static bool IsConfiguredForUs(string? zapretRootPath)
    {
        if (string.IsNullOrWhiteSpace(zapretRootPath)) return false;
        try
        {
            var userListPath = Path.Combine(zapretRootPath, "lists", "list-general-user.txt");
            if (!File.Exists(userListPath)) return false;
            return File.ReadLines(userListPath)
                .Any(l => string.Equals(l.Trim(), OurDomain, StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    public static string? DetectZapretRootFromRegistry()
    {
        string[] serviceNames = { "WinDivert", "WinDivert14", "WinDivert1.4", "windivert" };
        foreach (var svc in serviceNames)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{svc}");
                if (key?.GetValue("ImagePath") is not string imagePath) continue;
                var root = ZapretRootFromImagePath(imagePath);
                if (root != null) return root;
            }
            catch {}
        }
        return null;
    }

    private static string? ZapretRootFromImagePath(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return null;
        var p = imagePath.Trim().Trim('"');
        if (p.StartsWith(@"\??\", StringComparison.Ordinal)) p = p.Substring(4);
        try
        {
            var sysDir = Path.GetDirectoryName(p);
            if (string.IsNullOrEmpty(sysDir)) return null;
            var root = string.Equals(Path.GetFileName(sysDir), "bin", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(sysDir)
                : sysDir;
            return !string.IsNullOrEmpty(root) && Directory.Exists(root) ? root : null;
        }
        catch { return null; }
    }

    public static bool IsWinwsRunning()
    {
        try
        {
            var procs = Process.GetProcessesByName("winws");
            try { return procs.Length > 0; }
            finally { foreach (var p in procs) p.Dispose(); }
        }
        catch { return false; }
    }

    public sealed record DetectResult(bool Installed, bool ConfiguredForUs, string? DetectedRoot);

    public static DetectResult Detect(string? fallbackPath)
    {
        var root = DetectZapretRootFromRegistry();
        var installed = root != null;
        if (root == null && IsInstalledAt(fallbackPath)) root = fallbackPath;
        var configured = IsConfiguredForUs(root) && IsWinwsRunning();
        return new DetectResult(installed || IsInstalledAt(root), configured, root);
    }

    public sealed record RestartResult(bool Restarted, string? Message);

    public static RestartResult RestartRunning()
    {
        List<(int Pid, string? Exe, string? Cmd)> procs;
        try { procs = QueryWinws(); }
        catch (Exception ex) { return new RestartResult(false, Loc.T("zapret.queryProcessesFailed", ("reason", ex.Message))); }

        if (procs.Count == 0)
            return new RestartResult(false, Loc.T("zapret.winwsNotRunning"));

        var spec = procs.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Exe) && !string.IsNullOrWhiteSpace(p.Cmd));
        if (spec.Exe == null || spec.Cmd == null)
            return new RestartResult(false, Loc.T("zapret.cannotReadCommandLine"));

        var exe = spec.Exe!;
        var args = StripLeadingExe(spec.Cmd!, exe);
        var workDir = Path.GetDirectoryName(exe) ?? Environment.CurrentDirectory;

        foreach (var p in procs)
        {
            try { using var proc = Process.GetProcessById(p.Pid); proc.Kill(true); proc.WaitForExit(4000); }
            catch {}
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName        = exe,
                Arguments       = args,
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow  = true,
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            return new RestartResult(false, Loc.T("zapret.stoppedButRelaunchFailed", ("reason", ex.Message)));
        }

        for (int i = 0; i < 10; i++)
        {
            System.Threading.Thread.Sleep(400);
            try { if (QueryWinws().Count > 0) return new RestartResult(true, null); }
            catch { break; }
        }
        return new RestartResult(false, Loc.T("zapret.relaunchedButNotUp"));
    }

    private static List<(int Pid, string? Exe, string? Cmd)> QueryWinws()
    {
        var psi = new ProcessStartInfo
        {
            FileName               = "powershell",
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(
            "Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'winws.exe' } | " +
            "Select-Object ProcessId,ExecutablePath,CommandLine | ConvertTo-Json -Compress");

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(8000);

        var list = new List<(int, string?, string?)>();
        var json = stdout.Trim();
        if (string.IsNullOrEmpty(json)) return list;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
            foreach (var el in root.EnumerateArray()) Add(el);
        else if (root.ValueKind == JsonValueKind.Object)
            Add(root);
        return list;

        void Add(JsonElement el)
        {
            int pid = el.TryGetProperty("ProcessId", out var p) && p.TryGetInt32(out var v) ? v : 0;
            string? exe = el.TryGetProperty("ExecutablePath", out var e) ? e.GetString() : null;
            string? cmd = el.TryGetProperty("CommandLine", out var c) ? c.GetString() : null;
            if (pid != 0) list.Add((pid, exe, cmd));
        }
    }

    private static string StripLeadingExe(string commandLine, string exePath)
    {
        var s = commandLine.TrimStart();
        if (s.StartsWith('"'))
        {
            int end = s.IndexOf('"', 1);
            return end >= 0 ? s[(end + 1)..].TrimStart() : "";
        }
        var exeName = Path.GetFileName(exePath);
        int idx = s.IndexOf(exeName, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            int after = idx + exeName.Length;
            return after < s.Length ? s[after..].TrimStart() : "";
        }
        int sp = s.IndexOf(' ');
        return sp >= 0 ? s[(sp + 1)..].TrimStart() : "";
    }

    private static int EnsureLineInFile(string path, string line)
    {
        var existing = File.Exists(path)
            ? File.ReadAllLines(path)
            : Array.Empty<string>();
        if (existing.Any(l => string.Equals(l.Trim(), line, StringComparison.OrdinalIgnoreCase)))
            return 0;

        var needsLeadingNewline = existing.Length > 0
            && !string.IsNullOrEmpty(existing[^1])
            && !File.ReadAllText(path).EndsWith("\n");
        var prefix = needsLeadingNewline ? Environment.NewLine : string.Empty;
        File.AppendAllText(path, prefix + line + Environment.NewLine);
        return 1;
    }

    private static int EnsureLinesInFile(string path, string[] lines)
    {
        var existing = new HashSet<string>(
            File.Exists(path)
                ? File.ReadAllLines(path).Select(l => l.Trim())
                : Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        var toAdd = lines.Where(l => !existing.Contains(l.Trim())).ToList();
        if (toAdd.Count == 0) return 0;

        var needsLeadingNewline = File.Exists(path)
            && new FileInfo(path).Length > 0
            && !File.ReadAllText(path).EndsWith("\n");
        var prefix = needsLeadingNewline ? Environment.NewLine : string.Empty;
        File.AppendAllText(path, prefix + string.Join(Environment.NewLine, toAdd) + Environment.NewLine);
        return toAdd.Count;
    }
}
