namespace MiamiGraphics.Shell.Admin;

public interface IGunpackRepository
{

    Task<List<GunpackItem>> ListAsync(GunpackFilter? filter = null);
    Task<GunpackItem?>      GetByIdAsync(string id);
    Task<GunpackItem?>      FindByWeaponsRpfSha256Async(string sha256);
    Task                    AddAsync(GunpackItem item);
    Task                    UpdateAsync(string id, Action<GunpackItem> update);
    Task                    DeleteAsync(string id);
    Task<long>              IncrementDownloadsAsync(string id);

    Task<List<GunpackGun>>  ListGunsAsync(string gunpackId);
    Task                    BulkInsertGunsAsync(IEnumerable<GunpackGun> guns);
    Task                    UpdateGunAsync(Guid gunId, Action<GunpackGun> update);
    Task                    DeleteGunAsync(Guid gunId);
    Task                    DeleteAllGunsForPackAsync(string gunpackId);

    Task                    AddWithServiceRoleAsync(GunpackItem item, string serviceRoleKey);
    Task                    BulkUpsertGunsWithServiceRoleAsync(IEnumerable<GunpackGun> guns, string serviceRoleKey);
    Task                    DeleteAllGunsForPackWithServiceRoleAsync(string gunpackId, string serviceRoleKey);

    Task<List<GunpackVariant>> ListVariantsAsync(string gunpackId);
    Task<GunpackVariant?>      GetVariantByIdAsync(Guid variantId);
    Task                       AddVariantAsync(GunpackVariant variant, string serviceRoleKey);
    Task<int>                  PatchVariantAsync(Guid variantId, Dictionary<string, object?> patch, string serviceRoleKey);
    Task                       DeleteVariantAsync(Guid variantId, string serviceRoleKey);
}
