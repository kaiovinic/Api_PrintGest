namespace PrintGest.Domain.Entities;

public sealed record ItemPedido(
    long Id,
    long PedidoId,
    string Descricao,
    string? Tamanho,
    int Quantidade,
    decimal ValorUnitario,
    decimal ValorTotal);
