using System.Security.Cryptography;
using System.Text;
using MySqlConnector;

namespace Palsys.Server.Identity;

/// <summary>
/// Keeps a short-lived digest of successful authenticator codes so a code
/// cannot be accepted repeatedly during its validity window. The raw code is
/// never persisted.
/// </summary>
public sealed class MfaReplayStore
{
    private readonly string connectionString;

    public MfaReplayStore(IConfiguration configuration)
    {
        connectionString = IdentityConnection.Build(configuration);
    }

    public async Task<bool> TryConsumeAsync(long identityUserId, string code, CancellationToken cancellationToken)
    {
        if (identityUserId <= 0 || string.IsNullOrWhiteSpace(code)) return false;

        var codeHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim())))
            .ToLowerInvariant();

        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var cleanup = connection.CreateCommand())
        {
            cleanup.Transaction = transaction;
            cleanup.CommandText = "DELETE FROM identity_mfa_replays WHERE used_at < DATE_SUB(UTC_TIMESTAMP(3), INTERVAL 2 MINUTE)";
            await cleanup.ExecuteNonQueryAsync(cancellationToken);
        }

        try
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO identity_mfa_replays (identity_user_id, code_hash, used_at) VALUES (@user, @hash, UTC_TIMESTAMP(3))";
            insert.Parameters.AddWithValue("@user", identityUserId);
            insert.Parameters.AddWithValue("@hash", codeHash);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }
        catch (MySqlException exception) when (exception.Number == 1062)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }
    }
}
