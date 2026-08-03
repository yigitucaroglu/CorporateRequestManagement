using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.Approvals.Common;
using CorporateRequestManagement.Domain.Enums;

namespace CorporateRequestManagement.Api.Contracts.Approvals;

public sealed record ApproveRequest(string RowVersion);
public sealed record RejectRequest(string RowVersion, string Reason);
public sealed record GetPendingApprovalsRequest(int PageNumber = PageRequest.DefaultPage, int PageSize = PageRequest.DefaultPageSize,
    Guid? DepartmentId = null, RequestType? RequestType = null, DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null, string? SearchTerm = null, string SortBy = ApprovalSortFields.SubmittedDate,
    SortDirection SortDirection = SortDirection.Ascending);
