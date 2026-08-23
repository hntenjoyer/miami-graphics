using MiamiGraphics.Shell.Repositories.Models;

namespace MiamiGraphics.Shell.Repositories;

public interface IUserRepository
{
    Task<User?> AuthenticateAsync(string login, string password);

    Task RegisterRequestAsync(string email, string username, string password, CancellationToken ct = default);

    Task<User> RegisterConfirmAsync(string email, string code, CancellationToken ct = default);

    Task<User?> GetProfileAsync(string userId, CancellationToken ct = default);

    Task RequestPasswordResetAsync(string email, CancellationToken ct = default);

    Task ConsumePasswordResetAsync(string code, string newPassword, CancellationToken ct = default);

    Task<User> UpdateProfileAsync(string userId, string username, string? avatarUrl, CancellationToken ct = default);

    Task ChangePasswordRequestAsync(string userId, string oldPassword, string newPassword, CancellationToken ct = default);

    Task ChangePasswordConfirmAsync(string userId, string code, CancellationToken ct = default);

    Task ChangeEmailRequestAsync(string userId, string currentPassword, string newEmail, CancellationToken ct = default);

    Task<User> ChangeEmailConfirmAsync(string userId, string code, CancellationToken ct = default);

    Task<IReadOnlyList<User>> GetAllAsync();
    Task<User?> GetByIdAsync(string id);
    Task UpsertAsync(User user);
    Task DeleteAsync(string id);
}
