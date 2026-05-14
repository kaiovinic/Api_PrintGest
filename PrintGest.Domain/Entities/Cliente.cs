namespace PrintGest.Domain.Entities;

public sealed record Cliente(
    long Id,
    string Nome,
    string? CpfCnpj,
    string? Empresa,
    string? Telefone,
    string? Email,
    string? Endereco,
    string? Cidade);
