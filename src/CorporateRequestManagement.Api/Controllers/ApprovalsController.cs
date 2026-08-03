using CorporateRequestManagement.Api.Contracts.Approvals;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.Approvals.Common;
using CorporateRequestManagement.Application.Features.Approvals.Decisions;
using CorporateRequestManagement.Application.Features.Approvals.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Api.Controllers;

[ApiController, Authorize, Route("api/approvals")]
public sealed class ApprovalsController(ISender sender) : ControllerBase
{
    [HttpGet("manager"), Authorize(Policy = "Requests.ManagerApproval")]
    public Task<PagedResult<ApprovalQueueItemDto>> ManagerQueue([FromQuery] GetPendingApprovalsRequest request, CancellationToken ct) =>
        sender.Send(new GetManagerApprovalQueueQuery(request.PageNumber, request.PageSize, request.RequestType, request.CreatedFrom, request.CreatedTo, request.SortBy, request.SortDirection), ct);
    [HttpGet("manager/{requestId:guid}"), Authorize(Policy = "Requests.ManagerApproval")]
    public Task<ApprovalRequestDetailDto> ManagerDetail(Guid requestId, CancellationToken ct) => sender.Send(new GetManagerApprovalDetailQuery(requestId), ct);
    [HttpPost("manager/{requestId:guid}/approve"), Authorize(Policy = "Requests.ManagerApproval")]
    public Task<ApprovalRequestDetailDto> ManagerApprove(Guid requestId, ApproveRequest request, CancellationToken ct) => sender.Send(new ManagerApproveCommand(requestId, request.RowVersion), ct);
    [HttpPost("manager/{requestId:guid}/reject"), Authorize(Policy = "Requests.ManagerApproval")]
    public Task<ApprovalRequestDetailDto> ManagerReject(Guid requestId, RejectRequest request, CancellationToken ct) => sender.Send(new ManagerRejectCommand(requestId, request.RowVersion, request.Reason), ct);

    [HttpGet("admin"), Authorize(Policy = "Requests.AdminApproval")]
    public Task<PagedResult<ApprovalQueueItemDto>> AdminQueue([FromQuery] GetPendingApprovalsRequest request, CancellationToken ct) =>
        sender.Send(new GetAdminApprovalQueueQuery(request.PageNumber, request.PageSize, request.DepartmentId, request.RequestType, request.CreatedFrom, request.CreatedTo, request.SortBy, request.SortDirection), ct);
    [HttpGet("admin/{requestId:guid}"), Authorize(Policy = "Requests.AdminApproval")]
    public Task<ApprovalRequestDetailDto> AdminDetail(Guid requestId, CancellationToken ct) => sender.Send(new GetAdminApprovalDetailQuery(requestId), ct);
    [HttpPost("admin/{requestId:guid}/approve"), Authorize(Policy = "Requests.AdminApproval")]
    public Task<ApprovalRequestDetailDto> AdminApprove(Guid requestId, ApproveRequest request, CancellationToken ct) => sender.Send(new AdminApproveCommand(requestId, request.RowVersion), ct);
    [HttpPost("admin/{requestId:guid}/reject"), Authorize(Policy = "Requests.AdminApproval")]
    public Task<ApprovalRequestDetailDto> AdminReject(Guid requestId, RejectRequest request, CancellationToken ct) => sender.Send(new AdminRejectCommand(requestId, request.RowVersion, request.Reason), ct);
}
