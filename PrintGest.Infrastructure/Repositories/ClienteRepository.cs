using MySqlConnector;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class ClienteRepository(IUnitOfWork unitOfWork) : IClienteRepository
{
    public async Task<IReadOnlyList<Cliente>> ListAsync(CancellationToken cancellationToken = default)
    {
        var clientes = new List<Cliente>();
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT id, nome, cpf_cnpj, empresa, telefone, email, endereco, cidade
            FROM clientes
            ORDER BY nome;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            clientes.Add(Map(reader));
        }

        return clientes;
    }

    public async Task<IReadOnlyList<Cliente>> SearchAsync(string termo, CancellationToken cancellationToken = default)
    {
        var clientes = new List<Cliente>();
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT id, nome, cpf_cnpj, empresa, telefone, email, endereco, cidade
            FROM clientes
            WHERE nome LIKE @termo OR cpf_cnpj LIKE @termo OR telefone LIKE @termo
            ORDER BY nome
            LIMIT 20;
            """;
        command.Parameters.AddWithValue("@termo", $"%{termo}%");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            clientes.Add(Map(reader));
        }

        return clientes;
    }

    public async Task<long> CreateAsync(Cliente cliente, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            INSERT INTO clientes (nome, cpf_cnpj, empresa, telefone, email, endereco, cidade)
            VALUES (@nome, @cpfCnpj, @empresa, @telefone, @email, @endereco, @cidade);
            SELECT LAST_INSERT_ID();
            """;
        PreencherParametros(command, cliente);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<bool> UpdateAsync(long id, Cliente cliente, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            UPDATE clientes
            SET nome = @nome,
                cpf_cnpj = @cpfCnpj,
                empresa = @empresa,
                telefone = @telefone,
                email = @email,
                endereco = @endereco,
                cidade = @cidade
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        PreencherParametros(command, cliente);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<Cliente?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT id, nome, cpf_cnpj, empresa, telefone, email, endereco, cidade
            FROM clientes
            WHERE id = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    private static void PreencherParametros(MySqlCommand command, Cliente cliente)
    {
        command.Parameters.Add(new MySqlParameter("@nome", cliente.Nome));
        command.Parameters.Add(new MySqlParameter("@cpfCnpj", (object?)cliente.CpfCnpj ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@empresa", (object?)cliente.Empresa ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@telefone", (object?)cliente.Telefone ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@email", (object?)cliente.Email ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@endereco", (object?)cliente.Endereco ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@cidade", (object?)cliente.Cidade ?? DBNull.Value));
    }

    private static Cliente Map(MySqlDataReader reader)
    {
        return new Cliente(
            reader.GetInt64("id"),
            reader.GetString("nome"),
            reader.NullableString("cpf_cnpj"),
            reader.NullableString("empresa"),
            reader.NullableString("telefone"),
            reader.NullableString("email"),
            reader.NullableString("endereco"),
            reader.NullableString("cidade"));
    }
}
