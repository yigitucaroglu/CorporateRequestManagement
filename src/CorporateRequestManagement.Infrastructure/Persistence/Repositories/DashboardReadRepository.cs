using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Features.Dashboard;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace CorporateRequestManagement.Infrastructure.Persistence.Repositories;

internal sealed class DashboardReadRepository(ApplicationDbContext context) : IDashboardReadRepository
{
    public async Task<EmployeeDashboardDto> GetEmployeeAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        IQueryable<CorporateRequest> requests = context.CorporateRequests.AsNoTracking().Where(x => x.CreatedByUserId == userId);
        List<StatusCount> statuses = await requests.GroupBy(x => x.Status).Select(x => new StatusCount(x.Key, x.Count())).ToListAsync(ct);
        DateBounds bounds = DateBounds.Create(now);
        List<DashboardPeriodDto> periods = await PeriodsAsync(requests, bounds, x => x.CreatedDate, x => x.CompletedDate, ct);
        List<DayCount> created = await DayCountsAsync(requests.Select(x => x.CreatedDate), bounds.TrendStart, bounds.Tomorrow, ct);
        List<DashboardRecentRequestDto> recent = await requests.OrderByDescending(x => x.UpdatedDate).ThenByDescending(x => x.Id).Take(5)
            .Select(x => new DashboardRecentRequestDto(x.Id, x.Title, x.RequestType, x.Status, x.UpdatedDate)).ToListAsync(ct);
        return new(statuses.Sum(x => x.Count), Count(statuses, RequestStatus.PendingManagerApproval, RequestStatus.PendingAdminApproval),
            Count(statuses, RequestStatus.Approved), Count(statuses, RequestStatus.Rejected), periods,
            Trend(bounds, created, []), StatusDistribution(statuses), recent);
    }

    public async Task<ManagerDashboardDto> GetManagerAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        IQueryable<Guid> departments = context.Departments.AsNoTracking().Where(x => x.ManagerId == userId).Select(x => x.Id);
        IQueryable<CorporateRequest> incoming = context.CorporateRequests.AsNoTracking()
            .Where(x => departments.Contains(x.DepartmentId) && x.SubmittedDate != null);
        IQueryable<RequestApproval> decisions = context.RequestApprovals.AsNoTracking()
            .Where(x => x.ApprovedByUserId == userId && x.ApprovalLevel == ApprovalLevel.Manager);
        DateBounds bounds = DateBounds.Create(now);
        List<DashboardPeriodDto> periods = await ManagerPeriodsAsync(incoming, decisions, bounds, ct);
        List<DayCount> incomingTrend = await DayCountsAsync(incoming.Where(x => x.SubmittedDate != null).Select(x => x.SubmittedDate!.Value), bounds.TrendStart, bounds.Tomorrow, ct);
        List<DayCount> decisionTrend = await DecisionDayCountsAsync(decisions, bounds.TrendStart, bounds.Tomorrow, ct);
        List<StatusCount> decisionCounts = await decisions.GroupBy(x => x.Status).Select(x => new StatusCount((RequestStatus)(int)x.Key, x.Count())).ToListAsync(ct);
        int pending = await context.CorporateRequests.AsNoTracking().CountAsync(x => departments.Contains(x.DepartmentId)
            && x.Status == RequestStatus.PendingManagerApproval && x.CreatedByUserId != userId, ct);
        List<DashboardRecentRequestDto> recent = await context.CorporateRequests.AsNoTracking().Where(x => x.CreatedByUserId == userId)
            .OrderByDescending(x => x.UpdatedDate).ThenByDescending(x => x.Id).Take(5)
            .Select(x => new DashboardRecentRequestDto(x.Id, x.Title, x.RequestType, x.Status, x.UpdatedDate)).ToListAsync(ct);
        List<DashboardPendingApprovalDto> preview = await PendingPreview(RequestStatus.PendingManagerApproval, userId, departments, true).Take(5).ToListAsync(ct);
        int monthlyIncoming = periods.Single(x => x.PeriodKey == "month").PrimaryCount;
        int monthlyApproved = await decisions.CountAsync(x => x.ActionDate >= bounds.MonthStart && x.ActionDate < bounds.NextMonth && x.Status == ApprovalStatus.Approved, ct);
        int monthlyRejected = await decisions.CountAsync(x => x.ActionDate >= bounds.MonthStart && x.ActionDate < bounds.NextMonth && x.Status == ApprovalStatus.Rejected, ct);
        return new(pending, monthlyIncoming, monthlyApproved, monthlyRejected, periods, Trend(bounds, incomingTrend, decisionTrend),
            [new("Approved", decisionCounts.Where(x => (int)x.Status == (int)ApprovalStatus.Approved).Sum(x => x.Count)), new("Rejected", decisionCounts.Where(x => (int)x.Status == (int)ApprovalStatus.Rejected).Sum(x => x.Count))], recent, preview);
    }

    public async Task<AdminDashboardDto> GetAdminAsync(Guid userId, DateTimeOffset now, CancellationToken ct = default)
    {
        IQueryable<CorporateRequest> requests = context.CorporateRequests.AsNoTracking();
        DateBounds bounds = DateBounds.Create(now);
        List<StatusCount> statuses = await requests.GroupBy(x => x.Status).Select(x => new StatusCount(x.Key, x.Count())).ToListAsync(ct);
        List<DashboardPeriodDto> periods = await PeriodsAsync(requests, bounds, x => x.CreatedDate, x => x.CompletedDate, ct);
        List<MonthCount> created = await MonthCountsAsync(requests.Select(x => x.CreatedDate), bounds.MonthlyTrendStart, bounds.NextMonth, ct);
        List<MonthCount> completed = await MonthCountsAsync(requests.Where(x => x.CompletedDate != null).Select(x => x.CompletedDate!.Value), bounds.MonthlyTrendStart, bounds.NextMonth, ct);
        List<DepartmentCount> top = await (from request in context.CorporateRequests.AsNoTracking()
            join department in context.Departments.AsNoTracking() on request.DepartmentId equals department.Id
            group request by department.Name into grouped orderby grouped.Count() descending, grouped.Key
            select new DepartmentCount(grouped.Key, grouped.Count())).Take(6).ToListAsync(ct);
        IReadOnlyList<DashboardDistributionItemDto> departments = top.Count <= 5
            ? top.Select(x => new DashboardDistributionItemDto(x.Name, x.Count)).ToList()
            : top.Take(5).Select(x => new DashboardDistributionItemDto(x.Name, x.Count)).Append(new("Diğer", statuses.Sum(x => x.Count) - top.Take(5).Sum(x => x.Count))).ToList();
        List<DashboardPendingApprovalDto> preview = await PendingPreview(RequestStatus.PendingAdminApproval, userId, null, false).Take(5).ToListAsync(ct);
        int monthlyCreated = periods.Single(x => x.PeriodKey == "month").PrimaryCount;
        return new(Count(statuses, RequestStatus.PendingAdminApproval), monthlyCreated,
            await requests.CountAsync(x => x.CompletedDate >= bounds.MonthStart && x.CompletedDate < bounds.NextMonth && x.Status == RequestStatus.Approved, ct),
            await requests.CountAsync(x => x.CompletedDate >= bounds.MonthStart && x.CompletedDate < bounds.NextMonth && x.Status == RequestStatus.Rejected, ct),
            periods, MonthlyTrend(bounds, created, completed), StatusDistribution(statuses), departments, preview);
    }

    private IQueryable<DashboardPendingApprovalDto> PendingPreview(RequestStatus status, Guid actor, IQueryable<Guid>? departments, bool manager)
        => from request in context.CorporateRequests.AsNoTracking()
           join requester in context.Users.AsNoTracking() on request.CreatedByUserId equals requester.Id
           join department in context.Departments.AsNoTracking() on request.DepartmentId equals department.Id
           where request.Status == status && request.CreatedByUserId != actor && (!manager || departments!.Contains(request.DepartmentId))
           orderby request.SubmittedDate, request.Id
           select new DashboardPendingApprovalDto(request.Id, request.Title, requester.FirstName + " " + requester.LastName, department.Name, request.Amount, request.SubmittedDate);

    private static async Task<List<DashboardPeriodDto>> PeriodsAsync(IQueryable<CorporateRequest> source, DateBounds b,
        System.Linq.Expressions.Expression<Func<CorporateRequest, DateTimeOffset>> primary,
        System.Linq.Expressions.Expression<Func<CorporateRequest, DateTimeOffset?>> secondary, CancellationToken ct)
    {
        return [new("today", await source.CountAsync(Between(primary, b.Today, b.Tomorrow), ct), await source.CountAsync(BetweenNullable(secondary, b.Today, b.Tomorrow), ct)),
            new("month", await source.CountAsync(Between(primary, b.MonthStart, b.NextMonth), ct), await source.CountAsync(BetweenNullable(secondary, b.MonthStart, b.NextMonth), ct)),
            new("year", await source.CountAsync(Between(primary, b.YearStart, b.NextYear), ct), await source.CountAsync(BetweenNullable(secondary, b.YearStart, b.NextYear), ct))];
    }

    private static async Task<List<DashboardPeriodDto>> ManagerPeriodsAsync(IQueryable<CorporateRequest> incoming, IQueryable<RequestApproval> decisions, DateBounds b, CancellationToken ct)
        => [new("today", await incoming.CountAsync(x => x.SubmittedDate >= b.Today && x.SubmittedDate < b.Tomorrow, ct), await decisions.CountAsync(x => x.ActionDate >= b.Today && x.ActionDate < b.Tomorrow, ct)),
            new("month", await incoming.CountAsync(x => x.SubmittedDate >= b.MonthStart && x.SubmittedDate < b.NextMonth, ct), await decisions.CountAsync(x => x.ActionDate >= b.MonthStart && x.ActionDate < b.NextMonth, ct)),
            new("year", await incoming.CountAsync(x => x.SubmittedDate >= b.YearStart && x.SubmittedDate < b.NextYear, ct), await decisions.CountAsync(x => x.ActionDate >= b.YearStart && x.ActionDate < b.NextYear, ct))];

    private static System.Linq.Expressions.Expression<Func<CorporateRequest, bool>> Between(System.Linq.Expressions.Expression<Func<CorporateRequest, DateTimeOffset>> selector, DateTimeOffset start, DateTimeOffset end)
    { var p=selector.Parameters[0]; return System.Linq.Expressions.Expression.Lambda<Func<CorporateRequest,bool>>(System.Linq.Expressions.Expression.AndAlso(System.Linq.Expressions.Expression.GreaterThanOrEqual(selector.Body,System.Linq.Expressions.Expression.Constant(start)),System.Linq.Expressions.Expression.LessThan(selector.Body,System.Linq.Expressions.Expression.Constant(end))),p); }
    private static System.Linq.Expressions.Expression<Func<CorporateRequest, bool>> BetweenNullable(System.Linq.Expressions.Expression<Func<CorporateRequest, DateTimeOffset?>> selector, DateTimeOffset start, DateTimeOffset end)
    { var p=selector.Parameters[0]; var has=System.Linq.Expressions.Expression.Property(selector.Body,"HasValue"); var val=System.Linq.Expressions.Expression.Property(selector.Body,"Value"); return System.Linq.Expressions.Expression.Lambda<Func<CorporateRequest,bool>>(System.Linq.Expressions.Expression.AndAlso(has,System.Linq.Expressions.Expression.AndAlso(System.Linq.Expressions.Expression.GreaterThanOrEqual(val,System.Linq.Expressions.Expression.Constant(start)),System.Linq.Expressions.Expression.LessThan(val,System.Linq.Expressions.Expression.Constant(end)))),p); }

    private static Task<List<DayCount>> DayCountsAsync(IQueryable<DateTimeOffset> source, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
        => source.Where(x => x >= start && x < end).GroupBy(x => new { x.Year, x.Month, x.Day }).Select(x => new DayCount(x.Key.Year, x.Key.Month, x.Key.Day, x.Count())).ToListAsync(ct);
    private static Task<List<DayCount>> DecisionDayCountsAsync(IQueryable<RequestApproval> source, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
        => source.Where(x => x.ActionDate >= start && x.ActionDate < end).GroupBy(x => new { x.ActionDate.Year, x.ActionDate.Month, x.ActionDate.Day }).Select(x => new DayCount(x.Key.Year, x.Key.Month, x.Key.Day, x.Count())).ToListAsync(ct);
    private static Task<List<MonthCount>> MonthCountsAsync(IQueryable<DateTimeOffset> source, DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
        => source.Where(x => x >= start && x < end).GroupBy(x => new { x.Year, x.Month }).Select(x => new MonthCount(x.Key.Year, x.Key.Month, x.Count())).ToListAsync(ct);
    private static List<DashboardTrendPointDto> Trend(DateBounds b, IReadOnlyList<DayCount> primary, IReadOnlyList<DayCount> secondary)
        => Enumerable.Range(0, 30).Select(i => b.TrendStart.AddDays(i)).Select(x => new DashboardTrendPointDto(x.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), primary.FirstOrDefault(c => c.Year == x.Year && c.Month == x.Month && c.Day == x.Day)?.Count ?? 0, secondary.FirstOrDefault(c => c.Year == x.Year && c.Month == x.Month && c.Day == x.Day)?.Count ?? 0)).ToList();
    private static List<DashboardTrendPointDto> MonthlyTrend(DateBounds b, IReadOnlyList<MonthCount> primary, IReadOnlyList<MonthCount> secondary)
        => Enumerable.Range(0, 12).Select(i => b.MonthlyTrendStart.AddMonths(i)).Select(x => new DashboardTrendPointDto(x.ToString("yyyy-MM", CultureInfo.InvariantCulture), primary.FirstOrDefault(c => c.Year == x.Year && c.Month == x.Month)?.Count ?? 0, secondary.FirstOrDefault(c => c.Year == x.Year && c.Month == x.Month)?.Count ?? 0)).ToList();
    private static List<DashboardDistributionItemDto> StatusDistribution(IEnumerable<StatusCount> values)
        => Enum.GetValues<RequestStatus>().Select(x => new DashboardDistributionItemDto(x.ToString(), values.FirstOrDefault(v => v.Status == x)?.Count ?? 0)).ToList();
    private static int Count(IEnumerable<StatusCount> values, params RequestStatus[] statuses) => values.Where(x => statuses.Contains(x.Status)).Sum(x => x.Count);

    private sealed record StatusCount(RequestStatus Status, int Count);
    private sealed record DayCount(int Year, int Month, int Day, int Count);
    private sealed record MonthCount(int Year, int Month, int Count);
    private sealed record DepartmentCount(string Name, int Count);
    private sealed record DateBounds(DateTimeOffset Today, DateTimeOffset Tomorrow, DateTimeOffset MonthStart, DateTimeOffset NextMonth, DateTimeOffset YearStart, DateTimeOffset NextYear, DateTimeOffset TrendStart, DateTimeOffset MonthlyTrendStart)
    { public static DateBounds Create(DateTimeOffset now) { var today=new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero); var month=new DateTimeOffset(today.Year,today.Month,1,0,0,0,TimeSpan.Zero); var year=new DateTimeOffset(today.Year,1,1,0,0,0,TimeSpan.Zero); return new(today,today.AddDays(1),month,month.AddMonths(1),year,year.AddYears(1),today.AddDays(-29),month.AddMonths(-11)); } }
}
