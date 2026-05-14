using PrintGest.Domain.Entities;

namespace PrintGest.Application.Abstractions;

public interface IPedidoRepository
{
    Task<IReadOnlyList<PedidoResumo>> ListRecentAsync(CancellationToken cancellationToken = default);
}
