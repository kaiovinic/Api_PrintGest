namespace PrintGest.Domain.Entities;

public sealed record Despesa(
    long Id,
    long CadastradoPorUsuarioId,
    string? GrupoDespesaId,
    int NumeroParcela,
    int TotalParcelas,
    string Categoria,
    string Descricao,
    decimal Valor,
    decimal ValorTotal,
    DateOnly Vencimento,
    string Status,
    DateOnly? DataPagamento,
    string? Observacao);
