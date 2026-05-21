using PrintGest.Application.Abstractions;
using PrintGest.Domain.Entities;
using PrintGest.Infrastructure.Data;
using MySqlConnector;

namespace PrintGest.Infrastructure.Repositories;

public sealed class PedidoRepository(MySqlConnectionFactory factory) : IPedidoRepository
{
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
            ORDER BY p.data_pedido DESC, p.id DESC
            LIMIT 20;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var entregaOrdinal = reader.GetOrdinal("data_entrega");
            pedidos.Add(new PedidoResumo(
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
                reader.NullableString("motivo_cancelamento")));
        }

        return pedidos;
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

