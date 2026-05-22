using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Domain.Enums;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
public sealed class UsuariosController(IUsuarioRepository usuarios) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] string? nome, [FromQuery] string? email, [FromQuery] string? perfil, [FromQuery] string? status, CancellationToken cancellationToken)
    {
        var lista = await usuarios.ListAsync(new UsuarioFiltro(nome, email, perfil, status), cancellationToken);
        return Ok(lista.Select(usuario => new
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
        if (!Enum.TryParse<PerfilUsuario>(request.Perfil, true, out var perfil))
        {
            return BadRequest(new { mensagem = "Perfil inválido." });
        }

        var usuario = new Usuario(
            0,
            request.Nome,
            request.Email,
            request.Telefone,
            "HASH_DA_SENHA_123456789",
            perfil,
            StatusUsuario.Ativo,
            true
        );

        var id = await usuarios.CreateAsync(usuario, cancellationToken);
        return CreatedAtAction(nameof(Listar), new { id }, new { id, senhaPadrao = "123456789" });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Editar(long id, [FromBody] UsuarioRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<PerfilUsuario>(request.Perfil, true, out var perfil))
        {
            return BadRequest(new { mensagem = "Perfil inválido." });
        }

        var usuario = new Usuario(
            id,
            request.Nome,
            request.Email,
            request.Telefone,
            "",
            perfil,
            StatusUsuario.Ativo,
            false
        );

        var atualizado = await usuarios.UpdateAsync(usuario, cancellationToken);
        return atualizado ? NoContent() : NotFound();
    }

    [HttpPatch("{id:long}/bloquear")]
    public Task<IActionResult> Bloquear(long id, CancellationToken cancellationToken)
    {
        return AlterarStatus(id, StatusUsuario.Bloqueado, cancellationToken);
    }

    [HttpPatch("{id:long}/desbloquear")]
    public Task<IActionResult> Desbloquear(long id, CancellationToken cancellationToken)
    {
        return AlterarStatus(id, StatusUsuario.Ativo, cancellationToken);
    }

    [HttpPatch("{id:long}/resetar-senha")]
    public async Task<IActionResult> ResetarSenha(long id, CancellationToken cancellationToken)
    {
        var atualizado = await usuarios.UpdatePasswordAsync(id, "HASH_DA_SENHA_123456789", true, cancellationToken);
        return atualizado
            ? Ok(new { senhaPadrao = "123456789" })
            : NotFound();
    }

    private async Task<IActionResult> AlterarStatus(long id, StatusUsuario status, CancellationToken cancellationToken)
    {
        var atualizado = await usuarios.UpdateStatusAsync(id, status, cancellationToken);
        return atualizado ? NoContent() : NotFound();
    }
}

public sealed record UsuarioRequest(
    [Required(ErrorMessage = "Informe o nome do usuario.")]
    [StringLength(120, ErrorMessage = "O nome deve ter no maximo 120 caracteres.")]
    string Nome,
    [Required(ErrorMessage = "Informe o email do usuario.")]
    [EmailAddress(ErrorMessage = "Informe um email valido.")]
    string Email,
    [StringLength(30, ErrorMessage = "O telefone deve ter no maximo 30 caracteres.")]
    string? Telefone,
    [Required(ErrorMessage = "Informe o perfil do usuario.")]
    string Perfil);

