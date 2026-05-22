using System.Data.Common;
using MySqlConnector;
using PrintGest.Application.Abstractions;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class EstoqueRepository(IUnitOfWork unitOfWork) : IEstoqueRepository
{
    public async Task<ResultadoPaginado<ProdutoEstoqueDto>> ListarProdutosAsync(int pagina, int tamanhoPagina, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaEstoque(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        var paging = NormalizarPaginacao(pagina, tamanhoPagina);

        var total = await ContarAsync(connection, (MySqlTransaction?)unitOfWork.Transaction, "produtos_estoque", cancellationToken);
        var produtos = new List<ProdutoEstoqueDto>();
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT id, nome, categoria, tamanho, unidade, quantidade_atual, estoque_minimo,
                   custo_unitario, fornecedor, observacao, ativo
            FROM produtos_estoque
            ORDER BY nome
            LIMIT @limit OFFSET @offset;
            """;
        command.Parameters.AddWithValue("@limit", paging.TamanhoPagina);
        command.Parameters.AddWithValue("@offset", paging.Offset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var quantidadeAtual = reader.GetInt32("quantidade_atual");
            var custoUnitario = reader.GetDecimal("custo_unitario");
            produtos.Add(new ProdutoEstoqueDto(
                reader.GetInt64("id"),
                reader.GetString("nome"),
                reader.GetString("categoria"),
                reader.NullableString("tamanho"),
                reader.GetString("unidade"),
                quantidadeAtual,
                reader.GetInt32("estoque_minimo"),
                custoUnitario,
                quantidadeAtual * custoUnitario,
                reader.NullableString("fornecedor"),
                reader.NullableString("observacao"),
                reader.GetBoolean("ativo")));
        }

        return CriarResultado(produtos, total, paging.Pagina, paging.TamanhoPagina);
    }

    public async Task<long> CriarProdutoAsync(ProdutoEstoqueRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaEstoque(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            INSERT INTO produtos_estoque
                (nome, categoria, tamanho, unidade, quantidade_atual, estoque_minimo, custo_unitario, fornecedor, observacao)
            VALUES
                (@nome, @categoria, @tamanho, @unidade, 0, @estoqueMinimo, 0, @fornecedor, @observacao);
            SELECT LAST_INSERT_ID();
            """;
        PreencherProduto(command, request);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<bool> EditarProdutoAsync(long id, ProdutoEstoqueRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaEstoque(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            UPDATE produtos_estoque
            SET nome = @nome,
                categoria = @categoria,
                tamanho = @tamanho,
                unidade = @unidade,
                estoque_minimo = @estoqueMinimo,
                fornecedor = @fornecedor,
                observacao = @observacao
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        PreencherProduto(command, request);
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<CategoriaEstoqueDto>> ListarCategoriasAsync(CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaEstoque(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT nome
            FROM categorias_estoque
            WHERE ativo = TRUE
            UNION
            SELECT DISTINCT categoria AS nome
            FROM produtos_estoque
            WHERE categoria IS NOT NULL AND categoria <> ''
            ORDER BY nome;
            """;
        var categorias = new List<CategoriaEstoqueDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) categorias.Add(new CategoriaEstoqueDto(reader.GetString("nome")));
        return categorias;
    }

    public async Task CriarCategoriaAsync(CategoriaEstoqueRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaEstoque(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            INSERT INTO categorias_estoque (nome)
            VALUES (@nome)
            ON DUPLICATE KEY UPDATE ativo = TRUE;
            """;
        command.Parameters.AddWithValue("@nome", request.Nome.Trim());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RegistrarMovimentacaoAsync(MovimentacaoEstoqueRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaEstoque(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);

        var transacaoCriada = false;
        var transaction = unitOfWork.Transaction;
        if (transaction == null)
        {
            transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
            transacaoCriada = true;
        }

        try
        {
            if (request.PedidoId is not null)
            {
                await using var pedido = (MySqlCommand)connection.CreateCommand();
                pedido.Transaction = (MySqlTransaction)transaction;
                pedido.CommandText = "SELECT COUNT(*) FROM pedidos WHERE id = @pedidoId;";
                pedido.Parameters.AddWithValue("@pedidoId", request.PedidoId.Value);
                var pedidoExiste = Convert.ToInt32(await pedido.ExecuteScalarAsync(cancellationToken)) > 0;
                if (!pedidoExiste)
                {
                    throw new InvalidOperationException("Pedido vinculado nao encontrado. Informe um pedido existente ou deixe o campo vazio.");
                }
            }

            await using var insert = (MySqlCommand)connection.CreateCommand();
            insert.Transaction = (MySqlTransaction)transaction;
            insert.CommandText = """
                INSERT INTO movimentacoes_estoque (produto_id, pedido_id, usuario_id, tipo, quantidade, custo_unitario, observacao)
                VALUES (@produtoId, @pedidoId, @usuarioId, @tipo, @quantidade, @custoUnitario, @observacao);
                """;
            insert.Parameters.AddWithValue("@produtoId", request.ProdutoId);
            insert.Parameters.AddWithValue("@pedidoId", ToDb(request.PedidoId));
            insert.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
            insert.Parameters.AddWithValue("@tipo", request.Tipo);
            insert.Parameters.AddWithValue("@quantidade", request.Quantidade);
            insert.Parameters.AddWithValue("@custoUnitario", ToDb(request.CustoUnitario));
            insert.Parameters.AddWithValue("@observacao", ToDb(request.Observacao));
            await insert.ExecuteNonQueryAsync(cancellationToken);

            await using var update = (MySqlCommand)connection.CreateCommand();
            update.Transaction = (MySqlTransaction)transaction;
            update.CommandText = request.Tipo.ToUpperInvariant() == "ENTRADA"
                ? """
                  UPDATE produtos_estoque
                  SET custo_unitario =
                        CASE
                          WHEN quantidade_atual + @quantidade <= 0 THEN @custoUnitario
                          ELSE ((quantidade_atual * custo_unitario) + (@quantidade * @custoUnitario)) / (quantidade_atual + @quantidade)
                        END,
                      quantidade_atual = quantidade_atual + @quantidade
                  WHERE id = @produtoId;
                  """
                : "UPDATE produtos_estoque SET quantidade_atual = quantidade_atual - @quantidade WHERE id = @produtoId;";
            update.Parameters.AddWithValue("@produtoId", request.ProdutoId);
            update.Parameters.AddWithValue("@quantidade", request.Quantidade);
            update.Parameters.AddWithValue("@custoUnitario", request.CustoUnitario ?? 0);
            await update.ExecuteNonQueryAsync(cancellationToken);

            if (transacaoCriada)
            {
                await unitOfWork.CommitAsync(cancellationToken);
            }
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

    public async Task<ResultadoPaginado<MovimentacaoEstoqueDto>> ListarMovimentacoesAsync(int pagina, int tamanhoPagina, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await GarantirEstruturaEstoque(connection, (MySqlTransaction?)unitOfWork.Transaction, cancellationToken);
        var paging = NormalizarPaginacao(pagina, tamanhoPagina);
        var total = await ContarAsync(connection, (MySqlTransaction?)unitOfWork.Transaction, "movimentacoes_estoque", cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT m.id, m.tipo, m.quantidade, m.custo_unitario, m.movimentado_em, m.observacao,
                   p.nome AS produto, p.tamanho AS tamanho, u.nome AS usuario, m.pedido_id
            FROM movimentacoes_estoque m
            INNER JOIN produtos_estoque p ON p.id = m.produto_id
            INNER JOIN usuarios u ON u.id = m.usuario_id
            ORDER BY m.movimentado_em DESC
            LIMIT @limit OFFSET @offset;
            """;
        command.Parameters.AddWithValue("@limit", paging.TamanhoPagina);
        command.Parameters.AddWithValue("@offset", paging.Offset);

        var movimentacoes = new List<MovimentacaoEstoqueDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var custoOrdinal = reader.GetOrdinal("custo_unitario");
            var pedidoOrdinal = reader.GetOrdinal("pedido_id");
            var custo = reader.IsDBNull(custoOrdinal) ? (decimal?)null : reader.GetDecimal(custoOrdinal);
            var quantidade = reader.GetInt32("quantidade");
            movimentacoes.Add(new MovimentacaoEstoqueDto(
                reader.GetInt64("id"),
                reader.GetString("tipo"),
                quantidade,
                custo,
                custo is null ? null : quantidade * custo.Value,
                reader.GetString("produto"),
                reader.NullableString("tamanho"),
                reader.GetString("usuario"),
                reader.IsDBNull(pedidoOrdinal) ? null : reader.GetInt64(pedidoOrdinal),
                reader.GetDateTime("movimentado_em"),
                reader.NullableString("observacao")));
        }

        return CriarResultado(movimentacoes, total, paging.Pagina, paging.TamanhoPagina);
    }

    private static void PreencherProduto(MySqlCommand command, ProdutoEstoqueRequest request)
    {
        command.Parameters.AddWithValue("@nome", request.Nome.Trim());
        command.Parameters.AddWithValue("@categoria", request.Categoria.Trim());
        command.Parameters.AddWithValue("@tamanho", ToDb(request.Tamanho));
        command.Parameters.AddWithValue("@unidade", request.Unidade.Trim());
        command.Parameters.AddWithValue("@estoqueMinimo", request.EstoqueMinimo);
        command.Parameters.AddWithValue("@fornecedor", ToDb(request.Fornecedor));
        command.Parameters.AddWithValue("@observacao", ToDb(request.Observacao));
    }

    private static async Task<int> ContarAsync(DbConnection connection, MySqlTransaction? transaction, string tabela, CancellationToken cancellationToken)
    {
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM {tabela};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task GarantirEstruturaEstoque(DbConnection connection, MySqlTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var categorias = (MySqlCommand)connection.CreateCommand();
        categorias.Transaction = transaction;
        categorias.CommandText = """
            CREATE TABLE IF NOT EXISTS categorias_estoque (
                id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                nome VARCHAR(120) NOT NULL UNIQUE,
                ativo BOOLEAN NOT NULL DEFAULT TRUE,
                criado_em DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """;
        await categorias.ExecuteNonQueryAsync(cancellationToken);

        await GarantirColuna(connection, transaction, "movimentacoes_estoque", "custo_unitario", "ALTER TABLE movimentacoes_estoque ADD COLUMN custo_unitario DECIMAL(10,2) NULL AFTER quantidade;", cancellationToken);
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

    private static object ToDb<T>(T? value) => value is null || value is string text && string.IsNullOrWhiteSpace(text) ? DBNull.Value : value;
}
