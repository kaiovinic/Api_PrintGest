using Microsoft.EntityFrameworkCore;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class ClienteRepository(PrintGestDbContext context) : IClienteRepository
{
    public async Task<IReadOnlyList<Cliente>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await context.Clientes
            .AsNoTracking()
            .OrderBy(c => c.Nome)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Cliente>> SearchAsync(string termo, CancellationToken cancellationToken = default)
    {
        return await context.Clientes
            .AsNoTracking()
            .Where(c => c.Nome.Contains(termo) || 
                        (c.CpfCnpj != null && c.CpfCnpj.Contains(termo)) || 
                        (c.Telefone != null && c.Telefone.Contains(termo)))
            .OrderBy(c => c.Nome)
            .Take(20)
            .ToListAsync(cancellationToken);
    }

    public async Task<long> CreateAsync(Cliente cliente, CancellationToken cancellationToken = default)
    {
        context.Clientes.Add(cliente);
        await context.SaveChangesAsync(cancellationToken);
        return cliente.Id;
    }

    public async Task<bool> UpdateAsync(long id, Cliente cliente, CancellationToken cancellationToken = default)
    {
        var existing = await context.Clientes.FindAsync(new object[] { id }, cancellationToken);
        if (existing == null)
        {
            return false;
        }

        var updated = cliente with { Id = id };
        context.Entry(existing).CurrentValues.SetValues(updated);
        return await context.SaveChangesAsync(cancellationToken) > 0;
    }

    public async Task<Cliente?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await context.Clientes
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }
}
