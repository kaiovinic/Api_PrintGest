using Microsoft.AspNetCore.Mvc;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/estoque")]
public sealed class EstoqueController(MySqlConnectionFactory factory) : ControllerBase
{
    [HttpGet("produtos")]
    public async Task<IActionResult> ListarProdutos(CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, nome, categoria, tamanho, unidade, quantidade_atual, estoque_minimo,
                   custo_unitario, fornecedor, observacao, ativo
            FROM produtos_estoque
            ORDER BY nome;
            """;

        var produtos = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            produtos.Add(new
            {
                Id = reader.GetInt64("id"),
                Nome = reader.GetString("nome"),
                Categoria = reader.GetString("categoria"),
                Tamanho = reader.NullableString("tamanho"),
                Unidade = reader.GetString("unidade"),
                QuantidadeAtual = reader.GetInt32("quantidade_atual"),
                EstoqueMinimo = reader.GetInt32("estoque_minimo"),
                CustoUnitario = reader.GetDecimal("custo_unitario"),
                Fornecedor = reader.NullableString("fornecedor"),
                Observacao = reader.NullableString("observacao"),
                Ativo = reader.GetBoolean("ativo")
            });
        }

        return Ok(produtos);
    }

    [HttpPost("produtos")]
    public async Task<IActionResult> CriarProduto([FromBody] ProdutoRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO produtos_estoque
                (nome, categoria, tamanho, unidade, quantidade_atual, estoque_minimo, custo_unitario, fornecedor, observacao)
            VALUES
                (@nome, @categoria, @tamanho, @unidade, @quantidadeAtual, @estoqueMinimo, @custoUnitario, @fornecedor, @observacao);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@nome", request.Nome);
        command.Parameters.AddWithValue("@categoria", request.Categoria);
        command.Parameters.AddWithValue("@tamanho", request.Tamanho);
        command.Parameters.AddWithValue("@unidade", request.Unidade);
        command.Parameters.AddWithValue("@quantidadeAtual", request.QuantidadeAtual);
        command.Parameters.AddWithValue("@estoqueMinimo", request.EstoqueMinimo);
        command.Parameters.AddWithValue("@custoUnitario", request.CustoUnitario);
        command.Parameters.AddWithValue("@fornecedor", request.Fornecedor);
        command.Parameters.AddWithValue("@observacao", request.Observacao);

        var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        return CreatedAtAction(nameof(ListarProdutos), new { id }, new { id });
    }

    [HttpPut("produtos/{id:long}")]
    public async Task<IActionResult> EditarProduto(long id, [FromBody] ProdutoRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE produtos_estoque
            SET nome = @nome,
                categoria = @categoria,
                tamanho = @tamanho,
                unidade = @unidade,
                estoque_minimo = @estoqueMinimo,
                custo_unitario = @custoUnitario,
                fornecedor = @fornecedor,
                observacao = @observacao
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@nome", request.Nome);
        command.Parameters.AddWithValue("@categoria", request.Categoria);
        command.Parameters.AddWithValue("@tamanho", request.Tamanho);
        command.Parameters.AddWithValue("@unidade", request.Unidade);
        command.Parameters.AddWithValue("@estoqueMinimo", request.EstoqueMinimo);
        command.Parameters.AddWithValue("@custoUnitario", request.CustoUnitario);
        command.Parameters.AddWithValue("@fornecedor", request.Fornecedor);
        command.Parameters.AddWithValue("@observacao", request.Observacao);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ? NotFound() : NoContent();
    }

    [HttpPost("movimentacoes")]
    public async Task<IActionResult> RegistrarMovimentacao([FromBody] MovimentacaoEstoqueRequest request, CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO movimentacoes_estoque (produto_id, pedido_id, usuario_id, tipo, quantidade, observacao)
            VALUES (@produtoId, @pedidoId, @usuarioId, @tipo, @quantidade, @observacao);
            """;
        insert.Parameters.AddWithValue("@produtoId", request.ProdutoId);
        insert.Parameters.AddWithValue("@pedidoId", request.PedidoId);
        insert.Parameters.AddWithValue("@usuarioId", request.UsuarioId);
        insert.Parameters.AddWithValue("@tipo", request.Tipo);
        insert.Parameters.AddWithValue("@quantidade", request.Quantidade);
        insert.Parameters.AddWithValue("@observacao", request.Observacao);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        await using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = request.Tipo.ToUpperInvariant() == "ENTRADA"
            ? "UPDATE produtos_estoque SET quantidade_atual = quantidade_atual + @quantidade WHERE id = @produtoId;"
            : "UPDATE produtos_estoque SET quantidade_atual = quantidade_atual - @quantidade WHERE id = @produtoId;";
        update.Parameters.AddWithValue("@produtoId", request.ProdutoId);
        update.Parameters.AddWithValue("@quantidade", request.Quantidade);
        await update.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Ok(new { mensagem = "Movimentação registrada com sucesso." });
    }

    [HttpGet("movimentacoes")]
    public async Task<IActionResult> ListarMovimentacoes(CancellationToken cancellationToken)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.id, m.tipo, m.quantidade, m.movimentado_em, m.observacao,
                   p.nome AS produto, u.nome AS usuario, m.pedido_id
            FROM movimentacoes_estoque m
            INNER JOIN produtos_estoque p ON p.id = m.produto_id
            INNER JOIN usuarios u ON u.id = m.usuario_id
            ORDER BY m.movimentado_em DESC
            LIMIT 50;
            """;

        var movimentacoes = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var pedidoOrdinal = reader.GetOrdinal("pedido_id");
            movimentacoes.Add(new
            {
                Id = reader.GetInt64("id"),
                Tipo = reader.GetString("tipo"),
                Quantidade = reader.GetInt32("quantidade"),
                Produto = reader.GetString("produto"),
                Usuario = reader.GetString("usuario"),
                PedidoId = reader.IsDBNull(pedidoOrdinal) ? (long?)null : reader.GetInt64(pedidoOrdinal),
                MovimentadoEm = reader.GetDateTime("movimentado_em"),
                Observacao = reader.NullableString("observacao")
            });
        }

        return Ok(movimentacoes);
    }
}

public sealed record ProdutoRequest(
    string Nome,
    string Categoria,
    string? Tamanho,
    string Unidade,
    int QuantidadeAtual,
    int EstoqueMinimo,
    decimal CustoUnitario,
    string? Fornecedor,
    string? Observacao);

public sealed record MovimentacaoEstoqueRequest(
    long ProdutoId,
    long? PedidoId,
    long UsuarioId,
    string Tipo,
    int Quantidade,
    string? Observacao);
