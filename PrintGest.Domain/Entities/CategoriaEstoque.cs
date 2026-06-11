namespace PrintGest.Domain.Entities;

public sealed record CategoriaEstoque(
    long Id,
    string Nome,
    bool Ativo,
    DateTime CriadoEm);
