using MySqlConnector;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class PedidoRepository(IUnitOfWork unitOfWork) : IPedidoRepository
{
    public async Task<ResultadoPaginado<PedidoResumo>> ListAsync(PedidoFiltro filtro, CancellationToken cancellationToken = default)
    {
        var pedidos = new List<PedidoResumo>();
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirColunaValorEstornado((MySqlConnection)connection, cancellationToken);
        var periodo = ResolverPeriodo(filtro);

        var pagina = Math.Max(filtro.Pagina, 1);
        var tamanhoPagina = Math.Clamp(filtro.TamanhoPagina, 5, 100);
        var offset = (pagina - 1) * tamanhoPagina;
        var statusFiltro = NormalizarStatus(filtro.Status);

        await using var totalCommand = (MySqlCommand)connection.CreateCommand();
        totalCommand.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        totalCommand.CommandText = """
            SELECT COUNT(*)
            FROM pedidos p
            WHERE p.data_pedido BETWEEN @inicio AND @fim
              AND (@status IS NULL OR p.status = @status);
            """;
        PreencherFiltros(totalCommand, periodo, statusFiltro);
        var total = Convert.ToInt32(await totalCommand.ExecuteScalarAsync(cancellationToken));
        var totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)tamanhoPagina);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT p.id, p.numero, c.nome AS cliente, p.tipo, p.status, p.data_pedido,
                   p.data_entrega, p.total, p.valor_pago, p.valor_estornado, p.saldo_devedor, u.nome AS criado_por,
                   p.motivo_cancelamento
            FROM pedidos p
            INNER JOIN clientes c ON c.id = p.cliente_id
            INNER JOIN usuarios u ON u.id = p.criado_por_usuario_id
            WHERE p.data_pedido BETWEEN @inicio AND @fim
              AND (@status IS NULL OR p.status = @status)
            ORDER BY p.data_pedido DESC, p.id DESC
            LIMIT @limit OFFSET @offset;
            """;
        PreencherFiltros(command, periodo, statusFiltro);
        command.Parameters.AddWithValue("@limit", tamanhoPagina);
        command.Parameters.AddWithValue("@offset", offset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pedidos.Add(MapResumo(reader));
        }

        return new ResultadoPaginado<PedidoResumo>(pedidos, total, pagina, tamanhoPagina, totalPaginas);
    }

    public async Task<IReadOnlyList<PedidoResumo>> ListRecentAsync(CancellationToken cancellationToken = default)
    {
        var pedidos = new List<PedidoResumo>();
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirColunaValorEstornado((MySqlConnection)connection, cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT p.id, p.numero, c.nome AS cliente, p.tipo, p.status, p.data_pedido,
                   p.data_entrega, p.total, p.valor_pago, p.valor_estornado, p.saldo_devedor, u.nome AS criado_por,
                   p.motivo_cancelamento
            FROM pedidos p
            INNER JOIN clientes c ON c.id = p.cliente_id
            INNER JOIN usuarios u ON u.id = p.criado_por_usuario_id
            WHERE p.status IN ('ABERTO', 'ORCADO')
            ORDER BY p.data_pedido DESC, p.id DESC
            LIMIT 20;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pedidos.Add(MapResumo(reader));
        }

        return pedidos;
    }

    public async Task<(string Tipo, string Status, decimal ValorPago, decimal SaldoDevedor)?> ObterEstadoPedidoAsync(long id, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = "SELECT tipo, status, valor_pago, saldo_devedor FROM pedidos WHERE id = @id LIMIT 1" + (unitOfWork.Transaction != null ? " FOR UPDATE;" : ";");
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (
                reader.GetString("tipo"),
                reader.GetString("status"),
                reader.GetDecimal("valor_pago"),
                reader.GetDecimal("saldo_devedor")
            );
        }
        return null;
    }

    public async Task<PedidoDetalhe?> GetDetailsByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirColuna((MySqlConnection)connection, (MySqlTransaction?)unitOfWork.Transaction, "pagamentos", "registrado_em", "ALTER TABLE pagamentos ADD COLUMN registrado_em DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;", cancellationToken);
        await GarantirEstruturaCancelamento((MySqlConnection)connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
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
                   p.motivo_cancelamento,
                   p.valor_estornado,
                   p.total,
                   p.valor_pago,
                   p.saldo_devedor,
                   p.observacao_estorno
            FROM pedidos p
            INNER JOIN clientes c ON c.id = p.cliente_id
            WHERE p.id = @id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", id);

        long clienteId = 0, usuarioId = 0;
        string numero = "", cliente = "", tipo = "", status = "";
        string? empresa = null, cpfCnpj = null, telefone = null, endereco = null, cidade = null;
        DateOnly dataPedido = default;
        DateOnly? dataEntrega = null;
        string? vendedor = null, formaPagamento = null, condicaoPagamento = null;
        string? frente = null, fundo = null, observacao = null, motivoCancelamento = null, observacaoEstorno = null, outrosItens = null;
        decimal valorEstornado = 0, total = 0, valorPago = 0, saldoDevedor = 0;

        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var dataEntregaOrdinal = reader.GetOrdinal("data_entrega");
            
            clienteId = reader.GetInt64("cliente_id");
            usuarioId = reader.GetInt64("criado_por_usuario_id");
            numero = reader.GetString("numero");
            cliente = reader.GetString("cliente");
            empresa = reader.NullableString("empresa");
            cpfCnpj = reader.NullableString("cpf_cnpj");
            telefone = reader.NullableString("telefone");
            endereco = reader.NullableString("endereco");
            cidade = reader.NullableString("cidade");
            tipo = reader.GetString("tipo");
            status = reader.GetString("status");
            dataPedido = DateOnly.FromDateTime(reader.GetDateTime("data_pedido"));
            dataEntrega = reader.IsDBNull(dataEntregaOrdinal) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(dataEntregaOrdinal));
            vendedor = reader.NullableString("vendedor");
            formaPagamento = reader.NullableString("forma_pagamento");
            condicaoPagamento = reader.NullableString("condicao_pagamento");
            frente = reader.NullableString("frente");
            fundo = reader.NullableString("fundo");
            observacao = reader.NullableString("observacao");
            motivoCancelamento = reader.NullableString("motivo_cancelamento");
            valorEstornado = reader.GetDecimal("valor_estornado");
            observacaoEstorno = reader.NullableString("observacao_estorno");
            outrosItens = reader.NullableString("tamanhos_femininos");
            total = reader.GetDecimal("total");
            valorPago = reader.GetDecimal("valor_pago");
            saldoDevedor = reader.GetDecimal("saldo_devedor");
        }

        var itens = await ListarItensPedido(id, cancellationToken);
        var pagamentos = await ListarPagamentosPedido(id, cancellationToken);

        return new PedidoDetalhe(
            id,
            numero,
            clienteId,
            cliente,
            empresa,
            cpfCnpj,
            telefone,
            endereco,
            cidade,
            usuarioId,
            tipo,
            status,
            dataPedido,
            dataEntrega,
            vendedor,
            formaPagamento,
            condicaoPagamento,
            frente,
            fundo,
            observacao,
            motivoCancelamento,
            valorEstornado,
            valorPago - valorEstornado, // valor retido
            observacaoEstorno,
            outrosItens,
            total,
            valorPago,
            saldoDevedor,
            itens,
            pagamentos
        );
    }

    public async Task<long> CriarPedidoAsync(PedidoCadastroDto request, string tipo, string status, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        var transaction = (MySqlTransaction?)await unitOfWork.BeginTransactionAsync(cancellationToken);

        var clienteId = await SalvarCliente((MySqlConnection)connection, transaction, request, cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
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

        await SubstituirItensPedido((MySqlConnection)connection, transaction, id, request.Itens, cancellationToken);
        if (tipo == "PEDIDO" && request.ValorPago > 0)
        {
            await GarantirColuna((MySqlConnection)connection, transaction, "pagamentos", "registrado_em", "ALTER TABLE pagamentos ADD COLUMN registrado_em DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;", cancellationToken);
            await RegistrarPagamentoInicial((MySqlConnection)connection, transaction, id, request, cancellationToken);
        }

        return id;
    }

    public async Task<bool> EditarOrcamentoAsync(long id, PedidoCadastroDto request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        var transaction = (MySqlTransaction?)await unitOfWork.BeginTransactionAsync(cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
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
            return false;
        }

        await AtualizarCliente((MySqlConnection)connection, transaction, request, cancellationToken);
        await SubstituirItensPedido((MySqlConnection)connection, transaction, id, request.Itens, cancellationToken);
        return true;
    }

    public async Task<bool> EditarPedidoAsync(long id, PedidoCadastroDto request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        var transaction = (MySqlTransaction?)await unitOfWork.BeginTransactionAsync(cancellationToken);

        // Verifica se era orçamento antes do UPDATE (detecta conversão)
        await using var tipoQuery = (MySqlCommand)connection.CreateCommand();
        tipoQuery.Transaction = transaction;
        tipoQuery.CommandText = "SELECT tipo FROM pedidos WHERE id = @id LIMIT 1;";
        tipoQuery.Parameters.AddWithValue("@id", id);
        var tipoAtual = Convert.ToString(await tipoQuery.ExecuteScalarAsync(cancellationToken));
        var eraOrcamento = tipoAtual == "ORCAMENTO";

        await using var command = (MySqlCommand)connection.CreateCommand();
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
            return false;
        }

        await AtualizarCliente((MySqlConnection)connection, transaction, request, cancellationToken);
        await SubstituirItensPedido((MySqlConnection)connection, transaction, id, request.Itens, cancellationToken);

        // Se era orçamento e há valor de entrada, registra o pagamento inicial
        if (eraOrcamento && request.ValorPago > 0)
        {
            await GarantirColuna((MySqlConnection)connection, transaction, "pagamentos", "registrado_em",
                "ALTER TABLE pagamentos ADD COLUMN registrado_em DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;",
                cancellationToken);
            await RegistrarPagamentoInicial((MySqlConnection)connection, transaction, id, request, cancellationToken);
        }

        return true;
    }

    public async Task<bool> ConverterEmPedidoAsync(long id, ConverterPedidoDto request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        var transaction = (MySqlTransaction?)await unitOfWork.BeginTransactionAsync(cancellationToken);

        await using var pedido = (MySqlCommand)connection.CreateCommand();
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

        if (await pedido.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            return false;
        }

        if (request.ValorEntrada > 0)
        {
            await GarantirColuna((MySqlConnection)connection, transaction, "pagamentos", "registrado_em", "ALTER TABLE pagamentos ADD COLUMN registrado_em DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;", cancellationToken);
            await using var pagamento = (MySqlCommand)connection.CreateCommand();
            pagamento.Transaction = transaction;
            pagamento.CommandText = """
                INSERT INTO pagamentos (pedido_id, registrado_por_usuario_id, forma_pagamento, condicao_pagamento, valor_total, observacao)
                SELECT id, @usuarioId, @formaPagamento, @condicaoPagamento, @valorEntrada, 'Conversão de orçamento em pedido'
                FROM pedidos
                WHERE id = @id;
                """;
            pagamento.Parameters.AddWithValue("@id", id);
            pagamento.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
            pagamento.Parameters.AddWithValue("@formaPagamento", NormalizarFormaPagamento(request.FormaPagamento));
            pagamento.Parameters.AddWithValue("@condicaoPagamento", NormalizarCondicaoPagamento(request.CondicaoPagamento));
            pagamento.Parameters.AddWithValue("@valorEntrada", request.ValorEntrada);
            await pagamento.ExecuteNonQueryAsync(cancellationToken);
        }

        return true;
    }

    public async Task<bool> CancelarPedidoAsync(long id, AlterarStatusPedidoDto request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        var transaction = (MySqlTransaction?)await unitOfWork.BeginTransactionAsync(cancellationToken);

        await GarantirEstruturaCancelamento((MySqlConnection)connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE pedidos
            SET status = 'CANCELADO',
                cancelado_por_usuario_id = @usuarioId,
                cancelado_em = CURRENT_TIMESTAMP,
                motivo_cancelamento = @observacao,
                valor_estornado = @valorEstornado,
                observacao_estorno = @observacaoEstorno
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
        command.Parameters.AddWithValue("@observacao", request.Observacao);
        command.Parameters.AddWithValue("@valorEstornado", request.ValorDevolvido);
        command.Parameters.AddWithValue("@observacaoEstorno", ToDb(request.ObservacaoEstorno));
        
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            return false;
        }

        if (request.ValorDevolvido > 0)
        {
            await using var caixa = (MySqlCommand)connection.CreateCommand();
            caixa.Transaction = transaction;
            caixa.CommandText = """
                INSERT INTO caixa_movimentacoes
                    (pedido_id, tipo, forma_pagamento, categoria, descricao, valor, usuario_id, observacao)
                VALUES
                    (@pedidoId, 'SAIDA', @formaPagamento, 'Estorno de pedido', @descricao, @valor, @usuarioId, @observacaoEstorno);
                """;
            caixa.Parameters.AddWithValue("@pedidoId", id);
            caixa.Parameters.AddWithValue("@formaPagamento", NormalizarFormaPagamento(request.FormaDevolucao));
            caixa.Parameters.AddWithValue("@descricao", "Devolução do pedido cancelado");
            caixa.Parameters.AddWithValue("@valor", request.ValorDevolvido);
            caixa.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
            caixa.Parameters.AddWithValue("@observacaoEstorno", ToDb(request.ObservacaoEstorno ?? request.Observacao));
            await caixa.ExecuteNonQueryAsync(cancellationToken);
        }

        return true;
    }

    public async Task<bool> RegistrarEstornoAsync(long id, EstornarPedidoDto request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        var transaction = (MySqlTransaction?)await unitOfWork.BeginTransactionAsync(cancellationToken);

        await GarantirEstruturaCancelamento((MySqlConnection)connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);

        await using var caixa = (MySqlCommand)connection.CreateCommand();
        caixa.Transaction = transaction;
        caixa.CommandText = """
            INSERT INTO caixa_movimentacoes
                (pedido_id, tipo, forma_pagamento, categoria, descricao, valor, usuario_id, observacao)
            VALUES
                (@pedidoId, 'SAIDA', @formaPagamento, 'Complemento de estorno', 'Complemento de devolucao do pedido cancelado', @valor, @usuarioId, @observacao);
            """;
        caixa.Parameters.AddWithValue("@pedidoId", id);
        caixa.Parameters.AddWithValue("@formaPagamento", NormalizarFormaPagamento(request.FormaDevolucao));
        caixa.Parameters.AddWithValue("@valor", request.ValorDevolvido);
        caixa.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
        caixa.Parameters.AddWithValue("@observacao", request.Observacao.Trim());
        await caixa.ExecuteNonQueryAsync(cancellationToken);

        await using var pedido = (MySqlCommand)connection.CreateCommand();
        pedido.Transaction = transaction;
        pedido.CommandText = """
            UPDATE pedidos
            SET valor_estornado = valor_estornado + @valorDevolvido,
                observacao_estorno = CONCAT_WS('\n', NULLIF(observacao_estorno, ''), @observacao)
            WHERE id = @id;
            """;
        pedido.Parameters.AddWithValue("@id", id);
        pedido.Parameters.AddWithValue("@valorDevolvido", request.ValorDevolvido);
        pedido.Parameters.AddWithValue("@observacao", request.Observacao.Trim());
        
        return await pedido.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> FinalizarPedidoAsync(long id, FinalizarPedidoDto request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        var transaction = (MySqlTransaction?)await unitOfWork.BeginTransactionAsync(cancellationToken);

        await using var consulta = (MySqlCommand)connection.CreateCommand();
        consulta.Transaction = transaction;
        consulta.CommandText = "SELECT saldo_devedor, valor_pago FROM pedidos WHERE id = @id LIMIT 1 FOR UPDATE;";
        consulta.Parameters.AddWithValue("@id", id);

        decimal saldoDevedor = 0;
        decimal valorPago = 0;
        await using (var reader = await consulta.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                saldoDevedor = reader.GetDecimal("saldo_devedor");
                valorPago = reader.GetDecimal("valor_pago");
            }
            else
            {
                return false;
            }
        }

        if (saldoDevedor > 0 && request.ReceberSaldo)
        {
            await GarantirColuna(
                (MySqlConnection)connection,
                transaction,
                "pagamentos",
                "registrado_em",
                "ALTER TABLE pagamentos ADD COLUMN registrado_em DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;",
                cancellationToken);

            var formaPagamento = NormalizarFormaPagamento(request.FormaPagamento);
            await using var pagamento = (MySqlCommand)connection.CreateCommand();
            pagamento.Transaction = transaction;
            pagamento.CommandText = """
                INSERT INTO pagamentos (pedido_id, registrado_por_usuario_id, forma_pagamento, condicao_pagamento, valor_total, observacao)
                VALUES (@pedidoId, @usuarioId, @formaPagamento, 'PAGAMENTO_NO_PEDIDO', @valorTotal, @observacao);
                """;
            pagamento.Parameters.AddWithValue("@pedidoId", id);
            pagamento.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
            pagamento.Parameters.AddWithValue("@formaPagamento", formaPagamento);
            pagamento.Parameters.AddWithValue("@valorTotal", saldoDevedor);
            pagamento.Parameters.AddWithValue("@observacao", ToDb(request.Observacao ?? "Pagamento do saldo devedor na finalização do pedido."));
            await pagamento.ExecuteNonQueryAsync(cancellationToken);
            
            valorPago += saldoDevedor;
            saldoDevedor = 0;
        }

        await using var finalizar = (MySqlCommand)connection.CreateCommand();
        finalizar.Transaction = transaction;
        finalizar.CommandText = """
            UPDATE pedidos
            SET status = 'FINALIZADO',
                finalizado_por_usuario_id = @usuarioId,
                finalizado_em = CURRENT_TIMESTAMP,
                valor_pago = @valorPago,
                saldo_devedor = @saldoDevedor
            WHERE id = @id;
            """;
        finalizar.Parameters.AddWithValue("@id", id);
        finalizar.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
        finalizar.Parameters.AddWithValue("@valorPago", valorPago);
        finalizar.Parameters.AddWithValue("@saldoDevedor", saldoDevedor);

        return await finalizar.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private async Task<IReadOnlyList<ItemPedidoDetalhe>> ListarItensPedido(long pedidoId, CancellationToken cancellationToken)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT id, descricao, tamanho, quantidade, valor_unitario, valor_total
            FROM itens_pedido
            WHERE pedido_id = @pedidoId
            ORDER BY id;
            """;
        command.Parameters.AddWithValue("@pedidoId", pedidoId);

        var itens = new List<ItemPedidoDetalhe>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            itens.Add(new ItemPedidoDetalhe(
                reader.GetInt64("id"),
                reader.GetString("descricao"),
                reader.NullableString("tamanho"),
                reader.GetInt32("quantidade"),
                reader.GetDecimal("valor_unitario"),
                reader.GetDecimal("valor_total")
            ));
        }

        return itens;
    }

    private async Task<IReadOnlyList<PagamentoDetalhe>> ListarPagamentosPedido(long pedidoId, CancellationToken cancellationToken)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT p.id, p.forma_pagamento, p.condicao_pagamento, p.valor_total, p.observacao, p.registrado_em, u.nome AS usuario
            FROM pagamentos p
            INNER JOIN usuarios u ON u.id = p.registrado_por_usuario_id
            WHERE p.pedido_id = @pedidoId
            ORDER BY p.registrado_em DESC, p.id DESC;
            """;
        command.Parameters.AddWithValue("@pedidoId", pedidoId);

        var pagamentos = new List<PagamentoDetalhe>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pagamentos.Add(new PagamentoDetalhe(
                reader.GetInt64("id"),
                reader.GetString("forma_pagamento"),
                reader.GetString("condicao_pagamento"),
                reader.GetDecimal("valor_total"),
                reader.NullableString("observacao"),
                reader.GetDateTime("registrado_em"),
                reader.GetString("usuario")
            ));
        }

        return pagamentos;
    }

    private static void PreencherFiltros(MySqlCommand command, (DateTime Inicio, DateTime Fim) periodo, string? status)
    {
        command.Parameters.AddWithValue("@inicio", periodo.Inicio);
        command.Parameters.AddWithValue("@fim", periodo.Fim);
        command.Parameters.AddWithValue("@status", status is null ? DBNull.Value : status);
    }

    private static PedidoResumo MapResumo(MySqlDataReader reader)
    {
        var entregaOrdinal = reader.GetOrdinal("data_entrega");
        return new PedidoResumo(
            reader.GetInt64("id"),
            reader.GetString("numero"),
            reader.GetString("cliente"),
            Mapping.TipoPedido(reader.GetString("tipo")),
            Mapping.StatusPedido(reader.GetString("status")),
            DateOnly.FromDateTime(reader.GetDateTime("data_pedido")),
            reader.IsDBNull(entregaOrdinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(entregaOrdinal)),
            reader.GetDecimal("total"),
            reader.GetDecimal("valor_pago"),
            reader.GetDecimal("valor_estornado"),
            reader.GetDecimal("saldo_devedor"),
            reader.GetString("criado_por"),
            reader.NullableString("motivo_cancelamento"));
    }

    private static (DateTime Inicio, DateTime Fim) ResolverPeriodo(PedidoFiltro filtro)
    {
        if (filtro.Inicio is not null || filtro.Fim is not null)
        {
            var inicio = filtro.Inicio?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
            var fim = filtro.Fim?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.MaxValue;
            return (inicio, fim);
        }

        var ano = filtro.Ano ?? DateTime.Today.Year;
        var mes = filtro.Mes ?? DateTime.Today.Month;
        var primeiroDia = new DateTime(ano, mes, 1);
        return (primeiroDia, primeiroDia.AddMonths(1).AddTicks(-1));
    }

    private static string? NormalizarStatus(string? status)
    {
        return status?.Trim().ToUpperInvariant() switch
        {
            null or "" or "TODOS" => null,
            "ABERTO" => "ABERTO",
            "ORCADO" or "ORÇADO" or "ORCAMENTO" or "ORÇAMENTO" => "ORCADO",
            "FINALIZADO" => "FINALIZADO",
            "CANCELADO" => "CANCELADO",
            _ => status.Trim().ToUpperInvariant()
        };
    }

    private static async Task GarantirColunaValorEstornado(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'pedidos'
              AND COLUMN_NAME = 'valor_estornado';
            """;
        var existe = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        if (existe)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE pedidos ADD COLUMN valor_estornado DECIMAL(10,2) NOT NULL DEFAULT 0 AFTER motivo_cancelamento;";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void PreencherParametrosPedido(MySqlCommand command, PedidoCadastroDto request, long? clienteId = null)
    {
        command.Parameters.Add(new MySqlParameter("@numero", request.Numero));
        command.Parameters.Add(new MySqlParameter("@clienteId", clienteId ?? request.ClienteId));
        command.Parameters.Add(new MySqlParameter("@usuarioId", request.UsuarioId));
        command.Parameters.Add(new MySqlParameter("@dataPedido", request.DataPedido.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new MySqlParameter("@dataEntrega", request.DataEntrega?.ToDateTime(TimeOnly.MinValue)));
        command.Parameters.Add(new MySqlParameter("@vendedor", (object?)request.Vendedor ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@formaPagamento", (object?)NormalizarFormaPagamento(request.FormaPagamento) ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@condicaoPagamento", (object?)NormalizarCondicaoPagamento(request.CondicaoPagamento) ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@frente", (object?)request.Frente ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@fundo", (object?)request.Fundo ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@tamanhosMasculinos", DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@tamanhosFemininos", (object?)request.OutrosItens ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@observacao", (object?)request.Observacao ?? DBNull.Value));
        command.Parameters.Add(new MySqlParameter("@total", request.Total));
    }

    private async Task<long> SalvarCliente(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PedidoCadastroDto request,
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
        PedidoCadastroDto request,
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
        IReadOnlyList<ItemPedidoCadastroDto> itens,
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

    private static async Task RegistrarPagamentoInicial(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long pedidoId,
        PedidoCadastroDto request,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO pagamentos (pedido_id, registrado_por_usuario_id, forma_pagamento, condicao_pagamento, valor_total, observacao)
            VALUES (@pedidoId, @usuarioId, @formaPagamento, @condicaoPagamento, @valorTotal, 'Entrada registrada na criação do pedido');
            """;
        command.Parameters.AddWithValue("@pedidoId", pedidoId);
        command.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
        command.Parameters.AddWithValue("@formaPagamento", NormalizarFormaPagamento(request.FormaPagamento));
        command.Parameters.AddWithValue("@condicaoPagamento", NormalizarCondicaoPagamento(request.CondicaoPagamento));
        command.Parameters.AddWithValue("@valorTotal", request.ValorPago);
        await command.ExecuteNonQueryAsync(cancellationToken);
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


    private static async Task GarantirEstruturaCancelamento(MySqlConnection connection, MySqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await GarantirColuna(connection, transaction, "pedidos", "valor_estornado", "ALTER TABLE pedidos ADD COLUMN valor_estornado DECIMAL(10,2) NOT NULL DEFAULT 0 AFTER motivo_cancelamento;", cancellationToken);
        await GarantirColuna(connection, transaction, "pedidos", "observacao_estorno", "ALTER TABLE pedidos ADD COLUMN observacao_estorno VARCHAR(300) NULL AFTER valor_estornado;", cancellationToken);

        await using var tabelaCaixa = connection.CreateCommand();
        tabelaCaixa.Transaction = transaction;
        tabelaCaixa.CommandText = """
            CREATE TABLE IF NOT EXISTS caixa_movimentacoes (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
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
        await tabelaCaixa.ExecuteNonQueryAsync(cancellationToken);
        await GarantirColuna(connection, transaction, "caixa_movimentacoes", "pedido_id", "ALTER TABLE caixa_movimentacoes ADD COLUMN pedido_id BIGINT UNSIGNED NULL AFTER id;", cancellationToken);
    }

    private static async Task GarantirColuna(MySqlConnection connection, MySqlTransaction? transaction, string tabela, string coluna, string alterSql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
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
        if (existe)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.Transaction = transaction;
        alter.CommandText = alterSql;
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }
}
