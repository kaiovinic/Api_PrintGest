using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Services;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
public sealed class UsuariosController(UsuarioService service, MySqlConnectionFactory factory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        var usuarios = await service.ListAsync(cancellationToken);
        return Ok(usuarios.Select(usuario => new
        {
            usuario.Id,
            usuario.Nome,
            usuario.Email,
            Perfil = usuario.Perfil.ToString().ToUpperInvariant(),
            Status = usuario.Status.ToString().ToUpperInvariant(),
            usuario.DeveTrocarSenha
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] UsuarioRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO usuarios (nome, email, telefone, senha_hash, perfil, status, deve_trocar_senha)
            VALUES (@nome, @email, @telefone, 'HASH_DA_SENHA_123456789', @perfil, 'ATIVO', TRUE);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@nome", request.Nome);
        command.Parameters.AddWithValue("@email", request.Email);
        command.Parameters.AddWithValue("@telefone", request.Telefone);
        command.Parameters.AddWithValue("@perfil", request.Perfil);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return CreatedAtAction(nameof(Listar), new { id }, new { id, senhaPadrao = "123456789" });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Editar(long id, [FromBody] UsuarioRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE usuarios
            SET nome = @nome, email = @email, telefone = @telefone, perfil = @perfil
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@nome", request.Nome);
        command.Parameters.AddWithValue("@email", request.Email);
        command.Parameters.AddWithValue("@telefone", request.Telefone);
        command.Parameters.AddWithValue("@perfil", request.Perfil);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? NotFound() : NoContent();
    }

    [HttpPatch("{id:long}/bloquear")]
    public Task<IActionResult> Bloquear(long id, CancellationToken cancellationToken)
    {
        return AlterarStatus(id, "BLOQUEADO", cancellationToken);
    }

    [HttpPatch("{id:long}/desbloquear")]
    public Task<IActionResult> Desbloquear(long id, CancellationToken cancellationToken)
    {
        return AlterarStatus(id, "ATIVO", cancellationToken);
    }

    [HttpPatch("{id:long}/resetar-senha")]
    public async Task<IActionResult> ResetarSenha(long id, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE usuarios
            SET senha_hash = 'HASH_DA_SENHA_123456789', deve_trocar_senha = TRUE
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0
            ? NotFound()
            : Ok(new { senhaPadrao = "123456789" });
    }

    private async Task<IActionResult> AlterarStatus(long id, string status, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE usuarios SET status = @status WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@status", status);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? NotFound() : NoContent();
    }
}

public sealed record UsuarioRequest(
    string Nome,
    string Email,
    string? Telefone,
    string Perfil);
