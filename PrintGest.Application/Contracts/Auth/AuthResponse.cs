namespace PrintGest.Application.Contracts.Auth;

public sealed record AuthResponse(
    long UsuarioId,
    string Nome,
    string Email,
    string Perfil,
    bool DeveTrocarSenha,
    string AccessToken);
