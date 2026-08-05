using CorporateRequestManagement.Api.Contracts.Administration; using CorporateRequestManagement.Application.Features.Account; using CorporateRequestManagement.Application.Features.Administration; using MediatR; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc;
namespace CorporateRequestManagement.Api.Controllers;
[ApiController, Route("api/account"), Authorize]
public sealed class AccountController(ISender sender) : ControllerBase
{
    [HttpGet("profile")] public Task<AdminUserDto> Profile(CancellationToken ct) => sender.Send(new GetProfileQuery(), ct);
    [HttpPost("change-password")] public async Task<IActionResult> ChangePassword(ChangePasswordRequest r, CancellationToken ct) { await sender.Send(new ChangePasswordCommand(r.CurrentPassword, r.NewPassword, r.ConfirmNewPassword), ct); return NoContent(); }
}
