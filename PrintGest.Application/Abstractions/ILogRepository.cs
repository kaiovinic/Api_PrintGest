using PrintGest.Domain.Entities;

namespace PrintGest.Application.Abstractions;

public interface ILogRepository
{
    Task<ResultadoPaginado<LogSistema>> ListAsync(
        string? entidade,
        long? entidadeId,
        DateOnly? dataInicio,
        DateOnly? dataFinal,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default);

    Task<long> CreateAsync(LogSistema log, CancellationToken cancellationToken = default);
}
