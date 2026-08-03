using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Domain.Enums;

namespace CorporateRequestManagement.Application.Features.Approvals.Common;

public sealed record ApprovalQueueItemDto(Guid RequestId, string Title, RequestType RequestType, decimal? Amount,
    RequestStatus RequestStatus, string RequesterName, Guid DepartmentId, string DepartmentName,
    DateTimeOffset CreatedDate, DateTimeOffset? SubmittedDate, string RowVersion);

public sealed record ApprovalHistoryItemDto(ApprovalLevel ApprovalLevel, ApprovalStatus ApprovalStatus,
    string? Description, DateTimeOffset ActionDate, string ApproverName);

public sealed record ApprovalRequestDetailDto(Guid RequestId, string Title, string Description, RequestType RequestType,
    decimal? Amount, RequestStatus RequestStatus, Guid RequesterId, string RequesterName, Guid DepartmentId,
    string DepartmentName, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate, DateTimeOffset? SubmittedDate,
    DateTimeOffset? CompletedDate, string RowVersion, IReadOnlyList<ApprovalHistoryItemDto> ApprovalHistory);

public sealed record ApprovalQueueCriteria(int PageNumber, int PageSize, RequestType? RequestType,
    Guid? DepartmentId, DateTimeOffset? CreatedFrom, DateTimeOffset? CreatedTo, string SortBy, SortDirection SortDirection);

public static class ApprovalSortFields
{
    public const string SubmittedDate = "submittedDate";
    public const string CreatedDate = "createdDate";
    public const string Amount = "amount";
    public const string RequestType = "requestType";
    public const string RequesterName = "requesterName";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { SubmittedDate, CreatedDate, Amount, RequestType, RequesterName };
}

public static class ApprovalAuditActions
{
    public const string ManagerApproved = "CorporateRequest.ManagerApproved";
    public const string ManagerRejected = "CorporateRequest.ManagerRejected";
    public const string AdminApproved = "CorporateRequest.AdminApproved";
    public const string AdminRejected = "CorporateRequest.AdminRejected";
}
