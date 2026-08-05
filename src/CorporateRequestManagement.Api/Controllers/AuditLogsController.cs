using CorporateRequestManagement.Api.Contracts.Administration; using CorporateRequestManagement.Application.Common.Models; using CorporateRequestManagement.Application.Features.Administration; using MediatR; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc;
namespace CorporateRequestManagement.Api.Controllers;
[ApiController, Route("api/audit-logs"), Authorize(Policy = "Admin.Exact")]
public sealed class AuditLogsController(ISender sender) : ControllerBase
{
    [HttpGet] public Task<PagedResult<AuditLogDto>> List([FromQuery] AuditListRequest r, CancellationToken ct) => sender.Send(new GetAuditLogsQuery(r.PageNumber, r.PageSize, r.UserId, r.EntityName, r.EntityId, r.Action, r.CreatedFrom, r.CreatedTo, r.SortDirection), ct);
    [HttpGet("actors")] public Task<IReadOnlyList<AuditActorDto>> Actors(CancellationToken ct) => sender.Send(new GetAuditActorsQuery(), ct);
    [HttpGet("{id:guid}")] public Task<AuditLogDto> Detail(Guid id, CancellationToken ct) => sender.Send(new GetAuditLogQuery(id), ct);
}
