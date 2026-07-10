using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;
using PrintGest.Application.Contracts.Auth;
using PrintGest.Application.Services;
using PrintGest.Domain.Enums;
using System.Text.RegularExpressions;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService, IUsuarioRepository usuarioRepository) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var usuario = await usuarioRepository.GetByEmailAsync(request.Email.Trim(), cancellationToken);
            if (usuario?.Status == StatusUsuario.Bloqueado)
            {
                return Unauthorized(new { mensagem = "Usuário bloqueado. Entre em contato com o administrador." });
            }
        }

        var response = await authService.LoginAsync(request, cancellationToken);
        return response is null
            ? Unauthorized(new { mensagem = "Email ou senha inválidos." })
            : Ok(response);
    }

    [Authorize]
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

        var usuario = await usuarioRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (usuario is null)
        {
            return NotFound(new { mensagem = "Usuário não encontrado." });
        }

        if (usuario.Status == StatusUsuario.Bloqueado)
        {
            return Forbid();
        }

        if (!AuthService.SenhaValida(request.SenhaAtual, usuario.SenhaHash))
        {
            return Unauthorized(new { mensagem = "Senha atual inválida." });
        }

        var sucesso = await usuarioRepository.UpdatePasswordAsync(
            usuario.Id, 
            AuthService.GerarHashLocal(request.NovaSenha), 
            false, 
            cancellationToken);

        return sucesso ? NoContent() : BadRequest(new { mensagem = "Não foi possível atualizar a senha." });
    }

    [Authorize]
    [HttpPost("autorizar-supervisor")]
    public async Task<IActionResult> AutorizarSupervisor(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Senha))
        {
            return BadRequest(new { mensagem = "Informe o e-mail e a senha do supervisor." });
        }

        var usuario = await usuarioRepository.GetByEmailAsync(request.Email.Trim(), cancellationToken);
        if (usuario is null || usuario.Status == StatusUsuario.Bloqueado)
        {
            return Unauthorized(new { mensagem = "Supervisor não encontrado ou bloqueado." });
        }

        if (usuario.Perfil is not (PerfilUsuario.Admin or PerfilUsuario.Gerente))
        {
            return Unauthorized(new { mensagem = "Acesso negado. Apenas gerentes ou administradores podem autorizar." });
        }

        if (!AuthService.SenhaValida(request.Senha, usuario.SenhaHash))
        {
            return Unauthorized(new { mensagem = "Senha incorreta." });
        }

        return Ok(new { autorizado = true, nome = usuario.Nome });
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
