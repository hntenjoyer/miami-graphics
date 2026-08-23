using MiamiGraphics.Shell.Admin;

namespace MiamiGraphics.Shell.Services;

public interface ISelectedGunsInstaller
{
    Task<List<SelectedGun>> ListInstalledAsync();

    Task<bool> IsInstalledAsync(string internalName);

    Task<bool> HasAnySelectedAsync();

    Task<MiamiGraphics.Bridge.InjectResultDto> InstallGunAsync(
        string gunpackId,
        string internalName,
        EmitProgress emit,
        CancellationToken ct);

    Task<MiamiGraphics.Bridge.InjectResultDto> RemoveGunAsync(
        string internalName,
        EmitProgress emit,
        CancellationToken ct);

    Task<MiamiGraphics.Bridge.InjectResultDto> RebuildAsync(
        EmitProgress emit,
        CancellationToken ct);

    Task SetVanillaSlotsAsync(string? packId, IReadOnlyCollection<string>? internalNames);

    Task<VerifyReport> VerifyAsync();

    Task<MiamiGraphics.Bridge.InjectResultDto> UninstallAllAsync(
        EmitProgress emit,
        CancellationToken ct);

    Task<MiamiGraphics.Bridge.InjectResultDto> ApplyStandaloneCustomAsync(
        string internalName,
        string displayName,
        string packId,
        IReadOnlyDictionary<string, byte[]> gunFilesByName,
        EmitProgress emit,
        CancellationToken ct);

    Task<MiamiGraphics.Bridge.InjectResultDto> ApplyStandaloneAnimAsync(
        string internalName, string displayName, string packId,
        IReadOnlyDictionary<string, byte[]> gunFilesByName,
        IReadOnlyList<TargetDlcEditor.AnimLooseFile> looseFiles,
        (string DlcName, string RelPath, byte[] Bytes)? updateRpfPatch,
        EmitProgress emit,
        CancellationToken ct);

    Task<byte[]?> BuildBytesFromStateAsync(CancellationToken ct);

    Task<MiamiGraphics.Bridge.CustomSkinAppliedDto?> GetCustomSkinAsync();

    Task SetCustomSkinAsync(string internalName, string displayName, string packId);

    Task<MiamiGraphics.Bridge.InjectResultDto> RemoveCustomSkinAsync(
        EmitProgress emit,
        CancellationToken ct);

    Task<List<CustomSkinEntry>> GetCustomsAsync();

    Task AddOrReplaceCustomAsync(
        string internalName, string displayName, string packId,
        IReadOnlyDictionary<string, byte[]> files);

    Task<MiamiGraphics.Bridge.InjectResultDto> RemoveCustomAsync(
        string internalName, EmitProgress emit, CancellationToken ct);

    Task ForgetCustomAsync(string internalName);

    Task<bool> ReconcileStateAsync();

    public delegate void EmitProgress(string phase, int percent, string? errorMessage, string? detailMessage);
}

public sealed record VerifyReport(
    bool Ok,
    int  StateGunsCount,
    bool TargetDlcExists,
    bool RpfPresentInDlc,
    string? StateSha,
    string? ActualSha,
    string Summary);
