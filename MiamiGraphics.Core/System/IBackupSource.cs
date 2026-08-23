using System.IO;

namespace MiamiGraphics.Core.System;

public interface IBackupSource
{
    Task<Stream> GetCleanUpdateRpfAsync(string exeVersion, IProgress<int>? progress, CancellationToken ct);
    Task<Stream> GetCleanDlcRpfAsync(IProgress<int>? progress, CancellationToken ct);

    Task<Stream> GetCleanUpdateRpfAsync(string exeVersion, IProgress<int>? progress,
            Action<long, long>? bytesProgress, CancellationToken ct)
        => GetCleanUpdateRpfAsync(exeVersion, progress, ct);

    Task<Stream> GetCleanDlcRpfAsync(IProgress<int>? progress,
            Action<long, long>? bytesProgress, CancellationToken ct)
        => GetCleanDlcRpfAsync(progress, ct);

    Task<(string? sha256, long size)> GetCleanUpdateRpfMetaAsync(string exeVersion, CancellationToken ct)
        => Task.FromResult<(string?, long)>((null, 0));
}
