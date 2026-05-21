using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/caixa")]
public sealed class CaixaController(ICaixaRepository caixa) : ControllerBase
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
            return CreatedAtAction(nameof(ListarMovimentacoes), new { id }, new { id });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { mensagem = exception.Message });
        }
    }
}
