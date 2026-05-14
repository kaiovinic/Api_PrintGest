using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;
using PrintGest.Application.Contracts.Auth;
using PrintGest.Application.Services;
using PrintGest.Infrastructure.Data;
using System.Text.RegularExpressions;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService, MySqlConnectionFactory factory) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.LoginAsync(request, cancellationToken);
        return response is null ? Unauthorized() : Ok(response);
    }

    [HttpPatch("trocar-senha")]
    public async Task<IActionResult> TrocarSenha(
        [FromBody] TrocarSenhaRequest request,
        CancellationToken cancellationToken)
    {
        if (request.NovaSenha != request.ConfirmarNovaSenha)
        {
            return BadRequest(new { mensagem = "A confirmação da nova senha não confere." });
        }

        if (!SenhaForte(request.NovaSenha))
        {
            return BadRequest(new
            {
                mensagem = "A senha precisa ter no mínimo 8 caracteres, letra maiúscula, minúscula, número e caractere especial."
            });
        }

        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var select = connection.CreateCommand();
        select.CommandText = """
            SELECT id, senha_hash, status
            FROM usuarios
            WHERE email = @email
            LIMIT 1;
            """;
        select.Parameters.AddWithValue("@email", request.Email);

        long id;
        string senhaHash;
        string status;
        await using (var reader = await select.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return NotFound(new { mensagem = "Usuário não encontrado." });
            }

            id = reader.GetInt64("id");
            senhaHash = reader.GetString("senha_hash");
            status = reader.GetString("status");
        }

        if (status == "BLOQUEADO")
        {
            return Forbid();
        }

        if (!AuthService.SenhaValida(request.SenhaAtual, senhaHash))
        {
            return Unauthorized(new { mensagem = "Senha atual inválida." });
        }

        await using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE usuarios
            SET senha_hash = @senhaHash, deve_trocar_senha = FALSE
            WHERE id = @id;
            """;
        update.Parameters.AddWithValue("@id", id);
        update.Parameters.AddWithValue("@senhaHash", AuthService.GerarHashLocal(request.NovaSenha));
        await update.ExecuteNonQueryAsync(cancellationToken);

        return NoContent();
    }

    private static bool SenhaForte(string senha)
    {
        return senha.Length >= 8
            && Regex.IsMatch(senha, "[a-z]")
            && Regex.IsMatch(senha, "[A-Z]")
            && Regex.IsMatch(senha, "[0-9]")
            && Regex.IsMatch(senha, "[^a-zA-Z0-9]");
    }
}

public sealed record TrocarSenhaRequest(
    string Email,
    string SenhaAtual,
    string NovaSenha,
    string ConfirmarNovaSenha);
