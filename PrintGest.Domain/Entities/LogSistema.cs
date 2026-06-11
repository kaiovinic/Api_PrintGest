namespace PrintGest.Domain.Entities;

public sealed record LogSistema(
    long Id,
    long UsuarioId,
    string Entidade,
    long EntidadeId,
    string Acao,
    string? Descricao,
    DateTime CriadoEm)
{
    public string? Usuario { get; init; }

    public LogSistema(
        long id,
        long usuarioId,
        string? usuario,
        string entidade,
        long entidadeId,
        string acao,
        string? descricao,
        DateTime criadoEm) 
        : this(id, usuarioId, entidade, entidadeId, acao, descricao, criadoEm)
    {
        Usuario = usuario;
    }
}
