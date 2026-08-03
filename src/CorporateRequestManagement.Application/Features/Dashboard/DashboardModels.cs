using CorporateRequestManagement.Domain.Enums;

namespace CorporateRequestManagement.Application.Features.Dashboard;

public sealed record DashboardPeriodDto(string PeriodKey, int PrimaryCount, int SecondaryCount);
public sealed record DashboardTrendPointDto(string PeriodKey, int PrimaryCount, int SecondaryCount);
public sealed record DashboardDistributionItemDto(string Label, int Count);
public sealed record DashboardRecentRequestDto(Guid Id, string Title, RequestType RequestType, RequestStatus Status, DateTimeOffset Date);
public sealed record DashboardPendingApprovalDto(Guid RequestId, string Title, string RequesterName, string DepartmentName, decimal? Amount, DateTimeOffset? SubmittedDate);
public sealed record EmployeeDashboardDto(int TotalRequests, int PendingRequests, int ApprovedRequests, int RejectedRequests,
    IReadOnlyList<DashboardPeriodDto> Periods, IReadOnlyList<DashboardTrendPointDto> Trend,
    IReadOnlyList<DashboardDistributionItemDto> StatusDistribution, IReadOnlyList<DashboardRecentRequestDto> RecentRequests);
public sealed record ManagerDashboardDto(int PendingApprovals, int MonthlyIncoming, int MonthlyApproved, int MonthlyRejected,
    IReadOnlyList<DashboardPeriodDto> Periods, IReadOnlyList<DashboardTrendPointDto> Trend,
    IReadOnlyList<DashboardDistributionItemDto> DecisionDistribution, IReadOnlyList<DashboardRecentRequestDto> RecentRequests,
    IReadOnlyList<DashboardPendingApprovalDto> PendingPreview);
public sealed record AdminDashboardDto(int PendingApprovals, int MonthlyCreated, int MonthlyApproved, int MonthlyRejected,
    IReadOnlyList<DashboardPeriodDto> Periods, IReadOnlyList<DashboardTrendPointDto> Trend,
    IReadOnlyList<DashboardDistributionItemDto> StatusDistribution, IReadOnlyList<DashboardDistributionItemDto> DepartmentDistribution,
    IReadOnlyList<DashboardPendingApprovalDto> PendingPreview);
