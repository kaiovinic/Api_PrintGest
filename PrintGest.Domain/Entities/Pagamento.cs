namespace PrintGest.Domain.Entities;

public sealed record Pagamento(
    long Id,
    long PedidoId,
    long RegistradoPorUsuarioId,
    string FormaPagamento,
    string CondicaoPagamento,
    decimal ValorTotal,
    string? Observacao,
    DateTime RegistradoEm);
