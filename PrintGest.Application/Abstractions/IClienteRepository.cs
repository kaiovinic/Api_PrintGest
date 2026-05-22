using PrintGest.Domain.Entities;

namespace PrintGest.Application.Abstractions;

public interface IClienteRepository
{
    Task<IReadOnlyList<Cliente>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Cliente>> SearchAsync(string termo, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(Cliente cliente, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(long id, Cliente cliente, CancellationToken cancellationToken = default);
    Task<Cliente?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
}
