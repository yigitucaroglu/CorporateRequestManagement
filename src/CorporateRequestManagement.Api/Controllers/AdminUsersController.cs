using CorporateRequestManagement.Api.Contracts.Administration;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.Administration;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Api.Controllers;
[ApiController, Route("api/admin/users"), Authorize(Policy = "Admin.Exact")]
public sealed class AdminUsersController(ISender sender) : ControllerBase
{
    [HttpGet] public Task<PagedResult<AdminUserDto>> List([FromQuery] UserListRequest r, CancellationToken ct) => sender.Send(new GetAdminUsersQuery(r.PageNumber, r.PageSize, r.SearchTerm, r.Role, r.DepartmentId, r.IsActive, r.SortBy, r.SortDirection), ct);
    [HttpGet("{id:guid}")] public Task<AdminUserDto> Detail(Guid id, CancellationToken ct) => sender.Send(new GetAdminUserQuery(id), ct);
    [HttpPost] public async Task<IActionResult> Create(CreateUserRequest r, CancellationToken ct) { AdminUserDto result = await sender.Send(new CreateAdminUserCommand(r.FirstName, r.LastName, r.Email, r.InitialPassword, r.ConfirmInitialPassword, r.Role, r.DepartmentId, r.IsActive), ct); return CreatedAtAction(nameof(Detail), new { id = result.Id }, result); }
    [HttpPut("{id:guid}")] public Task<AdminUserDto> Update(Guid id, UpdateUserRequest r, CancellationToken ct) => sender.Send(new UpdateAdminUserCommand(id, r.FirstName, r.LastName, r.Email, r.Role, r.DepartmentId, r.IsActive), ct);
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await sender.Send(new DeleteAdminUserCommand(id), ct); return NoContent(); }
    [HttpPost("{id:guid}/reset-password")] public async Task<IActionResult> Reset(Guid id, PasswordRequest r, CancellationToken ct) { await sender.Send(new ResetAdminUserPasswordCommand(id, r.NewPassword, r.ConfirmPassword), ct); return NoContent(); }
}
