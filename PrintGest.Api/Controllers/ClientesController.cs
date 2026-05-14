using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/clientes")]
public sealed class ClientesController(IClienteRepository clientes) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        return Ok(await clientes.ListAsync(cancellationToken));
    }
}
