using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;
using MySqlConnector;

namespace PrintGest.Infrastructure.Repositories;

public sealed class PedidoRepository(MySqlConnectionFactory factory) : IPedidoRepository
{
    public async Task<IReadOnlyList<PedidoResumo>> ListAsync(PedidoFiltro filtro, CancellationToken cancellationToken = default)
    {
        var pedidos = new List<PedidoResumo>();
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await GarantirColunaValorEstornado(connection, cancellationToken);
        var periodo = ResolverPeriodo(filtro);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.numero, c.nome AS cliente, p.tipo, p.status, p.data_pedido,
                   p.data_entrega, p.total, p.valor_pago, p.valor_estornado, p.saldo_devedor, u.nome AS criado_por,
                   p.motivo_cancelamento
            FROM pedidos p
            INNER JOIN clientes c ON c.id = p.cliente_id
            INNER JOIN usuarios u ON u.id = p.criado_por_usuario_id
            WHERE p.data_pedido BETWEEN @inicio AND @fim
              AND (@status IS NULL OR p.status = @status)
            ORDER BY p.data_pedido DESC, p.id DESC;
            """;
        command.Parameters.AddWithValue("@inicio", periodo.Inicio);
        command.Parameters.AddWithValue("@fim", periodo.Fim);
        command.Parameters.AddWithValue("@status", NormalizarStatus(filtro.Status) is { } status ? status : DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pedidos.Add(MapResumo(reader));
        }

        return pedidos;
    }

    public async Task<IReadOnlyList<PedidoResumo>> ListRecentAsync(CancellationToken cancellationToken = default)
    {
        var pedidos = new List<PedidoResumo>();
        await using var connection = factory.Create();
        await connection.OpenAsync(cancellationToken);
        await GarantirColunaValorEstornado(connection, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.numero, c.nome AS cliente, p.tipo, p.status, p.data_pedido,
                   p.data_entrega, p.total, p.valor_pago, p.valor_estornado, p.saldo_devedor, u.nome AS criado_por,
                   p.motivo_cancelamento
            FROM pedidos p
            INNER JOIN clientes c ON c.id = p.cliente_id
            INNER JOIN usuarios u ON u.id = p.criado_por_usuario_id
            WHERE p.status IN ('ABERTO', 'ORCADO')
            ORDER BY p.data_pedido DESC, p.id DESC
            LIMIT 20;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            pedidos.Add(MapResumo(reader));
        }

        return pedidos;
    }

    private static PedidoResumo MapResumo(MySqlDataReader reader)
    {
        var entregaOrdinal = reader.GetOrdinal("data_entrega");
        return new PedidoResumo(
            reader.GetInt64("id"),
            reader.GetString("numero"),
            reader.GetString("cliente"),
            Mapping.TipoPedido(reader.GetString("tipo")),
            Mapping.StatusPedido(reader.GetString("status")),
            DateOnly.FromDateTime(reader.GetDateTime("data_pedido")),
            reader.IsDBNull(entregaOrdinal) ? null : DateOnly.FromDateTime(reader.GetDateTime(entregaOrdinal)),
            reader.GetDecimal("total"),
            reader.GetDecimal("valor_pago"),
            reader.GetDecimal("valor_estornado"),
            reader.GetDecimal("saldo_devedor"),
            reader.GetString("criado_por"),
            reader.NullableString("motivo_cancelamento"));
    }

    private static (DateTime Inicio, DateTime Fim) ResolverPeriodo(PedidoFiltro filtro)
    {
        if (filtro.Inicio is not null || filtro.Fim is not null)
        {
            var inicio = filtro.Inicio?.ToDateTime(TimeOnly.MinValue) ?? DateTime.MinValue;
            var fim = filtro.Fim?.ToDateTime(TimeOnly.MaxValue) ?? DateTime.MaxValue;
            return (inicio, fim);
        }

        var ano = filtro.Ano ?? DateTime.Today.Year;
        var mes = filtro.Mes ?? DateTime.Today.Month;
        var primeiroDia = new DateTime(ano, mes, 1);
        return (primeiroDia, primeiroDia.AddMonths(1).AddTicks(-1));
    }

    private static string? NormalizarStatus(string? status)
    {
        return status?.Trim().ToUpperInvariant() switch
        {
            null or "" or "TODOS" => null,
            "ABERTO" => "ABERTO",
            "ORCADO" or "ORÇADO" or "ORCAMENTO" or "ORÇAMENTO" => "ORCADO",
            "FINALIZADO" => "FINALIZADO",
            "CANCELADO" => "CANCELADO",
            _ => status.Trim().ToUpperInvariant()
        };
    }

    private static async Task GarantirColunaValorEstornado(MySqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'pedidos'
              AND COLUMN_NAME = 'valor_estornado';
            """;
        var existe = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        if (existe)
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE pedidos ADD COLUMN valor_estornado DECIMAL(10,2) NOT NULL DEFAULT 0 AFTER motivo_cancelamento;";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }
}
