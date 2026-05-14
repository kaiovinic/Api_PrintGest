using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/pedidos")]
public sealed class PedidosController(IPedidoRepository pedidos) : ControllerBase
{
    [HttpGet("recentes")]
    public async Task<IActionResult> ListarRecentes(CancellationToken cancellationToken)
    {
        return Ok(await pedidos.ListRecentAsync(cancellationToken));
    }
}
