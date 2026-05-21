using PrintGest.Domain.Entities;

namespace PrintGest.Application.Abstractions;

public interface IPedidoRepository
{
    Task<ResultadoPaginado<PedidoResumo>> ListAsync(PedidoFiltro filtro, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PedidoResumo>> ListRecentAsync(CancellationToken cancellationToken = default);
}

public sealed record PedidoFiltro(int? Ano, int? Mes, DateOnly? Inicio, DateOnly? Fim, string? Status, int Pagina = 1, int TamanhoPagina = 10);

public sealed record ResultadoPaginado<T>(
    IReadOnlyList<T> Itens,
    int Total,
    int Pagina,
    int TamanhoPagina,
    int TotalPaginas);
