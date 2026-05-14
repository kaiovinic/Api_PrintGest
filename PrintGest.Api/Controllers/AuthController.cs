using Microsoft.AspNetCore.Mvc;
using PrintGest.Application.Abstractions;
using PrintGest.Application.Contracts.Auth;

namespace PrintGest.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.LoginAsync(request, cancellationToken);
        return response is null ? Unauthorized() : Ok(response);
    }
}
