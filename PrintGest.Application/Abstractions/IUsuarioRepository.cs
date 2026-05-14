using PrintGest.Domain.Entities;

namespace PrintGest.Application.Abstractions;

public interface IUsuarioRepository
{
    Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Usuario>> ListAsync(CancellationToken cancellationToken = default);
}
