namespace PrintGest.Application.Contracts.Auth;

public sealed record CancelarMovimentacaoRequest(
    long UsuarioId,
    string SupervisorEmail,
    string SupervisorSenha);
