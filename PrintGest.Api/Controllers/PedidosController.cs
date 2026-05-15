using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
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

    [HttpGet("{id:long}")]
    public async Task<IActionResult> ObterPorId(long id, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id,
                   p.numero,
                   p.cliente_id,
                   c.nome AS cliente,
                   c.empresa,
                   c.cpf_cnpj,
                   c.telefone,
                   c.endereco,
                   c.cidade,
                   p.criado_por_usuario_id,
                   p.tipo,
                   p.status,
                   p.data_pedido,
                   p.data_entrega,
                   p.vendedor,
                   p.forma_pagamento,
                   p.condicao_pagamento,
                   p.frente,
                   p.fundo,
                   p.tamanhos_masculinos,
                   p.tamanhos_femininos,
                   p.observacao,
                   p.total,
                   p.valor_pago,
                   p.saldo_devedor
            FROM pedidos p
            INNER JOIN clientes c ON c.id = p.cliente_id
            WHERE p.id = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return NotFound();
        }

        var dataEntregaOrdinal = reader.GetOrdinal("data_entrega");
        var detalhe = new
        {
            Id = reader.GetInt64("id"),
            Numero = reader.GetString("numero"),
            ClienteId = reader.GetInt64("cliente_id"),
            Cliente = reader.GetString("cliente"),
            Empresa = reader.NullableString("empresa"),
            CpfCnpj = reader.NullableString("cpf_cnpj"),
            Telefone = reader.NullableString("telefone"),
            Endereco = reader.NullableString("endereco"),
            Cidade = reader.NullableString("cidade"),
            UsuarioId = reader.GetInt64("criado_por_usuario_id"),
            Tipo = reader.GetString("tipo"),
            Status = reader.GetString("status"),
            DataPedido = DateOnly.FromDateTime(reader.GetDateTime("data_pedido")),
            DataEntrega = reader.IsDBNull(dataEntregaOrdinal) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(dataEntregaOrdinal)),
            Vendedor = reader.NullableString("vendedor"),
            FormaPagamento = reader.NullableString("forma_pagamento"),
            CondicaoPagamento = reader.NullableString("condicao_pagamento"),
            Frente = reader.NullableString("frente"),
            Fundo = reader.NullableString("fundo"),
            Observacao = reader.NullableString("observacao"),
            OutrosItens = reader.NullableString("tamanhos_femininos"),
            Total = reader.GetDecimal("total"),
            ValorPago = reader.GetDecimal("valor_pago"),
            SaldoDevedor = reader.GetDecimal("saldo_devedor"),
            Itens = await ListarItensPedido(id, cancellationToken)
        };

        return Ok(detalhe);
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
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotFound();
        }

        await AtualizarCliente(connection, transaction, request, cancellationToken);
        await SubstituirItensPedido(connection, transaction, id, request.Itens, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> EditarPedido(long id, [FromBody] PedidoRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE pedidos
            SET tipo = 'PEDIDO',
                status = CASE WHEN status = 'ORCADO' THEN 'ABERTO' ELSE status END,
                data_pedido = @dataPedido,
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
                valor_pago = @valorPago,
                saldo_devedor = @total - @valorPago
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@valorPago", request.ValorPago);
        PreencherParametrosPedido(command, request);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotFound();
        }

        await AtualizarCliente(connection, transaction, request, cancellationToken);
        await SubstituirItensPedido(connection, transaction, id, request.Itens, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
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
        pedido.Parameters.AddWithValue("@formaPagamento", NormalizarFormaPagamento(request.FormaPagamento));
        pedido.Parameters.AddWithValue("@condicaoPagamento", NormalizarCondicaoPagamento(request.CondicaoPagamento));
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
        pagamento.Parameters.AddWithValue("@formaPagamento", NormalizarFormaPagamento(request.FormaPagamento));
        pagamento.Parameters.AddWithValue("@condicaoPagamento", NormalizarCondicaoPagamento(request.CondicaoPagamento));
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
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var clienteId = await SalvarCliente(connection, transaction, request, cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
        PreencherParametrosPedido(command, request, clienteId);
        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));

        await SubstituirItensPedido(connection, transaction, id, request.Itens, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return id;
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

    private static void PreencherParametrosPedido(System.Data.Common.DbCommand command, PedidoRequest request, long? clienteId = null)
    {
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@numero", request.Numero));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@clienteId", clienteId ?? request.ClienteId));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@usuarioId", request.UsuarioId));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@dataPedido", request.DataPedido.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@dataEntrega", request.DataEntrega?.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@vendedor", request.Vendedor));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@formaPagamento", NormalizarFormaPagamento(request.FormaPagamento)));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@condicaoPagamento", NormalizarCondicaoPagamento(request.CondicaoPagamento)));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@frente", request.Frente));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@fundo", request.Fundo));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@tamanhosMasculinos", DBNull.Value));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@tamanhosFemininos", request.OutrosItens));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@observacao", request.Observacao));
        command.Parameters.Add(new MySqlConnector.MySqlParameter("@total", request.Total));
    }

    private async Task<long> SalvarCliente(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PedidoRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ClienteId > 0)
        {
            await AtualizarCliente(connection, transaction, request, cancellationToken);
            return request.ClienteId;
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO clientes (nome, empresa, cpf_cnpj, telefone, endereco, cidade)
            VALUES (@nome, @empresa, @cpfCnpj, @telefone, @endereco, @cidade);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@nome", request.ClienteNome);
        command.Parameters.AddWithValue("@empresa", ToDb(request.Empresa));
        command.Parameters.AddWithValue("@cpfCnpj", ToDb(request.CpfCnpj));
        command.Parameters.AddWithValue("@telefone", ToDb(request.Telefone));
        command.Parameters.AddWithValue("@endereco", ToDb(request.Endereco));
        command.Parameters.AddWithValue("@cidade", ToDb(request.Cidade));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task AtualizarCliente(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PedidoRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE clientes
            SET nome = @nome,
                empresa = @empresa,
                cpf_cnpj = @cpfCnpj,
                telefone = @telefone,
                endereco = @endereco,
                cidade = @cidade
            WHERE id = @clienteId;
            """;
        command.Parameters.AddWithValue("@clienteId", request.ClienteId);
        command.Parameters.AddWithValue("@nome", request.ClienteNome);
        command.Parameters.AddWithValue("@empresa", ToDb(request.Empresa));
        command.Parameters.AddWithValue("@cpfCnpj", ToDb(request.CpfCnpj));
        command.Parameters.AddWithValue("@telefone", ToDb(request.Telefone));
        command.Parameters.AddWithValue("@endereco", ToDb(request.Endereco));
        command.Parameters.AddWithValue("@cidade", ToDb(request.Cidade));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SubstituirItensPedido(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long pedidoId,
        IReadOnlyList<ItemPedidoRequest> itens,
        CancellationToken cancellationToken)
    {
        await using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM itens_pedido WHERE pedido_id = @pedidoId;";
        delete.Parameters.AddWithValue("@pedidoId", pedidoId);
        await delete.ExecuteNonQueryAsync(cancellationToken);

        foreach (var item in itens.Where(item => !string.IsNullOrWhiteSpace(item.Descricao)))
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO itens_pedido (pedido_id, descricao, tamanho, quantidade, valor_unitario, valor_total)
                VALUES (@pedidoId, @descricao, @tamanho, @quantidade, @valorUnitario, @valorTotal);
                """;
            insert.Parameters.AddWithValue("@pedidoId", pedidoId);
            insert.Parameters.AddWithValue("@descricao", item.Descricao);
            insert.Parameters.AddWithValue("@tamanho", ToDb(item.Tamanho));
            insert.Parameters.AddWithValue("@quantidade", item.Quantidade);
            insert.Parameters.AddWithValue("@valorUnitario", item.ValorUnitario);
            insert.Parameters.AddWithValue("@valorTotal", item.ValorTotal);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<object>> ListarItensPedido(long pedidoId, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, descricao, tamanho, quantidade, valor_unitario, valor_total
            FROM itens_pedido
            WHERE pedido_id = @pedidoId
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("@pedidoId", pedidoId);

        var itens = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            itens.Add(new
            {
                Id = reader.GetInt64("id"),
                Descricao = reader.GetString("descricao"),
                Tamanho = reader.NullableString("tamanho"),
                Quantidade = reader.GetInt32("quantidade"),
                ValorUnitario = reader.GetDecimal("valor_unitario"),
                ValorTotal = reader.GetDecimal("valor_total")
            });
        }

        return itens;
    }

    private static object ToDb(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
    }

    private static string? NormalizarFormaPagamento(string? formaPagamento)
    {
        return formaPagamento?.Trim().ToUpperInvariant() switch
        {
            null or "" => null,
            "PIX" => "PIX",
            "DINHEIRO" => "DINHEIRO",
            "CRÉDITO" or "CREDITO" or "CARTÃO DE CRÉDITO" or "CARTAO DE CREDITO" or "CARTAO_CREDITO" => "CARTAO_CREDITO",
            "DÉBITO" or "DEBITO" or "CARTÃO DE DÉBITO" or "CARTAO DE DEBITO" or "CARTAO_DEBITO" => "CARTAO_DEBITO",
            _ => formaPagamento
        };
    }

    private static string? NormalizarCondicaoPagamento(string? condicaoPagamento)
    {
        return condicaoPagamento?.Trim().ToUpperInvariant() switch
        {
            null or "" => null,
            "PAGO" or "À VISTA" or "A VISTA" or "A_VISTA" => "A_VISTA",
            "PAGAMENTO NO PEDIDO" or "PAGAMENTO_NO_PEDIDO" => "PAGAMENTO_NO_PEDIDO",
            "PARCELADO" or "ADIANTAMENTO" => "ADIANTAMENTO",
            "PAGAR NA ENTREGA" => "ADIANTAMENTO",
            _ => condicaoPagamento
        };
    }
}

public sealed record PedidoRequest(
    string Numero,
    long ClienteId,
    string ClienteNome,
    string? Empresa,
    string? CpfCnpj,
    string? Telefone,
    string? Endereco,
    string? Cidade,
    long UsuarioId,
    DateOnly DataPedido,
    DateOnly? DataEntrega,
    string? Vendedor,
    string? FormaPagamento,
    string? CondicaoPagamento,
    string? Frente,
    string? Fundo,
    string? Observacao,
    string? OutrosItens,
    decimal Total,
    decimal ValorPago,
    IReadOnlyList<ItemPedidoRequest> Itens);

public sealed record ItemPedidoRequest(
    string Descricao,
    string? Tamanho,
    int Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal);

public sealed record ConverterPedidoRequest(
    long UsuarioId,
    string FormaPagamento,
    string CondicaoPagamento,
    decimal ValorEntrada);

public sealed record AlterarStatusPedidoRequest(long UsuarioId, string? Observacao);
