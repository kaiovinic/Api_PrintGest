using PrintGest.Application.Abstractions;
using PrintGest.Application.Contracts.Auth;
using PrintGest.Application.Services;
using PrintGest.Domain.Entities;
using PrintGest.Domain.Enums;

namespace PrintGest.Tests;

public sealed class AuthServicePasswordTests
{
    [Fact]
    public async Task LoginAsync_DeveRetornarNull_QuandoEmailEstiverVazio()
    {
        var service = new AuthService(new FakeUsuarioRepositoryVazio());

        var response = await service.LoginAsync(new LoginRequest("", "qualquersenha"));

        Assert.Null(response);
    }

    [Fact]
    public async Task LoginAsync_DeveRetornarNull_QuandoSenhaEstiverVazia()
    {
        var service = new AuthService(new FakeUsuarioRepositoryVazio());

        var response = await service.LoginAsync(new LoginRequest("user@test.com", ""));

        Assert.Null(response);
    }

    [Fact]
    public async Task LoginAsync_DeveRetornarNull_QuandoUsuarioNaoExistir()
    {
        var service = new AuthService(new FakeUsuarioRepositoryVazio());

        var response = await service.LoginAsync(new LoginRequest("naoexiste@test.com", "123456789"));

        Assert.Null(response);
    }

    [Theory]
    [InlineData("123456789", "HASH_DA_SENHA_123456789", true)]
    [InlineData("123456789", "123456789", true)]
    [InlineData("senhaerrada", "HASH_DA_SENHA_123456789", false)]
    [InlineData("", "HASH_DA_SENHA_123456789", false)]
    public void SenhaValida_DeveValidarCorretamente(string senha, string hash, bool esperado)
    {
        var resultado = AuthService.SenhaValida(senha, hash);

        Assert.Equal(esperado, resultado);
    }

    [Fact]
    public void SenhaValida_DeveValidarComHashGerado()
    {
        const string senha = "MinhaSenh@Forte1";
        var hash = AuthService.GerarHashLocal(senha);

        Assert.True(AuthService.SenhaValida(senha, hash));
        Assert.False(AuthService.SenhaValida("outrasenha", hash));
    }

    [Fact]
    public void GerarHashLocal_DeveProuzirHashDeterministico()
    {
        const string senha = "TesteSenha123!";

        var hash1 = AuthService.GerarHashLocal(senha);
        var hash2 = AuthService.GerarHashLocal(senha);

        Assert.Equal(hash1, hash2);
        Assert.StartsWith("SHA256:", hash1);
    }

    [Fact]
    public async Task LoginAsync_DeveRetornarUsuario_ComSenhaHashGerada()
    {
        const string senha = "Senh@Forte123";
        var hash = AuthService.GerarHashLocal(senha);

        var service = new AuthService(new FakeUsuarioRepositorySimples(new Usuario(
            10,
            "Ana Gerente",
            "ana@print.com",
            null,
            hash,
            PerfilUsuario.Gerente,
            StatusUsuario.Ativo,
            false)));

        var response = await service.LoginAsync(new LoginRequest("ana@print.com", senha));

        Assert.NotNull(response);
        Assert.Equal("Ana Gerente", response.Nome);
        Assert.Equal("GERENTE", response.Perfil);
        Assert.False(response.DeveTrocarSenha);
    }

    private sealed class FakeUsuarioRepositoryVazio : IUsuarioRepository
    {
        public Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult<Usuario?>(null);
        public Task<Usuario?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Usuario?>(null);
        public Task<IReadOnlyList<Usuario>> ListAsync(UsuarioFiltro filtro, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Usuario>>([]);
        public Task<long> CreateAsync(Usuario usuario, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(Usuario usuario, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdateStatusAsync(long id, StatusUsuario status, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdatePasswordAsync(long id, string hashSenha, bool deveTrocarSenha, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeUsuarioRepositorySimples(Usuario usuario) : IUsuarioRepository
    {
        public Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult<Usuario?>(usuario.Email == email ? usuario : null);
        public Task<Usuario?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Usuario?>(usuario.Id == id ? usuario : null);
        public Task<IReadOnlyList<Usuario>> ListAsync(UsuarioFiltro filtro, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Usuario>>([usuario]);
        public Task<long> CreateAsync(Usuario usuario, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdateAsync(Usuario usuario, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdateStatusAsync(long id, StatusUsuario status, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> UpdatePasswordAsync(long id, string hashSenha, bool deveTrocarSenha, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
