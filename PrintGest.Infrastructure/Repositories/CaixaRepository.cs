using System.Data.Common;
using MySqlConnector;
using PrintGest.Application.Abstractions;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class CaixaRepository(IUnitOfWork unitOfWork) : ICaixaRepository
{
    public async Task<CaixaResumoDto> ObterResumoAsync(DateOnly? inicio, DateOnly? fim, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaCaixa(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        var periodo = ResolverPeriodo(inicio, fim);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
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
        command.Parameters.AddWithValue("@inicio", periodo.Inicio);
        command.Parameters.AddWithValue("@fim", periodo.Fim);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var entradas = reader.GetDecimal("entradas");
        var saidas = reader.GetDecimal("saidas");
        return new CaixaResumoDto(
            entradas,
            saidas,
            entradas - saidas,
            reader.GetDecimal("dinheiro"),
            reader.GetDecimal("pix"),
            reader.GetDecimal("cartao_credito"),
            reader.GetDecimal("cartao_debito"));
    }

    public async Task<ResultadoPaginado<CaixaMovimentacaoDto>> ListarMovimentacoesAsync(DateOnly? inicio, DateOnly? fim, int pagina, int tamanhoPagina, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaCaixa(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        var periodo = ResolverPeriodo(inicio, fim);
        var paging = NormalizarPaginacao(pagina, tamanhoPagina);

        await using var totalCommand = (MySqlCommand)connection.CreateCommand();
        totalCommand.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        totalCommand.CommandText = """
            SELECT COUNT(*)
            FROM (
                SELECT registrado_em AS data_movimento FROM pagamentos
                UNION ALL
                SELECT movimentado_em AS data_movimento FROM caixa_movimentacoes
            ) caixa
            WHERE data_movimento BETWEEN @inicio AND @fim;
            """;
        totalCommand.Parameters.AddWithValue("@inicio", periodo.Inicio);
        totalCommand.Parameters.AddWithValue("@fim", periodo.Fim);
        var total = Convert.ToInt32(await totalCommand.ExecuteScalarAsync(cancellationToken));

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
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
            LIMIT @limit OFFSET @offset;
            """;
        command.Parameters.AddWithValue("@inicio", periodo.Inicio);
        command.Parameters.AddWithValue("@fim", periodo.Fim);
        command.Parameters.AddWithValue("@limit", paging.TamanhoPagina);
        command.Parameters.AddWithValue("@offset", paging.Offset);

        var movimentacoes = new List<CaixaMovimentacaoDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            movimentacoes.Add(new CaixaMovimentacaoDto(
                reader.GetString("id"),
                reader.GetString("tipo"),
                reader.GetString("forma_pagamento"),
                reader.GetString("categoria"),
                reader.GetString("descricao"),
                reader.GetDecimal("valor"),
                reader.GetDateTime("movimentado_em"),
                reader.GetString("usuario"),
                reader.NullableString("observacao"),
                reader.GetString("origem")));
        }

        return CriarResultado(movimentacoes, total, paging.Pagina, paging.TamanhoPagina);
    }

    public async Task<long> CriarMovimentacaoAsync(CaixaMovimentacaoRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaCaixa(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        var tipo = request.Tipo.ToUpperInvariant();
        if (tipo == "ENTRADA" && request.PedidoId is not null)
        {
            return await RegistrarPagamentoPedido(unitOfWork, request, cancellationToken);
        }

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
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
        command.Parameters.AddWithValue("@observacao", ToDb(request.Observacao));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<long> RegistrarPagamentoPedido(IUnitOfWork unitOfWork, CaixaMovimentacaoRequest request, CancellationToken cancellationToken)
    {
        var transacaoCriada = false;
        var transaction = unitOfWork.Transaction;
        if (transaction == null)
        {
            transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            transacaoCriada = true;
        }

        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);

        try
        {
            await using var consulta = (MySqlCommand)connection.CreateCommand();
            consulta.Transaction = (MySqlTransaction)transaction;
            consulta.CommandText = """
                SELECT tipo, status, valor_pago, saldo_devedor
                FROM pedidos
                WHERE id = @pedidoId
                LIMIT 1
                FOR UPDATE;
                """;
            consulta.Parameters.AddWithValue("@pedidoId", request.PedidoId);

            decimal valorPago;
            decimal saldoDevedor;
            await using (var reader = await consulta.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Pedido nao encontrado.");
                var tipoPedido = reader.GetString("tipo");
                var statusPedido = reader.GetString("status");
                valorPago = reader.GetDecimal("valor_pago");
                saldoDevedor = reader.GetDecimal("saldo_devedor");

                if (tipoPedido != "PEDIDO") throw new InvalidOperationException("Somente pedidos podem receber pagamento no caixa.");
                if (statusPedido is "CANCELADO" or "FINALIZADO") throw new InvalidOperationException("Nao e possivel registrar pagamento para pedido cancelado ou finalizado.");
                if (request.Valor > saldoDevedor) throw new InvalidOperationException("O valor informado e maior que o saldo devedor do pedido.");
            }

            await using var pagamento = (MySqlCommand)connection.CreateCommand();
            pagamento.Transaction = (MySqlTransaction)transaction;
            pagamento.CommandText = """
                INSERT INTO pagamentos (pedido_id, registrado_por_usuario_id, forma_pagamento, condicao_pagamento, valor_total, observacao)
                VALUES (@pedidoId, @usuarioId, @formaPagamento, 'PAGAMENTO_NO_PEDIDO', @valorTotal, @observacao);
                SELECT LAST_INSERT_ID();
                """;
            pagamento.Parameters.AddWithValue("@pedidoId", request.PedidoId);
            pagamento.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
            pagamento.Parameters.AddWithValue("@formaPagamento", NormalizarFormaPagamento(request.FormaPagamento));
            pagamento.Parameters.AddWithValue("@valorTotal", request.Valor);
            pagamento.Parameters.AddWithValue("@observacao", ToDb(request.Observacao ?? request.Descricao));
            var id = Convert.ToInt64(await pagamento.ExecuteScalarAsync(cancellationToken));

            await using var pedido = (MySqlCommand)connection.CreateCommand();
            pedido.Transaction = (MySqlTransaction)transaction;
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

            if (transacaoCriada)
            {
                await unitOfWork.CommitAsync(cancellationToken);
            }
            return id;
        }
        catch
        {
            if (transacaoCriada)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
            }
            throw;
        }
    }

    private static async Task GarantirEstruturaCaixa(DbConnection connection, MySqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await GarantirColunaRegistradoEmPagamentos(connection, transaction, cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = transaction;
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
        await GarantirColuna(connection, transaction, "caixa_movimentacoes", "pedido_id", "ALTER TABLE caixa_movimentacoes ADD COLUMN pedido_id BIGINT UNSIGNED NULL AFTER id;", cancellationToken);
    }

    private static async Task GarantirColunaRegistradoEmPagamentos(DbConnection connection, MySqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await GarantirColuna(connection, transaction, "pagamentos", "registrado_em", "ALTER TABLE pagamentos ADD COLUMN registrado_em DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;", cancellationToken);
    }

    private static async Task GarantirColuna(DbConnection connection, MySqlTransaction? transaction, string tabela, string coluna, string alterSql, CancellationToken cancellationToken)
    {
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = @tabela
              AND COLUMN_NAME = @coluna;
            """;
        command.Parameters.AddWithValue("@tabela", tabela);
        command.Parameters.AddWithValue("@coluna", coluna);
        var existe = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        if (existe) return;
        await using var alter = (MySqlCommand)connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = alterSql;
        try { await alter.ExecuteNonQueryAsync(cancellationToken); }
        catch (MySqlException exception) when (exception.Number == 1060) { }
    }

    private static (DateTime Inicio, DateTime Fim) ResolverPeriodo(DateOnly? inicio, DateOnly? fim)
    {
        return (
            inicio?.ToDateTime(TimeOnly.MinValue) ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
            fim?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.Today.Date.AddDays(1).AddTicks(-1));
    }

    private static string NormalizarFormaPagamento(string formaPagamento)
    {
        return formaPagamento.Trim().ToUpperInvariant() switch
        {
            "DINHEIRO" => "DINHEIRO",
            "PIX" => "PIX",
            "CARTAO_CREDITO" or "CARTAO CREDITO" or "CREDITO" => "CARTAO_CREDITO",
            "CARTAO_DEBITO" or "CARTAO DEBITO" or "DEBITO" => "CARTAO_DEBITO",
            _ => formaPagamento.Trim().ToUpperInvariant()
        };
    }

    private static (int Pagina, int TamanhoPagina, int Offset) NormalizarPaginacao(int pagina, int tamanhoPagina)
    {
        var page = Math.Max(pagina, 1);
        var size = Math.Clamp(tamanhoPagina, 5, 100);
        return (page, size, (page - 1) * size);
    }

    private static ResultadoPaginado<T> CriarResultado<T>(IReadOnlyList<T> itens, int total, int pagina, int tamanhoPagina)
    {
        var totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)tamanhoPagina);
        return new ResultadoPaginado<T>(itens, total, pagina, tamanhoPagina, totalPaginas);
    }

    private static object ToDb(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}
