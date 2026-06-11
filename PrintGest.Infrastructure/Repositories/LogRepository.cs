using Microsoft.EntityFrameworkCore;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class LogRepository(PrintGestDbContext context) : ILogRepository
{
    public async Task<ResultadoPaginado<LogSistema>> ListAsync(
        string? entidade,
        long? entidadeId,
        DateOnly? dataInicio,
        DateOnly? dataFinal,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default)
    {
        var paginaAtual = Math.Max(pagina, 1);
        var tamanho = Math.Clamp(tamanhoPagina, 5, 100);
        var offset = (paginaAtual - 1) * tamanho;

        var query = from log in context.LogsSistema
                    join user in context.Usuarios on log.UsuarioId equals user.Id into userGroup
                    from user in userGroup.DefaultIfEmpty()
                    select new { log, UsuarioNome = user != null ? user.Nome : null };

        if (!string.IsNullOrWhiteSpace(entidade))
        {
            query = query.Where(q => q.log.Entidade == entidade);
        }

        if (entidadeId.HasValue)
        {
            query = query.Where(q => q.log.EntidadeId == entidadeId.Value);
        }

        if (dataInicio.HasValue)
        {
            var dtInicio = dataInicio.Value.ToDateTime(TimeOnly.MinValue);
            query = query.Where(q => q.log.CriadoEm >= dtInicio);
        }

        if (dataFinal.HasValue)
        {
            var dtFinal = dataFinal.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(q => q.log.CriadoEm <= dtFinal);
        }

        var total = await query.CountAsync(cancellationToken);
        var totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)tamanho);

        var items = await query.OrderByDescending(q => q.log.CriadoEm)
                              .Skip(offset)
                              .Take(tamanho)
                              .ToListAsync(cancellationToken);

        var logs = items.Select(i => i.log with { Usuario = i.UsuarioNome }).ToList();

        return new ResultadoPaginado<LogSistema>(logs, total, paginaAtual, tamanho, totalPaginas);
    }

    public async Task<long> CreateAsync(LogSistema log, CancellationToken cancellationToken = default)
    {
        // Enforce setting correct timestamp if not set or just insert as-is
        var logToInsert = log.CriadoEm == default ? log with { CriadoEm = DateTime.UtcNow } : log;
        context.LogsSistema.Add(logToInsert);
        await context.SaveChangesAsync(cancellationToken);
        return logToInsert.Id;
    }
}
