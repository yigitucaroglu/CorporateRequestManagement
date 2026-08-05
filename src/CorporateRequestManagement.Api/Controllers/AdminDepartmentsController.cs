using CorporateRequestManagement.Api.Contracts.Administration;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.Administration;
using MediatR; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc;
namespace CorporateRequestManagement.Api.Controllers;
[ApiController, Route("api/admin/departments"), Authorize(Policy = "Admin.Exact")]
public sealed class AdminDepartmentsController(ISender sender) : ControllerBase
{
    [HttpGet] public Task<PagedResult<DepartmentDto>> List([FromQuery] DepartmentListRequest r, CancellationToken ct) => sender.Send(new GetDepartmentsQuery(r.PageNumber, r.PageSize, r.SearchTerm, r.HasManager, r.SortBy, r.SortDirection), ct);
    [HttpGet("{id:guid}")] public Task<DepartmentDto> Detail(Guid id, CancellationToken ct) => sender.Send(new GetDepartmentQuery(id), ct);
    [HttpGet("{id:guid}/eligible-managers")] public Task<IReadOnlyList<EligibleManagerDto>> EligibleManagers(Guid id, CancellationToken ct) => sender.Send(new GetEligibleDepartmentManagersQuery(id), ct);
    [HttpPost] public async Task<IActionResult> Create(DepartmentRequest r, CancellationToken ct) { DepartmentDto result = await sender.Send(new CreateDepartmentCommand(r.Name), ct); return CreatedAtAction(nameof(Detail), new { id = result.Id }, result); }
    [HttpPut("{id:guid}")] public Task<DepartmentDto> Update(Guid id, DepartmentRequest r, CancellationToken ct) => sender.Send(new UpdateDepartmentCommand(id, r.Name), ct);
    [HttpPut("{id:guid}/manager")] public Task<DepartmentDto> Manager(Guid id, DepartmentManagerRequest r, CancellationToken ct) => sender.Send(new AssignDepartmentManagerCommand(id, r.ManagerId), ct);
    [HttpPost("{id:guid}/manager/create-and-assign")] public async Task<IActionResult> CreateAndAssignManager(Guid id, CreateDepartmentManagerRequest r, CancellationToken ct) { DepartmentDto result = await sender.Send(new CreateAndAssignDepartmentManagerCommand(id, r.FirstName, r.LastName, r.Email, r.InitialPassword, r.ConfirmPassword, r.IsActive), ct); return CreatedAtAction(nameof(Detail), new { id }, result); }
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { await sender.Send(new DeleteDepartmentCommand(id), ct); return NoContent(); }
}
