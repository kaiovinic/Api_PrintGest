using PrintGest.Application.Abstractions;
using PrintGest.Application.Contracts.Auth;
using PrintGest.Application.Services;
using PrintGest.Domain.Entities;
using PrintGest.Domain.Enums;

namespace PrintGest.Tests;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task LoginAsync_DeveRetornarUsuario_QuandoSenhaPadraoEstiverCorreta()
    {
        var service = new AuthService(new FakeUsuarioRepository(new Usuario(
            1,
            "Maria Atendente",
            "maria@print.com",
            null,
            "HASH_DA_SENHA_123456789",
            PerfilUsuario.Operacional,
            StatusUsuario.Ativo,
            true)));

        var response = await service.LoginAsync(new LoginRequest("maria@print.com", "123456789"));

        Assert.NotNull(response);
        Assert.Equal("Maria Atendente", response.Nome);
        Assert.Equal("OPERACIONAL", response.Perfil);
        Assert.True(response.DeveTrocarSenha);
    }

    [Fact]
    public async Task LoginAsync_DeveNegarAcesso_QuandoUsuarioEstiverBloqueado()
    {
        var service = new AuthService(new FakeUsuarioRepository(new Usuario(
            2,
            "João",
            "joao@print.com",
            null,
            "HASH_DA_SENHA_123456789",
            PerfilUsuario.Operacional,
            StatusUsuario.Bloqueado,
            true)));

        var response = await service.LoginAsync(new LoginRequest("joao@print.com", "123456789"));

        Assert.Null(response);
    }

    [Fact]
    public async Task LoginAsync_DeveNegarAcesso_QuandoSenhaEstiverIncorreta()
    {
        var service = new AuthService(new FakeUsuarioRepository(new Usuario(
            3,
            "Carlos",
            "carlos@print.com",
            null,
            "HASH_DA_SENHA_123456789",
            PerfilUsuario.Gerente,
            StatusUsuario.Ativo,
            false)));

        var response = await service.LoginAsync(new LoginRequest("carlos@print.com", "senha-errada"));

        Assert.Null(response);
    }

    private sealed class FakeUsuarioRepository(Usuario? usuario) : IUsuarioRepository
    {
        public Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(usuario?.Email == email ? usuario : null);
        }

        public Task<Usuario?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(usuario?.Id == id ? usuario : null);
        }

        public Task<IReadOnlyList<Usuario>> ListAsync(UsuarioFiltro filtro, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<Usuario> usuarios = usuario is null ? [] : [usuario];
            return Task.FromResult(usuarios);
        }

        public Task<long> CreateAsync(Usuario usuario, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<bool> UpdateAsync(Usuario usuario, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<bool> UpdateStatusAsync(long id, StatusUsuario status, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<bool> UpdatePasswordAsync(long id, string hashSenha, bool deveTrocarSenha, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
