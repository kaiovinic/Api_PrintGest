using PrintGest.Application.Abstractions;

namespace PrintGest.Infrastructure.Data;

public sealed class MySqlDatabaseHealthCheck(MySqlConnectionFactory factory) : IDatabaseHealthCheck
{
    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result) == 1;
    }
}
