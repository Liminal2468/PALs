using MySqlConnector;
using System.Security.Cryptography;
using System.Text.Json;

namespace Palsys.Server.Data;

public sealed record PalsUser(long Id, string Email, string DisplayName, string Role, string Status);

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

    public async Task<long> ProvisionStudentAsync(string email, CancellationToken cancellationToken)
    {
        await ExecuteAsync("""
            INSERT INTO roles (role_key, description) VALUES ('student', 'Student using PALs')
            ON DUPLICATE KEY UPDATE description = VALUES(description)
            """, cancellationToken);
        await ExecuteAsync("""
            INSERT INTO users (external_subject, email, display_name, account_status, terms_version, terms_accepted_at)
            VALUES (@subject, @email, @display_name, 'active', 'development', CURRENT_TIMESTAMP(3))
            ON DUPLICATE KEY UPDATE account_status = 'active', updated_at = CURRENT_TIMESTAMP(3)
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

    private static string DisplayName(string email)
    {
        var local = email[..email.IndexOf('@')];
        return string.Join(' ', local.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
