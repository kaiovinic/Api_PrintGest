using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/clientes")]
public sealed class ClientesController(IClienteRepository clientes, MySqlConnectionFactory factory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        return Ok(await clientes.ListAsync(cancellationToken));
    }

    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar([FromQuery] string termo, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, nome, cpf_cnpj, empresa, telefone, email, endereco, cidade
            FROM clientes
            WHERE nome LIKE @termo OR cpf_cnpj LIKE @termo OR telefone LIKE @termo
            ORDER BY nome
            LIMIT 20;
            """;
        command.Parameters.AddWithValue("@termo", $"%{termo}%");

        var resultado = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            resultado.Add(new
            {
                Id = reader.GetInt64("id"),
                Nome = reader.GetString("nome"),
                CpfCnpj = reader.NullableString("cpf_cnpj"),
                Empresa = reader.NullableString("empresa"),
                Telefone = reader.NullableString("telefone"),
                Email = reader.NullableString("email"),
                Endereco = reader.NullableString("endereco"),
                Cidade = reader.NullableString("cidade")
            });
        }

        return Ok(resultado);
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] ClienteRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO clientes (nome, cpf_cnpj, empresa, telefone, email, endereco, cidade)
            VALUES (@nome, @cpfCnpj, @empresa, @telefone, @email, @endereco, @cidade);
            SELECT LAST_INSERT_ID();
            """;
        PreencherParametros(command, request);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return CreatedAtAction(nameof(Listar), new { id }, new { id });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Editar(long id, [FromBody] ClienteRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
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
        PreencherParametros(command, request);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? NotFound() : NoContent();
    }

    private static void PreencherParametros(System.Data.Common.DbCommand command, ClienteRequest request)
    {
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@nome", request.Nome));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@cpfCnpj", request.CpfCnpj));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@empresa", request.Empresa));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@telefone", request.Telefone));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@email", request.Email));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@endereco", request.Endereco));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@cidade", request.Cidade));
    }
}

public sealed record ClienteRequest(
    string Nome,
    string? CpfCnpj,
    string? Empresa,
    string? Telefone,
    string? Email,
    string? Endereco,
    string? Cidade);
