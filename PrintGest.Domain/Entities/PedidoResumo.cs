using PrintGest.Domain.Enums;

namespace PrintGest.Domain.Entities;

public sealed record PedidoResumo(
    long Id,
    string Numero,
    string Cliente,
    TipoPedido Tipo,
    StatusPedido Status,
    DateOnly DataPedido,
    DateOnly? DataEntrega,
    decimal Total,
    decimal ValorPago,
    decimal ValorEstornado,
    decimal SaldoDevedor,
    string CriadoPor,
    string? MotivoCancelamento);

