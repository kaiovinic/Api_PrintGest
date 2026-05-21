namespace PrintGest.Domain.Entities;

public sealed record LogSistema(
    long Id,
    long UsuarioId,
    string? Usuario,
    string Entidade,
    long EntidadeId,
    string Acao,
    string? Descricao,
    DateTime CriadoEm);
