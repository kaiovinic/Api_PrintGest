using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/financeiro")]
public sealed class FinanceiroController(IFinanceiroRepository financeiro) : ControllerBase
{
    [HttpGet("vendas")]
    public async Task<IActionResult> Vendas([FromQuery] int? ano, [FromQuery] int? mes, [FromQuery] DateOnly? inicio, [FromQuery] DateOnly? fim, [FromQuery] string? status, [FromQuery] int pagina = 1, [FromQuery] int tamanhoPagina = 10, CancellationToken cancellationToken = default)
    {
        var resultado = await financeiro.ObterVendasAsync(new FinanceiroFiltro(ano, mes, inicio, fim, status, pagina, tamanhoPagina), cancellationToken);
        return Ok(resultado);
    }

    [HttpGet("entradas")]
    public async Task<IActionResult> Entradas([FromQuery] int? ano, [FromQuery] int? mes, [FromQuery] DateOnly? inicio, [FromQuery] DateOnly? fim, [FromQuery] int pagina = 1, [FromQuery] int tamanhoPagina = 10, CancellationToken cancellationToken = default)
    {
        var resultado = await financeiro.ObterEntradasAsync(new FinanceiroFiltro(ano, mes, inicio, fim, null, pagina, tamanhoPagina), cancellationToken);
        return Ok(resultado);
    }

    [HttpGet("despesas")]
    public async Task<IActionResult> ListarDespesas([FromQuery] int? ano, [FromQuery] int? mes, [FromQuery] DateOnly? inicio, [FromQuery] DateOnly? fim, CancellationToken cancellationToken)
    {
        var resultado = await financeiro.ListarDespesasAsync(new FinanceiroFiltro(ano, mes, inicio, fim), cancellationToken);
        return Ok(resultado);
    }

    [HttpPost("despesas")]
    public async Task<IActionResult> CriarDespesa([FromBody] FinanceiroDespesaRequest request, CancellationToken cancellationToken)
    {
        if (request.Valor <= 0)
        {
            return BadRequest(new { mensagem = "Informe um valor maior que zero." });
        }

        if (request.QuantidadeParcelas < 1)
        {
            return BadRequest(new { mensagem = "Informe a quantidade de parcelas." });
        }

        var id = await financeiro.CriarDespesaAsync(request, cancellationToken);
        return CreatedAtAction(nameof(ListarDespesas), new { id }, new { id });
    }

    [HttpPatch("despesas/{id:long}/pagar")]
    public async Task<IActionResult> PagarDespesa(long id, CancellationToken cancellationToken)
    {
        return await financeiro.PagarDespesaAsync(id, cancellationToken) ? NoContent() : NotFound(new { mensagem = "Despesa nao encontrada." });
    }

    [HttpPut("despesas/{grupoDespesaId}")]
    public async Task<IActionResult> AtualizarDespesa(string grupoDespesaId, [FromBody] FinanceiroDespesaAtualizarRequest request, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return await financeiro.AtualizarDespesaAsync(grupoDespesaId, request, cancellationToken) ? NoContent() : NotFound(new { mensagem = "Despesa nao encontrada." });
    }

    [HttpGet("graficos")]
    public async Task<IActionResult> Graficos([FromQuery] int? ano, [FromQuery] int? mes, CancellationToken cancellationToken)
    {
        var resultado = await financeiro.ObterGraficosAsync(ano, mes, cancellationToken);
        return Ok(resultado);
    }
}
