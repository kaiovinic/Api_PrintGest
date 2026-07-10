namespace PrintGest.Application.Abstractions;

public interface ICaixaRepository
{
    Task<CaixaResumoDto> ObterResumoAsync(DateOnly? inicio, DateOnly? fim, CancellationToken cancellationToken = default);
    Task<ResultadoPaginado<CaixaMovimentacaoDto>> ListarMovimentacoesAsync(DateOnly? inicio, DateOnly? fim, int pagina, int tamanhoPagina, CancellationToken cancellationToken = default);
    Task<long> CriarMovimentacaoAsync(CaixaMovimentacaoRequest request, CancellationToken cancellationToken = default);
}

public sealed record CaixaResumoDto(
    decimal Entradas,
    decimal Saidas,
    decimal Saldo,
    decimal Dinheiro,
    decimal Pix,
    decimal CartaoCredito,
    decimal CartaoDebito);

public sealed record CaixaMovimentacaoDto(
    string Id,
    long? PedidoId,
    string Tipo,
    string FormaPagamento,
    string Categoria,
    string Descricao,
    decimal Valor,
    DateTime MovimentadoEm,
    string Usuario,
    string? Observacao,
    string Origem);

public sealed record CaixaMovimentacaoRequest(
    long UsuarioId,
    long? PedidoId,
    string Tipo,
    string FormaPagamento,
    string Categoria,
    string Descricao,
    decimal Valor,
    string? Observacao);
