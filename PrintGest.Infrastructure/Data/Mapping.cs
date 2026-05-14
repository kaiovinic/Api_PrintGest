using MySqlConnector;
using PrintGest.Domain.Enums;

namespace PrintGest.Infrastructure.Data;

public static class Mapping
{
    public static PerfilUsuario Perfil(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "ADMIN" => PerfilUsuario.Admin,
            "GERENTE" => PerfilUsuario.Gerente,
            _ => PerfilUsuario.Operacional
        };
    }

    public static StatusUsuario StatusUsuario(string value)
    {
        return value.ToUpperInvariant() == "BLOQUEADO"
            ? Domain.Enums.StatusUsuario.Bloqueado
            : Domain.Enums.StatusUsuario.Ativo;
    }

    public static TipoPedido TipoPedido(string value)
    {
        return value.ToUpperInvariant() == "PEDIDO"
            ? Domain.Enums.TipoPedido.Pedido
            : Domain.Enums.TipoPedido.Orcamento;
    }

    public static StatusPedido StatusPedido(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "ABERTO" => Domain.Enums.StatusPedido.Aberto,
            "FINALIZADO" => Domain.Enums.StatusPedido.Finalizado,
            "CANCELADO" => Domain.Enums.StatusPedido.Cancelado,
            _ => Domain.Enums.StatusPedido.Orcado
        };
    }

    public static string? NullableString(this MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
