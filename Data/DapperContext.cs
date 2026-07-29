using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
// using Npgsql;

namespace CMS.Data;

public sealed class DapperContext
{
    private readonly string _connectionString;

    public DapperContext(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
    }

    public SqlConnection  CreateConnection()
    {
        return new SqlConnection (_connectionString);
    }
    
    public async Task<SqlConnection> CreateOpenConnectionAsync()
    {
        var connection = CreateConnection();
        await connection.OpenAsync();
        return connection;
    }

    // public NpgsqlConnection CreateConnection()
    // {
    //     return new NpgsqlConnection(_connectionString);
    // }

    // public async Task<NpgsqlConnection> CreateOpenConnectionAsync()
    // {
    //     var connection = CreateConnection();
    //     await connection.OpenAsync();
    //     return connection;
    // }
}
