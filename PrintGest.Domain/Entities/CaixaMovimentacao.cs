namespace PrintGest.Domain.Entities;

public sealed record CaixaMovimentacao(
    long Id,
    long? PedidoId,
    string Tipo,
    string FormaPagamento,
    string Categoria,
    string Descricao,
    decimal Valor,
    long UsuarioId,
    string? Observacao,
    DateTime MovimentadoEm);
