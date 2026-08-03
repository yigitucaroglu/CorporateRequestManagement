using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.Approvals.Common;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.Infrastructure.Persistence.Repositories;

internal sealed class ApprovalReadRepository(ApplicationDbContext context) : IApprovalReadRepository
{
    public Task<PagedResult<ApprovalQueueItemDto>> GetManagerQueueAsync(Guid managerUserId, ApprovalQueueCriteria criteria, CancellationToken ct = default) =>
        GetQueueAsync(managerUserId, RequestStatus.PendingManagerApproval, true, criteria, ct);

    public Task<PagedResult<ApprovalQueueItemDto>> GetAdminQueueAsync(Guid adminUserId, ApprovalQueueCriteria criteria, CancellationToken ct = default) =>
        GetQueueAsync(adminUserId, RequestStatus.PendingAdminApproval, false, criteria, ct);

    public Task<ApprovalRequestDetailDto?> GetManagerDetailAsync(Guid requestId, Guid managerUserId, CancellationToken ct = default) =>
        GetDetailAsync(requestId, managerUserId, RequestStatus.PendingManagerApproval, true, ct);

    public Task<ApprovalRequestDetailDto?> GetAdminDetailAsync(Guid requestId, Guid adminUserId, CancellationToken ct = default) =>
        GetDetailAsync(requestId, adminUserId, RequestStatus.PendingAdminApproval, false, ct);

    public Task<ApprovalRequestDetailDto?> GetDecisionResultAsync(Guid requestId, CancellationToken ct = default) =>
        GetDetailAsync(requestId, null, null, false, ct);

    public async Task<IReadOnlyList<ApprovalHistoryItemDto>?> GetHistoryAsync(Guid requestId, Guid userId, UserRole role, CancellationToken ct = default)
    {
        bool allowed = await (from request in context.CorporateRequests.AsNoTracking()
            join department in context.Departments.AsNoTracking() on request.DepartmentId equals department.Id
            where request.Id == requestId && (request.CreatedByUserId == userId || role == UserRole.Admin || department.ManagerId == userId)
            select request.Id).AnyAsync(ct);
        if (!allowed) return null;
        return await HistoryQuery(requestId).ToListAsync(ct);
    }

