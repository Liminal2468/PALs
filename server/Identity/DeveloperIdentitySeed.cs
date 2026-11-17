using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Palsys.Server.Identity;

public static class DeveloperIdentitySeed
{
    private static readonly string[] AllowedRoles = ["developer", "admin", "tutor", "tutee"];

    public static async Task<int> RunAsync(
        string email,
        string password,
        string role,
        IServiceProvider services,
        IConfiguration configuration)
    {
        email = email.Trim().ToLowerInvariant();
        role = role.Trim().ToLowerInvariant();
        if (!AllowedRoles.Contains(role, StringComparer.Ordinal))
            throw new ArgumentException("Role must be developer, admin, tutor, or tutee.");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
            throw new ArgumentException("Email and password are required.");

        var userManager = services.GetRequiredService<UserManager<PalIdentityUser>>();
        var roleManager = services.GetRequiredService<RoleManager<PalIdentityRole>>();
        var identityRole = await EnsureRoleAsync(role, roleManager);
        var palUserId = await EnsurePalUserAsync(email, configuration);
        var identityUser = await userManager.FindByEmailAsync(email);

        if (identityUser is null)
        {
            identityUser = new PalIdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                PalUserId = palUserId,
            };
            EnsureSucceeded(await userManager.CreateAsync(identityUser, password), "create Identity user");
        }
        else
        {
            identityUser.PalUserId = palUserId;
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(identityUser);
            EnsureSucceeded(await userManager.ResetPasswordAsync(identityUser, resetToken, password), "reset Identity password");
            EnsureSucceeded(await userManager.UpdateAsync(identityUser), "update Identity user");
        }

        if (!await userManager.IsInRoleAsync(identityUser, identityRole.Name!))
            EnsureSucceeded(await userManager.AddToRoleAsync(identityUser, identityRole.Name!), "assign Identity role");

        await EnsurePalRoleAsync(palUserId, role, configuration);
        return 0;
    }

    private static async Task<PalIdentityRole> EnsureRoleAsync(string role, RoleManager<PalIdentityRole> roleManager)
    {
        var existing = await roleManager.FindByNameAsync(role);
        if (existing is not null) return existing;
        var created = new PalIdentityRole { Name = role, NormalizedName = role.ToUpperInvariant() };
        EnsureSucceeded(await roleManager.CreateAsync(created), "create Identity role");
        return created;
    }

    private static async Task<long> EnsurePalUserAsync(string email, IConfiguration configuration)
    {
        await using var connection = new MySqlConnection(IdentityConnection.Build(configuration));
        await connection.OpenAsync();
        await using var find = connection.CreateCommand();
        find.CommandText = "SELECT id FROM users WHERE LOWER(email) = LOWER(@email) LIMIT 1";
        find.Parameters.AddWithValue("@email", email);
        var existing = await find.ExecuteScalarAsync();
        if (existing is not null && existing != DBNull.Value) return Convert.ToInt64(existing);

        await using var insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO users
                (external_subject, email, display_name, account_status, terms_version, terms_accepted_at)
            VALUES
                (@subject, @email, @display_name, 'active', 'development', CURRENT_TIMESTAMP(3));
            SELECT LAST_INSERT_ID();
            """;
        insert.Parameters.AddWithValue("@subject", $"identity:{email}");
        insert.Parameters.AddWithValue("@email", email);
        insert.Parameters.AddWithValue("@display_name", DisplayName(email));
        return Convert.ToInt64(await insert.ExecuteScalarAsync());
    }

    private static async Task EnsurePalRoleAsync(long userId, string role, IConfiguration configuration)
    {
        await using var connection = new MySqlConnection(IdentityConnection.Build(configuration));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO roles (role_key, description)
            VALUES (@role, @description)
            ON DUPLICATE KEY UPDATE description = VALUES(description);
            INSERT INTO user_roles (user_id, role_id)
            SELECT @user_id, id FROM roles WHERE role_key = @role
            AND NOT EXISTS (
                SELECT 1 FROM user_roles
                WHERE user_id = @user_id AND role_id = roles.id
            );
            """;
        command.Parameters.AddWithValue("@role", role);
        command.Parameters.AddWithValue("@description", $"PALs {role} account");
        command.Parameters.AddWithValue("@user_id", userId);
        await command.ExecuteNonQueryAsync();
    }

    private static string DisplayName(string email)
    {
        var local = email[..email.IndexOf('@')];
        return string.Join(' ', local.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (result.Succeeded) return;
        var details = string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));
        throw new InvalidOperationException($"Could not {operation}: {details}");
    }
}
