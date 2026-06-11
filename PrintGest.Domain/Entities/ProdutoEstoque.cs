namespace PrintGest.Domain.Entities;

public sealed record ProdutoEstoque(
    long Id,
    string Nome,
    string Categoria,
    string? Tamanho,
    string Unidade,
    int QuantidadeAtual,
    int EstoqueMinimo,
    decimal CustoUnitario,
    string? Fornecedor,
    string? Observacao,
    bool Ativo);
