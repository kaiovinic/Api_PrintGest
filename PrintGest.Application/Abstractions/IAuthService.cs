using PrintGest.Application.Contracts.Auth;

namespace PrintGest.Application.Abstractions;

public interface IAuthService
{
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
}
