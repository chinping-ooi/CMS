using System.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
// using Npgsql;

namespace CMS.Services;

public sealed class DatabaseConnectionService
{
    private readonly IConfiguration _configuration;

    public DatabaseConnectionService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public IDbConnection CreateConnection(string name = "DefaultConnection")
    {
        var connectionString = _configuration.GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{name}' is not configured.");
        }

        return new SqlConnection(connectionString);
    }

    public async Task<SqlConnection> CreateOpenConnectionAsync(string name = "DefaultConnection")
    {
        var connection = (SqlConnection)CreateConnection(name);
        await connection.OpenAsync();
        return connection;
    }
}
