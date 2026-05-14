using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/pedidos")]
public sealed class PedidosController(IPedidoRepository pedidos, MySqlConnectionFactory factory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        return Ok(await pedidos.ListRecentAsync(cancellationToken));
    }

    [HttpGet("recentes")]
    public async Task<IActionResult> ListarRecentes(CancellationToken cancellationToken)
    {
        return Ok(await pedidos.ListRecentAsync(cancellationToken));
    }

    [HttpPost("orcamentos")]
    public async Task<IActionResult> CriarOrcamento([FromBody] PedidoRequest request, CancellationToken cancellationToken)
    {
        var id = await CriarPedidoInterno(request, "ORCAMENTO", "ORCADO", cancellationToken);
        return CreatedAtAction(nameof(Listar), new { id }, new { id });
    }

    [HttpPost]
    public async Task<IActionResult> CriarPedido([FromBody] PedidoRequest request, CancellationToken cancellationToken)
    {
        var id = await CriarPedidoInterno(request, "PEDIDO", "ABERTO", cancellationToken);
        return CreatedAtAction(nameof(Listar), new { id }, new { id });
    }

    [HttpPut("{id:long}/orcamento")]
    public async Task<IActionResult> EditarOrcamento(long id, [FromBody] PedidoRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE pedidos
            SET data_pedido = @dataPedido,
                data_entrega = @dataEntrega,
                vendedor = @vendedor,
                forma_pagamento = @formaPagamento,
                condicao_pagamento = @condicaoPagamento,
                frente = @frente,
                fundo = @fundo,
                tamanhos_masculinos = @tamanhosMasculinos,
                tamanhos_femininos = @tamanhosFemininos,
                observacao = @observacao,
                subtotal = @total,
                total = @total,
                saldo_devedor = @total - valor_pago
            WHERE id = @id AND tipo = 'ORCAMENTO';
            """;
        command.Parameters.AddWithValue("@id", id);
        PreencherParametrosPedido(command, request);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? NotFound() : NoContent();
    }

    [HttpPatch("{id:long}/converter-em-pedido")]
    public async Task<IActionResult> ConverterEmPedido(long id, [FromBody] ConverterPedidoRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var pedido = connection.CreateCommand();
        pedido.Transaction = transaction;
        pedido.CommandText = """
            UPDATE pedidos
            SET tipo = 'PEDIDO',
                status = 'ABERTO',
                forma_pagamento = @formaPagamento,
                condicao_pagamento = @condicaoPagamento,
                valor_pago = @valorEntrada,
                saldo_devedor = total - @valorEntrada
            WHERE id = @id AND tipo = 'ORCAMENTO';
            """;
        pedido.Parameters.AddWithValue("@id", id);
        pedido.Parameters.AddWithValue("@formaPagamento", request.FormaPagamento);
        pedido.Parameters.AddWithValue("@condicaoPagamento", request.CondicaoPagamento);
        pedido.Parameters.AddWithValue("@valorEntrada", request.ValorEntrada);
        var afetados = await pedido.ExecuteNonQueryAsync(cancellationToken);
        if (afetados == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotFound();
        }

        await using var pagamento = connection.CreateCommand();
        pagamento.Transaction = transaction;
        pagamento.CommandText = """
            INSERT INTO pagamentos (pedido_id, registrado_por_usuario_id, forma_pagamento, condicao_pagamento, valor_total, observacao)
            SELECT id, @usuarioId, @formaPagamento, @condicaoPagamento, total, 'Conversão de orçamento em pedido'
            FROM pedidos
            WHERE id = @id;
            """;
        pagamento.Parameters.AddWithValue("@id", id);
        pagamento.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
        pagamento.Parameters.AddWithValue("@formaPagamento", request.FormaPagamento);
        pagamento.Parameters.AddWithValue("@condicaoPagamento", request.CondicaoPagamento);
        await pagamento.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Ok(new { mensagem = "Orçamento convertido em pedido." });
    }

    [HttpPatch("{id:long}/cancelar")]
    public Task<IActionResult> Cancelar(long id, [FromBody] AlterarStatusPedidoRequest request, CancellationToken cancellationToken)
    {
        return AlterarStatus(id, "CANCELADO", request.UsuarioId, request.Observacao, cancellationToken);
    }

    [HttpPatch("{id:long}/finalizar")]
    public Task<IActionResult> Finalizar(long id, [FromBody] AlterarStatusPedidoRequest request, CancellationToken cancellationToken)
    {
        return AlterarStatus(id, "FINALIZADO", request.UsuarioId, request.Observacao, cancellationToken);
    }

    private async Task<long> CriarPedidoInterno(PedidoRequest request, string tipo, string status, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pedidos
                (numero, cliente_id, criado_por_usuario_id, tipo, status, data_pedido, data_entrega,
                 vendedor, forma_pagamento, condicao_pagamento, frente, fundo, tamanhos_masculinos,
                 tamanhos_femininos, observacao, subtotal, total, valor_pago, saldo_devedor)
            VALUES
                (@numero, @clienteId, @usuarioId, @tipo, @status, @dataPedido, @dataEntrega,
                 @vendedor, @formaPagamento, @condicaoPagamento, @frente, @fundo, @tamanhosMasculinos,
                 @tamanhosFemininos, @observacao, @total, @total, @valorPago, @saldoDevedor);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@tipo", tipo);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@valorPago", request.ValorPago);
        command.Parameters.AddWithValue("@saldoDevedor", request.Total - request.ValorPago);
        PreencherParametrosPedido(command, request);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task<IActionResult> AlterarStatus(long id, string status, long usuarioId, string? observacao, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = status == "FINALIZADO"
            ? """
              UPDATE pedidos
              SET status = 'FINALIZADO', finalizado_por_usuario_id = @usuarioId, finalizado_em = CURRENT_TIMESTAMP
              WHERE id = @id;
              """
            : """
              UPDATE pedidos
              SET status = 'CANCELADO', cancelado_por_usuario_id = @usuarioId, cancelado_em = CURRENT_TIMESTAMP, motivo_cancelamento = @observacao
              WHERE id = @id;
              """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@usuarioId", usuarioId);
        command.Parameters.AddWithValue("@observacao", observacao);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? NotFound() : NoContent();
    }

    private static void PreencherParametrosPedido(System.Data.Common.DbCommand command, PedidoRequest request)
    {
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@numero", request.Numero));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@clienteId", request.ClienteId));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@usuarioId", request.UsuarioId));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@dataPedido", request.DataPedido.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@dataEntrega", request.DataEntrega?.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@vendedor", request.Vendedor));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@formaPagamento", request.FormaPagamento));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@condicaoPagamento", request.CondicaoPagamento));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@frente", request.Frente));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@fundo", request.Fundo));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@tamanhosMasculinos", request.TamanhosMasculinos));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@tamanhosFemininos", request.TamanhosFemininos));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@observacao", request.Observacao));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@total", request.Total));
    }
}

public sealed record PedidoRequest(
    string Numero,
    long ClienteId,
    long UsuarioId,
    DateOnly DataPedido,
    DateOnly? DataEntrega,
    string? Vendedor,
    string? FormaPagamento,
    string? CondicaoPagamento,
    string? Frente,
    string? Fundo,
    string? TamanhosMasculinos,
    string? TamanhosFemininos,
    string? Observacao,
    decimal Total,
    decimal ValorPago);

public sealed record ConverterPedidoRequest(
    long UsuarioId,
    string FormaPagamento,
    string CondicaoPagamento,
    decimal ValorEntrada);

public sealed record AlterarStatusPedidoRequest(long UsuarioId, string? Observacao);
