namespace PrintGest.Application.Abstractions;

public interface IDatabaseHealthCheck
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
}
