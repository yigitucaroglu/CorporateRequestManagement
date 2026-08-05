using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.Administration;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Application.Features.Approvals.Common;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CorporateRequestManagement.Infrastructure.Persistence.Repositories;

internal sealed class AdministrationReadStore(ApplicationDbContext context) : IAdministrationQueryService
{
    public async Task<PagedResult<AdminUserDto>> GetUsersAsync(AdminUserCriteria c, CancellationToken ct = default)
    {
        var q = context.Users.AsNoTracking();
        string? search = Normalize(c.SearchTerm);
        if (search is not null) q = q.Where(x => x.FirstName.Contains(search) || x.LastName.Contains(search) || x.Email.Contains(search));
        if (c.Role.HasValue) q = q.Where(x => x.Role == c.Role);
        if (c.DepartmentId.HasValue) q = q.Where(x => x.DepartmentId == c.DepartmentId);
        if (c.IsActive.HasValue) q = q.Where(x => x.IsActive == c.IsActive);
        int count = await q.CountAsync(ct);
        bool desc = c.SortDirection == SortDirection.Descending;
        q = c.SortBy.ToLowerInvariant() switch
        {
            "name" => desc ? q.OrderByDescending(x => x.FirstName).ThenByDescending(x => x.LastName).ThenByDescending(x => x.Id) : q.OrderBy(x => x.FirstName).ThenBy(x => x.LastName).ThenBy(x => x.Id),
            "email" => desc ? q.OrderByDescending(x => x.Email).ThenByDescending(x => x.Id) : q.OrderBy(x => x.Email).ThenBy(x => x.Id),
            "role" => desc ? q.OrderByDescending(x => x.Role).ThenByDescending(x => x.Id) : q.OrderBy(x => x.Role).ThenBy(x => x.Id),
            _ => desc ? q.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.Id) : q.OrderBy(x => x.CreatedDate).ThenBy(x => x.Id)
        };
        PageRequest page = PageRequest.Create(c.PageNumber, c.PageSize);
        List<AdminUserDto> items = await q.Skip(page.Skip).Take(page.PageSize).Select(u => new AdminUserDto(u.Id, u.FirstName, u.LastName, u.Email, u.Role, u.DepartmentId,
            context.Departments.Where(d => d.Id == u.DepartmentId).Select(d => d.Name).FirstOrDefault(), u.IsActive, u.CreatedDate)).ToListAsync(ct);
        return new(items, count, page);
    }

    public Task<AdminUserDto?> GetUserAsync(Guid id, CancellationToken ct = default) =>
        (from u in context.Users.AsNoTracking() join d in context.Departments.AsNoTracking() on u.DepartmentId equals d.Id into departments
         from d in departments.DefaultIfEmpty() where u.Id == id
         select new AdminUserDto(u.Id, u.FirstName, u.LastName, u.Email, u.Role, u.DepartmentId, d == null ? null : d.Name, u.IsActive, u.CreatedDate)).SingleOrDefaultAsync(ct);

    public async Task<PagedResult<DepartmentDto>> GetDepartmentsAsync(DepartmentCriteria c, CancellationToken ct = default)
    {
        var q = context.Departments.AsNoTracking();
        string? search = Normalize(c.SearchTerm); if (search is not null) q = q.Where(x => x.Name.Contains(search));
        if (c.HasManager.HasValue) q = q.Where(x => c.HasManager.Value ? x.ManagerId != null : x.ManagerId == null);
        int count = await q.CountAsync(ct); bool desc = c.SortDirection == SortDirection.Descending;
        q = c.SortBy.Equals("name", StringComparison.OrdinalIgnoreCase)
            ? (desc ? q.OrderByDescending(x => x.Name).ThenByDescending(x => x.Id) : q.OrderBy(x => x.Name).ThenBy(x => x.Id))
            : (desc ? q.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.Id) : q.OrderBy(x => x.CreatedDate).ThenBy(x => x.Id));
        PageRequest page = PageRequest.Create(c.PageNumber, c.PageSize);
        List<DepartmentDto> items = await q.Skip(page.Skip).Take(page.PageSize).Select(d => new DepartmentDto(d.Id, d.Name, d.ManagerId,
            context.Users.Where(u => u.Id == d.ManagerId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault(),
            context.Users.Count(u => u.DepartmentId == d.Id && u.IsActive), context.CorporateRequests.Count(r => r.DepartmentId == d.Id &&
                (r.Status == RequestStatus.Draft || r.Status == RequestStatus.PendingManagerApproval || r.Status == RequestStatus.PendingAdminApproval)), d.CreatedDate)).ToListAsync(ct);
        return new(items, count, page);
    }

    public Task<DepartmentDto?> GetDepartmentAsync(Guid id, CancellationToken ct = default) => context.Departments.AsNoTracking()
        .Where(d => d.Id == id).Select(d => new DepartmentDto(d.Id, d.Name, d.ManagerId,
            context.Users.Where(u => u.Id == d.ManagerId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault(),
            context.Users.Count(u => u.DepartmentId == d.Id && u.IsActive), context.CorporateRequests.Count(r => r.DepartmentId == d.Id &&
                (r.Status == RequestStatus.Draft || r.Status == RequestStatus.PendingManagerApproval || r.Status == RequestStatus.PendingAdminApproval)), d.CreatedDate)).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<EligibleManagerDto>> GetEligibleManagersAsync(Guid departmentId, CancellationToken ct = default) =>
        await context.Users.AsNoTracking()
            .Where(user => user.DepartmentId == departmentId && user.Role == UserRole.Manager && user.IsActive)
            .OrderBy(user => user.FirstName).ThenBy(user => user.LastName).ThenBy(user => user.Email).ThenBy(user => user.Id)
            .Select(user => new EligibleManagerDto(user.Id, user.FirstName + " " + user.LastName, user.Email))
            .ToListAsync(ct);

    public Task<PagedResult<ScopedRequestDto>> GetAdminRequestsAsync(ScopedRequestCriteria c, CancellationToken ct = default) => GetRequestsAsync(null, c, ct);
    public Task<PagedResult<ScopedRequestDto>> GetManagerRequestsAsync(Guid managerId, ScopedRequestCriteria c, CancellationToken ct = default) => GetRequestsAsync(managerId, c, ct);
    public async Task<ScopedRequestDto?> GetAdminRequestAsync(Guid id, CancellationToken ct = default)
    {
        RequestRow? row = await RequestProjection(context.CorporateRequests.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync(ct);
        return row is null ? null : row.ToDto(await ApprovalHistoryAsync(id, ct));
    }

    public async Task<ScopedRequestDto?> GetManagerRequestAsync(Guid id, Guid managerId, CancellationToken ct = default)
    {
        RequestRow? row = await RequestProjection(context.CorporateRequests.AsNoTracking()
            .Where(x => x.Id == id && context.Departments.Any(d => d.Id == x.DepartmentId && d.ManagerId == managerId) && x.CreatedByUserId != managerId)).SingleOrDefaultAsync(ct);
        return row is null ? null : row.ToDto(await ApprovalHistoryAsync(id, ct));
    }

    private async Task<PagedResult<ScopedRequestDto>> GetRequestsAsync(Guid? managerId, ScopedRequestCriteria c, CancellationToken ct)
    {
        var q = context.CorporateRequests.AsNoTracking();
        if (managerId.HasValue) q = q.Where(x => context.Departments.Any(d => d.Id == x.DepartmentId && d.ManagerId == managerId) && x.CreatedByUserId != managerId);
        string? search = Normalize(c.SearchTerm); if (search is not null) q = q.Where(x => x.Title.Contains(search) || x.Description.Contains(search) || context.Users.Any(u => u.Id == x.CreatedByUserId && ((u.FirstName + " " + u.LastName).Contains(search) || u.Email.Contains(search))));
        if (c.Status.HasValue) q = q.Where(x => x.Status == c.Status); if (c.RequestType.HasValue) q = q.Where(x => x.RequestType == c.RequestType);
        if (c.DepartmentId.HasValue) q = q.Where(x => x.DepartmentId == c.DepartmentId); if (c.CreatedFrom.HasValue) q = q.Where(x => x.CreatedDate >= c.CreatedFrom);
        if (c.CreatedTo.HasValue) q = q.Where(x => x.CreatedDate <= c.CreatedTo); if (c.MinAmount.HasValue) q = q.Where(x => x.Amount >= c.MinAmount);
        if (c.MaxAmount.HasValue) q = q.Where(x => x.Amount <= c.MaxAmount);
        int count = await q.CountAsync(ct); bool desc = c.SortDirection == SortDirection.Descending;
        q = c.SortBy.Equals("amount", StringComparison.OrdinalIgnoreCase)
            ? (desc ? q.OrderByDescending(x => x.Amount).ThenByDescending(x => x.Id) : q.OrderBy(x => x.Amount).ThenBy(x => x.Id))
            : (desc ? q.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.Id) : q.OrderBy(x => x.CreatedDate).ThenBy(x => x.Id));
        PageRequest page = PageRequest.Create(c.PageNumber, c.PageSize); List<RequestRow> rows = await RequestProjection(q.Skip(page.Skip).Take(page.PageSize)).ToListAsync(ct); return new(rows.Select(x => x.ToDto([])), count, page);
    }

    private Task<List<ApprovalHistoryItemDto>> ApprovalHistoryAsync(Guid requestId, CancellationToken ct) =>
        (from approval in context.RequestApprovals.AsNoTracking()
         join user in context.Users.IgnoreQueryFilters().AsNoTracking() on approval.ApprovedByUserId equals user.Id
         where approval.RequestId == requestId
         orderby approval.ActionDate, approval.Id
         select new ApprovalHistoryItemDto(approval.ApprovalLevel, approval.Status, approval.Description,
             approval.ActionDate, user.FirstName + " " + user.LastName)).ToListAsync(ct);

    private IQueryable<RequestRow> RequestProjection(IQueryable<CorporateRequestManagement.Domain.Entities.CorporateRequest> source) =>
        from r in source join u in context.Users.AsNoTracking() on r.CreatedByUserId equals u.Id
        join d in context.Departments.AsNoTracking() on r.DepartmentId equals d.Id
        select new RequestRow(r.Id, r.Title, r.Description, u.Id, u.FirstName + " " + u.LastName, u.Email, d.Id, d.Name,
            r.RequestType, r.Amount, r.Status, r.CreatedDate, r.SubmittedDate, r.CompletedDate, EF.Property<byte[]>(r, "_rowVersion"));

    public async Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(AuditLogCriteria c, CancellationToken ct = default)
    {
        var q = context.AuditLogs.AsNoTracking(); if (c.UserId.HasValue) q = q.Where(x => x.UserId == c.UserId); if (!string.IsNullOrWhiteSpace(c.EntityName)) q = q.Where(x => x.EntityName == c.EntityName.Trim());
        if (c.EntityId.HasValue) q = q.Where(x => x.EntityId == c.EntityId); if (!string.IsNullOrWhiteSpace(c.Action)) q = q.Where(x => x.Action == c.Action.Trim());
        if (c.CreatedFrom.HasValue) q = q.Where(x => x.CreatedDate >= c.CreatedFrom); if (c.CreatedTo.HasValue) q = q.Where(x => x.CreatedDate <= c.CreatedTo);
        int count = await q.CountAsync(ct); q = c.SortDirection == SortDirection.Ascending ? q.OrderBy(x => x.CreatedDate).ThenBy(x => x.Id) : q.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.Id);
        PageRequest page = PageRequest.Create(c.PageNumber, c.PageSize); List<AuditLogDto> items = await AuditProjection(q.Skip(page.Skip).Take(page.PageSize)).ToListAsync(ct);
        return new(items.Select(Redact), count, page);
    }
    public async Task<AuditLogDto?> GetAuditLogAsync(Guid id, CancellationToken ct = default)
    {
        AuditLogDto? item = await AuditProjection(context.AuditLogs.AsNoTracking().Where(a => a.Id == id)).SingleOrDefaultAsync(ct);
        return item is null ? null : Redact(item);
    }
    public async Task<IReadOnlyList<AuditActorDto>> GetAuditActorsAsync(CancellationToken ct = default) =>
        await context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => context.AuditLogs.AsNoTracking().Any(a => a.UserId == u.Id))
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName).ThenBy(u => u.Email).ThenBy(u => u.Id)
            .Select(u => new AuditActorDto(u.Id, (u.FirstName + " " + u.LastName).Trim(), u.Email, u.Role, u.IsDeleted, false))
            .ToListAsync(ct);

    private IQueryable<AuditLogDto> AuditProjection(IQueryable<AuditLog> source) =>
        from audit in source
        join actor in context.Users.IgnoreQueryFilters().AsNoTracking() on audit.UserId equals (Guid?)actor.Id into actors
        from actor in actors.DefaultIfEmpty()
        select new AuditLogDto(
            audit.Id,
            audit.UserId,
            audit.UserId == null ? "Sistem" : actor != null ? (actor.FirstName + " " + actor.LastName).Trim() : "Kullanıcı bilgisi bulunamıyor",
            audit.EntityName,
            audit.EntityId,
            audit.Action,
            audit.CreatedDate,
            audit.OldValues,
            audit.NewValues,
            actor != null ? actor.Email : null,
            actor != null ? actor.Role : null,
            actor != null && actor.IsDeleted,
            audit.UserId == null);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static AuditLogDto Redact(AuditLogDto item) => item with { OldValues = RedactValue(item.OldValues), NewValues = RedactValue(item.NewValues) };
    private static string? RedactValue(string? value)
    {
        if (value is null) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "[İçerik güvenlik nedeniyle gösterilemiyor]";
            Dictionary<string, object?> safe = [];
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!AllowedAuditFields.Contains(property.Name) || !IsSafeScalar(property.Value)) continue;
                safe[property.Name] = ScalarValue(property.Value);
            }
            return safe.Count == 0 ? "[İçerik güvenlik nedeniyle gösterilemiyor]" : JsonSerializer.Serialize(safe);
        }
        catch (JsonException)
        {
            return "[İçerik güvenlik nedeniyle gösterilemiyor]";
        }
    }
    private static readonly HashSet<string> AllowedAuditFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Title", "RequestType", "Amount", "Status", "Role", "DepartmentId", "IsActive", "Name", "Email"
    };
    private static bool IsSafeScalar(JsonElement value) => value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;
    private static object? ScalarValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetDecimal(out decimal number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
    private sealed record RequestRow(Guid Id, string Title, string Description, Guid RequesterId, string RequesterName,
        string RequesterEmail, Guid DepartmentId, string DepartmentName, RequestType RequestType, decimal? Amount,
        RequestStatus Status, DateTimeOffset CreatedDate, DateTimeOffset? SubmittedDate, DateTimeOffset? CompletedDate,
        byte[] RowVersion)
    {
        public ScopedRequestDto ToDto(IReadOnlyList<ApprovalHistoryItemDto> history) => new(Id, Title, Description, RequesterId, RequesterName, RequesterEmail,
            DepartmentId, DepartmentName, RequestType, Amount, Status, CreatedDate, SubmittedDate, CompletedDate,
            CorporateRequestManagement.Application.Features.CorporateRequests.Common.RowVersionCodec.Encode(RowVersion), history);
    }
}
