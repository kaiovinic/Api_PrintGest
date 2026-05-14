using Microsoft.AspNetCore.Mvc;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/logs")]
public sealed class LogsController(MySqlConnectionFactory factory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] string? entidade,
        [FromQuery] long? entidadeId,
        [FromQuery] DateOnly? dataInicio,
        [FromQuery] DateOnly? dataFinal,
        CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT l.id,
                   l.usuario_id,
                   u.nome AS usuario,
                   l.entidade,
                   l.entidade_id,
                   l.acao,
                   l.descricao,
                   l.criado_em
            FROM logs_sistema l
            LEFT JOIN usuarios u ON u.id = l.usuario_id
            WHERE (@entidade IS NULL OR l.entidade = @entidade)
              AND (@entidadeId IS NULL OR l.entidade_id = @entidadeId)
              AND (@dataInicio IS NULL OR DATE(l.criado_em) >= @dataInicio)
              AND (@dataFinal IS NULL OR DATE(l.criado_em) <= @dataFinal)
            ORDER BY l.criado_em DESC
            LIMIT 200;
            """;
        command.Parameters.AddWithValue("@entidade", (object?)entidade ?? DBNull.Value);
        command.Parameters.AddWithValue("@entidadeId", (object?)entidadeId ?? DBNull.Value);
        command.Parameters.AddWithValue("@dataInicio", dataInicio?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@dataFinal", dataFinal?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);

        var logs = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            logs.Add(new
            {
                Id = reader.GetInt64("id"),
                UsuarioId = reader.GetInt64("usuario_id"),
                Usuario = reader.NullableString("usuario"),
                Entidade = reader.GetString("entidade"),
                EntidadeId = reader.GetInt64("entidade_id"),
                Acao = reader.GetString("acao"),
                Descricao = reader.NullableString("descricao"),
                CriadoEm = reader.GetDateTime("criado_em")
            });
        }

        return Ok(logs);
    }

    [HttpPost]
    public async Task<IActionResult> Registrar([FromBody] LogRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO logs_sistema (usuario_id, entidade, entidade_id, acao, descricao)
            VALUES (@usuarioId, @entidade, @entidadeId, @acao, @descricao);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
        command.Parameters.AddWithValue("@entidade", request.Entidade);
        command.Parameters.AddWithValue("@entidadeId", request.EntidadeId);
        command.Parameters.AddWithValue("@acao", request.Acao);
        command.Parameters.AddWithValue("@descricao", (object?)request.Descricao ?? DBNull.Value);

        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return CreatedAtAction(nameof(Listar), new { id }, new { id });
    }
}

public sealed record LogRequest(
    long UsuarioId,
    string Entidade,
    long EntidadeId,
    string Acao,
    string? Descricao);
