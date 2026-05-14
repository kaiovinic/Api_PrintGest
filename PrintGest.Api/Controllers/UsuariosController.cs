using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Services;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/usuarios")]
public sealed class UsuariosController(UsuarioService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar(CancellationToken cancellationToken)
    {
        var usuarios = await service.ListAsync(cancellationToken);
        return Ok(usuarios.Select(usuario => new
        {
            usuario.Id,
            usuario.Nome,
            usuario.Email,
            Perfil = usuario.Perfil.ToString().ToUpperInvariant(),
            Status = usuario.Status.ToString().ToUpperInvariant(),
            usuario.DeveTrocarSenha
        }));
    }
}
