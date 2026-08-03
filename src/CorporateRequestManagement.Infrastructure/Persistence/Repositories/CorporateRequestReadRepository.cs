using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.Infrastructure.Persistence.Repositories;

internal sealed class CorporateRequestReadRepository(ApplicationDbContext context) : ICorporateRequestReadRepository
{
    public async Task<CorporateRequestDetailDto?> GetOwnedDetailAsync(Guid requestId, Guid ownerId, CancellationToken cancellationToken = default)
    {
        DetailRow? row = await (from request in context.CorporateRequests.AsNoTracking()
            join department in context.Departments.AsNoTracking() on request.DepartmentId equals department.Id
            where request.Id == requestId && request.CreatedByUserId == ownerId
            select new DetailRow(request.Id, request.Title, request.Description, request.RequestType, request.Amount,
                request.Status, request.DepartmentId, department.Name, request.CreatedDate, request.UpdatedDate,
                request.SubmittedDate, request.CompletedDate, EF.Property<byte[]>(request, "_rowVersion")))
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : row.ToDto();
    }

    public async Task<PagedResult<CorporateRequestListItemDto>> GetOwnedPageAsync(Guid ownerId, CorporateRequestListCriteria criteria, CancellationToken cancellationToken = default)
    {
        var query = context.CorporateRequests.AsNoTracking().Where(request => request.CreatedByUserId == ownerId);
        if (criteria.Status.HasValue) query = query.Where(request => request.Status == criteria.Status.Value);
        if (criteria.RequestType.HasValue) query = query.Where(request => request.RequestType == criteria.RequestType.Value);
        if (criteria.CreatedFrom.HasValue) query = query.Where(request => request.CreatedDate >= criteria.CreatedFrom.Value);
        if (criteria.CreatedTo.HasValue) query = query.Where(request => request.CreatedDate <= criteria.CreatedTo.Value);
        int totalCount = await query.CountAsync(cancellationToken);
        bool descending = criteria.SortDirection == SortDirection.Descending;
        query = criteria.SortBy.ToLowerInvariant() switch
        {
            "updateddate" => descending ? query.OrderByDescending(x => x.UpdatedDate).ThenByDescending(x => x.Id) : query.OrderBy(x => x.UpdatedDate).ThenBy(x => x.Id),
            "title" => descending ? query.OrderByDescending(x => x.Title).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Title).ThenBy(x => x.Id),
            "amount" => descending ? query.OrderByDescending(x => x.Amount).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Amount).ThenBy(x => x.Id),
            "status" => descending ? query.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Status).ThenBy(x => x.Id),
            _ => descending ? query.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.Id) : query.OrderBy(x => x.CreatedDate).ThenBy(x => x.Id)
        };
        PageRequest page = PageRequest.Create(criteria.PageNumber, criteria.PageSize);
        List<ListRow> rows = await query.Skip(page.Skip).Take(page.PageSize)
            .Select(request => new ListRow(request.Id, request.Title, request.RequestType, request.Amount, request.Status,
                request.CreatedDate, request.UpdatedDate, request.SubmittedDate, EF.Property<byte[]>(request, "_rowVersion")))
            .ToListAsync(cancellationToken);
        return new PagedResult<CorporateRequestListItemDto>(rows.Select(x => x.ToDto()), totalCount, page);
    }

    private sealed record DetailRow(Guid Id, string Title, string Description, Domain.Enums.RequestType RequestType, decimal? Amount,
        Domain.Enums.RequestStatus Status, Guid DepartmentId, string DepartmentName, DateTimeOffset CreatedDate,
        DateTimeOffset UpdatedDate, DateTimeOffset? SubmittedDate, DateTimeOffset? CompletedDate, byte[] RowVersion)
    {
        public CorporateRequestDetailDto ToDto() => new(Id, Title, Description, RequestType, Amount, Status, DepartmentId,
            DepartmentName, CreatedDate, UpdatedDate, SubmittedDate, CompletedDate, RowVersionCodec.Encode(RowVersion));
    }
    private sealed record ListRow(Guid Id, string Title, Domain.Enums.RequestType RequestType, decimal? Amount,
        Domain.Enums.RequestStatus Status, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate,
        DateTimeOffset? SubmittedDate, byte[] RowVersion)
    {
        public CorporateRequestListItemDto ToDto() => new(Id, Title, RequestType, Amount, Status, CreatedDate, UpdatedDate,
            SubmittedDate, RowVersionCodec.Encode(RowVersion));
    }
}
