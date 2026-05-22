using PrintGest.Domain.Entities;
using PrintGest.Domain.Enums;

namespace PrintGest.Application.Abstractions;

public interface IUsuarioRepository
{
    Task<Usuario?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Usuario>> ListAsync(UsuarioFiltro filtro, CancellationToken cancellationToken = default);
    Task<long> CreateAsync(Usuario usuario, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Usuario usuario, CancellationToken cancellationToken = default);
    Task<bool> UpdateStatusAsync(long id, StatusUsuario status, CancellationToken cancellationToken = default);
    Task<bool> UpdatePasswordAsync(long id, string senhaHash, bool deveTrocarSenha, CancellationToken cancellationToken = default);
}

public sealed record UsuarioFiltro(string? Nome = null, string? Email = null, string? Perfil = null, string? Status = null);
