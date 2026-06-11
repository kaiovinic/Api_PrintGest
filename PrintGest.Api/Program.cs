using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PrintGest.Application;
using PrintGest.Application.Settings;
using PrintGest.Infrastructure;
using System.Text;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var erros = context.ModelState
            .Where(item => item.Value?.Errors.Count > 0)
            .ToDictionary(
                item => item.Key,
                item => item.Value!.Errors.Select(error => error.ErrorMessage).ToArray());

        return new BadRequestObjectResult(new
        {
            mensagem = "Existem campos invalidos na requisicao.",
            erros
        });
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("PrintGestWeb", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "https://localhost:5173",
                "http://localhost:5174",
                "https://localhost:5174",
                "http://127.0.0.1:5173",
                "https://127.0.0.1:5173",
                "http://127.0.0.1:5174",
                "https://127.0.0.1:5174")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddApplication();
builder.Services.AddInfrastructure();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") != "true")
{
    app.UseHttpsRedirection();
}
app.UseCors("PrintGestWeb");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<PrintGest.Infrastructure.Data.PrintGestDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    int retries = 6;
    while (retries > 0)
    {
        try
        {
            context.Database.EnsureCreated();
            if (!context.Usuarios.Any())
            {
                context.Usuarios.Add(new PrintGest.Domain.Entities.Usuario(
                    0,
                    "Administrador",
                    "admin@print.com",
                    null,
                    "123456789",
                    PrintGest.Domain.Enums.PerfilUsuario.Admin,
                    PrintGest.Domain.Enums.StatusUsuario.Ativo,
                    true));
                context.SaveChanges();
                logger.LogInformation("Banco de dados criado e usuário administrador padrão inserido.");
            }
            else
            {
                logger.LogInformation("Conexão com o banco de dados estabelecida com sucesso.");
            }
            break;
        }
        catch (Exception ex)
        {
            retries--;
            logger.LogWarning(ex, "Falha ao conectar ou criar o banco de dados. Tentando novamente em 5 segundos... ({Retries} tentativas restantes)", retries);
            if (retries == 0) throw;
            Thread.Sleep(5000);
        }
    }
}

app.Run();

public partial class Program;
