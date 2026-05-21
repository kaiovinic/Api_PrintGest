using MySqlConnector;
using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;

namespace PrintGest.Infrastructure.Repositories;

public sealed class LogRepository(IUnitOfWork unitOfWork) : ILogRepository
{
    public async Task<ResultadoPaginado<LogSistema>> ListAsync(
        string? entidade,
        long? entidadeId,
        DateOnly? dataInicio,
        DateOnly? dataFinal,
        int pagina,
        int tamanhoPagina,
        CancellationToken cancellationToken = default)
    {
        var logs = new List<LogSistema>();
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);

        var paginaAtual = Math.Max(pagina, 1);
        var tamanho = Math.Clamp(tamanhoPagina, 5, 100);
        var offset = (paginaAtual - 1) * tamanho;

        // 1. Contar Total
        await using var totalCommand = (MySqlCommand)connection.CreateCommand();
        totalCommand.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        totalCommand.CommandText = """
            SELECT COUNT(*)
            FROM logs_sistema l
            WHERE (@entidade IS NULL OR l.entidade = @entidade)
              AND (@entidadeId IS NULL OR l.entidade_id = @entidadeId)
              AND (@dataInicio IS NULL OR DATE(l.criado_em) >= @dataInicio)
              AND (@dataFinal IS NULL OR DATE(l.criado_em) <= @dataFinal);
            """;
        totalCommand.Parameters.AddWithValue("@entidade", (object?)entidade ?? DBNull.Value);
        totalCommand.Parameters.AddWithValue("@entidadeId", (object?)entidadeId ?? DBNull.Value);
        totalCommand.Parameters.AddWithValue("@dataInicio", dataInicio?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        totalCommand.Parameters.AddWithValue("@dataFinal", dataFinal?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        var total = Convert.ToInt32(await totalCommand.ExecuteScalarAsync(cancellationToken));
        var totalPaginas = total == 0 ? 1 : (int)Math.Ceiling(total / (double)tamanho);

        // 2. Buscar Itens
        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            SELECT l.id,
                   l.usuario_id,
                   u.nome AS usuario,
                   l.entidade,
                   l.entidade_id,
                   l.acao,
                   l.descricao,
                   l.criado_em
            FROM logs_sistema l
            LEFT JOIN usuarios u ON u.id = l.usuario_id
            WHERE (@entidade IS NULL OR l.entidade = @entidade)
              AND (@entidadeId IS NULL OR l.entidade_id = @entidadeId)
              AND (@dataInicio IS NULL OR DATE(l.criado_em) >= @dataInicio)
              AND (@dataFinal IS NULL OR DATE(l.criado_em) <= @dataFinal)
            ORDER BY l.criado_em DESC
            LIMIT @limit OFFSET @offset;
            """;
        command.Parameters.AddWithValue("@entidade", (object?)entidade ?? DBNull.Value);
        command.Parameters.AddWithValue("@entidadeId", (object?)entidadeId ?? DBNull.Value);
        command.Parameters.AddWithValue("@dataInicio", dataInicio?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@dataFinal", dataFinal?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@limit", tamanho);
        command.Parameters.AddWithValue("@offset", offset);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            logs.Add(new LogSistema(
                reader.GetInt64("id"),
                reader.GetInt64("usuario_id"),
                reader.NullableString("usuario"),
                reader.GetString("entidade"),
                reader.GetInt64("entidade_id"),
                reader.GetString("acao"),
                reader.NullableString("descricao"),
                reader.GetDateTime("criado_em")
            ));
        }

        return new ResultadoPaginado<LogSistema>(logs, total, paginaAtual, tamanho, totalPaginas);
    }

    public async Task<long> CreateAsync(LogSistema log, CancellationToken cancellationToken = default)
    {
        var connection = await unitOfWork.GetConnectionAsync(cancellationToken);

        await using var command = (MySqlCommand)connection.CreateCommand();
        command.Transaction = (MySqlTransaction?)unitOfWork.Transaction;
        command.CommandText = """
            INSERT INTO logs_sistema (usuario_id, entidade, entidade_id, acao, descricao)
            VALUES (@usuarioId, @entidade, @entidadeId, @acao, @descricao);
            SELECT LAST_INSERT_ID();
            """;
        command.Parameters.AddWithValue("@usuarioId", log.UsuarioId);
        command.Parameters.AddWithValue("@entidade", log.Entidade);
        command.Parameters.AddWithValue("@entidadeId", log.EntidadeId);
        command.Parameters.AddWithValue("@acao", log.Acao);
        command.Parameters.AddWithValue("@descricao", (object?)log.Descricao ?? DBNull.Value);

        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }
}
