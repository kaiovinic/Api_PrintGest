using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;

namespace PrintGest.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/clientes")]
public sealed class ClientesController(IClienteRepository clientes) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        return Ok(await clientes.ListAsync(cancellationToken));
    }

    [HttpGet("buscar")]
    public async Task<IActionResult> Buscar([FromQuery] string termo, CancellationToken cancellationToken)
    {
        return Ok(await clientes.SearchAsync(termo, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Criar([FromBody] ClienteRequest request, CancellationToken cancellationToken)
    {
        var cliente = new Cliente(
            0,
            request.Nome,
            request.CpfCnpj,
            request.Empresa,
            request.Telefone,
            request.Email,
            request.Endereco,
            request.Cidade);

        var id = await clientes.CreateAsync(cliente, cancellationToken);
        return CreatedAtAction(nameof(Listar), new { id }, new { id });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Editar(long id, [FromBody] ClienteRequest request, CancellationToken cancellationToken)
    {
        var cliente = new Cliente(
            id,
            request.Nome,
            request.CpfCnpj,
            request.Empresa,
            request.Telefone,
            request.Email,
            request.Endereco,
            request.Cidade);

        var atualizado = await clientes.UpdateAsync(id, cliente, cancellationToken);
        return atualizado ? NoContent() : NotFound();
    }
}

public sealed record ClienteRequest(
    string Nome,
    string? CpfCnpj,
    string? Empresa,
    string? Telefone,
    string? Email,
    string? Endereco,
    string? Cidade);
