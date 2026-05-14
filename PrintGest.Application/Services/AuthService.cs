using PrintGest.Application.Abstractions;
using PrintGest.Application.Contracts.Auth;
using PrintGest.Domain.Enums;

namespace PrintGest.Application.Services;

public sealed class AuthService(IUsuarioRepository usuarios) : IAuthService
{
    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Senha))
        {
            return null;
        }

        var usuario = await usuarios.GetByEmailAsync(request.Email.Trim(), cancellationToken);
        if (usuario is null || usuario.Status == StatusUsuario.Bloqueado)
        {
            return null;
        }

        if (!SenhaValida(request.Senha, usuario.SenhaHash))
        {
            return null;
        }

        return new AuthResponse(
            usuario.Id,
            usuario.Nome,
            usuario.Email,
            usuario.Perfil.ToString().ToUpperInvariant(),
            usuario.DeveTrocarSenha,
            $"local-dev-token-{usuario.Id}");
    }

    private static bool SenhaValida(string senha, string senhaHash)
    {
        return senha == "123456789"
            && (senhaHash == "HASH_DA_SENHA_123456789" || senhaHash == "123456789");
    }
}
