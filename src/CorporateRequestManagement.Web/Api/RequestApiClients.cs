using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.WebUtilities;

namespace CorporateRequestManagement.Web.Api;

public enum RequestTypeContract { Expense = 1, Equipment = 2, Leave = 3, Other = 4 }
public enum RequestStatusContract { Draft = 1, PendingManagerApproval = 2, PendingAdminApproval = 3, Approved = 4, Rejected = 5, Cancelled = 6 }
public enum ApprovalLevelContract { Manager = 1, Admin = 2 }
public enum ApprovalStatusContract { Approved = 1, Rejected = 2 }
public sealed record PagedContract<T>(IReadOnlyList<T> Items, int TotalCount, int PageNumber, int PageSize, int TotalPages, bool HasPreviousPage, bool HasNextPage);
public sealed record RequestListItemContract(Guid Id, string Title, RequestTypeContract RequestType, decimal? Amount, RequestStatusContract Status,
    DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate, DateTimeOffset? SubmittedDate, string RowVersion);
public sealed record RequestDetailContract(Guid Id, string Title, string Description, RequestTypeContract RequestType, decimal? Amount,
    RequestStatusContract Status, Guid DepartmentId, string? DepartmentName, DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate,
    DateTimeOffset? SubmittedDate, DateTimeOffset? CompletedDate, string RowVersion);
public sealed record ApprovalHistoryContract(ApprovalLevelContract ApprovalLevel, ApprovalStatusContract ApprovalStatus, string? Description, DateTimeOffset ActionDate, string ApproverName);
public sealed record ApprovalQueueItemContract(Guid RequestId, string Title, RequestTypeContract RequestType, decimal? Amount,
    RequestStatusContract RequestStatus, string RequesterName, Guid DepartmentId, string DepartmentName, DateTimeOffset CreatedDate,
    DateTimeOffset? SubmittedDate, string RowVersion);
public sealed record ApprovalDetailContract(Guid RequestId, string Title, string Description, RequestTypeContract RequestType, decimal? Amount,
    RequestStatusContract RequestStatus, Guid RequesterId, string RequesterName, Guid DepartmentId, string DepartmentName,
    DateTimeOffset CreatedDate, DateTimeOffset UpdatedDate, DateTimeOffset? SubmittedDate, DateTimeOffset? CompletedDate,
    string RowVersion, IReadOnlyList<ApprovalHistoryContract> ApprovalHistory);

public sealed record RequestListQuery(int PageNumber = 1, int PageSize = 20, RequestStatusContract? Status = null, RequestTypeContract? RequestType = null,
    DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null, string? SearchTerm = null, string SortBy = "createdDate", string SortDirection = "Descending");
public sealed record ApprovalListQuery(int PageNumber = 1, int PageSize = 20, RequestTypeContract? RequestType = null,
    DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null, string? SearchTerm = null, Guid? DepartmentId = null,
    string SortBy = "submittedDate", string SortDirection = "Ascending");

