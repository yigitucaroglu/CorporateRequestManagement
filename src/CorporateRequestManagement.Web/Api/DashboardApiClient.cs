namespace CorporateRequestManagement.Web.Api;

public sealed record DashboardPeriodContract(string PeriodKey, int PrimaryCount, int SecondaryCount);
public sealed record DashboardTrendPointContract(string PeriodKey, int PrimaryCount, int SecondaryCount);
public sealed record DashboardDistributionItemContract(string Label, int Count);
public sealed record DashboardRecentRequestContract(Guid Id, string Title, RequestTypeContract RequestType, RequestStatusContract Status, DateTimeOffset Date);
public sealed record DashboardPendingApprovalContract(Guid RequestId, string Title, string RequesterName, string DepartmentName, decimal? Amount, DateTimeOffset? SubmittedDate);
public sealed record EmployeeDashboardContract(int TotalRequests, int PendingRequests, int ApprovedRequests, int RejectedRequests,
    IReadOnlyList<DashboardPeriodContract> Periods, IReadOnlyList<DashboardTrendPointContract> Trend,
    IReadOnlyList<DashboardDistributionItemContract> StatusDistribution, IReadOnlyList<DashboardRecentRequestContract> RecentRequests);
public sealed record ManagerDashboardContract(int PendingApprovals, int MonthlyIncoming, int MonthlyApproved, int MonthlyRejected,
    IReadOnlyList<DashboardPeriodContract> Periods, IReadOnlyList<DashboardTrendPointContract> Trend,
    IReadOnlyList<DashboardDistributionItemContract> DecisionDistribution, IReadOnlyList<DashboardRecentRequestContract> RecentRequests,
    IReadOnlyList<DashboardPendingApprovalContract> PendingPreview);
public sealed record AdminDashboardContract(int PendingApprovals, int MonthlyCreated, int MonthlyApproved, int MonthlyRejected,
    IReadOnlyList<DashboardPeriodContract> Periods, IReadOnlyList<DashboardTrendPointContract> Trend,
    IReadOnlyList<DashboardDistributionItemContract> StatusDistribution, IReadOnlyList<DashboardDistributionItemContract> DepartmentDistribution,
    IReadOnlyList<DashboardPendingApprovalContract> PendingPreview);

public interface IDashboardApiClient
{
    Task<WebApiResult<EmployeeDashboardContract>> GetEmployeeAsync(CancellationToken cancellationToken = default);
    Task<WebApiResult<ManagerDashboardContract>> GetManagerAsync(CancellationToken cancellationToken = default);
    Task<WebApiResult<AdminDashboardContract>> GetAdminAsync(CancellationToken cancellationToken = default);
}

internal sealed class DashboardApiClient(IBackendApiGateway api) : IDashboardApiClient
{
    public Task<WebApiResult<EmployeeDashboardContract>> GetEmployeeAsync(CancellationToken ct = default) => api.SendAsync<EmployeeDashboardContract>(HttpMethod.Get, "api/dashboard/employee", cancellationToken: ct);
    public Task<WebApiResult<ManagerDashboardContract>> GetManagerAsync(CancellationToken ct = default) => api.SendAsync<ManagerDashboardContract>(HttpMethod.Get, "api/dashboard/manager", cancellationToken: ct);
    public Task<WebApiResult<AdminDashboardContract>> GetAdminAsync(CancellationToken ct = default) => api.SendAsync<AdminDashboardContract>(HttpMethod.Get, "api/dashboard/admin", cancellationToken: ct);
}
