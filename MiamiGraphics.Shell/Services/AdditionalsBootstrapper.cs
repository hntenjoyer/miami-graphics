using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Shell.Services;

internal static class AdditionalsBootstrapper
{
    private const string Marker = @"Keys\gtav_ng_key.dat";

    private static readonly string[] Urls =
    {
        "https://ru.miamigraphicsstorage.uk/tools/additionals.zip",
        "https://miamigraphicsstorage.uk/tools/additionals.zip",
        "https://cdn.miamigraphicsstorage.uk/tools/additionals.zip",
    };

    private static readonly (string Sha256, long Size)[] AcceptedArchives =
    {
        ("E9A9CA7598AD0696360421BCE819408EA57CDFE6092DA3ED316577BA3DF8E969", 33_169_569L),
    };

    public static async Task EnsureAsync(CancellationToken ct = default)
    {
        if (AdditionalsResolver.FindAdditionalsRoot() is { } existing)
        {
            Debug.WriteLine($"[additionals.bootstrap] already present: {existing}");
            return;
        }

        var targetDir = GetLocalAdditionalsDir();
        if (IsValidAdditionals(targetDir))
        {
            Debug.WriteLine($"[additionals.bootstrap] already present in LocalAppData: {targetDir}");
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "MiamiGraphics", "additionals_bootstrap", Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(tempRoot, "additionals.zip");
        var partialPath = zipPath + ".partial";
        var extractDir = Path.Combine(tempRoot, "extract");

        try
        {
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(extractDir);

            await DownloadArchiveAsync(partialPath, ct);
            File.Move(partialPath, zipPath, overwrite: true);

            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            var sourceDir = ResolveExtractedAdditionalsRoot(extractDir);
            if (!IsValidAdditionals(sourceDir))
                throw new InvalidDataException("Downloaded additionals.zip does not contain additionals/Keys/gtav_ng_key.dat.");

            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);

            CopyDirectory(sourceDir, targetDir);

            if (!IsValidAdditionals(targetDir))
                throw new InvalidDataException("additionals bootstrap finished, but marker file is still missing.");

            Debug.WriteLine($"[additionals.bootstrap] installed to: {targetDir}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[additionals.bootstrap] failed: {ex}");
            DownloadLog.Write("verify", "additionals bootstrap", ex);
        }
        finally
        {
            TryDeleteFile(partialPath);
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
            catch (Exception ex) { Debug.WriteLine($"[additionals.bootstrap] temp cleanup skipped: {ex.Message}"); }
        }
    }

    public static async Task<string?> ForceRefreshKeysAsync(CancellationToken ct = default)
    {
        var targetDir = GetLocalAdditionalsDir();
        var tempRoot = Path.Combine(Path.GetTempPath(), "MiamiGraphics", "additionals_refresh", Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(tempRoot, "additionals.zip");
        var partialPath = zipPath + ".partial";
        var extractDir = Path.Combine(tempRoot, "extract");

        try
        {
            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(extractDir);

            await DownloadArchiveAsync(partialPath, ct);
            File.Move(partialPath, zipPath, overwrite: true);

            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
            var sourceDir = ResolveExtractedAdditionalsRoot(extractDir);
            if (!IsValidAdditionals(sourceDir))
                throw new InvalidDataException("Downloaded additionals.zip does not contain additionals/Keys/gtav_ng_key.dat.");

            if (Directory.Exists(targetDir))
                Directory.Delete(targetDir, recursive: true);
            CopyDirectory(sourceDir, targetDir);

            var keysDir = Path.Combine(targetDir, "Keys");
            if (File.Exists(Path.Combine(keysDir, "gtav_ng_key.dat")))
            {
                Debug.WriteLine($"[additionals.refresh] fresh keys installed to: {keysDir}");
                return keysDir;
            }
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[additionals.refresh] failed: {ex}");
            DownloadLog.Write("verify", "additionals refresh", ex);
            return null;
        }
        finally
        {
            TryDeleteFile(partialPath);
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
            catch { }
        }
    }

    private static string GetLocalAdditionalsDir()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "MiamiGraphics", "additionals");
    }

    private static bool IsValidAdditionals(string? dir)
        => !string.IsNullOrWhiteSpace(dir)
           && Directory.Exists(dir)
           && File.Exists(Path.Combine(dir, Marker));

    private static async Task DownloadArchiveAsync(string destinationPath, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        Exception? lastError = null;
        bool integrityFailed = false;

        foreach (var url in Urls)
        {
            try
            {
                Debug.WriteLine($"[additionals.bootstrap] downloading {url}");
                using var http = HttpClientFactory.CreateFragmenting(TimeSpan.FromMinutes(10));
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                await using (var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
                {
                    await input.CopyToAsync(output, ct);
                    await output.FlushAsync(ct);
                }

                var reason = await Task.Run(() => VerifyArchive(destinationPath), ct);
                if (reason is null) return;

                integrityFailed = true;
                Debug.WriteLine($"[additionals.bootstrap] {url}: архив не совпал с эталоном - {reason}");
                DownloadLog.Write("verify", $"additionals.zip с {url} не совпал с эталоном - {reason}");
                TryDeleteFile(destinationPath);
                continue;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                lastError = ex;
                TryDeleteFile(destinationPath);
                Debug.WriteLine($"[additionals.bootstrap] download failed from {url}: {ex.Message}");
            }
        }

        throw new IOException(
            integrityFailed
                ? Loc.T("error.additionalsIntegrityFailed")
                : Loc.T("error.additionalsDownloadFailed"),
            lastError);
    }

    private static string? VerifyArchive(string path)
    {
        try
        {
            if (AcceptedArchives.Length == 0)
                return "в сборку не вшит ни один эталон";

            var size = new FileInfo(path).Length;
            var actual = ComputeSha256(path);
            foreach (var (sha, expectedSize) in AcceptedArchives)
            {
                if (size == expectedSize && string.Equals(actual, sha, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.WriteLine($"[additionals.verify] ok: {size} B, sha256 {actual}");
                    return null;
                }
            }
            return $"размер {size} B, sha256 {actual} - нет в списке эталонов";
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

    private static string ResolveExtractedAdditionalsRoot(string extractDir)
    {
        var direct = Path.Combine(extractDir, "additionals");
        if (Directory.Exists(direct))
            return direct;

        var dirs = Directory.GetDirectories(extractDir);
        if (dirs.Length == 1 && string.Equals(Path.GetFileName(dirs[0]), "additionals", StringComparison.OrdinalIgnoreCase))
            return dirs[0];

        return extractDir;
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(targetDir, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destination = Path.Combine(targetDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
