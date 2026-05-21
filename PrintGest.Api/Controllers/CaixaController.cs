using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/caixa")]
public sealed class CaixaController(MySqlConnectionFactory factory) : ControllerBase
{
    [HttpGet("resumo")]
    public async Task<IActionResult> Resumo([FromQuery] DateOnly? inicio, [FromQuery] DateOnly? fim, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await GarantirEstruturaCaixa(connection, cancellationToken);

        var dataInicio = inicio?.ToDateTime(TimeOnly.MinValue) ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var dataFim = fim?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.Today.Date.AddDays(1).AddTicks(-1);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN tipo = 'ENTRADA' THEN valor ELSE 0 END), 0) AS entradas,
                COALESCE(SUM(CASE WHEN tipo = 'SAIDA' THEN valor ELSE 0 END), 0) AS saidas,
                COALESCE(SUM(CASE WHEN tipo = 'ENTRADA' AND forma_pagamento = 'DINHEIRO' THEN valor ELSE 0 END), 0) AS dinheiro,
                COALESCE(SUM(CASE WHEN tipo = 'ENTRADA' AND forma_pagamento = 'PIX' THEN valor ELSE 0 END), 0) AS pix,
                COALESCE(SUM(CASE WHEN tipo = 'ENTRADA' AND forma_pagamento = 'CARTAO_CREDITO' THEN valor ELSE 0 END), 0) AS cartao_credito,
                COALESCE(SUM(CASE WHEN tipo = 'ENTRADA' AND forma_pagamento = 'CARTAO_DEBITO' THEN valor ELSE 0 END), 0) AS cartao_debito
            FROM (
                SELECT 'ENTRADA' AS tipo, forma_pagamento, valor_total AS valor, registrado_em AS data_movimento
                FROM pagamentos
                UNION ALL
                SELECT tipo, forma_pagamento, valor, movimentado_em AS data_movimento
                FROM caixa_movimentacoes
            ) caixa
            WHERE data_movimento BETWEEN @inicio AND @fim;
            """;
        command.Parameters.AddWithValue("@inicio", dataInicio);
        command.Parameters.AddWithValue("@fim", dataFim);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var entradas = reader.GetDecimal("entradas");
        var saidas = reader.GetDecimal("saidas");
        return Ok(new
        {
            Entradas = entradas,
            Saidas = saidas,
            Saldo = entradas - saidas,
            Dinheiro = reader.GetDecimal("dinheiro"),
            Pix = reader.GetDecimal("pix"),
            CartaoCredito = reader.GetDecimal("cartao_credito"),
            CartaoDebito = reader.GetDecimal("cartao_debito")
        });
    }

    [HttpGet("movimentacoes")]
    public async Task<IActionResult> ListarMovimentacoes([FromQuery] DateOnly? inicio, [FromQuery] DateOnly? fim, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await GarantirEstruturaCaixa(connection, cancellationToken);

        var dataInicio = inicio?.ToDateTime(TimeOnly.MinValue) ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var dataFim = fim?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.Today.Date.AddDays(1).AddTicks(-1);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT *
            FROM (
                SELECT
                    CONCAT('PAG-', p.id) AS id,
                    'ENTRADA' AS tipo,
                    p.forma_pagamento,
                    'Pedido' AS categoria,
                    CONCAT('Pagamento do pedido ', ped.numero, ' - ', c.nome) AS descricao,
                    p.valor_total AS valor,
                    p.registrado_em AS movimentado_em,
                    u.nome AS usuario,
                    p.observacao,
                    'PEDIDO' AS origem
                FROM pagamentos p
                INNER JOIN pedidos ped ON ped.id = p.pedido_id
                INNER JOIN clientes c ON c.id = ped.cliente_id
                INNER JOIN usuarios u ON u.id = p.registrado_por_usuario_id
                UNION ALL
                SELECT
                    CONCAT('CX-', m.id) AS id,
                    m.tipo,
                    m.forma_pagamento,
                    m.categoria,
                    m.descricao,
                    m.valor,
                    m.movimentado_em,
                    u.nome AS usuario,
                    m.observacao,
                    'MANUAL' AS origem
                FROM caixa_movimentacoes m
                INNER JOIN usuarios u ON u.id = m.usuario_id
            ) caixa
            WHERE movimentado_em BETWEEN @inicio AND @fim
            ORDER BY movimentado_em DESC
            LIMIT 100;
            """;
        command.Parameters.AddWithValue("@inicio", dataInicio);
        command.Parameters.AddWithValue("@fim", dataFim);

        var movimentacoes = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            movimentacoes.Add(new
            {
                Id = reader.GetString("id"),
                Tipo = reader.GetString("tipo"),
                FormaPagamento = reader.GetString("forma_pagamento"),
                Categoria = reader.GetString("categoria"),
                Descricao = reader.GetString("descricao"),
                Valor = reader.GetDecimal("valor"),
                MovimentadoEm = reader.GetDateTime("movimentado_em"),
                Usuario = reader.GetString("usuario"),
                Observacao = reader.NullableString("observacao"),
                Origem = reader.GetString("origem")
            });
        }

        return Ok(movimentacoes);
    }

    [HttpPost("movimentacoes")]
    public async Task<IActionResult> CriarMovimentacao([FromBody] CaixaMovimentacaoRequest request, CancellationToken cancellationToken)
    {
        if (request.Valor <= 0)
        {
            return BadRequest(new { mensagem = "Informe um valor maior que zero." });
        }

        var tipo = request.Tipo.ToUpperInvariant();
        if (tipo is not ("ENTRADA" or "SAIDA"))
        {
            return BadRequest(new { mensagem = "Tipo de movimentação inválido." });
        }

        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await GarantirEstruturaCaixa(connection, cancellationToken);

        if (tipo == "ENTRADA" && request.PedidoId is not null)
        {
            return await RegistrarPagamentoPedido(connection, request, cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO caixa_movimentacoes
                (tipo, forma_pagamento, categoria, descricao, valor, usuario_id, observacao)
            VALUES
                (@tipo, @formaPagamento, @categoria, @descricao, @valor, @usuarioId, @observacao);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@tipo", tipo);
        command.Parameters.AddWithValue("@formaPagamento", NormalizarFormaPagamento(request.FormaPagamento));
        command.Parameters.AddWithValue("@categoria", request.Categoria.Trim());
        command.Parameters.AddWithValue("@descricao", request.Descricao.Trim());
        command.Parameters.AddWithValue("@valor", request.Valor);
        command.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
        command.Parameters.AddWithValue("@observacao", request.Observacao);

        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return CreatedAtAction(nameof(ListarMovimentacoes), new { id }, new { id });
    }

    private static async Task<IActionResult> RegistrarPagamentoPedido(MySqlConnection connection, CaixaMovimentacaoRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var consulta = connection.CreateCommand();
        consulta.Transaction = transaction;
        consulta.CommandText = """
            SELECT tipo, status, valor_pago, saldo_devedor
            FROM pedidos
            WHERE id = @pedidoId
            LIMIT 1
            FOR UPDATE;
            """;
        consulta.Parameters.AddWithValue("@pedidoId", request.PedidoId);

        await using var reader = await consulta.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new NotFoundObjectResult(new { mensagem = "Pedido não encontrado." });
        }

        var tipoPedido = reader.GetString("tipo");
        var statusPedido = reader.GetString("status");
        var valorPago = reader.GetDecimal("valor_pago");
        var saldoDevedor = reader.GetDecimal("saldo_devedor");
        await reader.DisposeAsync();

        if (tipoPedido != "PEDIDO")
        {
            await transaction.RollbackAsync(cancellationToken);
            return new BadRequestObjectResult(new { mensagem = "Somente pedidos podem receber pagamento no caixa." });
        }

        if (statusPedido is "CANCELADO" or "FINALIZADO")
        {
            await transaction.RollbackAsync(cancellationToken);
            return new BadRequestObjectResult(new { mensagem = "Não é possível registrar pagamento para pedido cancelado ou finalizado." });
        }

        if (request.Valor > saldoDevedor)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new BadRequestObjectResult(new { mensagem = "O valor informado é maior que o saldo devedor do pedido." });
        }

        await using var pagamento = connection.CreateCommand();
        pagamento.Transaction = transaction;
        pagamento.CommandText = """
            INSERT INTO pagamentos (pedido_id, registrado_por_usuario_id, forma_pagamento, condicao_pagamento, valor_total, observacao)
            VALUES (@pedidoId, @usuarioId, @formaPagamento, 'PAGAMENTO_NO_PEDIDO', @valorTotal, @observacao);
            SELECT LAST_INSERT_ID();
            """;
        pagamento.Parameters.AddWithValue("@pedidoId", request.PedidoId);
        pagamento.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
        pagamento.Parameters.AddWithValue("@formaPagamento", NormalizarFormaPagamento(request.FormaPagamento));
        pagamento.Parameters.AddWithValue("@valorTotal", request.Valor);
        pagamento.Parameters.AddWithValue("@observacao", request.Observacao ?? request.Descricao);
        var id = Convert.ToInt64(await pagamento.ExecuteScalarAsync(cancellationToken));

        await using var pedido = connection.CreateCommand();
        pedido.Transaction = transaction;
        pedido.CommandText = """
            UPDATE pedidos
            SET valor_pago = @valorPago,
                saldo_devedor = @saldoDevedor
            WHERE id = @pedidoId;
            """;
        pedido.Parameters.AddWithValue("@pedidoId", request.PedidoId);
        pedido.Parameters.AddWithValue("@valorPago", valorPago + request.Valor);
        pedido.Parameters.AddWithValue("@saldoDevedor", saldoDevedor - request.Valor);
        await pedido.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return new CreatedAtActionResult(nameof(ListarMovimentacoes), "Caixa", new { id }, new { id });
    }

    private static async Task GarantirEstruturaCaixa(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await GarantirColunaRegistradoEmPagamentos(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS caixa_movimentacoes (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                pedido_id BIGINT UNSIGNED NULL,
                tipo VARCHAR(20) NOT NULL,
                forma_pagamento VARCHAR(30) NOT NULL,
                categoria VARCHAR(120) NOT NULL,
                descricao VARCHAR(255) NOT NULL,
                valor DECIMAL(10,2) NOT NULL,
                usuario_id BIGINT UNSIGNED NOT NULL,
                observacao VARCHAR(300) NULL,
                movimentado_em DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT fk_caixa_usuario FOREIGN KEY (usuario_id) REFERENCES usuarios(id)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await GarantirColunaCaixa(connection, cancellationToken);
    }

    private static async Task GarantirColunaCaixa(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var coluna = connection.CreateCommand();
        coluna.CommandText = """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'caixa_movimentacoes'
              AND COLUMN_NAME = 'pedido_id';
            """;
        var existe = Convert.ToInt32(await coluna.ExecuteScalarAsync(cancellationToken)) > 0;
        if (existe)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE caixa_movimentacoes ADD COLUMN pedido_id BIGINT UNSIGNED NULL AFTER id;";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task GarantirColunaRegistradoEmPagamentos(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var coluna = connection.CreateCommand();
        coluna.CommandText = """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'pagamentos'
              AND COLUMN_NAME = 'registrado_em';
            """;
        var existe = Convert.ToInt32(await coluna.ExecuteScalarAsync(cancellationToken)) > 0;
        if (existe)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE pagamentos ADD COLUMN registrado_em DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string NormalizarFormaPagamento(string formaPagamento)
    {
        return formaPagamento.Trim().ToUpperInvariant() switch
        {
            "DINHEIRO" => "DINHEIRO",
            "PIX" => "PIX",
            "CARTAO_CREDITO" or "CARTÃO CRÉDITO" or "CARTAO CREDITO" or "CRÉDITO" or "CREDITO" => "CARTAO_CREDITO",
            "CARTAO_DEBITO" or "CARTÃO DÉBITO" or "CARTAO DEBITO" or "DÉBITO" or "DEBITO" => "CARTAO_DEBITO",
            _ => formaPagamento.Trim().ToUpperInvariant()
        };
    }
}

public sealed record CaixaMovimentacaoRequest(
    long UsuarioId,
    long? PedidoId,
    string Tipo,
    string FormaPagamento,
    string Categoria,
    string Descricao,
    decimal Valor,
    string? Observacao);
