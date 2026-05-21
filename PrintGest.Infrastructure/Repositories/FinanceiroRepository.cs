using System.Data.Common;
using MySqlConnector;
using PrintGest.Application.Abstractions;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class FinanceiroRepository(IUnitOfWork unitOfWork) : IFinanceiroRepository
{
    public async Task<FinanceiroVendasResult> ObterVendasAsync(FinanceiroFiltro filtro, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaFinanceiro(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        var periodo = ResolverPeriodo(filtro);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT p.id,
                   p.numero,
                   c.nome AS cliente,
                   p.tipo,
                   p.status,
                   p.data_pedido,
                   p.data_entrega,
                   p.total,
                   p.valor_pago,
                   p.valor_estornado,
                   p.saldo_devedor,
                   u.nome AS criado_por,
                   p.motivo_cancelamento
            FROM pedidos p
            INNER JOIN clientes c ON c.id = p.cliente_id
            INNER JOIN usuarios u ON u.id = p.criado_por_usuario_id
            WHERE p.tipo = 'PEDIDO'
              AND p.data_pedido BETWEEN @inicio AND @fim
              AND (@status IS NULL OR p.status = @status)
            ORDER BY p.data_pedido DESC, p.id DESC;
            """;
        PreencherFiltros(command, periodo, filtro.Status);

        var pedidos = new List<FinanceiroPedido>();
        decimal total = 0;
        decimal pago = 0;
        decimal saldo = 0;
        decimal devolvido = 0;
        var devolucoes = 0;
        var emAndamento = 0;
        var entrouHoje = 0m;
        var hoje = DateOnly.FromDateTime(DateTime.Today);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var dataPedido = DateOnly.FromDateTime(reader.GetDateTime("data_pedido"));
            var valorPago = reader.GetDecimal("valor_pago");
            var valorEstornado = reader.GetDecimal("valor_estornado");
            var saldoDevedor = reader.GetDecimal("saldo_devedor");
            var statusPedido = reader.GetString("status");
            total += reader.GetDecimal("total");
            pago += valorPago;
            saldo += saldoDevedor;
            devolvido += valorEstornado;
            if (valorEstornado > 0) devolucoes++;
            if (statusPedido == "ABERTO") emAndamento++;
            if (dataPedido == hoje) entrouHoje += valorPago;

            pedidos.Add(new FinanceiroPedido(
                reader.GetInt64("id"),
                reader.GetString("numero"),
                reader.GetString("cliente"),
                reader.GetString("tipo"),
                statusPedido,
                dataPedido,
                GetNullableDateOnly(reader, "data_entrega"),
                reader.GetDecimal("total"),
                valorPago,
                valorEstornado,
                saldoDevedor,
                reader.GetString("criado_por"),
                reader.NullableString("motivo_cancelamento")));
        }

        return new FinanceiroVendasResult(
            new FinanceiroPeriodo(DateOnly.FromDateTime(periodo.Inicio), DateOnly.FromDateTime(periodo.Fim.Date)),
            new FinanceiroVendasResumo(total, pago, saldo, pedidos.Count, devolucoes, devolvido, emAndamento, entrouHoje),
            Paginar(pedidos, filtro.Pagina, filtro.TamanhoPagina));
    }

    public async Task<FinanceiroEntradasResult> ObterEntradasAsync(FinanceiroFiltro filtro, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaFinanceiro(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        var periodo = ResolverPeriodo(filtro);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT *
            FROM (
                SELECT 'PAGAMENTO' AS origem, p.forma_pagamento, p.valor_total AS valor, p.registrado_em AS data_movimento,
                       CONCAT('Pagamento do pedido ', ped.numero, ' - ', c.nome) AS descricao, u.nome AS usuario
                FROM pagamentos p
                INNER JOIN pedidos ped ON ped.id = p.pedido_id
                INNER JOIN clientes c ON c.id = ped.cliente_id
                INNER JOIN usuarios u ON u.id = p.registrado_por_usuario_id
                UNION ALL
                SELECT 'CAIXA' AS origem, m.forma_pagamento, m.valor, m.movimentado_em AS data_movimento,
                       m.descricao, u.nome AS usuario
                FROM caixa_movimentacoes m
                INNER JOIN usuarios u ON u.id = m.usuario_id
                WHERE m.tipo = 'ENTRADA'
            ) entradas
            WHERE data_movimento BETWEEN @inicio AND @fim
            ORDER BY data_movimento DESC;
            """;
        command.Parameters.AddWithValue("@inicio", periodo.Inicio);
        command.Parameters.AddWithValue("@fim", periodo.Fim);

        var itens = new List<FinanceiroEntrada>();
        decimal dinheiro = 0;
        decimal pix = 0;
        decimal credito = 0;
        decimal debito = 0;
        decimal hojeTotal = 0;
        var hoje = DateOnly.FromDateTime(DateTime.Today);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var forma = reader.GetString("forma_pagamento");
            var valor = reader.GetDecimal("valor");
            var data = reader.GetDateTime("data_movimento");
            if (forma == "DINHEIRO") dinheiro += valor;
            if (forma == "PIX") pix += valor;
            if (forma == "CARTAO_CREDITO") credito += valor;
            if (forma == "CARTAO_DEBITO") debito += valor;
            if (DateOnly.FromDateTime(data) == hoje) hojeTotal += valor;

            itens.Add(new FinanceiroEntrada(
                reader.GetString("origem"),
                forma,
                valor,
                data,
                reader.GetString("descricao"),
                reader.GetString("usuario")));
        }

        return new FinanceiroEntradasResult(
            new FinanceiroEntradasResumo(dinheiro + pix + credito + debito, dinheiro, pix, credito, debito, hojeTotal),
            Paginar(itens, filtro.Pagina, filtro.TamanhoPagina));
    }

    public async Task<FinanceiroDespesasResult> ListarDespesasAsync(FinanceiroFiltro filtro, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaFinanceiro(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        var periodo = ResolverPeriodo(filtro);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT id, grupo_despesa_id, numero_parcela, total_parcelas, categoria, descricao, valor, valor_total, vencimento, status, data_pagamento, observacao
            FROM despesas
            WHERE vencimento BETWEEN @inicio AND @fim
            ORDER BY CASE WHEN vencimento = CURRENT_DATE() AND status <> 'PAGO' THEN 0 ELSE 1 END,
                     vencimento ASC,
                     grupo_despesa_id,
                     numero_parcela;
            """;
        command.Parameters.AddWithValue("@inicio", periodo.Inicio.Date);
        command.Parameters.AddWithValue("@fim", periodo.Fim.Date);

        var despesas = new List<FinanceiroDespesa>();
        var categorias = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var totalMes = 0m;
        var naoPagas = 0m;
        var pagas = 0m;
        var vencimentoHojeValor = 0m;
        var vencimentoHojeQtd = 0;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var vencimento = DateOnly.FromDateTime(reader.GetDateTime("vencimento"));
            var valor = reader.GetDecimal("valor");
            var status = reader.GetString("status");
            var categoria = reader.GetString("categoria");
            categorias.Add(categoria);
            totalMes += valor;
            if (status == "PAGO") pagas += valor; else naoPagas += valor;
            if (vencimento == hoje && status != "PAGO")
            {
                vencimentoHojeQtd++;
                vencimentoHojeValor += valor;
            }

            despesas.Add(new FinanceiroDespesa(
                reader.GetInt64("id"),
                reader.GetString("grupo_despesa_id"),
                reader.GetInt32("numero_parcela"),
                reader.GetInt32("total_parcelas"),
                categoria,
                reader.GetString("descricao"),
                valor,
                reader.GetDecimal("valor_total"),
                vencimento,
                status,
                GetNullableDateOnly(reader, "data_pagamento"),
                reader.NullableString("observacao")));
        }

        return new FinanceiroDespesasResult(
            new FinanceiroDespesasResumo(despesas.Count, vencimentoHojeQtd, vencimentoHojeValor, totalMes, naoPagas, pagas),
            categorias,
            despesas);
    }

    public async Task<long> CriarDespesaAsync(FinanceiroDespesaRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaFinanceiro(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);

        var transacaoCriada = false;
        var transaction = unitOfWork.Transaction;
        if (transaction == null)
        {
            transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            transacaoCriada = true;
        }

        try
        {
            var totalParcelas = request.CondicaoPagamento == "PARCELADO" ? request.QuantidadeParcelas : 1;
            var grupoId = Guid.NewGuid().ToString("N");
            var valorParcela = Math.Round(request.Valor / totalParcelas, 2);
            var restante = request.Valor;
            long primeiroId = 0;

            for (var parcela = 1; parcela <= totalParcelas; parcela++)
            {
                var valor = parcela == totalParcelas ? restante : valorParcela;
                restante -= valor;
                await using var command = (MySqlCommand)connection.CreateCommand();
                command.Transaction = (MySqlTransaction)transaction;
                command.CommandText = """
                    INSERT INTO despesas
                        (cadastrado_por_usuario_id, grupo_despesa_id, numero_parcela, total_parcelas, categoria, descricao, valor, valor_total, vencimento, status, observacao)
                    VALUES
                        (@usuarioId, @grupoId, @numeroParcela, @totalParcelas, @categoria, @descricao, @valor, @valorTotal, @vencimento, 'ABERTO', @observacao);
                    SELECT LAST_INSERT_ID();
                    """;
                command.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
                command.Parameters.AddWithValue("@grupoId", grupoId);
                command.Parameters.AddWithValue("@numeroParcela", parcela);
                command.Parameters.AddWithValue("@totalParcelas", totalParcelas);
                command.Parameters.AddWithValue("@categoria", request.Categoria.Trim());
                command.Parameters.AddWithValue("@descricao", request.Descricao.Trim());
                command.Parameters.AddWithValue("@valor", valor);
                command.Parameters.AddWithValue("@valorTotal", request.Valor);
                command.Parameters.AddWithValue("@vencimento", request.Vencimento.AddMonths(parcela - 1).ToDateTime(TimeOnly.MinValue));
                command.Parameters.AddWithValue("@observacao", ToDb(request.Observacao));
                var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
                if (primeiroId == 0) primeiroId = id;
            }

            if (transacaoCriada)
            {
                await unitOfWork.CommitAsync(cancellationToken);
            }

            return primeiroId;
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

    public async Task<bool> PagarDespesaAsync(long id, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaFinanceiro(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = "UPDATE despesas SET status = 'PAGO', data_pagamento = CURRENT_DATE() WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> AtualizarDespesaAsync(string grupoDespesaId, FinanceiroDespesaAtualizarRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaFinanceiro(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT id
            FROM despesas
            WHERE grupo_despesa_id = @grupoId
            ORDER BY numero_parcela;
            """;
        command.Parameters.AddWithValue("@grupoId", grupoDespesaId);

        var ids = new List<long>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(reader.GetInt64("id"));
            }
        }

        if (ids.Count == 0)
        {
            return false;
        }

        var transacaoCriada = false;
        var transaction = unitOfWork.Transaction;
        if (transaction == null)
        {
            transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            transacaoCriada = true;
        }

        try
        {
            var valorParcela = Math.Round(request.Valor / ids.Count, 2);
            var restante = request.Valor;
            for (var index = 0; index < ids.Count; index++)
            {
                var valor = index == ids.Count - 1 ? restante : valorParcela;
                restante -= valor;
                await using var update = (MySqlCommand)connection.CreateCommand();
                update.Transaction = (MySqlTransaction)transaction;
                update.CommandText = """
                    UPDATE despesas
                    SET categoria = @categoria,
                        descricao = @descricao,
                        valor = @valor,
                        valor_total = @valorTotal,
                        vencimento = @vencimento,
                        observacao = @observacao
                    WHERE id = @id;
                    """;
                update.Parameters.AddWithValue("@id", ids[index]);
                update.Parameters.AddWithValue("@categoria", request.Categoria.Trim());
                update.Parameters.AddWithValue("@descricao", request.Descricao.Trim());
                update.Parameters.AddWithValue("@valor", valor);
                update.Parameters.AddWithValue("@valorTotal", request.Valor);
                update.Parameters.AddWithValue("@vencimento", request.Vencimento.AddMonths(index).ToDateTime(TimeOnly.MinValue));
                update.Parameters.AddWithValue("@observacao", ToDb(request.Observacao));
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

            if (transacaoCriada)
            {
                await unitOfWork.CommitAsync(cancellationToken);
            }
            return true;
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

    public async Task<FinanceiroGraficosResult> ObterGraficosAsync(int? ano, int? mes, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaFinanceiro(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        var anoFiltro = ano ?? DateTime.Today.Year;
        var mesFiltro = mes ?? DateTime.Today.Month;

        var receitaAnual = await ListarMensal(connection, (MySqlTransaction?)unitOfWork.Transaction, "SELECT MONTH(data_pedido) mes, COALESCE(SUM(valor_pago),0) valor FROM pedidos WHERE YEAR(data_pedido)=@ano GROUP BY MONTH(data_pedido);", anoFiltro, cancellationToken);
        var despesaAnual = await ListarMensal(connection, (MySqlTransaction?)unitOfWork.Transaction, "SELECT MONTH(vencimento) mes, COALESCE(SUM(valor),0) valor FROM despesas WHERE YEAR(vencimento)=@ano GROUP BY MONTH(vencimento);", anoFiltro, cancellationToken);
        var despesasMes = await ListarPorCategoria(connection, (MySqlTransaction?)unitOfWork.Transaction, anoFiltro, mesFiltro, cancellationToken);
        var clientesMes = await ListarTopClientes(connection, (MySqlTransaction?)unitOfWork.Transaction, anoFiltro, mesFiltro, cancellationToken);

        return new FinanceiroGraficosResult(anoFiltro, mesFiltro, receitaAnual, despesaAnual, despesasMes, clientesMes);
    }

    private static async Task<IReadOnlyList<FinanceiroGraficoMensal>> ListarMensal(DbConnection connection, MySqlTransaction? transaction, string sql, int ano, CancellationToken cancellationToken)
    {
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@ano", ano);
        var valores = Enumerable.Range(1, 12).ToDictionary(mes => mes, _ => 0m);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) valores[reader.GetInt32("mes")] = reader.GetDecimal("valor");
        return valores.Select(item => new FinanceiroGraficoMensal(item.Key, item.Value)).ToList();
    }

    private static async Task<IReadOnlyList<FinanceiroDespesaCategoria>> ListarPorCategoria(DbConnection connection, MySqlTransaction? transaction, int ano, int mes, CancellationToken cancellationToken)
    {
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT categoria, COALESCE(SUM(valor),0) valor FROM despesas WHERE YEAR(vencimento)=@ano AND MONTH(vencimento)=@mes GROUP BY categoria ORDER BY valor DESC;";
        command.Parameters.AddWithValue("@ano", ano);
        command.Parameters.AddWithValue("@mes", mes);
        var itens = new List<FinanceiroDespesaCategoria>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) itens.Add(new FinanceiroDespesaCategoria(reader.GetString("categoria"), reader.GetDecimal("valor")));
        return itens;
    }

    private static async Task<IReadOnlyList<FinanceiroClienteValor>> ListarTopClientes(DbConnection connection, MySqlTransaction? transaction, int ano, int mes, CancellationToken cancellationToken)
    {
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT c.nome cliente, COALESCE(SUM(p.total),0) valor
            FROM pedidos p
            INNER JOIN clientes c ON c.id = p.cliente_id
            WHERE p.tipo = 'PEDIDO' AND YEAR(p.data_pedido)=@ano AND MONTH(p.data_pedido)=@mes
            GROUP BY c.nome
            ORDER BY valor DESC
            LIMIT 10;
            """;
        command.Parameters.AddWithValue("@ano", ano);
        command.Parameters.AddWithValue("@mes", mes);
        var itens = new List<FinanceiroClienteValor>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) itens.Add(new FinanceiroClienteValor(reader.GetString("cliente"), reader.GetDecimal("valor")));
        return itens;
    }

    private static ResultadoPaginado<T> Paginar<T>(IReadOnlyList<T> itens, int pagina, int tamanhoPagina)
    {
        var pageSize = Math.Clamp(tamanhoPagina, 1, 100);
        var total = itens.Count;
        var totalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Clamp(pagina, 1, totalPaginas);
        var pageItems = itens.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new ResultadoPaginado<T>(pageItems, total, page, pageSize, totalPaginas);
    }

    private static (DateTime Inicio, DateTime Fim) ResolverPeriodo(FinanceiroFiltro filtro)
    {
        if (filtro.Inicio is not null || filtro.Fim is not null)
        {
            var start = filtro.Inicio?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
            var end = filtro.Fim?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.MaxValue;
            return (start, end);
        }

        var year = filtro.Ano ?? DateTime.Today.Year;
        var month = filtro.Mes ?? DateTime.Today.Month;
        var first = new DateTime(year, month, 1);
        var last = first.AddMonths(1).AddTicks(-1);
        return (first, last);
    }

    private static void PreencherFiltros(MySqlCommand command, (DateTime Inicio, DateTime Fim) periodo, string? status)
    {
        command.Parameters.AddWithValue("@inicio", periodo.Inicio);
        command.Parameters.AddWithValue("@fim", periodo.Fim);
        command.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(status) ? DBNull.Value : status.Trim().ToUpperInvariant());
    }

    private static async Task GarantirEstruturaFinanceiro(DbConnection connection, MySqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await GarantirColuna(connection, transaction, "pedidos", "valor_estornado", "ALTER TABLE pedidos ADD COLUMN valor_estornado DECIMAL(10,2) NOT NULL DEFAULT 0 AFTER motivo_cancelamento;", cancellationToken);
        await GarantirColuna(connection, transaction, "despesas", "grupo_despesa_id", "ALTER TABLE despesas ADD COLUMN grupo_despesa_id VARCHAR(40) NULL AFTER id;", cancellationToken);
        await GarantirColuna(connection, transaction, "despesas", "numero_parcela", "ALTER TABLE despesas ADD COLUMN numero_parcela INT NOT NULL DEFAULT 1 AFTER grupo_despesa_id;", cancellationToken);
        await GarantirColuna(connection, transaction, "despesas", "total_parcelas", "ALTER TABLE despesas ADD COLUMN total_parcelas INT NOT NULL DEFAULT 1 AFTER numero_parcela;", cancellationToken);
        await GarantirColuna(connection, transaction, "despesas", "valor_total", "ALTER TABLE despesas ADD COLUMN valor_total DECIMAL(10,2) NOT NULL DEFAULT 0 AFTER valor;", cancellationToken);
        await AjustarColunaCategoriaDespesa(connection, transaction, cancellationToken);

        await using var update = (MySqlCommand)connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE despesas SET grupo_despesa_id = COALESCE(grupo_despesa_id, CONCAT('LEGADO-', id)), valor_total = CASE WHEN valor_total = 0 THEN valor ELSE valor_total END;";
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AjustarColunaCategoriaDespesa(DbConnection connection, MySqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var consulta = (MySqlCommand)connection.CreateCommand();
        consulta.Transaction = transaction;
        consulta.CommandText = """
            SELECT DATA_TYPE
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'despesas'
              AND COLUMN_NAME = 'categoria'
            LIMIT 1;
            """;
        var tipoAtual = Convert.ToString(await consulta.ExecuteScalarAsync(cancellationToken));
        if (string.Equals(tipoAtual, "varchar", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "ALTER TABLE despesas MODIFY COLUMN categoria VARCHAR(120) NOT NULL;";
        await command.ExecuteNonQueryAsync(cancellationToken);
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
        try
        {
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (MySqlException exception) when (exception.Number == 1060)
        {
            // Outra requisicao pode ter criado a coluna entre a consulta e o ALTER TABLE.
        }
    }

    private static DateOnly? GetNullableDateOnly(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(ordinal));
    }

    private static object ToDb(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
}
