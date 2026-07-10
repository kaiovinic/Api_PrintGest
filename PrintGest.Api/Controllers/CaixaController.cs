using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;
using PrintGest.Application.Contracts.Auth;
using PrintGest.Application.Services;
using PrintGest.Domain.Entities;
using PrintGest.Domain.Enums;

namespace PrintGest.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/caixa")]
public sealed class CaixaController(ICaixaRepository caixa, ILogRepository logRepository, IUsuarioRepository usuarioRepository) : ControllerBase
{
    [HttpGet("resumo")]
    public async Task<IActionResult> Resumo([FromQuery] DateOnly? inicio, [FromQuery] DateOnly? fim, CancellationToken cancellationToken)
    {
        return Ok(await caixa.ObterResumoAsync(inicio, fim, cancellationToken));
    }

    [HttpGet("movimentacoes")]
    public async Task<IActionResult> ListarMovimentacoes([FromQuery] DateOnly? inicio, [FromQuery] DateOnly? fim, [FromQuery] int pagina = 1, [FromQuery] int tamanhoPagina = 10, CancellationToken cancellationToken = default)
    {
        return Ok(await caixa.ListarMovimentacoesAsync(inicio, fim, pagina, tamanhoPagina, cancellationToken));
    }

    [HttpPost("movimentacoes")]
    public async Task<IActionResult> CriarMovimentacao([FromBody] CaixaMovimentacaoRequest request, CancellationToken cancellationToken)
    {
        if (request.Valor <= 0) return BadRequest(new { mensagem = "Informe um valor maior que zero." });
        var tipo = request.Tipo.ToUpperInvariant();
        if (tipo is not ("ENTRADA" or "SAIDA")) return BadRequest(new { mensagem = "Tipo de movimentacao invalido." });

        try
        {
            var id = await caixa.CriarMovimentacaoAsync(request, cancellationToken);
            await logRepository.CreateAsync(new LogSistema(0, request.UsuarioId, null, "Caixa", id, tipo, $"{request.Tipo} de {request.Valor:C} — {request.Descricao}.", DateTime.UtcNow), cancellationToken);
            return CreatedAtAction(nameof(ListarMovimentacoes), new { id }, new { id });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { mensagem = exception.Message });
        }
    }

    [HttpPost("movimentacoes/{id}/cancelar")]
    public async Task<IActionResult> CancelarMovimentacao(
        string id,
        [FromBody] CancelarMovimentacaoRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SupervisorEmail) || string.IsNullOrWhiteSpace(request.SupervisorSenha))
        {
            return BadRequest(new { mensagem = "Informe o e-mail e a senha do supervisor." });
        }

        var supervisor = await usuarioRepository.GetByEmailAsync(request.SupervisorEmail.Trim(), cancellationToken);
        if (supervisor is null || supervisor.Status == StatusUsuario.Bloqueado)
        {
            return Unauthorized(new { mensagem = "Supervisor não encontrado ou bloqueado." });
        }

        if (supervisor.Perfil is not (PerfilUsuario.Admin or PerfilUsuario.Gerente))
        {
            return Unauthorized(new { mensagem = "Somente administradores ou gerentes podem autorizar cancelamentos." });
        }

        if (!AuthService.SenhaValida(request.SupervisorSenha, supervisor.SenhaHash))
        {
            return Unauthorized(new { mensagem = "Senha do supervisor incorreta." });
        }

        try
        {
            var sucesso = await caixa.DeletarMovimentacaoAsync(id, request.UsuarioId, cancellationToken);
            if (!sucesso) return NotFound(new { mensagem = "Movimentação não encontrada." });
            return Ok(new { mensagem = "Movimentação cancelada com sucesso." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { mensagem = exception.Message });
        }
    }
}
