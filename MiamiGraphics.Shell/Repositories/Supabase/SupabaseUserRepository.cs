using MiamiGraphics.Shell.Repositories.Models;
using MiamiGraphics.Shell.Services;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MiamiGraphics.Shell.Repositories.Supabase;

internal sealed class SupabaseUserRepository : IUserRepository
{
    private readonly SupabaseClient _sb;

    public SupabaseUserRepository(SupabaseClient sb)
    {
        _sb = sb;
    }

    public async Task<User?> AuthenticateAsync(string login, string password)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrEmpty(password))
            return null;

        var hash = HashPassword(password);

        AuthRow? row;
        try
        {
            row = await _sb.RpcSingleAsync<AuthRow>(
                "authenticate_user",
                new { p_login = login, p_password_hash = hash });
        }
        catch (SupabaseException sx)
        {

            Debug.WriteLine($"[auth] FAIL kind={sx.Kind} status={sx.StatusCode} msg={sx.Message}");
            throw;
        }

        if (row is null)
        {
            Debug.WriteLine($"[auth] miss: login='{login}' (no matching row)");
            return null;
        }

        SupabaseClient.UserSessionToken = row.SessionToken;
        Debug.WriteLine($"[auth] ok: id='{row.Id}' role='{row.Role}' token={(string.IsNullOrEmpty(row.SessionToken) ? "none" : "set")}");
        return new User
        {
            Id           = row.Id,
            Username     = row.Username,
            Email        = row.Email,
            PasswordHash = hash,
            Role         = row.Role,
            CreatedAt    = row.CreatedAt,
            TesterAccess = row.TesterAccess,
        };
    }

    public async Task RegisterRequestAsync(string email, string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "Email is required.");
        if (string.IsNullOrWhiteSpace(username))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "Username is required.");
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "Password must be at least 8 characters.");

        var hash = HashPassword(password);
        try
        {
            await _sb.RpcVoidAsync(
                "register_request",
                new { p_email = email, p_username = username, p_password_hash = hash },
                ct);
        }
        catch (SupabaseException sx)
        {
            Debug.WriteLine($"[register.request] FAIL kind={sx.Kind} status={sx.StatusCode} msg={sx.Message}");
            throw;
        }

        Debug.WriteLine($"[register.request] ok: email='{email}' username='{username}' (code emailed)");
    }

    public async Task<User> RegisterConfirmAsync(string email, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "Email is required.");
        if (string.IsNullOrWhiteSpace(code))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "code_required");

        AuthRow? row;
        try
        {
            row = await _sb.RpcSingleAsync<AuthRow>(
                "register_confirm",
                new { p_email = email, p_code = code },
                ct);
        }
        catch (SupabaseException sx)
        {
            Debug.WriteLine($"[register.confirm] FAIL kind={sx.Kind} status={sx.StatusCode} msg={sx.Message}");
            throw;
        }

        if (row is null)
            throw new SupabaseException(SupabaseErrorKind.Server, "register_confirm returned no row.");

        SupabaseClient.UserSessionToken = row.SessionToken;
        Debug.WriteLine($"[register.confirm] ok: id='{row.Id}' username='{row.Username}' email='{row.Email}' token={(string.IsNullOrEmpty(row.SessionToken) ? "none" : "set")}");

        return new User
        {
            Id           = row.Id,
            Username     = row.Username,
            Email        = row.Email,
            PasswordHash = string.Empty,
            Role         = row.Role,
            CreatedAt    = row.CreatedAt,
            TesterAccess = row.TesterAccess,
        };
    }

    private static string HashPassword(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ReadInstallerPromo()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MiamiGraphics", "referral.json");
            if (!File.Exists(path)) return string.Empty;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("promo", out var el)) return string.Empty;
            var code = (el.GetString() ?? string.Empty).Trim().ToUpperInvariant();
            return Regex.IsMatch(code, "^[A-Z0-9]{2,16}$") ? code : string.Empty;
        }
        catch { return string.Empty; }
    }

    public async Task<bool> AttachReferralAsync(string promo, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(promo)) return false;
        try
        {
            var token = SupabaseClient.UserSessionToken;
            if (string.IsNullOrEmpty(token)) return false;
            var res = await _sb.RpcJsonAsync(
                "referral_attach_signup_secure",
                new { p_token = token, p_promo = promo.Trim().ToUpperInvariant() },
                ct);
            return res?["ok"]?.GetValue<bool>() == true;
        }
        catch (SupabaseException sx)
        {
            Debug.WriteLine($"[referral.attach] FAIL kind={sx.Kind} msg={sx.Message}");
            return false;
        }
    }

    private sealed class ReferralAttachRow
    {
        [JsonPropertyName("ok")]    public bool Ok { get; init; }
        [JsonPropertyName("error")] public string? Error { get; init; }
    }

    public async Task<User?> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        var row = await _sb.RpcSingleAsync<ProfileRow>(
            "user_get_profile",
            new { p_user_id = userId },
            ct);
        if (row is null) return null;
        return ToUserFromProfile(row);
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken ct = default)
    {

        await _sb.RpcVoidAsync(
            "password_reset_request",
            new { p_email = email ?? string.Empty },
            ct);
        Debug.WriteLine($"[pw-reset.request] requested for '{email}' (server suppresses existence)");
    }

    public async Task ConsumePasswordResetAsync(string code, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "password_hash_invalid");

        var hash = HashPassword(newPassword);
        await _sb.RpcVoidAsync(
            "password_reset_consume",
            new { p_code = code, p_new_password_hash = hash },
            ct);
        Debug.WriteLine($"[pw-reset.consume] ok code='{code}'");
    }

    public async Task ChangePasswordRequestAsync(string userId, string oldPassword, string newPassword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "user_required");
        if (string.IsNullOrEmpty(oldPassword))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "password_hash_invalid");
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 8)
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "password_hash_invalid");

        var oldHash = HashPassword(oldPassword);
        var newHash = HashPassword(newPassword);

        await _sb.RpcVoidAsync(
            "change_password_request_secure",
            new { p_token = SupabaseClient.UserSessionToken, p_old_hash = oldHash, p_new_hash = newHash },
            ct);

        Debug.WriteLine($"[change-pw.request] ok user='{userId}' (code emailed)");
    }

    public async Task ChangePasswordConfirmAsync(string userId, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "user_required");
        if (string.IsNullOrWhiteSpace(code))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "code_required");

        await _sb.RpcVoidAsync(
            "change_password_confirm_secure",
            new { p_token = SupabaseClient.UserSessionToken, p_code = code },
            ct);

        Debug.WriteLine($"[change-pw.confirm] ok user='{userId}'");
    }

    public async Task ChangeEmailRequestAsync(string userId, string currentPassword, string newEmail, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "user_required");
        if (string.IsNullOrEmpty(currentPassword))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "password_hash_invalid");
        if (string.IsNullOrWhiteSpace(newEmail))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "email_required");

        var hash = HashPassword(currentPassword);

        await _sb.RpcVoidAsync(
            "change_email_request_secure",
            new { p_token = SupabaseClient.UserSessionToken, p_password_hash = hash, p_new_email = newEmail },
            ct);

        Debug.WriteLine($"[change-email.request] ok user='{userId}' new='{newEmail}' (code emailed)");
    }

    public async Task<User> ChangeEmailConfirmAsync(string userId, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "user_required");
        if (string.IsNullOrWhiteSpace(code))
            throw new SupabaseException(SupabaseErrorKind.BadRequest, "code_required");

        var row = await _sb.RpcSingleAsync<AuthRow>(
            "change_email_confirm_secure",
            new { p_token = SupabaseClient.UserSessionToken, p_code = code },
            ct);

        if (row is null)
            throw new SupabaseException(SupabaseErrorKind.Server, "change_email_confirm returned no row.");

        Debug.WriteLine($"[change-email.confirm] ok user='{row.Id}' email='{row.Email}'");
        return new User
        {
            Id           = row.Id,
            Username     = row.Username,
            Email        = row.Email,
            PasswordHash = string.Empty,
            Role         = row.Role,
            CreatedAt    = row.CreatedAt,
        };
    }

    public async Task<User> UpdateProfileAsync(string userId, string username, string? avatarUrl, CancellationToken ct = default)
    {
        var row = await _sb.RpcSingleAsync<UpdateRow>(
            "user_update_profile_secure",
            new { p_token = SupabaseClient.UserSessionToken, p_username = username, p_avatar_url = avatarUrl ?? string.Empty },
            ct);
        if (row is null)
            throw new SupabaseException(SupabaseErrorKind.Server, "user_update_profile returned no row.");
        Debug.WriteLine($"[profile] update ok: id='{row.Id}' username='{row.Username}'");
        return new User
        {
            Id        = row.Id,
            Username  = row.Username,
            Email     = row.Email     ?? string.Empty,
            Role      = row.Role,
            CreatedAt = row.CreatedAt,
            AvatarUrl = row.AvatarUrl,
        };
    }

    private static User ToUserFromProfile(ProfileRow r) => new()
    {
        Id        = r.Id,
        Username  = r.Username,
        Email     = r.Email     ?? string.Empty,
        Role      = r.Role,
        CreatedAt = r.CreatedAt,
        AvatarUrl = r.AvatarUrl,
    };

    public Task<IReadOnlyList<User>> GetAllAsync() => throw new NotImplementedException();
    public Task<User?> GetByIdAsync(string id)     => throw new NotImplementedException();
    public Task UpsertAsync(User user)             => throw new NotImplementedException();
    public Task DeleteAsync(string id)             => throw new NotImplementedException();

    private sealed class AuthRow
    {
        public string   Id           { get; set; } = string.Empty;
        public string   Username     { get; set; } = string.Empty;
        public string   Email        { get; set; } = string.Empty;
        public string   Role         { get; set; } = string.Empty;
        public DateTime CreatedAt    { get; set; }
        public string?  SessionToken { get; set; }
        public bool     TesterAccess { get; set; }
    }

    private sealed class ProfileRow
    {
        public string   Id        { get; set; } = string.Empty;
        public string   Username  { get; set; } = string.Empty;
        public string?  Email     { get; set; }
        public string   Role      { get; set; } = "User";
        public string?  AvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class UpdateRow
    {
        public string   Id        { get; set; } = string.Empty;
        public string   Username  { get; set; } = string.Empty;
        public string?  Email     { get; set; }
        public string   Role      { get; set; } = "User";
        public string?  AvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
