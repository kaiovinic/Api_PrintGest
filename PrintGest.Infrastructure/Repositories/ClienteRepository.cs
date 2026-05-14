using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class ClienteRepository(MySqlConnectionFactory factory) : IClienteRepository
{
    public async Task<IReadOnlyList<Cliente>> ListAsync(CancellationToken cancellationToken = default)
    {
        var clientes = new List<Cliente>();
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, nome, cpf_cnpj, empresa, telefone, email, endereco, cidade
            FROM clientes
            ORDER BY nome;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            clientes.Add(new Cliente(
                reader.GetInt64("id"),
                reader.GetString("nome"),
                reader.NullableString("cpf_cnpj"),
                reader.NullableString("empresa"),
                reader.NullableString("telefone"),
                reader.NullableString("email"),
                reader.NullableString("endereco"),
                reader.NullableString("cidade")));
        }

        return clientes;
    }
}
