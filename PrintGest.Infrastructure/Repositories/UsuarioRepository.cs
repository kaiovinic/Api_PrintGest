using Microsoft.EntityFrameworkCore;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Domain.Enums;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class UsuarioRepository(PrintGestDbContext context) : IUsuarioRepository
{
    public async Task<Usuario?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await context.Usuarios
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await context.Usuarios
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<IReadOnlyList<Usuario>> ListAsync(UsuarioFiltro filtro, CancellationToken cancellationToken = default)
    {
        var query = context.Usuarios.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filtro.Nome))
        {
            query = query.Where(u => u.Nome.Contains(filtro.Nome));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Email))
        {
            query = query.Where(u => u.Email.Contains(filtro.Email));
        }

        if (Enum.TryParse<PerfilUsuario>(filtro.Perfil, true, out var perfil))
        {
            query = query.Where(u => u.Perfil == perfil);
        }

        if (Enum.TryParse<StatusUsuario>(filtro.Status, true, out var status))
        {
            query = query.Where(u => u.Status == status);
        }

        return await query.OrderBy(u => u.Nome).ToListAsync(cancellationToken);
    }

    public async Task<long> CreateAsync(Usuario usuario, CancellationToken cancellationToken = default)
    {
        context.Usuarios.Add(usuario);
        await context.SaveChangesAsync(cancellationToken);
        return usuario.Id;
    }

    public async Task<bool> UpdateAsync(Usuario usuario, CancellationToken cancellationToken = default)
    {
        var existing = await context.Usuarios.FindAsync(new object[] { usuario.Id }, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        var updated = existing with
        {
            Nome = usuario.Nome,
            Email = usuario.Email,
            Telefone = usuario.Telefone,
            Perfil = usuario.Perfil
        };

        context.Entry(existing).CurrentValues.SetValues(updated);
        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<bool> UpdateStatusAsync(long id, StatusUsuario status, CancellationToken cancellationToken = default)
    {
        var existing = await context.Usuarios.FindAsync(new object[] { id }, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        var updated = existing with { Status = status };
        context.Entry(existing).CurrentValues.SetValues(updated);
        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<bool> UpdatePasswordAsync(long id, string senhaHash, bool deveTrocarSenha, CancellationToken cancellationToken = default)
    {
        var existing = await context.Usuarios.FindAsync(new object[] { id }, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        var updated = existing with { SenhaHash = senhaHash, DeveTrocarSenha = deveTrocarSenha };
        context.Entry(existing).CurrentValues.SetValues(updated);
        return await context.SaveChangesAsync(cancellationToken) > 0;
    }
}
