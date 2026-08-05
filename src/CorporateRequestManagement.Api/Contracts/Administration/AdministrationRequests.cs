using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Domain.Enums;

namespace CorporateRequestManagement.Api.Contracts.Administration;

public sealed record UserListRequest(int PageNumber = 1, int PageSize = 20, string? SearchTerm = null, UserRole? Role = null, Guid? DepartmentId = null, bool? IsActive = null, string SortBy = "createdDate", SortDirection SortDirection = SortDirection.Descending);
public sealed record CreateUserRequest(string FirstName, string LastName, string Email, string InitialPassword, string ConfirmInitialPassword, UserRole Role, Guid? DepartmentId, bool IsActive = true);
public sealed record UpdateUserRequest(string FirstName, string LastName, string Email, UserRole Role, Guid? DepartmentId, bool IsActive);
public sealed record PasswordRequest(string NewPassword, string ConfirmPassword);
public sealed record DepartmentListRequest(int PageNumber = 1, int PageSize = 20, string? SearchTerm = null, bool? HasManager = null, string SortBy = "createdDate", SortDirection SortDirection = SortDirection.Descending);
public sealed record DepartmentRequest(string Name);
public sealed record DepartmentManagerRequest(Guid? ManagerId);
public sealed record CreateDepartmentManagerRequest(string FirstName, string LastName, string Email, string InitialPassword,
    string ConfirmPassword, bool IsActive = true);
public sealed record ScopedRequestListRequest(int PageNumber = 1, int PageSize = 20, string? SearchTerm = null, RequestStatus? Status = null, RequestType? RequestType = null, Guid? DepartmentId = null, DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null, decimal? MinAmount = null, decimal? MaxAmount = null, string SortBy = "createdDate", SortDirection SortDirection = SortDirection.Descending);
public sealed record DeleteRequest(string RowVersion);
public sealed record AuditListRequest(int PageNumber = 1, int PageSize = 20, Guid? UserId = null, string? EntityName = null, Guid? EntityId = null, string? Action = null, DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null, SortDirection SortDirection = SortDirection.Descending);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string ConfirmNewPassword);
