using PrintGest.Domain.Entities;

namespace PrintGest.Application.Abstractions;

public interface IClienteRepository
{
    Task<IReadOnlyList<Cliente>> ListAsync(CancellationToken cancellationToken = default);
}
