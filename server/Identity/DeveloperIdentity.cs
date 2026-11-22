using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Palsys.Server.Identity;

public sealed record DeveloperCredential(string Email, string Role, PalIdentityUser User)
{
    public bool TwoFactorEnabled => User.TwoFactorEnabled;
}

public sealed class DeveloperIdentityStore
{
    private static readonly string[] AllowedRoles = ["developer", "admin", "tutor", "tutee"];
    private readonly UserManager<PalIdentityUser> users;
    private readonly RoleManager<PalIdentityRole> roles;
    private readonly MfaReplayStore mfaReplay;
    private readonly string connectionString;
    private readonly bool requireMfaForPrivileged;

    public DeveloperIdentityStore(
        UserManager<PalIdentityUser> users,
        RoleManager<PalIdentityRole> roles,
        MfaReplayStore mfaReplay,
        IConfiguration configuration)
    {
        this.users = users;
        this.roles = roles;
        this.mfaReplay = mfaReplay;
        connectionString = IdentityConnection.Build(configuration);
        requireMfaForPrivileged = bool.TryParse(configuration["MFA_REQUIRE_PRIVILEGED"], out var required) && required;
    }

    public async Task<DeveloperCredential?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
    {
        var credential = await FindEligibleAsync(email, cancellationToken);
        if (credential is null || string.IsNullOrEmpty(password)) return null;
        if (!await users.CheckPasswordAsync(credential.User, password))
        {
            if (credential.User.LockoutEnabled)
                await users.AccessFailedAsync(credential.User);
            return null;
        }

        await users.ResetAccessFailedCountAsync(credential.User);
        return credential;
    }

    public async Task<DeveloperCredential?> FindEligibleAsync(string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null || !await users.IsEmailConfirmedAsync(user)) return null;
        if (await users.IsLockedOutAsync(user)) return null;

        var userRoles = await users.GetRolesAsync(user);
        var role = userRoles.FirstOrDefault(value => AllowedRoles.Contains(value, StringComparer.Ordinal));
        if (role is null || !await IsPalAccountActiveAsync(user.PalUserId, cancellationToken)) return null;
        if (!await roles.RoleExistsAsync(role)) return null;
        return new DeveloperCredential(user.Email ?? email.Trim().ToLowerInvariant(), role, user);
    }

    public async Task<PalIdentityUser?> FindAsync(string email) => await users.FindByEmailAsync(email.Trim());

    public async Task<bool> HasStudentPasswordAsync(long palUserId)
    {
        var user = await users.Users.SingleOrDefaultAsync(account => account.PalUserId == palUserId);
        return user is not null
            && !await HasPrivilegedRoleAsync(user)
            && await users.HasPasswordAsync(user);
    }

    public async Task<IdentityResult> SetStudentPasswordAsync(long palUserId, string email, string password)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(normalizedEmail);
        if (user is not null)
        {
            if ((user.PalUserId is not null && user.PalUserId != palUserId)
                || await HasPrivilegedRoleAsync(user)
                || await users.HasPasswordAsync(user))
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = "password_already_configured",
                    Description = "A password cannot be set for this account.",
                });
            }

            user.PalUserId = palUserId;
            user.EmailConfirmed = true;
            user.LockoutEnabled = true;
            var linked = await users.UpdateAsync(user);
            if (!linked.Succeeded) return linked;
            return await users.AddPasswordAsync(user, password);
        }

        var created = new PalIdentityUser
        {
            UserName = normalizedEmail,
            Email = normalizedEmail,
            EmailConfirmed = true,
            LockoutEnabled = true,
            PalUserId = palUserId,
        };
        return await users.CreateAsync(created, password);
    }

    public async Task<PalIdentityUser?> AuthenticateStudentAsync(
        long palUserId,
        string email,
        string password)
    {
        if (string.IsNullOrWhiteSpace(password)) return null;
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null
            || user.PalUserId != palUserId
            || !await users.IsEmailConfirmedAsync(user)
            || await users.IsLockedOutAsync(user)
            || await HasPrivilegedRoleAsync(user))
        {
            return null;
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            if (user.LockoutEnabled)
                await users.AccessFailedAsync(user);
            return null;
        }

        await users.ResetAccessFailedCountAsync(user);
        return user;
    }

    public bool RequiresMfa(DeveloperCredential credential) =>
        requireMfaForPrivileged
        && credential.Role is "admin" or "developer";

    public async Task<bool> VerifyAuthenticatorCodeAsync(
        PalIdentityUser user,
        string? code,
        CancellationToken cancellationToken,
        bool requireEnabled = true)
    {
        var normalized = NormalizeCode(code);
        if (normalized is null || (requireEnabled && !user.TwoFactorEnabled)) return false;
        if (!await users.VerifyTwoFactorTokenAsync(
                user,
                TokenOptions.DefaultAuthenticatorProvider,
                normalized))
            return false;

        return await mfaReplay.TryConsumeAsync(user.Id, normalized, cancellationToken);
    }

    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var normalized = code.Replace(" ", "", StringComparison.Ordinal).Trim();
        return normalized.Length is >= 6 and <= 8 && normalized.All(char.IsDigit)
            ? normalized
            : null;
    }

    private async Task<bool> IsPalAccountActiveAsync(long? palUserId, CancellationToken cancellationToken)
    {
        if (palUserId is null || string.IsNullOrWhiteSpace(connectionString)) return false;
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT account_status FROM users WHERE id = @id LIMIT 1";
        command.Parameters.AddWithValue("@id", palUserId.Value);
        var status = await command.ExecuteScalarAsync(cancellationToken);
        return status is string value && value.Equals("active", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> HasPrivilegedRoleAsync(PalIdentityUser user)
    {
        var userRoles = await users.GetRolesAsync(user);
        return userRoles.Contains("admin", StringComparer.Ordinal)
            || userRoles.Contains("developer", StringComparer.Ordinal);
    }
}
