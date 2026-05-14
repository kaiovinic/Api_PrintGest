using Microsoft.Extensions.DependencyInjection;
using PrintGest.Application.Abstractions;
using PrintGest.Application.Services;

namespace PrintGest.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<UsuarioService>();
        return services;
    }
}
