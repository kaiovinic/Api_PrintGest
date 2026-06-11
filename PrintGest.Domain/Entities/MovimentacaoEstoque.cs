namespace PrintGest.Domain.Entities;

public sealed record MovimentacaoEstoque(
    long Id,
    long ProdutoId,
    long? PedidoId,
    long UsuarioId,
    string Tipo,
    int Quantidade,
    decimal? CustoUnitario,
    string? Observacao,
    DateTime MovimentadoEm);
