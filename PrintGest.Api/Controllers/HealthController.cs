using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController(IDatabaseHealthCheck database) : ControllerBase
{
    [HttpGet]
    public IActionResult Health()
    {
        return Ok(new
        {
            status = "ok",
            service = "PrintGest.Api",
            timestamp = DateTimeOffset.UtcNow
        });
    }

    [HttpGet("database")]
    public async Task<IActionResult> Database(CancellationToken cancellationToken)
    {
        try
        {
            var connected = await database.CanConnectAsync(cancellationToken);
            return connected
                ? Ok(new { status = "ok", database = "connected" })
                : Problem("Não foi possível conectar ao banco de dados.");
        }
        catch (Exception ex)
        {
            return Problem($"Falha ao conectar ao banco de dados: {ex.Message}");
        }
    }
}
