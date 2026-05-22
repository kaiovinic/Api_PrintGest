using System.Data.Common;
using Microsoft.Extensions.Configuration;
using MySqlConnector;
using PrintGest.Application.Abstractions;

namespace PrintGest.Infrastructure.Data;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly string _connectionString;
    private MySqlConnection? _connection;
    private MySqlTransaction? _transaction;
    private bool _disposed;

    public UnitOfWork(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("PrintGest")
            ?? throw new InvalidOperationException("Connection string 'PrintGest' não configurada.");
    }

    public async Task<DbConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_connection == null)
        {
            _connection = new MySqlConnection(_connectionString);
            await _connection.OpenAsync(cancellationToken);
        }
        else if (_connection.State == System.Data.ConnectionState.Closed)
        {
            await _connection.OpenAsync(cancellationToken);
        }

        return _connection;
    }

    public DbTransaction? Transaction => _transaction;

    public async Task<DbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_transaction != null)
        {
            return _transaction;
        }

        var connection = await GetConnectionAsync(cancellationToken);
        _transaction = await ((MySqlConnection)connection).BeginTransactionAsync(cancellationToken);
        return _transaction;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_transaction == null)
        {
            throw new InvalidOperationException("Nenhuma transação ativa para efetuar commit.");
        }

        try
        {
            await _transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_transaction == null)
        {
            return;
        }

        try
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }

    private async Task DisposeTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _transaction?.Dispose();
        _connection?.Dispose();
        _disposed = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_transaction != null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }

        _disposed = true;
    }
}
