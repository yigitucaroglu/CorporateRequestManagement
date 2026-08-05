using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Application.Features.Approvals.Common;

namespace CorporateRequestManagement.Application.Features.Administration;

public sealed record AdminUserDto(Guid Id, string FirstName, string LastName, string Email, UserRole Role,
    Guid? DepartmentId, string? DepartmentName, bool IsActive, DateTimeOffset CreatedDate);
public sealed record AdminUserCriteria(int PageNumber, int PageSize, string? SearchTerm, UserRole? Role,
    Guid? DepartmentId, bool? IsActive, string SortBy, SortDirection SortDirection);
public sealed record DepartmentDto(Guid Id, string Name, Guid? ManagerId, string? ManagerName,
    int ActiveUserCount, int ActiveRequestCount, DateTimeOffset CreatedDate);
public sealed record EligibleManagerDto(Guid Id, string FullName, string Email);
public sealed record DepartmentCriteria(int PageNumber, int PageSize, string? SearchTerm, bool? HasManager,
    string SortBy, SortDirection SortDirection);
public sealed record ScopedRequestDto(Guid Id, string Title, string Description, Guid RequesterId,
    string RequesterName, string RequesterEmail, Guid DepartmentId, string DepartmentName, RequestType RequestType,
    decimal? Amount, RequestStatus Status, DateTimeOffset CreatedDate, DateTimeOffset? SubmittedDate,
    DateTimeOffset? CompletedDate, string RowVersion, IReadOnlyList<ApprovalHistoryItemDto> ApprovalHistory);
public sealed record ScopedRequestCriteria(int PageNumber, int PageSize, string? SearchTerm, RequestStatus? Status,
    RequestType? RequestType, Guid? DepartmentId, DateTimeOffset? CreatedFrom, DateTimeOffset? CreatedTo,
    decimal? MinAmount, decimal? MaxAmount, string SortBy, SortDirection SortDirection);
public sealed record AuditLogDto(Guid Id, Guid? ActorUserId, string ActorFullName, string EntityName, Guid EntityId,
    string Action, DateTimeOffset CreatedDate, string? OldValues, string? NewValues, string? ActorEmail = null,
    UserRole? ActorRole = null, bool ActorIsDeleted = false, bool IsSystemActor = false);
public sealed record AuditActorDto(Guid Id, string FullName, string Email, UserRole Role, bool IsDeleted, bool IsSystemActor = false);
public sealed record AuditLogCriteria(int PageNumber, int PageSize, Guid? UserId, string? EntityName,
    Guid? EntityId, string? Action, DateTimeOffset? CreatedFrom, DateTimeOffset? CreatedTo, SortDirection SortDirection);

public interface IAdministrationQueryService
{
    Task<PagedResult<AdminUserDto>> GetUsersAsync(AdminUserCriteria criteria, CancellationToken cancellationToken = default);
    Task<AdminUserDto?> GetUserAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<DepartmentDto>> GetDepartmentsAsync(DepartmentCriteria criteria, CancellationToken cancellationToken = default);
    Task<DepartmentDto?> GetDepartmentAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EligibleManagerDto>> GetEligibleManagersAsync(Guid departmentId, CancellationToken cancellationToken = default);
    Task<PagedResult<ScopedRequestDto>> GetAdminRequestsAsync(ScopedRequestCriteria criteria, CancellationToken cancellationToken = default);
    Task<PagedResult<ScopedRequestDto>> GetManagerRequestsAsync(Guid managerId, ScopedRequestCriteria criteria, CancellationToken cancellationToken = default);
    Task<ScopedRequestDto?> GetAdminRequestAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ScopedRequestDto?> GetManagerRequestAsync(Guid id, Guid managerId, CancellationToken cancellationToken = default);
    Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(AuditLogCriteria criteria, CancellationToken cancellationToken = default);
    Task<AuditLogDto?> GetAuditLogAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AuditActorDto>> GetAuditActorsAsync(CancellationToken cancellationToken = default);
}
