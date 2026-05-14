using PrintGest.Domain.Enums;

namespace PrintGest.Domain.Entities;

public sealed record Usuario(
    long Id,
    string Nome,
    string Email,
    string? Telefone,
    string SenhaHash,
    PerfilUsuario Perfil,
    StatusUsuario Status,
    bool DeveTrocarSenha);
