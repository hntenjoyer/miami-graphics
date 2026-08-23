namespace MiamiGraphics.Shell.Admin;

public interface IFeaturedPicksRepository
{
    Task<List<FeaturedPick>> ListAsync();

    Task UpsertAsync(int slotIndex, string reduxId);

    Task DeleteAsync(int slotIndex);
}
