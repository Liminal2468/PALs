using MySqlConnector;
using System.Security.Cryptography;
using System.Text.Json;

namespace Palsys.Server.Data;

public sealed record PalsUser(long Id, string Email, string DisplayName, string Role, string Status);
public sealed record PalsAccountSummary(long Id, string Email, string DisplayName, string Status, IReadOnlyList<string> Roles);

public sealed class PalsDataStore
{
    private readonly string connectionString;

    public PalsDataStore(IConfiguration configuration)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = configuration["DB_HOST"] ?? "127.0.0.1",
            Port = uint.TryParse(configuration["DB_PORT"], out var port) ? port : 3306,
            UserID = configuration["DB_USER"] ?? "",
            Password = configuration["DB_PASSWORD"] ?? "",
            Database = configuration["DB_NAME"] ?? "",
            SslMode = MySqlSslMode.Preferred,
            ConnectionTimeout = 5,
        };
        connectionString = builder.ConnectionString;
    }

    public async Task<PalsUser?> FindUserAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT u.id, u.email, u.display_name, u.account_status,
                   COALESCE((SELECT r.role_key FROM user_roles ur JOIN roles r ON r.id = ur.role_id
                             WHERE ur.user_id = u.id ORDER BY FIELD(r.role_key, 'admin', 'developer', 'tutor', 'tutee', 'student') LIMIT 1), 'student')
                   AS role_key
            FROM users u
            WHERE LOWER(u.email) = LOWER(@email) AND u.account_status = 'active'
            LIMIT 1
            """;
        var rows = await QueryAsync(sql, cancellationToken, ("@email", email.Trim()));
        if (rows.Count == 0) return null;
        var row = rows[0];
        return new PalsUser(Convert.ToInt64(row["id"]), Convert.ToString(row["email"])!, Convert.ToString(row["display_name"])!, Convert.ToString(row["role_key"])!, Convert.ToString(row["account_status"])!);
    }

    public async Task<Dictionary<string, object?>?> GetProfileAsync(long userId, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync("""
            SELECT u.id, u.email, u.display_name, u.account_status,
                   sp.grade_level, COALESCE(sp.section, sp.strand) AS section, sp.bio
            FROM users u
            LEFT JOIN student_profiles sp ON sp.user_id = u.id
            WHERE u.id = @id
            LIMIT 1
            """, cancellationToken, ("@id", userId));
        if (rows.Count == 0) return null;

        var row = rows[0];
        var roles = await QueryAsync("""
            SELECT r.role_key
            FROM user_roles ur JOIN roles r ON r.id = ur.role_id
            WHERE ur.user_id = @user
            ORDER BY FIELD(r.role_key, 'admin', 'developer', 'tutor', 'tutee', 'student')
            """, cancellationToken, ("@user", userId));
        var subjects = await QueryAsync("""
            SELECT s.name
            FROM user_subject_preferences usp JOIN subjects s ON s.id = usp.subject_id
            WHERE usp.user_id = @user AND s.is_active = TRUE
            ORDER BY s.name
            """, cancellationToken, ("@user", userId));

        var roleNames = roles.Select(item => Convert.ToString(item["role_key"]) ?? "").Where(value => value.Length > 0).ToArray();
        var subjectNames = subjects.Select(item => Convert.ToString(item["name"]) ?? "").Where(value => value.Length > 0).ToArray();
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = Convert.ToInt64(row["id"]),
            ["email"] = Convert.ToString(row["email"]),
            ["display_name"] = Convert.ToString(row["display_name"]),
            ["account_status"] = Convert.ToString(row["account_status"]),
            ["grade"] = row["grade_level"] is null ? null : $"Grade {Convert.ToInt32(row["grade_level"])}",
            ["section"] = Convert.ToString(row["section"]),
            ["bio"] = Convert.ToString(row["bio"]),
            ["role"] = roleNames.FirstOrDefault() ?? "student",
            ["roles"] = roleNames,
            ["subjects"] = subjectNames,
        };
    }

    public async Task<PalsAccountSummary?> FindAccountSummaryAsync(long userId, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync("""
            SELECT u.id, u.email, u.display_name, u.account_status,
                   GROUP_CONCAT(DISTINCT r.role_key ORDER BY FIELD(r.role_key, 'admin', 'developer', 'tutor', 'tutee', 'student') SEPARATOR ',') AS role_keys
            FROM users u
            LEFT JOIN user_roles ur ON ur.user_id = u.id
            LEFT JOIN roles r ON r.id = ur.role_id
            WHERE u.id = @id
            GROUP BY u.id, u.email, u.display_name, u.account_status
            LIMIT 1
            """, cancellationToken, ("@id", userId));
        if (rows.Count == 0) return null;
        var row = rows[0];
        var roles = (Convert.ToString(row["role_keys"]) ?? "student")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (roles.Length == 0) roles = ["student"];
        return new PalsAccountSummary(
            Convert.ToInt64(row["id"]),
            Convert.ToString(row["email"]) ?? "",
            Convert.ToString(row["display_name"]) ?? "",
            Convert.ToString(row["account_status"]) ?? "",
            roles);
    }

    public async Task<IReadOnlyList<PalsAccountSummary>> ListAccountSummariesAsync(CancellationToken cancellationToken)
    {
        var rows = await QueryAsync("""
            SELECT u.id, u.email, u.display_name, u.account_status,
                   GROUP_CONCAT(DISTINCT r.role_key ORDER BY FIELD(r.role_key, 'admin', 'developer', 'tutor', 'tutee', 'student') SEPARATOR ',') AS role_keys
            FROM users u
            LEFT JOIN user_roles ur ON ur.user_id = u.id
            LEFT JOIN roles r ON r.id = ur.role_id
            GROUP BY u.id, u.email, u.display_name, u.account_status
            ORDER BY u.email
            """, cancellationToken);
        return rows.Select(row =>
        {
            var roles = (Convert.ToString(row["role_keys"]) ?? "student")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (roles.Length == 0) roles = ["student"];
            return new PalsAccountSummary(
                Convert.ToInt64(row["id"]),
                Convert.ToString(row["email"]) ?? "",
                Convert.ToString(row["display_name"]) ?? "",
                Convert.ToString(row["account_status"]) ?? "",
                roles);
        }).ToArray();
    }

    public async Task UpdateProfileAsync(
        long userId,
        string? displayName,
        int? grade,
        string? section,
        string? bio,
        IReadOnlyCollection<string>? roles,
        IReadOnlyCollection<string>? subjects,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (displayName is not null)
            await ExecuteTransactionAsync(connection, transaction, "UPDATE users SET display_name = @name WHERE id = @id", cancellationToken, ("@name", displayName), ("@id", userId));

        var profileExists = Convert.ToInt32(await ScalarTransactionAsync(connection, transaction, "SELECT COUNT(*) FROM student_profiles WHERE user_id = @id", cancellationToken, ("@id", userId))) > 0;
        if (!profileExists)
        {
            if (grade is null || string.IsNullOrWhiteSpace(section))
                throw new ProfileRequirementsException("Grade and section are required when creating a profile.");
            await ExecuteTransactionAsync(connection, transaction, """
                INSERT INTO student_profiles (user_id, grade_level, strand, bio, section)
                VALUES (@user, @grade, @section, @bio, @section)
                """, cancellationToken,
                ("@user", userId), ("@grade", grade), ("@section", section.Trim()), ("@bio", bio));
        }
        else
        {
            await ExecuteTransactionAsync(connection, transaction, """
                UPDATE student_profiles
                SET grade_level = COALESCE(@grade, grade_level),
                    strand = COALESCE(@section, strand),
                    section = COALESCE(@section, section),
                    bio = COALESCE(@bio, bio),
                    updated_at = CURRENT_TIMESTAMP(3)
                WHERE user_id = @user
                """, cancellationToken,
                ("@user", userId), ("@grade", grade), ("@section", string.IsNullOrWhiteSpace(section) ? null : section.Trim()), ("@bio", bio));
        }

        if (roles is not null)
        {
            await ExecuteTransactionAsync(connection, transaction, """
                DELETE ur FROM user_roles ur
                JOIN roles r ON r.id = ur.role_id
                WHERE ur.user_id = @user AND r.role_key IN ('tutor', 'tutee')
                """, cancellationToken, ("@user", userId));
            foreach (var role in roles)
            {
                await ExecuteTransactionAsync(connection, transaction, """
                    INSERT INTO roles (role_key, description) VALUES (@role, @description)
                    ON DUPLICATE KEY UPDATE description = VALUES(description)
                    """, cancellationToken, ("@role", role), ("@description", $"{role} PALs participant"));
                await ExecuteTransactionAsync(connection, transaction, """
                    INSERT INTO user_roles (user_id, role_id)
                    SELECT @user, id FROM roles WHERE role_key = @role
                    ON DUPLICATE KEY UPDATE user_id = VALUES(user_id)
                    """, cancellationToken, ("@user", userId), ("@role", role));
            }
        }

        if (subjects is not null)
        {
            await ExecuteTransactionAsync(connection, transaction, "DELETE FROM user_subject_preferences WHERE user_id = @user", cancellationToken, ("@user", userId));
            foreach (var subject in subjects.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await ExecuteTransactionAsync(connection, transaction, """
                    INSERT INTO subjects (name, is_active) VALUES (@name, TRUE)
                    ON DUPLICATE KEY UPDATE is_active = TRUE
                    """, cancellationToken, ("@name", subject));
                await ExecuteTransactionAsync(connection, transaction, """
                    INSERT INTO user_subject_preferences (user_id, subject_id)
                    SELECT @user, id FROM subjects WHERE name = @name
                    ON DUPLICATE KEY UPDATE user_id = VALUES(user_id)
                    """, cancellationToken, ("@user", userId), ("@name", subject));
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PalsAccountSummary?> UpdateAccountAsync(
        long actorUserId,
        long targetUserId,
        string? accountStatus,
        IReadOnlyCollection<string>? roles,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (accountStatus is not null)
            await ExecuteTransactionAsync(connection, transaction, "UPDATE users SET account_status = @status WHERE id = @target", cancellationToken, ("@status", accountStatus), ("@target", targetUserId));

        if (roles is not null)
        {
            await ExecuteTransactionAsync(connection, transaction, """
                DELETE ur FROM user_roles ur
                JOIN roles r ON r.id = ur.role_id
                WHERE ur.user_id = @target AND r.role_key IN ('tutor', 'tutee')
                """, cancellationToken, ("@target", targetUserId));
            foreach (var role in roles)
            {
                await ExecuteTransactionAsync(connection, transaction, """
                    INSERT INTO roles (role_key, description) VALUES (@role, @description)
                    ON DUPLICATE KEY UPDATE description = VALUES(description)
                    """, cancellationToken, ("@role", role), ("@description", $"{role} PALs participant"));
                await ExecuteTransactionAsync(connection, transaction, """
                    INSERT INTO user_roles (user_id, role_id)
                    SELECT @target, id FROM roles WHERE role_key = @role
                    ON DUPLICATE KEY UPDATE user_id = VALUES(user_id)
                    """, cancellationToken, ("@target", targetUserId), ("@role", role));
            }
        }

        var details = JsonSerializer.Serialize(new { accountStatus, roles });
        await ExecuteTransactionAsync(connection, transaction, """
            INSERT INTO account_audits (actor_user_id, target_user_id, audit_action, details)
            VALUES (@actor, @target, 'account_updated', @details)
            """, cancellationToken,
            ("@actor", actorUserId), ("@target", targetUserId), ("@details", details));
        await transaction.CommitAsync(cancellationToken);
        return await FindAccountSummaryAsync(targetUserId, cancellationToken);
    }

    public async Task<IReadOnlyList<Dictionary<string, object?>>> ListPagesAsync(CancellationToken cancellationToken)
    {
        var rows = await QueryAsync("""
            SELECT id, page_key, title, route, is_enabled, show_in_navigation, allowed_roles
            FROM platform_pages
            ORDER BY id
            """, cancellationToken);
        return rows.Select(SafePage).ToArray();
    }

    public async Task<Dictionary<string, object?>?> UpdatePageAsync(
        long actorUserId,
        long pageId,
        string? title,
        bool? enabled,
        bool? showInNavigation,
        IReadOnlyCollection<string>? allowedRoles,
        CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var changed = await ExecuteTransactionAsync(connection, transaction, """
            UPDATE platform_pages
            SET title = COALESCE(@title, title),
                is_enabled = COALESCE(@enabled, is_enabled),
                show_in_navigation = COALESCE(@navigation, show_in_navigation),
                allowed_roles = COALESCE(@roles, allowed_roles),
                updated_at = CURRENT_TIMESTAMP(3)
            WHERE id = @id
            """, cancellationToken,
            ("@title", string.IsNullOrWhiteSpace(title) ? null : title.Trim()),
            ("@enabled", enabled),
            ("@navigation", showInNavigation),
            ("@roles", allowedRoles is null ? null : JsonSerializer.Serialize(allowedRoles)),
            ("@id", pageId));
        if (changed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var details = JsonSerializer.Serialize(new { title, enabled, showInNavigation, allowedRoles });
        await ExecuteTransactionAsync(connection, transaction, """
            INSERT INTO platform_page_audits (actor_user_id, page_id, audit_action, details)
            VALUES (@actor, @page, 'page_updated', @details)
            """, cancellationToken,
            ("@actor", actorUserId), ("@page", pageId), ("@details", details));
        await transaction.CommitAsync(cancellationToken);

        var rows = await QueryAsync("""
            SELECT id, page_key, title, route, is_enabled, show_in_navigation, allowed_roles
            FROM platform_pages WHERE id = @id LIMIT 1
            """, cancellationToken, ("@id", pageId));
        return rows.Count == 0 ? null : SafePage(rows[0]);
    }

    public async Task<long> ProvisionStudentAsync(string email, CancellationToken cancellationToken)
    {
        await ExecuteAsync("""
            INSERT INTO roles (role_key, description) VALUES ('student', 'Student using PALs')
            ON DUPLICATE KEY UPDATE description = VALUES(description)
            """, cancellationToken);
        await ExecuteAsync("""
            INSERT INTO users (external_subject, email, display_name, account_status, terms_version, terms_accepted_at)
            VALUES (@subject, @email, @display_name, 'active', NULL, NULL)
            ON DUPLICATE KEY UPDATE
              terms_accepted_at = IF(terms_version = 'development', NULL, terms_accepted_at),
              terms_version = IF(terms_version = 'development', NULL, terms_version),
              account_status = 'active',
              updated_at = CURRENT_TIMESTAMP(3)
            """, cancellationToken,
            ("@subject", $"local:{email.ToLowerInvariant()}"),
            ("@email", email.Trim().ToLowerInvariant()),
            ("@display_name", DisplayName(email)));
        var user = await FindUserAsync(email, cancellationToken);
        if (user is null) throw new InvalidOperationException("The account could not be provisioned.");
        await ExecuteAsync("""
            INSERT INTO user_roles (user_id, role_id)
            SELECT @user_id, id FROM roles WHERE role_key = 'student'
            AND NOT EXISTS (SELECT 1 FROM user_roles WHERE user_id = @user_id AND role_id = roles.id)
            """, cancellationToken, ("@user_id", user.Id));
        return user.Id;
    }

    public Task<int> RecordTermsAcceptanceAsync(string email, string termsVersion, CancellationToken cancellationToken) =>
        ExecuteAsync(
            "UPDATE users SET terms_version = @version, terms_accepted_at = UTC_TIMESTAMP(3) WHERE LOWER(email) = LOWER(@email)",
            cancellationToken,
            ("@version", termsVersion),
            ("@email", email.Trim()));

    public async Task<string> CreateSessionAsync(PalsUser user, CancellationToken cancellationToken)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace("+", "-", StringComparison.Ordinal).Replace("/", "_", StringComparison.Ordinal).TrimEnd('=');
        var data = JsonSerializer.Serialize(new { user.Email, user.Role, user.Id });
        await ExecuteAsync("INSERT INTO web_sessions (session_key, session_data, expires_at) VALUES (@key, @data, DATE_ADD(UTC_TIMESTAMP(3), INTERVAL 8 HOUR))", cancellationToken, ("@key", token), ("@data", data));
        return token;
    }

    public async Task<PalsUser?> FindSessionUserAsync(string token, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync("SELECT JSON_UNQUOTE(JSON_EXTRACT(session_data, '$.Email')) AS email FROM web_sessions WHERE session_key = @key AND expires_at > UTC_TIMESTAMP(3) LIMIT 1", cancellationToken, ("@key", token));
        var email = rows.SingleOrDefault()?["email"]?.ToString();
        return string.IsNullOrWhiteSpace(email) ? null : await FindUserAsync(email, cancellationToken);
    }

    public Task<int> RevokeSessionAsync(string token, CancellationToken cancellationToken) =>
        ExecuteAsync("DELETE FROM web_sessions WHERE session_key = @key", cancellationToken, ("@key", token));

    public async Task<List<Dictionary<string, object?>>> QueryAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<Dictionary<string, object?>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < reader.FieldCount; index++) row[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
            rows.Add(row);
        }
        return rows;
    }

    public async Task<int> ExecuteAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<object?> ScalarAsync(string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<int> ExecuteTransactionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ScalarTransactionAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        AddParameters(command, parameters);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static void AddParameters(MySqlCommand command, IEnumerable<(string Name, object? Value)> parameters)
    {
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
    }

    private static Dictionary<string, object?> SafePage(Dictionary<string, object?> row)
    {
        var rolesText = Convert.ToString(row["allowed_roles"]);
        string[] roles;
        try
        {
            roles = string.IsNullOrWhiteSpace(rolesText)
                ? []
                : JsonSerializer.Deserialize<string[]>(rolesText) ?? [];
        }
        catch (JsonException)
        {
            roles = [];
        }

        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = Convert.ToInt64(row["id"]),
            ["pageKey"] = Convert.ToString(row["page_key"]),
            ["title"] = Convert.ToString(row["title"]),
            ["route"] = Convert.ToString(row["route"]),
            ["isEnabled"] = Convert.ToBoolean(row["is_enabled"]),
            ["showInNavigation"] = Convert.ToBoolean(row["show_in_navigation"]),
            ["allowedRoles"] = roles,
        };
    }

    private static string DisplayName(string email)
    {
        var local = email[..email.IndexOf('@')];
        return string.Join(' ', local.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}

public sealed class ProfileRequirementsException : InvalidOperationException
{
    public ProfileRequirementsException(string message) : base(message) { }
}
