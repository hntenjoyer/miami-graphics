#nullable enable
using MiamiGraphics.Core.Injector;

namespace MiamiGraphics.Core.Services;

public interface IRpfPatchInjector
{
    bool InjectPatch(string patchDirectory);
    string? LastError { get; }
}

public sealed class RpfPatchInjector : IRpfPatchInjector
{
    private readonly RpfInjectEngine _engine;

    public RpfPatchInjector(string gtaRootPath) => _engine = new RpfInjectEngine(gtaRootPath);

    public bool InjectPatch(string patchDirectory) => _engine.InjectPatch(patchDirectory);

    public string? LastError => _engine.LastError;
}
