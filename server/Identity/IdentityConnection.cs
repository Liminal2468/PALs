using MySqlConnector;

namespace Palsys.Server.Identity;

public static class IdentityConnection
{
    public static string Build(IConfiguration configuration) => new MySqlConnectionStringBuilder
    {
        Server = configuration["DB_HOST"] ?? "127.0.0.1",
        Port = uint.TryParse(configuration["DB_PORT"], out var port) ? port : 3306,
        UserID = configuration["DB_USER"] ?? "",
        Password = configuration["DB_PASSWORD"] ?? "",
        Database = configuration["DB_NAME"] ?? "",
        SslMode = MySqlSslMode.Preferred,
        ConnectionTimeout = 5,
    }.ConnectionString;
}
