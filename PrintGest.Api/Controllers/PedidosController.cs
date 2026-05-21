using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using PrintGest.Application.Abstractions;
using PrintGest.Infrastructure.Data;
using System.ComponentModel.DataAnnotations;

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
        await GarantirColunaRegistradoEmPagamentos(connection, cancellationToken);
        await GarantirEstruturaCancelamento(connection, cancellationToken);
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
                   p.motivo_cancelamento,
                   p.valor_estornado,
                   p.observacao_estorno,
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
            MotivoCancelamento = reader.NullableString("motivo_cancelamento"),
            ValorEstornado = reader.GetDecimal("valor_estornado"),
            ValorRetido = reader.GetDecimal("valor_pago") - reader.GetDecimal("valor_estornado"),
            ObservacaoEstorno = reader.NullableString("observacao_estorno"),
            OutrosItens = reader.NullableString("tamanhos_femininos"),
            Total = reader.GetDecimal("total"),
            ValorPago = reader.GetDecimal("valor_pago"),
            SaldoDevedor = reader.GetDecimal("saldo_devedor"),
            Itens = await ListarItensPedido(id, cancellationToken),
            Pagamentos = await ListarPagamentosPedido(id, cancellationToken)
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
            return await CriarRespostaEdicaoOrcamentoNaoPermitida(connection, id, cancellationToken);
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

        await transaction.CommitAsync(cancellationToken);
        return Ok(new { mensagem = "Orçamento convertido em pedido." });
    }

    [HttpPatch("{id:long}/cancelar")]
    public Task<IActionResult> Cancelar(long id, [FromBody] AlterarStatusPedidoRequest request, CancellationToken cancellationToken)
    {
        return CancelarPedido(id, request, cancellationToken);
    }

    [HttpPatch("{id:long}/estornar")]
    public Task<IActionResult> Estornar(long id, [FromBody] EstornarPedidoRequest request, CancellationToken cancellationToken)
    {
        return RegistrarNovoEstorno(id, request, cancellationToken);
    }

    [HttpPatch("{id:long}/finalizar")]
    public Task<IActionResult> Finalizar(long id, [FromBody] FinalizarPedidoRequest request, CancellationToken cancellationToken)
    {
        return FinalizarPedido(id, request, cancellationToken);
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
        if (tipo == "PEDIDO" && request.ValorPago > 0)
        {
            await RegistrarPagamentoInicial(connection, transaction, id, request, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return id;
    }

    private static async Task RegistrarPagamentoInicial(
        MySqlConnection connection,
        MySqlTransaction transaction,
        long pedidoId,
        PedidoRequest request,
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

    private async Task<IReadOnlyList<object>> ListarPagamentosPedido(long pedidoId, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.forma_pagamento, p.condicao_pagamento, p.valor_total, p.observacao, p.registrado_em, u.nome AS usuario
            FROM pagamentos p
            INNER JOIN usuarios u ON u.id = p.registrado_por_usuario_id
            WHERE p.pedido_id = @pedidoId
            ORDER BY p.registrado_em DESC, p.id DESC;
            """;
        command.Parameters.AddWithValue("@pedidoId", pedidoId);

        var pagamentos = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pagamentos.Add(new
            {
                Id = reader.GetInt64("id"),
                FormaPagamento = reader.GetString("forma_pagamento"),
                CondicaoPagamento = reader.GetString("condicao_pagamento"),
                Valor = reader.GetDecimal("valor_total"),
                Observacao = reader.NullableString("observacao"),
                RegistradoEm = reader.GetDateTime("registrado_em"),
                Usuario = reader.GetString("usuario")
            });
        }

        return pagamentos;
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

    private static async Task GarantirEstruturaCancelamento(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await GarantirColuna(connection, "pedidos", "valor_estornado", "ALTER TABLE pedidos ADD COLUMN valor_estornado DECIMAL(10,2) NOT NULL DEFAULT 0 AFTER motivo_cancelamento;", cancellationToken);
        await GarantirColuna(connection, "pedidos", "observacao_estorno", "ALTER TABLE pedidos ADD COLUMN observacao_estorno VARCHAR(300) NULL AFTER valor_estornado;", cancellationToken);

        await using var tabelaCaixa = connection.CreateCommand();
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
        await GarantirColuna(connection, "caixa_movimentacoes", "pedido_id", "ALTER TABLE caixa_movimentacoes ADD COLUMN pedido_id BIGINT UNSIGNED NULL AFTER id;", cancellationToken);
    }

    private static async Task GarantirColuna(MySqlConnection connection, string tabela, string coluna, string alterSql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
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
        alter.CommandText = alterSql;
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IActionResult> AlterarStatus(long id, string status, long usuarioId, string? observacao, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var consulta = connection.CreateCommand();
        consulta.CommandText = "SELECT tipo, status, saldo_devedor FROM pedidos WHERE id = @id LIMIT 1;";
        consulta.Parameters.AddWithValue("@id", id);

        await using var reader = await consulta.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return NotFound(new { mensagem = "Pedido ou orçamento não encontrado." });
        }

        var tipoAtual = reader.GetString("tipo");
        var statusAtual = reader.GetString("status");
        var saldoDevedor = reader.GetDecimal("saldo_devedor");
        await reader.DisposeAsync();

        if (status == "CANCELADO")
        {
            if (string.IsNullOrWhiteSpace(observacao) || observacao.Trim().Length < 10)
            {
                return BadRequest(new { mensagem = "Informe o motivo do cancelamento com pelo menos 10 caracteres." });
            }

            if (statusAtual == "CANCELADO")
            {
                return BadRequest(new { mensagem = "Este registro já está cancelado." });
            }

            if (statusAtual == "FINALIZADO")
            {
                return BadRequest(new { mensagem = "Não é possível cancelar um pedido finalizado." });
            }
        }

        if (status == "FINALIZADO")
        {
            if (tipoAtual != "PEDIDO")
            {
                return BadRequest(new { mensagem = "Somente pedidos podem ser finalizados. Orçamentos devem ser convertidos em pedido primeiro." });
            }

            if (statusAtual == "CANCELADO")
            {
                return BadRequest(new { mensagem = "Não é possível finalizar um pedido cancelado." });
            }

            if (statusAtual == "FINALIZADO")
            {
                return BadRequest(new { mensagem = "Este pedido já está finalizado." });
            }

            if (saldoDevedor > 0)
            {
                return BadRequest(new { mensagem = "Não é possível finalizar pedido com saldo devedor em aberto." });
            }
        }

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
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0
            ? NotFound(new { mensagem = $"{FormatarTipo(tipoAtual)} não encontrado." })
            : NoContent();
    }

    private async Task<IActionResult> CancelarPedido(long id, AlterarStatusPedidoRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await GarantirEstruturaCancelamento(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var consulta = connection.CreateCommand();
        consulta.Transaction = transaction;
        consulta.CommandText = """
            SELECT tipo, status, valor_pago
            FROM pedidos
            WHERE id = @id
            LIMIT 1
            FOR UPDATE;
            """;
        consulta.Parameters.AddWithValue("@id", id);

        await using var reader = await consulta.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotFound(new { mensagem = "Pedido ou orçamento não encontrado." });
        }

        var tipoAtual = reader.GetString("tipo");
        var statusAtual = reader.GetString("status");
        var valorPago = reader.GetDecimal("valor_pago");
        await reader.DisposeAsync();

        if (string.IsNullOrWhiteSpace(request.Observacao) || request.Observacao.Trim().Length < 10)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Informe o motivo do cancelamento com pelo menos 10 caracteres." });
        }

        if (statusAtual == "CANCELADO")
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Este registro já está cancelado." });
        }

        if (statusAtual == "FINALIZADO")
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Não é possível cancelar um pedido finalizado." });
        }

        if (request.ValorDevolvido < 0 || request.ValorDevolvido > valorPago)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "O valor devolvido deve estar entre zero e o valor pago pelo cliente." });
        }

        if (request.ValorDevolvido > 0 && string.IsNullOrWhiteSpace(request.FormaDevolucao))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Informe a forma da devolução ao cliente." });
        }

        if (request.ValorDevolvido > 0 && request.ValorDevolvido < valorPago && string.IsNullOrWhiteSpace(request.ObservacaoEstorno))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Informe uma observação explicando a devolução parcial." });
        }

        await using var command = connection.CreateCommand();
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
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (request.ValorDevolvido > 0)
        {
            await using var caixa = connection.CreateCommand();
            caixa.Transaction = transaction;
            caixa.CommandText = """
                INSERT INTO caixa_movimentacoes
                    (pedido_id, tipo, forma_pagamento, categoria, descricao, valor, usuario_id, observacao)
                VALUES
                    (@pedidoId, 'SAIDA', @formaPagamento, 'Estorno de pedido', @descricao, @valor, @usuarioId, @observacao);
                """;
            caixa.Parameters.AddWithValue("@pedidoId", id);
            caixa.Parameters.AddWithValue("@formaPagamento", NormalizarFormaPagamento(request.FormaDevolucao));
            caixa.Parameters.AddWithValue("@descricao", $"Devolução do {FormatarTipo(tipoAtual).ToLowerInvariant()} cancelado");
            caixa.Parameters.AddWithValue("@valor", request.ValorDevolvido);
            caixa.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
            caixa.Parameters.AddWithValue("@observacao", ToDb(request.ObservacaoEstorno ?? request.Observacao));
            await caixa.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult> RegistrarNovoEstorno(long id, EstornarPedidoRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await GarantirEstruturaCancelamento(connection, cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var consulta = connection.CreateCommand();
        consulta.Transaction = transaction;
        consulta.CommandText = """
            SELECT tipo, status, valor_pago, valor_estornado
            FROM pedidos
            WHERE id = @id
            LIMIT 1
            FOR UPDATE;
            """;
        consulta.Parameters.AddWithValue("@id", id);

        await using var reader = await consulta.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotFound(new { mensagem = "Pedido nao encontrado." });
        }

        var tipoAtual = reader.GetString("tipo");
        var statusAtual = reader.GetString("status");
        var valorPago = reader.GetDecimal("valor_pago");
        var valorEstornadoAtual = reader.GetDecimal("valor_estornado");
        await reader.DisposeAsync();

        if (tipoAtual != "PEDIDO")
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Somente pedidos cancelados podem receber devolucao complementar." });
        }

        if (statusAtual != "CANCELADO")
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "A devolucao complementar so pode ser registrada para pedido cancelado." });
        }

        var valorRetido = valorPago - valorEstornadoAtual;
        if (valorRetido <= 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Este pedido nao possui valor retido para devolver." });
        }

        if (request.ValorDevolvido <= 0 || request.ValorDevolvido > valorRetido)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = $"O valor devolvido deve ser maior que zero e no maximo {valorRetido:C}." });
        }

        if (string.IsNullOrWhiteSpace(request.FormaDevolucao))
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Informe a forma da devolucao ao cliente." });
        }

        if (string.IsNullOrWhiteSpace(request.Observacao) || request.Observacao.Trim().Length < 10)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Informe uma observacao da devolucao com pelo menos 10 caracteres." });
        }

        await using var caixa = connection.CreateCommand();
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

        await using var pedido = connection.CreateCommand();
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
        await pedido.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult> FinalizarPedido(long id, FinalizarPedidoRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var consulta = connection.CreateCommand();
        consulta.Transaction = transaction;
        consulta.CommandText = "SELECT tipo, status, valor_pago, saldo_devedor FROM pedidos WHERE id = @id LIMIT 1 FOR UPDATE;";
        consulta.Parameters.AddWithValue("@id", id);

        await using var reader = await consulta.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotFound(new { mensagem = "Pedido não encontrado." });
        }

        var tipoAtual = reader.GetString("tipo");
        var statusAtual = reader.GetString("status");
        var valorPago = reader.GetDecimal("valor_pago");
        var saldoDevedor = reader.GetDecimal("saldo_devedor");
        await reader.DisposeAsync();

        if (tipoAtual != "PEDIDO")
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Somente pedidos podem ser finalizados. Orçamentos devem ser convertidos em pedido primeiro." });
        }

        if (statusAtual == "CANCELADO")
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Não é possível finalizar um pedido cancelado." });
        }

        if (statusAtual == "FINALIZADO")
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(new { mensagem = "Este pedido já está finalizado." });
        }

        if (saldoDevedor > 0)
        {
            if (!request.ReceberSaldo)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Não é possível finalizar pedido com saldo devedor em aberto." });
            }

            var formaPagamento = NormalizarFormaPagamento(request.FormaPagamento);
            if (string.IsNullOrWhiteSpace(formaPagamento))
            {
                await transaction.RollbackAsync(cancellationToken);
                return BadRequest(new { mensagem = "Informe a forma de pagamento para receber o saldo devedor." });
            }

            await using var pagamento = connection.CreateCommand();
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
        }

        await using var finalizar = connection.CreateCommand();
        finalizar.Transaction = transaction;
        finalizar.CommandText = """
            UPDATE pedidos
            SET status = 'FINALIZADO',
                finalizado_por_usuario_id = @usuarioId,
                finalizado_em = CURRENT_TIMESTAMP,
                valor_pago = @valorPago,
                saldo_devedor = 0
            WHERE id = @id;
            """;
        finalizar.Parameters.AddWithValue("@id", id);
        finalizar.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
        finalizar.Parameters.AddWithValue("@valorPago", valorPago + saldoDevedor);
        await finalizar.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private static async Task<IActionResult> CriarRespostaEdicaoOrcamentoNaoPermitida(
        MySqlConnection connection,
        long id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT tipo, status FROM pedidos WHERE id = @id LIMIT 1;";
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new NotFoundObjectResult(new { mensagem = "Pedido ou orçamento não encontrado." });
        }

        var tipo = reader.GetString("tipo");
        var status = reader.GetString("status");
        if (tipo == "PEDIDO")
        {
            return new BadRequestObjectResult(new
            {
                mensagem = "Não é permitido transformar um pedido em orçamento. Se o cliente desistiu, use a opção Cancelar."
            });
        }

        return new BadRequestObjectResult(new { mensagem = $"Não foi possível editar este orçamento porque ele está com status {FormatarStatus(status)}." });
    }

    private static string FormatarTipo(string tipo)
    {
        return tipo == "ORCAMENTO" ? "Orçamento" : "Pedido";
    }

    private static string FormatarStatus(string status)
    {
        return status switch
        {
            "ORCADO" => "orçado",
            "ABERTO" => "aberto",
            "FINALIZADO" => "finalizado",
            "CANCELADO" => "cancelado",
            _ => status.ToLowerInvariant()
        };
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
    [Required(ErrorMessage = "Informe o número do pedido ou orçamento.")]
    string Numero,
    long ClienteId,
    [Required(ErrorMessage = "Informe o nome do cliente.")]
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
    [Required(ErrorMessage = "Informe a descrição do item.")]
    string Descricao,
    string? Tamanho,
    [Range(1, int.MaxValue, ErrorMessage = "A quantidade do item deve ser maior que zero.")]
    int Quantidade,
    [Range(0.01, double.MaxValue, ErrorMessage = "O valor unitário deve ser maior que zero.")]
    decimal ValorUnitario,
    [Range(0.01, double.MaxValue, ErrorMessage = "O valor total do item deve ser maior que zero.")]
    decimal ValorTotal);

public sealed record ConverterPedidoRequest(
    long UsuarioId,
    string FormaPagamento,
    string CondicaoPagamento,
    decimal ValorEntrada);

public sealed record AlterarStatusPedidoRequest(
    [Range(1, long.MaxValue, ErrorMessage = "Informe o usuário responsável pela alteração.")]
    long UsuarioId,
    [MinLength(10, ErrorMessage = "Informe o motivo do cancelamento com pelo menos 10 caracteres.")]
    string? Observacao,
    decimal ValorDevolvido,
    string? FormaDevolucao,
    string? ObservacaoEstorno);

public sealed record EstornarPedidoRequest(
    [Range(1, long.MaxValue, ErrorMessage = "Informe o usuario responsavel pela devolucao.")]
    long UsuarioId,
    [Range(0.01, double.MaxValue, ErrorMessage = "Informe um valor de devolucao maior que zero.")]
    decimal ValorDevolvido,
    [Required(ErrorMessage = "Informe a forma da devolucao.")]
    string FormaDevolucao,
    [MinLength(10, ErrorMessage = "Informe uma observacao da devolucao com pelo menos 10 caracteres.")]
    string Observacao);

public sealed record FinalizarPedidoRequest(
    [Range(1, long.MaxValue, ErrorMessage = "Informe o usuário responsável pela finalização.")]
    long UsuarioId,
    string? Observacao,
    bool ReceberSaldo,
    string? FormaPagamento);

