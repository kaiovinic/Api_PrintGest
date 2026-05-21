using MySqlConnector;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Domain.Enums;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class UsuarioRepository(IUnitOfWork unitOfWork) : IUsuarioRepository
{
    public async Task<Usuario?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT id, nome, email, telefone, senha_hash, perfil, status, deve_trocar_senha
            FROM usuarios
            WHERE id = @id
            LIMIT 1;
            """;
        command.Parameters.Add(new MySqlParameter("@id", id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT id, nome, email, telefone, senha_hash, perfil, status, deve_trocar_senha
            FROM usuarios
            WHERE email = @email
            LIMIT 1;
            """;
        command.Parameters.Add(new MySqlParameter("@email", email));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<Usuario>> ListAsync(UsuarioFiltro filtro, CancellationToken cancellationToken = default)
    {
        var usuarios = new List<Usuario>();
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT id, nome, email, telefone, senha_hash, perfil, status, deve_trocar_senha
            FROM usuarios
            WHERE (@nome IS NULL OR nome LIKE CONCAT('%', @nome, '%'))
              AND (@email IS NULL OR email LIKE CONCAT('%', @email, '%'))
              AND (@perfil IS NULL OR perfil = @perfil)
              AND (@status IS NULL OR status = @status)
            ORDER BY nome;
            """;
        command.Parameters.AddWithValue("@nome", ToDb(filtro.Nome));
        command.Parameters.AddWithValue("@email", ToDb(filtro.Email));
        command.Parameters.AddWithValue("@perfil", ToDb(NormalizarEnum(filtro.Perfil)));
        command.Parameters.AddWithValue("@status", ToDb(NormalizarEnum(filtro.Status)));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            usuarios.Add(Map(reader));
        }

        return usuarios;
    }

    public async Task<long> CreateAsync(Usuario usuario, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            INSERT INTO usuarios (nome, email, telefone, senha_hash, perfil, status, deve_trocar_senha)
            VALUES (@nome, @email, @telefone, @senhaHash, @perfil, @status, @deveTrocarSenha);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@nome", usuario.Nome);
        command.Parameters.AddWithValue("@email", usuario.Email);
        command.Parameters.AddWithValue("@telefone", (object?)usuario.Telefone ?? DBNull.Value);
        command.Parameters.AddWithValue("@senhaHash", usuario.SenhaHash);
        command.Parameters.AddWithValue("@perfil", usuario.Perfil.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("@status", usuario.Status.ToString().ToUpperInvariant());
        command.Parameters.AddWithValue("@deveTrocarSenha", usuario.DeveTrocarSenha);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<bool> UpdateAsync(Usuario usuario, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            UPDATE usuarios
            SET nome = @nome, email = @email, telefone = @telefone, perfil = @perfil
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", usuario.Id);
        command.Parameters.AddWithValue("@nome", usuario.Nome);
        command.Parameters.AddWithValue("@email", usuario.Email);
        command.Parameters.AddWithValue("@telefone", (object?)usuario.Telefone ?? DBNull.Value);
        command.Parameters.AddWithValue("@perfil", usuario.Perfil.ToString().ToUpperInvariant());

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> UpdateStatusAsync(long id, StatusUsuario status, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = "UPDATE usuarios SET status = @status WHERE id = @id;";
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@status", status.ToString().ToUpperInvariant());

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> UpdatePasswordAsync(long id, string senhaHash, bool deveTrocarSenha, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            UPDATE usuarios
            SET senha_hash = @senhaHash, deve_trocar_senha = @deveTrocarSenha
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@senhaHash", senhaHash);
        command.Parameters.AddWithValue("@deveTrocarSenha", deveTrocarSenha);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private static string? NormalizarEnum(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static object ToDb(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static Usuario Map(MySqlDataReader reader)
    {
        return new Usuario(
            reader.GetInt64("id"),
            reader.GetString("nome"),
            reader.GetString("email"),
            reader.NullableString("telefone"),
            reader.GetString("senha_hash"),
            Mapping.Perfil(reader.GetString("perfil")),
            Mapping.StatusUsuario(reader.GetString("status")),
            reader.GetBoolean("deve_trocar_senha"));
    }
}
