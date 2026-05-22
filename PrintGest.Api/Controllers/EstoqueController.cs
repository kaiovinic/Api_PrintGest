using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;

namespace PrintGest.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/estoque")]
public sealed class EstoqueController(IEstoqueRepository estoque, ILogRepository logRepository) : ControllerBase
{
    [HttpGet("produtos")]
    public async Task<IActionResult> ListarProdutos([FromQuery] int pagina = 1, [FromQuery] int tamanhoPagina = 10, CancellationToken cancellationToken = default)
    {
        return Ok(await estoque.ListarProdutosAsync(pagina, tamanhoPagina, cancellationToken));
    }

    [HttpPost("produtos")]
    public async Task<IActionResult> CriarProduto([FromBody] ProdutoEstoqueRequest request, CancellationToken cancellationToken)
    {
        var id = await estoque.CriarProdutoAsync(request, cancellationToken);
        return CreatedAtAction(nameof(ListarProdutos), new { id }, new { id });
    }

    [HttpPut("produtos/{id:long}")]
    public async Task<IActionResult> EditarProduto(long id, [FromBody] ProdutoEstoqueRequest request, CancellationToken cancellationToken)
    {
        return await estoque.EditarProdutoAsync(id, request, cancellationToken) ? NoContent() : NotFound();
    }

    [HttpGet("categorias")]
    public async Task<IActionResult> ListarCategorias(CancellationToken cancellationToken)
    {
        return Ok(await estoque.ListarCategoriasAsync(cancellationToken));
    }

    [HttpPost("categorias")]
    public async Task<IActionResult> CriarCategoria([FromBody] CategoriaEstoqueRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Nome)) return BadRequest(new { mensagem = "Informe o nome da categoria." });
        await estoque.CriarCategoriaAsync(request, cancellationToken);
        return CreatedAtAction(nameof(ListarCategorias), new { nome = request.Nome }, new { nome = request.Nome.Trim() });
    }

    [HttpPost("movimentacoes")]
    public async Task<IActionResult> RegistrarMovimentacao([FromBody] MovimentacaoEstoqueRequest request, CancellationToken cancellationToken)
    {
        if (request.Tipo.Equals("ENTRADA", StringComparison.OrdinalIgnoreCase) && request.CustoUnitario is null or <= 0)
        {
            return BadRequest(new { mensagem = "Informe o custo unitario para registrar entrada de estoque." });
        }

        try
        {
            await estoque.RegistrarMovimentacaoAsync(request, cancellationToken);
            var nomeProduto = string.IsNullOrWhiteSpace(request.NomeProduto) ? $"produto #{request.ProdutoId}" : request.NomeProduto;
            await logRepository.CreateAsync(new LogSistema(0, request.UsuarioId, null, "Estoque", request.ProdutoId, request.Tipo.ToUpperInvariant(), $"{request.Tipo} de {request.Quantidade} unidade(s) — {nomeProduto}.", DateTime.UtcNow), cancellationToken);
            return Ok(new { mensagem = "Movimentacao registrada com sucesso." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { mensagem = exception.Message });
        }
    }

    [HttpGet("movimentacoes")]
    public async Task<IActionResult> ListarMovimentacoes([FromQuery] int pagina = 1, [FromQuery] int tamanhoPagina = 10, CancellationToken cancellationToken = default)
    {
        return Ok(await estoque.ListarMovimentacoesAsync(pagina, tamanhoPagina, cancellationToken));
    }

    [HttpPut("movimentacoes/{id:long}")]
    public async Task<IActionResult> EditarMovimentacao(long id, [FromBody] EditarMovimentacaoRequest request, CancellationToken cancellationToken)
    {
        if (request.Tipo.Equals("ENTRADA", StringComparison.OrdinalIgnoreCase) && request.CustoUnitario is null or <= 0)
            return BadRequest(new { mensagem = "Informe o custo unitario para registrar entrada de estoque." });

        try
        {
            await estoque.EditarMovimentacaoAsync(id, request, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException exception)
        {
            return NotFound(new { mensagem = exception.Message });
        }
    }
}
