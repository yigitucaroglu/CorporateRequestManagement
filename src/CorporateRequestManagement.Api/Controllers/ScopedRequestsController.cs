using CorporateRequestManagement.Api.Contracts.Administration;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.Administration;
using MediatR; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc;
namespace CorporateRequestManagement.Api.Controllers;
[ApiController, Route("api/admin/requests"), Authorize(Policy = "Admin.Exact")]
public sealed class AdminRequestsController(ISender sender) : ControllerBase
{
    [HttpGet] public Task<PagedResult<ScopedRequestDto>> List([FromQuery] ScopedRequestListRequest r, CancellationToken ct) => sender.Send(new GetAdminRequestsQuery(r.PageNumber, r.PageSize, r.SearchTerm, r.Status, r.RequestType, r.DepartmentId, r.CreatedFrom, r.CreatedTo, r.MinAmount, r.MaxAmount, r.SortBy, r.SortDirection), ct);
    [HttpGet("{id:guid}")] public Task<ScopedRequestDto> Detail(Guid id, CancellationToken ct) => sender.Send(new GetAdminRequestQuery(id), ct);
    [HttpDelete("{id:guid}")] public async Task<IActionResult> Delete(Guid id, DeleteRequest r, CancellationToken ct) { await sender.Send(new DeleteCorporateRequestCommand(id, r.RowVersion), ct); return NoContent(); }
}
[ApiController, Route("api/manager/requests"), Authorize(Policy = "Manager.Exact")]
public sealed class ManagerRequestsController(ISender sender) : ControllerBase
{
    [HttpGet] public Task<PagedResult<ScopedRequestDto>> List([FromQuery] ScopedRequestListRequest r, CancellationToken ct) => sender.Send(new GetManagerDepartmentRequestsQuery(r.PageNumber, r.PageSize, r.SearchTerm, r.Status, r.RequestType, r.CreatedFrom, r.CreatedTo, r.MinAmount, r.MaxAmount, r.SortBy, r.SortDirection), ct);
    [HttpGet("{id:guid}")] public Task<ScopedRequestDto> Detail(Guid id, CancellationToken ct) => sender.Send(new GetManagerDepartmentRequestQuery(id), ct);
}
