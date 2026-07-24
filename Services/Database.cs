using System.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;

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

        return new NpgsqlConnection(connectionString);
    }

    public async Task<NpgsqlConnection> CreateOpenConnectionAsync(string name = "DefaultConnection")
    {
        var connection = (NpgsqlConnection)CreateConnection(name);
        await connection.OpenAsync();
        return connection;
    }
}
