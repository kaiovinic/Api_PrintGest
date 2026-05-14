using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace PrintGest.Infrastructure.Data;

public sealed class MySqlConnectionFactory(IConfiguration configuration)
{
    public MySqlConnection Create()
    {
        var connectionString = configuration.GetConnectionString("PrintGest")
            ?? throw new InvalidOperationException("Connection string 'PrintGest' não configurada.");

        return new MySqlConnection(connectionString);
    }
}