public interface IRequestsApiClient
{
    Task<WebApiResult<PagedContract<RequestListItemContract>>> GetMineAsync(RequestListQuery query, CancellationToken cancellationToken = default);
    Task<WebApiResult<RequestDetailContract>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WebApiResult<RequestDetailContract>> CreateAsync(string title, string description, RequestTypeContract type, decimal? amount, CancellationToken cancellationToken = default);
    Task<WebApiResult<RequestDetailContract>> UpdateAsync(Guid id, string title, string description, RequestTypeContract type, decimal? amount, string rowVersion, CancellationToken cancellationToken = default);
    Task<WebApiResult<RequestDetailContract>> SubmitAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default);
    Task<WebApiResult<RequestDetailContract>> CancelAsync(Guid id, string rowVersion, CancellationToken cancellationToken = default);
    Task<WebApiResult<IReadOnlyList<ApprovalHistoryContract>>> GetHistoryAsync(Guid id, CancellationToken cancellationToken = default);
}
internal sealed class RequestsApiClient(IBackendApiGateway api) : IRequestsApiClient
{
    public Task<WebApiResult<PagedContract<RequestListItemContract>>> GetMineAsync(RequestListQuery q, CancellationToken ct = default) => api.SendAsync<PagedContract<RequestListItemContract>>(HttpMethod.Get, Query("api/requests", q), cancellationToken: ct);
    public Task<WebApiResult<RequestDetailContract>> GetByIdAsync(Guid id, CancellationToken ct = default) => api.SendAsync<RequestDetailContract>(HttpMethod.Get, $"api/requests/{id}", cancellationToken: ct);
    public Task<WebApiResult<RequestDetailContract>> CreateAsync(string title, string description, RequestTypeContract type, decimal? amount, CancellationToken ct = default) => api.SendAsync<RequestDetailContract>(HttpMethod.Post, "api/requests", new { title, description, requestType = type, amount }, ct);
    public Task<WebApiResult<RequestDetailContract>> UpdateAsync(Guid id, string title, string description, RequestTypeContract type, decimal? amount, string rowVersion, CancellationToken ct = default) => api.SendAsync<RequestDetailContract>(HttpMethod.Put, $"api/requests/{id}", new { title, description, requestType = type, amount, rowVersion }, ct);
    public Task<WebApiResult<RequestDetailContract>> SubmitAsync(Guid id, string rowVersion, CancellationToken ct = default) => api.SendAsync<RequestDetailContract>(HttpMethod.Post, $"api/requests/{id}/submit", new { rowVersion }, ct);
    public Task<WebApiResult<RequestDetailContract>> CancelAsync(Guid id, string rowVersion, CancellationToken ct = default) => api.SendAsync<RequestDetailContract>(HttpMethod.Post, $"api/requests/{id}/cancel", new { rowVersion }, ct);
    public Task<WebApiResult<IReadOnlyList<ApprovalHistoryContract>>> GetHistoryAsync(Guid id, CancellationToken ct = default) => api.SendAsync<IReadOnlyList<ApprovalHistoryContract>>(HttpMethod.Get, $"api/requests/{id}/approval-history", cancellationToken: ct);
    private static string Query(string path, RequestListQuery q) => QueryHelpers.AddQueryString(path, new Dictionary<string, string?>
    {
        ["pageNumber"] = q.PageNumber.ToString(CultureInfo.InvariantCulture),
        ["pageSize"] = q.PageSize.ToString(CultureInfo.InvariantCulture),
        ["status"] = q.Status?.ToString(),
        ["requestType"] = q.RequestType?.ToString(),
        ["createdFrom"] = Value(q.CreatedFrom),
        ["createdTo"] = Value(q.CreatedTo),
        ["searchTerm"] = q.SearchTerm,
        ["sortBy"] = q.SortBy,
        ["sortDirection"] = q.SortDirection
    });
    private static string? Value(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);
}

public interface IApprovalsApiClient
{
    Task<WebApiResult<PagedContract<ApprovalQueueItemContract>>> GetQueueAsync(bool admin, ApprovalListQuery query, CancellationToken cancellationToken = default);
    Task<WebApiResult<ApprovalDetailContract>> GetDetailAsync(bool admin, Guid id, CancellationToken cancellationToken = default);
    Task<WebApiResult<ApprovalDetailContract>> DecideAsync(bool admin, Guid id, bool approve, string rowVersion, string? reason, CancellationToken cancellationToken = default);
}
internal sealed class ApprovalsApiClient(IBackendApiGateway api) : IApprovalsApiClient
{
    public Task<WebApiResult<PagedContract<ApprovalQueueItemContract>>> GetQueueAsync(bool admin, ApprovalListQuery q, CancellationToken ct = default) =>
        api.SendAsync<PagedContract<ApprovalQueueItemContract>>(HttpMethod.Get, QueryHelpers.AddQueryString($"api/approvals/{(admin ? "admin" : "manager")}", new Dictionary<string, string?>
        {
            ["pageNumber"] = q.PageNumber.ToString(CultureInfo.InvariantCulture), ["pageSize"] = q.PageSize.ToString(CultureInfo.InvariantCulture),
            ["requestType"] = q.RequestType?.ToString(), ["createdFrom"] = Value(q.CreatedFrom), ["createdTo"] = Value(q.CreatedTo),
            ["searchTerm"] = q.SearchTerm, ["departmentId"] = admin ? q.DepartmentId?.ToString() : null,
            ["sortBy"] = q.SortBy, ["sortDirection"] = q.SortDirection
        }), cancellationToken: ct);
    public Task<WebApiResult<ApprovalDetailContract>> GetDetailAsync(bool admin, Guid id, CancellationToken ct = default) => api.SendAsync<ApprovalDetailContract>(HttpMethod.Get, $"api/approvals/{(admin ? "admin" : "manager")}/{id}", cancellationToken: ct);
    public Task<WebApiResult<ApprovalDetailContract>> DecideAsync(bool admin, Guid id, bool approve, string rowVersion, string? reason, CancellationToken ct = default) => api.SendAsync<ApprovalDetailContract>(HttpMethod.Post, $"api/approvals/{(admin ? "admin" : "manager")}/{id}/{(approve ? "approve" : "reject")}", approve ? new { rowVersion } : new { rowVersion, reason }, ct);
    private static string? Value(DateTimeOffset? value) => value?.ToString("O", CultureInfo.InvariantCulture);
}
