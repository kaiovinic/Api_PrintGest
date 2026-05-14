using Microsoft.AspNetCore.Mvc;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/financeiro")]
public sealed class FinanceiroController(MySqlConnectionFactory factory) : ControllerBase
{
    [HttpGet("resumo")]
    public async Task<IActionResult> Resumo(CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE((SELECT SUM(valor_pago) FROM pedidos WHERE status <> 'CANCELADO'), 0) AS receita,
                COALESCE((SELECT SUM(valor) FROM despesas WHERE status <> 'CANCELADO'), 0) AS despesas,
                COALESCE((SELECT COUNT(*) FROM pedidos WHERE status = 'FINALIZADO'), 0) AS pedidos_finalizados,
                COALESCE((SELECT AVG(total) FROM pedidos WHERE tipo = 'PEDIDO' AND status <> 'CANCELADO'), 0) AS ticket_medio;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var receita = reader.GetDecimal("receita");
        var despesas = reader.GetDecimal("despesas");
        return Ok(new
        {
            Receita = receita,
            Despesas = despesas,
            Saldo = receita - despesas,
            PedidosFinalizados = reader.GetInt64("pedidos_finalizados"),
            TicketMedio = reader.GetDecimal("ticket_medio")
        });
    }

    [HttpGet("despesas")]
    public async Task<IActionResult> ListarDespesas(CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, categoria, descricao, valor, vencimento, status, data_pagamento, observacao
            FROM despesas
            ORDER BY vencimento DESC;
            """;

        var despesas = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var pagamentoOrdinal = reader.GetOrdinal("data_pagamento");
            despesas.Add(new
            {
                Id = reader.GetInt64("id"),
                Categoria = reader.GetString("categoria"),
                Descricao = reader.GetString("descricao"),
                Valor = reader.GetDecimal("valor"),
                Vencimento = DateOnly.FromDateTime(reader.GetDateTime("vencimento")),
                Status = reader.GetString("status"),
                DataPagamento = reader.IsDBNull(pagamentoOrdinal) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(pagamentoOrdinal)),
                Observacao = reader.NullableString("observacao")
            });
        }

        return Ok(despesas);
    }

    [HttpPost("despesas")]
    public async Task<IActionResult> CriarDespesa([FromBody] DespesaRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO despesas (cadastrado_por_usuario_id, categoria, descricao, valor, vencimento, status, observacao)
            VALUES (@usuarioId, @categoria, @descricao, @valor, @vencimento, 'ABERTO', @observacao);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
        command.Parameters.AddWithValue("@categoria", request.Categoria);
        command.Parameters.AddWithValue("@descricao", request.Descricao);
        command.Parameters.AddWithValue("@valor", request.Valor);
        command.Parameters.AddWithValue("@vencimento", request.Vencimento.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@observacao", request.Observacao);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return CreatedAtAction(nameof(ListarDespesas), new { id }, new { id });
    }

    [HttpPatch("despesas/{id:long}/pagar")]
    public async Task<IActionResult> PagarDespesa(long id, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE despesas SET status = 'PAGO', data_pagamento = CURRENT_DATE() WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? NotFound() : NoContent();
    }
}

public sealed record DespesaRequest(
    long UsuarioId,
    string Categoria,
    string Descricao,
    decimal Valor,
    DateOnly Vencimento,
    string? Observacao);
