using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/logs")]
public sealed class LogsController(ILogRepository logRepository) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? entidade,
        [FromQuery] long? entidadeId,
        [FromQuery] DateOnly? dataInicio,
        [FromQuery] DateOnly? dataFinal,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanhoPagina = 20,
        CancellationToken cancellationToken = default)
    {
        var logs = await logRepository.ListAsync(entidade, entidadeId, dataInicio, dataFinal, pagina, tamanhoPagina, cancellationToken);
        return Ok(logs);
    }

    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] LogRequest request, CancellationToken cancellationToken)
    {
        var log = new LogSistema(
            0,
            request.UsuarioId,
            null,
            request.Entidade,
            request.EntidadeId,
            request.Acao,
            request.Descricao,
            DateTime.UtcNow);

        var id = await logRepository.CreateAsync(log, cancellationToken);
        return CreatedAtAction(nameof(Listar), new { id }, new { id });
    }
}

public sealed record LogRequest(
    long UsuarioId,
    string Entidade,
    long EntidadeId,
    string Acao,
    string? Descricao);
