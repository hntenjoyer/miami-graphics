using System.IO;
using System.Threading;
using MiamiGraphics.Core.I18n;

namespace MiamiGraphics.Shell.Services;

internal static class CriticalOperationGuard
{
    public const long OsSafetyMarginBytes = 2L * 1024 * 1024 * 1024;

    private static int _activeCount;

    public static bool IsActive => Volatile.Read(ref _activeCount) > 0;

    public static IDisposable Enter()
    {
        Interlocked.Increment(ref _activeCount);
        return new Releaser();
    }

    public static void EnsureSpaceAvailable(
        string targetPath, long requiredBytes, string? operationLabel = null)
    {
        if (requiredBytes <= 0) return;
        if (string.IsNullOrWhiteSpace(targetPath)) return;

        string driveRoot;
        try
        {
            driveRoot = Path.GetPathRoot(Path.GetFullPath(targetPath)) ?? string.Empty;
        }
        catch
        {

            return;
        }
        if (string.IsNullOrEmpty(driveRoot)) return;

        long freeBytes;
        try
        {
            var drive = new DriveInfo(driveRoot);
            if (!drive.IsReady) return;
            freeBytes = drive.AvailableFreeSpace;
        }
        catch
        {

            return;
        }

        long needed = requiredBytes + OsSafetyMarginBytes;
        if (freeBytes >= needed) return;

        throw new InsufficientDiskSpaceException(
            driveRoot:        driveRoot,
            requiredBytes:    requiredBytes,
            availableBytes:   freeBytes,
            osMarginBytes:    OsSafetyMarginBytes,
            operationLabel:   operationLabel);
    }

    public static bool TryEnsureSpaceAvailable(
        string targetPath, long requiredBytes, string operationLabel,
        out string? error)
    {
        try
        {
            EnsureSpaceAvailable(targetPath, requiredBytes, operationLabel);
            error = null;
            return true;
        }
        catch (InsufficientDiskSpaceException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private sealed class Releaser : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Interlocked.Decrement(ref _activeCount);
        }
    }
}

public sealed class InsufficientDiskSpaceException : IOException
{
    public string DriveRoot { get; }
    public long RequiredBytes { get; }
    public long AvailableBytes { get; }
    public long OsMarginBytes { get; }

    public InsufficientDiskSpaceException(
        string driveRoot, long requiredBytes, long availableBytes,
        long osMarginBytes, string? operationLabel)
        : base(BuildMessage(driveRoot, requiredBytes, availableBytes, osMarginBytes, operationLabel))
    {
        DriveRoot = driveRoot;
        RequiredBytes = requiredBytes;
        AvailableBytes = availableBytes;
        OsMarginBytes = osMarginBytes;
    }

    private static string BuildMessage(
        string driveRoot, long requiredBytes, long availableBytes,
        long osMarginBytes, string? operationLabel)
    {
        return Loc.T("error.notEnoughDiskSpace",
            ("drive", driveRoot.TrimEnd('\\')),
            ("operation", string.IsNullOrWhiteSpace(operationLabel) ? Loc.T("misc.operation") : operationLabel),
            ("need", FormatBytes(requiredBytes)),
            ("free", FormatBytes(availableBytes)),
            ("reserve", FormatBytes(osMarginBytes)));
    }

    private static string FormatBytes(long bytes)
    {
        const double GB = 1024L * 1024 * 1024;
        const double MB = 1024L * 1024;
        var ci = global::System.Globalization.CultureInfo.InvariantCulture;
        if (bytes >= GB) return Loc.T("misc.sizeGb", ("value", (bytes / GB).ToString("0.0", ci)));
        if (bytes >= MB) return Loc.T("misc.sizeMb", ("value", (bytes / MB).ToString("0.0", ci)));
        return Loc.T("misc.sizeKb", ("value", (bytes / 1024.0).ToString("0.0", ci)));
    }
}
