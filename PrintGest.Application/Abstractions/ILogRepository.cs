using PrintGest.Domain.Entities;

namespace PrintGest.Application.Abstractions;

public interface ILogRepository
{
    Task<IReadOnlyList<LogSistema>> ListAsync(
        string? entidade,
        long? entidadeId,
        DateOnly? dataInicio,
        DateOnly? dataFinal,
        CancellationToken cancellationToken = default);

    Task<long> CreateAsync(LogSistema log, CancellationToken cancellationToken = default);
}
