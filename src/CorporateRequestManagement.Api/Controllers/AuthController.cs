using CorporateRequestManagement.Api.Authentication;
using CorporateRequestManagement.Api.Contracts.Authentication;
using CorporateRequestManagement.Application.Features.Authentication.Common;
using CorporateRequestManagement.Application.Features.Authentication.CurrentUser;
using CorporateRequestManagement.Application.Features.Authentication.Login;
using CorporateRequestManagement.Application.Features.Authentication.Refresh;
using CorporateRequestManagement.Application.Features.Authentication.Revoke;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CorporateRequestManagement.Api.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<AuthenticationResult> Login(LoginRequest request, CancellationToken cancellationToken) =>
        sender.Send(new LoginCommand(request.Email, request.Password, ClientIpAddressResolver.Resolve(HttpContext)), cancellationToken);
    [HttpPost("refresh"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<AuthenticationResult> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken) =>
        sender.Send(new RefreshTokenCommand(request.RefreshToken, ClientIpAddressResolver.Resolve(HttpContext)), cancellationToken);
    [HttpPost("revoke"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<IActionResult> Revoke(RevokeRefreshTokenRequest request, CancellationToken cancellationToken)
    { await sender.Send(new RevokeRefreshTokenCommand(request.RefreshToken, ClientIpAddressResolver.Resolve(HttpContext)), cancellationToken); return NoContent(); }
    [HttpGet("me"), Authorize]
    public Task<AuthenticatedUserDto> Me(CancellationToken cancellationToken) => sender.Send(new GetCurrentUserQuery(), cancellationToken);
}