    private async Task<PagedResult<ApprovalQueueItemDto>> GetQueueAsync(Guid actorId, RequestStatus status, bool managerScope, ApprovalQueueCriteria criteria, CancellationToken ct)
    {
        var query = from request in context.CorporateRequests.AsNoTracking()
                    join requester in context.Users.AsNoTracking() on request.CreatedByUserId equals requester.Id
                    join department in context.Departments.AsNoTracking() on request.DepartmentId equals department.Id
                    where request.Status == status && request.CreatedByUserId != actorId
                        && (!managerScope || department.ManagerId == actorId)
                    select new QueueRow
                    {
                        RequestId = request.Id, Title = request.Title, RequestType = request.RequestType,
                        Amount = request.Amount, RequestStatus = request.Status,
                        RequesterName = requester.FirstName + " " + requester.LastName,
                        DepartmentId = request.DepartmentId, DepartmentName = department.Name,
                        CreatedDate = request.CreatedDate, SubmittedDate = request.SubmittedDate,
                        RowVersion = EF.Property<byte[]>(request, "_rowVersion")
                    };
        if (criteria.DepartmentId.HasValue && !managerScope) query = query.Where(x => x.DepartmentId == criteria.DepartmentId.Value);
        if (criteria.RequestType.HasValue) query = query.Where(x => x.RequestType == criteria.RequestType.Value);
        if (criteria.CreatedFrom.HasValue) query = query.Where(x => x.CreatedDate >= criteria.CreatedFrom.Value);
        if (criteria.CreatedTo.HasValue) query = query.Where(x => x.CreatedDate <= criteria.CreatedTo.Value);
        if (criteria.SearchTerm is not null)
        {
            string search = criteria.SearchTerm;
            query = managerScope
                ? query.Where(x => x.Title.Contains(search) || x.RequesterName.Contains(search))
                : query.Where(x => x.Title.Contains(search) || x.RequesterName.Contains(search) || x.DepartmentName.Contains(search));
        }
        int total = await query.CountAsync(ct);
        bool desc = criteria.SortDirection == SortDirection.Descending;
        query = criteria.SortBy.ToLowerInvariant() switch
        {
            "createddate" => desc ? query.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.RequestId) : query.OrderBy(x => x.CreatedDate).ThenBy(x => x.RequestId),
            "amount" => desc ? query.OrderByDescending(x => x.Amount).ThenByDescending(x => x.RequestId) : query.OrderBy(x => x.Amount).ThenBy(x => x.RequestId),
            "requesttype" => desc ? query.OrderByDescending(x => x.RequestType).ThenByDescending(x => x.RequestId) : query.OrderBy(x => x.RequestType).ThenBy(x => x.RequestId),
            "requestername" => desc ? query.OrderByDescending(x => x.RequesterName).ThenByDescending(x => x.RequestId) : query.OrderBy(x => x.RequesterName).ThenBy(x => x.RequestId),
            _ => desc ? query.OrderByDescending(x => x.SubmittedDate).ThenByDescending(x => x.RequestId) : query.OrderBy(x => x.SubmittedDate).ThenBy(x => x.RequestId)
        };
        PageRequest page = PageRequest.Create(criteria.PageNumber, criteria.PageSize);
        List<QueueRow> rows = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new(rows.Select(x => x.ToDto()), total, page);
    }

    private async Task<ApprovalRequestDetailDto?> GetDetailAsync(Guid requestId, Guid? actorId, RequestStatus? status, bool managerScope, CancellationToken ct)
    {
        DetailRow? row = await (from request in context.CorporateRequests.AsNoTracking()
            join requester in context.Users.AsNoTracking() on request.CreatedByUserId equals requester.Id
            join department in context.Departments.AsNoTracking() on request.DepartmentId equals department.Id
            where request.Id == requestId && (!status.HasValue || request.Status == status.Value)
                && (!actorId.HasValue || request.CreatedByUserId != actorId.Value)
                && (!managerScope || department.ManagerId == actorId)
            select new DetailRow(request.Id, request.Title, request.Description, request.RequestType, request.Amount, request.Status,
                request.CreatedByUserId, requester.FirstName + " " + requester.LastName, request.DepartmentId, department.Name,
                request.CreatedDate, request.UpdatedDate, request.SubmittedDate, request.CompletedDate, EF.Property<byte[]>(request, "_rowVersion")))
            .SingleOrDefaultAsync(ct);
        if (row is null) return null;
        List<ApprovalHistoryItemDto> history = await HistoryQuery(requestId).ToListAsync(ct);
        return row.ToDto(history);
    }

    private IQueryable<ApprovalHistoryItemDto> HistoryQuery(Guid requestId) =>
        from approval in context.RequestApprovals.AsNoTracking()
        join approver in context.Users.IgnoreQueryFilters().AsNoTracking() on approval.ApprovedByUserId equals approver.Id
        where approval.RequestId == requestId
        orderby approval.ActionDate, approval.Id
        select new ApprovalHistoryItemDto(approval.ApprovalLevel, approval.Status, approval.Description,
            approval.ActionDate, approver.FirstName + " " + approver.LastName);

    private sealed class QueueRow
    {
        public Guid RequestId { get; init; }
        public string Title { get; init; } = string.Empty;
        public RequestType RequestType { get; init; }
        public decimal? Amount { get; init; }
        public RequestStatus RequestStatus { get; init; }
        public string RequesterName { get; init; } = string.Empty;
        public Guid DepartmentId { get; init; }
        public string DepartmentName { get; init; } = string.Empty;
        public DateTimeOffset CreatedDate { get; init; }
        public DateTimeOffset? SubmittedDate { get; init; }
        public byte[] RowVersion { get; init; } = [];
        public ApprovalQueueItemDto ToDto() => new(RequestId, Title, RequestType, Amount, RequestStatus, RequesterName, DepartmentId, DepartmentName, CreatedDate, SubmittedDate, RowVersionCodec.Encode(RowVersion));
    }
    private sealed record DetailRow(Guid RequestId, string Title, string Description, RequestType RequestType, decimal? Amount,
        RequestStatus RequestStatus, Guid RequesterId, string RequesterName, Guid DepartmentId, string DepartmentName,
        DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate, DateTimeOffset? SubmittedDate, DateTimeOffset? CompletedDate, byte[] RowVersion)
    { public ApprovalRequestDetailDto ToDto(IReadOnlyList<ApprovalHistoryItemDto> history) => new(RequestId, Title, Description, RequestType, Amount, RequestStatus, RequesterId, RequesterName, DepartmentId, DepartmentName, CreatedDate, UpdatedDate, SubmittedDate, CompletedDate, RowVersionCodec.Encode(RowVersion), history); }
}
