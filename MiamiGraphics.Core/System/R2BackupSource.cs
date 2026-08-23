using System.IO;

namespace MiamiGraphics.Core.System;

public sealed class R2BackupSource : IBackupSource
{
    public Task<Stream> GetCleanUpdateRpfAsync(string exeVersion, IProgress<int>? progress, CancellationToken ct)
        => throw new NotImplementedException("Phase 2");

    public Task<Stream> GetCleanDlcRpfAsync(IProgress<int>? progress, CancellationToken ct)
        => throw new NotImplementedException("Phase 2");
}
