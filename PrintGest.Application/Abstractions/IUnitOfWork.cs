using System.Data.Common;

namespace PrintGest.Application.Abstractions;

public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    Task<DbConnection> GetConnectionAsync(CancellationToken cancellationToken = default);
    DbTransaction? Transaction { get; }
    Task<DbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
