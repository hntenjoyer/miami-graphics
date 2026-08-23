using System.Diagnostics;
using System.IO;

namespace MiamiGraphics.Shell.Services;

public sealed class GltfpackRunner
{
    public bool   IsAvailable { get; }
    public string? BinaryPath { get; }

    public GltfpackRunner()
    {
        BinaryPath = Discover();
        IsAvailable = !string.IsNullOrWhiteSpace(BinaryPath) && File.Exists(BinaryPath);
        if (IsAvailable)
            Debug.WriteLine($"[gltfpack] available at: {BinaryPath}");
        else
            Debug.WriteLine("[gltfpack] NOT available - gunpack GLBs will ship uncompressed (~7-8 MB each instead of ~2-3 MB). Drop gltfpack.exe into <exeDir>\\additionals\\tools\\ to enable.");
    }

    public async Task<bool> TryCompressAsync(string inputGlb, string outputGlb, CancellationToken ct)
    {
        if (!File.Exists(inputGlb)) return false;

        if (!IsAvailable)
        {

            File.Copy(inputGlb, outputGlb, overwrite: true);
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputGlb)!);

        var psi = new ProcessStartInfo
        {
            FileName               = BinaryPath!,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add(inputGlb);
        psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(outputGlb);
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("-tw");
        psi.ArgumentList.Add("-kn");

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
            {
                Debug.WriteLine($"[gltfpack] failed to spawn process for {inputGlb}");
                File.Copy(inputGlb, outputGlb, overwrite: true);
                return false;
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

            try
            {
                await proc.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                Debug.WriteLine($"[gltfpack] timeout/cancelled for {inputGlb}");
                File.Copy(inputGlb, outputGlb, overwrite: true);
                return false;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode != 0 || !File.Exists(outputGlb))
            {
                Debug.WriteLine($"[gltfpack] exit={proc.ExitCode} for {inputGlb}\n  stderr: {stderr}\n  stdout: {stdout}");
                File.Copy(inputGlb, outputGlb, overwrite: true);
                return false;
            }

            var inSize  = new FileInfo(inputGlb).Length;
            var outSize = new FileInfo(outputGlb).Length;
            Debug.WriteLine($"[gltfpack] {Path.GetFileName(inputGlb)}: {inSize / 1024} KB -> {outSize / 1024} KB ({(outSize * 100 / Math.Max(1, inSize))}%)");
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[gltfpack] EXCEPTION on {inputGlb}: {ex.Message}");
            try { File.Copy(inputGlb, outputGlb, overwrite: true); } catch { }
            return false;
        }
    }

    private static string? Discover()
    {
        var resolved = AdditionalsResolver.AdditionalsPath("tools", "gltfpack.exe");
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        var asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
                     ?? AppContext.BaseDirectory;

        var candidates = new List<string>
        {

            Path.Combine(asmDir, "additionals", "tools", "gltfpack.exe"),

            Path.Combine(asmDir, "..", "..", "..", "..", "additionals", "tools", "gltfpack.exe"),
            Path.Combine(asmDir, "..", "..", "..", "additionals", "tools", "gltfpack.exe"),
        };

        foreach (var c in candidates)
        {
            try
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full)) return full;
            }
            catch {  }
        }

        try
        {
            var psi = new ProcessStartInfo("where", "gltfpack")
            {
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            if (p.ExitCode == 0)
            {
                var firstLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                      .FirstOrDefault()?.Trim();
                if (!string.IsNullOrWhiteSpace(firstLine) && File.Exists(firstLine))
                    return firstLine;
            }
        }
        catch {  }

        return null;
    }
}
