using System.IO;

namespace MiamiGraphics.Core.System;

public sealed class LocalFolderBackupSource : IBackupSource
{
    private readonly string _root;

    public LocalFolderBackupSource(string root)
    {
        _root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public Task<Stream> GetCleanUpdateRpfAsync(string exeVersion, IProgress<int>? progress, CancellationToken ct)
    {
        var path = Path.Combine(_root, "update.rpf");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Clean update.rpf not found: {path}");

        progress?.Report(0);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        progress?.Report(100);
        return Task.FromResult(stream);
    }

    public Task<Stream> GetCleanDlcRpfAsync(IProgress<int>? progress, CancellationToken ct)
    {
        var path = Path.Combine(_root, "dlc.rpf");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Clean dlc.rpf not found: {path}");

        progress?.Report(0);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        progress?.Report(100);
        return Task.FromResult(stream);
    }
}
