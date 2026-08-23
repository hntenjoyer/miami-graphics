namespace MiamiGraphics.Shell.Admin;

public interface IGunpackWhitelistRepository
{
    Task<List<GunpackWhitelistEntry>> ListAsync();
}
