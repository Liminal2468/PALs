using Microsoft.AspNetCore.Identity;
using MySqlConnector;

namespace Palsys.Server.Identity;

public sealed record DeveloperCredential(string Email, string Role);

public sealed class DeveloperIdentityStore
{
    private static readonly string[] AllowedRoles = ["developer", "admin", "tutor", "tutee"];
    private readonly UserManager<PalIdentityUser> users;
    private readonly RoleManager<PalIdentityRole> roles;
    private readonly string connectionString;

    public DeveloperIdentityStore(
        UserManager<PalIdentityUser> users,
        RoleManager<PalIdentityRole> roles,
        IConfiguration configuration)
    {
        this.users = users;
        this.roles = roles;
        connectionString = IdentityConnection.Build(configuration);
    }

    public async Task<DeveloperCredential?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password)) return null;
        var user = await users.FindByEmailAsync(email.Trim());
        if (user is null || !await users.IsEmailConfirmedAsync(user)) return null;
        if (await users.IsLockedOutAsync(user)) return null;
        if (!await users.CheckPasswordAsync(user, password)) return null;

        var userRoles = await users.GetRolesAsync(user);
        var role = userRoles.FirstOrDefault(value => AllowedRoles.Contains(value, StringComparer.Ordinal));
        if (role is null || !await IsPalAccountActiveAsync(user.PalUserId, cancellationToken)) return null;
        if (!await roles.RoleExistsAsync(role)) return null;
        return new DeveloperCredential(user.Email ?? email.Trim().ToLowerInvariant(), role);
    }

    public async Task<PalIdentityUser?> FindAsync(string email) => await users.FindByEmailAsync(email.Trim());

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
}
