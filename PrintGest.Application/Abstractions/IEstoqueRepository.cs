namespace PrintGest.Application.Abstractions;

public interface IEstoqueRepository
{
    Task<ResultadoPaginado<ProdutoEstoqueDto>> ListarProdutosAsync(int pagina, int tamanhoPagina, CancellationToken cancellationToken = default);
    Task<long> CriarProdutoAsync(ProdutoEstoqueRequest request, CancellationToken cancellationToken = default);
    Task<bool> EditarProdutoAsync(long id, ProdutoEstoqueRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CategoriaEstoqueDto>> ListarCategoriasAsync(CancellationToken cancellationToken = default);
    Task CriarCategoriaAsync(CategoriaEstoqueRequest request, CancellationToken cancellationToken = default);
    Task RegistrarMovimentacaoAsync(MovimentacaoEstoqueRequest request, CancellationToken cancellationToken = default);
    Task<ResultadoPaginado<MovimentacaoEstoqueDto>> ListarMovimentacoesAsync(int pagina, int tamanhoPagina, CancellationToken cancellationToken = default);
}

public sealed record ProdutoEstoqueDto(
    long Id,
    string Nome,
    string Categoria,
    string? Tamanho,
    string Unidade,
    int QuantidadeAtual,
    int EstoqueMinimo,
    decimal CustoUnitario,
    decimal TotalEstoque,
    string? Fornecedor,
    string? Observacao,
    bool Ativo);

public sealed record CategoriaEstoqueDto(string Nome);

public sealed record MovimentacaoEstoqueDto(
    long Id,
    string Tipo,
    int Quantidade,
    decimal? CustoUnitario,
    decimal? Total,
    string Produto,
    string? Tamanho,
    string Usuario,
    long? PedidoId,
    DateTime MovimentadoEm,
    string? Observacao);

public sealed record ProdutoEstoqueRequest(
    string Nome,
    string Categoria,
    string? Tamanho,
    string Unidade,
    int? QuantidadeAtual,
    int EstoqueMinimo,
    decimal? CustoUnitario,
    string? Fornecedor,
    string? Observacao);

public sealed record CategoriaEstoqueRequest(string Nome);

public sealed record MovimentacaoEstoqueRequest(
    long ProdutoId,
    long? PedidoId,
    long UsuarioId,
    string Tipo,
    int Quantidade,
    decimal? CustoUnitario,
    string? Observacao,
    string? NomeProduto = null);
