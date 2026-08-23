using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Shell.Services;

public sealed class JreBootstrapper
{
    private const string JreZipUrl =
        "https://cdn.miamigraphicsstorage.uk/releases/MiamiGraphicsJre_1.0.0.zip";
    private const string JreSha256 =
        "C96194118CFEAEB205C1782DFC87E8B6ACA7A7EF8448F0E188AB946B232C6435";

    private static string AdditionalsDir =>
        Path.Combine(AppContext.BaseDirectory, "additionals");
    private static string JreDir => Path.Combine(AdditionalsDir, "jre");
    private static string JavaExePath => Path.Combine(JreDir, "bin", "java.exe");

    public sealed record EnsureResult(
        bool   Success,
        bool   AlreadyInstalled,
        string JrePath,
        long   DownloadedBytes,
        string? ErrorMessage);

    private static readonly System.Threading.SemaphoreSlim _gate = new(1, 1);

    public async Task<EnsureResult> EnsureInstalledAsync(
        IProgress<(string Phase, double Percent)>? progress = null,
        CancellationToken ct = default)
    {
        if (IsAlreadyInstalled())
            return new EnsureResult(true, true, JreDir, 0, null);

        await _gate.WaitAsync(ct);
        try
        {
            if (IsAlreadyInstalled())
                return new EnsureResult(true, true, JreDir, 0, null);
            return await EnsureInstalledCoreAsync(progress, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<EnsureResult> EnsureInstalledCoreAsync(
        IProgress<(string Phase, double Percent)>? progress,
        CancellationToken ct)
    {

        progress?.Report(("downloading", 0));
        Directory.CreateDirectory(AdditionalsDir);
        var lad = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var tmpDir = Path.Combine(lad, "MiamiGraphics", "tmp");
        Directory.CreateDirectory(tmpDir);
        var tmpZip = Path.Combine(tmpDir, "Jre.tmp.zip");

        long downloaded;
        try
        {
            long lastPct = -1;
            await Bridge.AppBridge.DownloadViaMirrorAsync(JreZipUrl, tmpZip,
                (done, total) =>
                {
                    if (total <= 0) return;
                    long pct = done * 100 / total;
                    if (pct == lastPct) return;
                    lastPct = pct;
                    progress?.Report(("downloading", pct));
                }, ct);
            downloaded = new FileInfo(tmpZip).Length;
        }
        catch (Exception ex)
        {
            return new EnsureResult(false, false, JreDir, 0, $"Download failed: {ex.Message}");
        }

        progress?.Report(("verifying", 0));
        try
        {
            var actual = await Task.Run(() => ComputeSha256(tmpZip), ct);
            if (!string.Equals(actual, JreSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(tmpZip); } catch { }
                return new EnsureResult(false, false, JreDir, downloaded,
                    Loc.T("jre.zipShaMismatch"));
            }
        }
        catch (Exception ex)
        {
            return new EnsureResult(false, false, JreDir, downloaded, $"SHA-256 check failed: {ex.Message}");
        }

        progress?.Report(("extracting", 0));
        try
        {
            if (Directory.Exists(JreDir))
            {
                try { Directory.Delete(JreDir, recursive: true); }
                catch (Exception ex) { Debug.WriteLine($"[jre] pre-clean failed: {ex.Message}"); }
            }
            await Task.Run(() => ZipFile.ExtractToDirectory(tmpZip, AdditionalsDir, overwriteFiles: true), ct);
            try { File.Delete(tmpZip); } catch { }
        }
        catch (Exception ex)
        {
            return new EnsureResult(false, false, JreDir, downloaded, $"Extract failed: {ex.Message}");
        }

        if (!IsAlreadyInstalled())
            return new EnsureResult(false, false, JreDir, downloaded,
                Loc.T("jre.extractedButJavaMissing"));

        progress?.Report(("done", 100));
        return new EnsureResult(true, false, JreDir, downloaded, null);
    }

    public static bool IsAlreadyInstalled() => File.Exists(JavaExePath);

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }
}
