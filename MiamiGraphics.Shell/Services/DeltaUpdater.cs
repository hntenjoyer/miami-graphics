using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MiamiGraphics.Core.I18n;
using MiamiGraphics.Core.Update;

namespace MiamiGraphics.Shell.Services;

public sealed class DeltaUpdateResult
{
    public bool Applying { get; init; }
    public bool NoOp { get; init; }
    public string? Error { get; init; }
    public long DownloadedBytes { get; init; }
    public int FilesChanged { get; init; }

    public static DeltaUpdateResult Fail(string error) => new() { Error = error };
}

public sealed class DeltaUpdater
{
    public static string AppDir
    {
        get
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            return string.IsNullOrEmpty(exe)
                ? AppContext.BaseDirectory
                : Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        }
    }

    public static string InstalledManifestPath => Path.Combine(AppDir, "installed_manifest.json");

    private static string UpdatesRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MiamiGraphics", "updates");

    public async Task<DeltaUpdateResult> TryUpdateAsync(
        string targetVersion,
        string manifestUrl,
        string? manifestSha256,
        Action<int, string>? onProgress,
        CancellationToken ct = default)
    {
        string? stagingDir = null;
        try
        {
            if (string.IsNullOrWhiteSpace(manifestUrl))
                return DeltaUpdateResult.Fail("no manifest url");

            var appDir = AppDir;
            if (!Directory.Exists(appDir) || !CanWrite(appDir))
                return DeltaUpdateResult.Fail($"app dir not writable: {appDir}");

            if (!IsSha256Hex(manifestSha256))
            {
                SessionLog.Warn("app-update",
                    $"delta отклонена: у версии {targetVersion} в app_versions нет корректного manifest_sha256 - уходим на полный инсталлер");
                return DeltaUpdateResult.Fail("no manifest sha256 in app_versions");
            }

            onProgress?.Invoke(2, Loc.T("update.checkingUpdate"));

            var work = Path.Combine(UpdatesRoot, "work");
            Directory.CreateDirectory(work);
            var manifestTmp = Path.Combine(work, $"manifest_{Sanitize(targetVersion)}.json");
            await Bridge.AppBridge.DownloadViaMirrorAsync(manifestUrl, manifestTmp, null, ct).ConfigureAwait(false);

            var expectedManifestSha = manifestSha256!.Trim();
            var actualManifestSha = Sha256File(manifestTmp);
            if (!actualManifestSha.Equals(expectedManifestSha, StringComparison.OrdinalIgnoreCase))
            {
                SafeDeleteFile(manifestTmp);
                SessionLog.Error("app-update",
                    $"delta отклонена: манифест {manifestUrl} не совпал с эталоном app_versions " +
                    $"(ждали {expectedManifestSha}, получили {actualManifestSha})");
                return DeltaUpdateResult.Fail("manifest sha mismatch");
            }

            var target = AppManifest.FromJson(await File.ReadAllTextAsync(manifestTmp, ct).ConfigureAwait(false));
            if (target is null || target.Files.Count == 0 || string.IsNullOrWhiteSpace(target.BlobBaseUrl))
                return DeltaUpdateResult.Fail("target manifest invalid");

            var installed = AppManifest.Load(InstalledManifestPath);
            bool authenticBaseline = installed != null;
            if (installed is null)
            {
                onProgress?.Invoke(6, Loc.T("update.verifyingInstalledFiles"));
                installed = await Task.Run(() => AppManifest.ComputeFromDirectory(appDir), ct).ConfigureAwait(false);
            }

            var diff = AppManifest.Diff(installed, target);
            if (diff.IsEmpty)
            {
                TrySave(target, InstalledManifestPath);
                return new DeltaUpdateResult { NoOp = true };
            }

            var staging = Path.Combine(UpdatesRoot, "staging", Sanitize(targetVersion));
            SafeDeleteDir(staging);
            if (Directory.Exists(staging))
            {
                SessionLog.Error("app-update", $"delta отклонена: не удалось очистить каталог сборки {staging}");
                return DeltaUpdateResult.Fail("staging dir not clean");
            }
            Directory.CreateDirectory(staging);
            stagingDir = staging;

            long total = Math.Max(1, diff.DownloadBytes);
            long done = 0;
            int idx = 0;
            foreach (var f in diff.ToDownload)
            {
                ct.ThrowIfCancellationRequested();
                idx++;
                if (!TryResolveInside(staging, f.Path, out var dest))
                {
                    SafeDeleteDir(staging);
                    SessionLog.Error("app-update", $"delta отклонена: недопустимый путь в манифесте: {f.Path}");
                    return DeltaUpdateResult.Fail($"bad path in manifest: {f.Path}");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                var url = target.BlobUrl(f);
                long fileBase = done;
                await Bridge.AppBridge.DownloadViaMirrorAsync(url, dest,
                    (recv, _) =>
                    {
                        var pct = 8 + (int)((fileBase + recv) * 82.0 / total);
                        onProgress?.Invoke(Math.Clamp(pct, 8, 90),
                            Loc.T("update.downloadingUpdate", ("index", idx), ("total", diff.ToDownload.Count)));
                    },
                    ct).ConfigureAwait(false);

                var got = Sha256File(dest);
                if (!got.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    SafeDeleteDir(staging);
                    SessionLog.Error("app-update",
                        $"delta отклонена: блоб {f.Path} не совпал с манифестом (ждали {f.Sha256}, получили {got})");
                    return DeltaUpdateResult.Fail($"blob sha mismatch for {f.Path}");
                }

                done += f.Size;
            }

            onProgress?.Invoke(92, Loc.T("update.applyingUpdate"));

            var stagingError = VerifyStagingAgainstManifest(staging, target);
            if (stagingError != null)
            {
                SafeDeleteDir(staging);
                SessionLog.Error("app-update", $"delta отклонена перед раскладкой: {stagingError}");
                return DeltaUpdateResult.Fail(stagingError);
            }

            target.Save(Path.Combine(staging, "manifest.json"));
            var deletes = authenticBaseline ? diff.ToDelete : new List<string>();
            foreach (var rel in deletes)
                if (!TryResolveInside(appDir, rel, out _))
                {
                    SafeDeleteDir(staging);
                    SessionLog.Error("app-update", $"delta отклонена: недопустимый путь в списке удаления: {rel}");
                    return DeltaUpdateResult.Fail($"bad delete path in manifest: {rel}");
                }
            await File.WriteAllLinesAsync(Path.Combine(staging, "__delete.txt"), deletes, ct).ConfigureAwait(false);

            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(appDir, "Miami Graphics.exe");
            LaunchApplyHelper(staging, appDir, exePath, Environment.ProcessId, targetVersion);

            return new DeltaUpdateResult
            {
                Applying = true,
                DownloadedBytes = done,
                FilesChanged = diff.ToDownload.Count,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (stagingDir != null) SafeDeleteDir(stagingDir);
            return DeltaUpdateResult.Fail("cancelled");
        }
        catch (Exception ex)
        {
            if (stagingDir != null) SafeDeleteDir(stagingDir);
            Debug.WriteLine($"[delta-update] failed: {ex}");
            SessionLog.Error("app-update", $"delta сорвалась ({targetVersion}) - уходим на полный инсталлер", ex);
            return DeltaUpdateResult.Fail(ex.Message);
        }
    }

    private static void LaunchApplyHelper(string staging, string appDir, string exePath, int pid, string version)
    {
        var updatesRoot = UpdatesRoot;
        Directory.CreateDirectory(updatesRoot);
        var helper = Path.Combine(updatesRoot, $"apply_{Sanitize(version)}.ps1");
        var log    = Path.Combine(updatesRoot, $"apply_{Sanitize(version)}.log");

        var script = $@"
$ErrorActionPreference = 'Stop'
$staging = {Ps(staging)}
$appDir  = {Ps(appDir)}
$exe     = {Ps(exePath)}
$log     = {Ps(log)}
$pidWait = {pid}

function Log([string]$m) {{ try {{ Add-Content -LiteralPath $log -Value ((Get-Date -Format 'HH:mm:ss') + ' ' + $m) }} catch {{ }} }}

Log 'helper started'
try {{ Wait-Process -Id $pidWait -Timeout 30 -ErrorAction SilentlyContinue }} catch {{ }}
Start-Sleep -Milliseconds 600
# Belt-and-suspenders: if the app relaunched itself, wait a touch more.
for ($i=0; $i -lt 20; $i++) {{
  $p = Get-Process -Id $pidWait -ErrorAction SilentlyContinue
  if (-not $p) {{ break }}
  Start-Sleep -Milliseconds 300
}}

try {{
  # 1) Copy staged files over the app dir (skip control files).
  $ctrl = @('manifest.json','__delete.txt')
  Get-ChildItem -LiteralPath $staging -Recurse -File | ForEach-Object {{
    $rel = $_.FullName.Substring($staging.Length).TrimStart('\','/')
    if ($ctrl -contains $rel) {{ return }}
    $target = Join-Path $appDir $rel
    $tdir = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $tdir)) {{ New-Item -ItemType Directory -Force -Path $tdir | Out-Null }}
    Copy-Item -LiteralPath $_.FullName -Destination $target -Force
  }}
  Log 'files copied'

  # 2) Remove files the new version dropped.
  $delFile = Join-Path $staging '__delete.txt'
  if (Test-Path -LiteralPath $delFile) {{
    Get-Content -LiteralPath $delFile | ForEach-Object {{
      $rel = $_.Trim(); if ($rel -eq '') {{ return }}
      $victim = Join-Path $appDir ($rel -replace '/','\')
      if (Test-Path -LiteralPath $victim) {{ try {{ Remove-Item -LiteralPath $victim -Force }} catch {{ }} }}
    }}
  }}

  # 3) Promote the manifest LAST (so a crash above re-heals via a fresh diff next launch).
  $srcManifest = Join-Path $staging 'manifest.json'
  if (Test-Path -LiteralPath $srcManifest) {{
    Copy-Item -LiteralPath $srcManifest -Destination (Join-Path $appDir 'installed_manifest.json') -Force
  }}
  Log 'manifest promoted'
}} catch {{
  Log ('apply failed: ' + $_.Exception.Message)
}}

# 4) Relaunch + clean up staging (best-effort).
if (Test-Path -LiteralPath $exe) {{ try {{ Start-Process -FilePath $exe | Out-Null; Log 'relaunched' }} catch {{ Log ('relaunch failed: ' + $_.Exception.Message) }} }}
try {{ Remove-Item -LiteralPath $staging -Recurse -Force }} catch {{ }}
Log 'done'
";
        File.WriteAllText(helper, script, new UTF8Encoding(false));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{helper}\"",
            UseShellExecute = true,
            Verb = "runas",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = updatesRoot,
        };
        Process.Start(psi);
    }

    private static string Ps(string value) => "'" + (value ?? "").Replace("'", "''") + "'";

    private static string Sanitize(string v) =>
        string.Concat((v ?? "").Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_'));

    private static bool IsSha256Hex(string? value)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v) || v.Length != 64) return false;
        foreach (var c in v)
            if (!char.IsAsciiHexDigit(c)) return false;
        return true;
    }

    private static bool TryResolveInside(string root, string relPath, out string full)
        => Core.System.SafePath.TryResolveInside(root, relPath, out full, out _);

    private static string? VerifyStagingAgainstManifest(string staging, AppManifest target)
    {
        var want = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in target.Files)
            want[AppManifest.NormalizePath(f.Path)] = f.Sha256;

        var rootFull = Path.GetFullPath(staging).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var full in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
        {
            var rel = AppManifest.NormalizePath(Path.GetRelativePath(rootFull, full));
            if (!want.TryGetValue(rel, out var expected))
                return $"в сборке лишний файл: {rel}";
            var got = Sha256File(full);
            if (!got.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return $"файл сборки {rel} не совпал с манифестом";
        }
        return null;
    }

    private static string Sha256File(string path)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".mg_write_probe.tmp");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    private static void TrySave(AppManifest m, string path) { try { m.Save(path); } catch { } }
    private static void SafeDeleteDir(string dir) { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { } }
    private static void SafeDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
