namespace PrintGest.Domain.Entities;

public sealed record Pedido(
    long Id,
    string Numero,
    long ClienteId,
    long CriadoPorUsuarioId,
    string Tipo,
    string Status,
    DateOnly DataPedido,
    DateOnly? DataEntrega,
    string? Vendedor,
    string? FormaPagamento,
    string? CondicaoPagamento,
    string? Frente,
    string? Fundo,
    string? TamanhosMasculinos,
    string? TamanhosFemininos,
    string? Observacao,
    string? MotivoCancelamento,
    decimal ValorEstornado,
    decimal Subtotal,
    decimal Total,
    decimal ValorPago,
    decimal SaldoDevedor,
    string? ObservacaoEstorno,
    long? CanceladoPorUsuarioId,
    DateTime? CanceladoEm,
    long? FinalizadoPorUsuarioId,
    DateTime? FinalizadoEm
)
{
    public ICollection<ItemPedido> Itens { get; init; } = new List<ItemPedido>();
    public ICollection<Pagamento> Pagamentos { get; init; } = new List<Pagamento>();
}
