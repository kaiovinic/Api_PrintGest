using MySqlConnector;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class UsuarioRepository(MySqlConnectionFactory factory) : IUsuarioRepository
{
    public async Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
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

    public async Task<IReadOnlyList<Usuario>> ListAsync(CancellationToken cancellationToken = default)
    {
        var usuarios = new List<Usuario>();
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, nome, email, telefone, senha_hash, perfil, status, deve_trocar_senha
            FROM usuarios
            ORDER BY nome;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            usuarios.Add(Map(reader));
        }

        return usuarios;
    }

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
