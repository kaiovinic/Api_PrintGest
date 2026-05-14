using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;

namespace PrintGest.Application.Services;

public sealed class UsuarioService(IUsuarioRepository usuarios)
{
    public Task<IReadOnlyList<Usuario>> ListAsync(CancellationToken cancellationToken = default)
    {
        return usuarios.ListAsync(cancellationToken);
    }
}
