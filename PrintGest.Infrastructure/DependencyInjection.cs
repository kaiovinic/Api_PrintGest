using Microsoft.Extensions.DependencyInjection;
using PrintGest.Application.Abstractions;
using PrintGest.Infrastructure.Data;
using PrintGest.Infrastructure.Repositories;

namespace PrintGest.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<MySqlConnectionFactory>();
        services.AddScoped<IDatabaseHealthCheck, MySqlDatabaseHealthCheck>();
        services.AddScoped<IUsuarioRepository, UsuarioRepository>();
        services.AddScoped<IClienteRepository, ClienteRepository>();
        services.AddScoped<IPedidoRepository, PedidoRepository>();
        services.AddScoped<IFinanceiroRepository, FinanceiroRepository>();
        return services;
    }
}

