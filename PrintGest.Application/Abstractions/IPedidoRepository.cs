using PrintGest.Domain.Entities;

namespace PrintGest.Application.Abstractions;

public interface IPedidoRepository
{
    Task<IReadOnlyList<PedidoResumo>> ListAsync(PedidoFiltro filtro, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PedidoResumo>> ListRecentAsync(CancellationToken cancellationToken = default);
}

public sealed record PedidoFiltro(int? Ano, int? Mes, DateOnly? Inicio, DateOnly? Fim, string? Status);
