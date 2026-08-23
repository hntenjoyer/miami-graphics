using MiamiGraphics.Bridge;

namespace MiamiGraphics.Shell.Repositories;

public interface IAppSettingsRepository
{
    Task<AppSettingsDto> GetAsync();
    Task SaveAsync(AppSettingsDto settings);
}
