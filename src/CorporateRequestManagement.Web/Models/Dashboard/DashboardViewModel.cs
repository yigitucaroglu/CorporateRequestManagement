using CorporateRequestManagement.Web.Api;

namespace CorporateRequestManagement.Web.Models.Dashboard;

public sealed record DashboardViewModel(string FullName, string Role, object Analytics);
public sealed record EmployeeDashboardViewModel(string FullName, EmployeeDashboardContract Analytics);
public sealed record ManagerDashboardViewModel(string FullName, ManagerDashboardContract Analytics);
public sealed record AdminDashboardViewModel(string FullName, AdminDashboardContract Analytics);

public enum DashboardTrendGranularity { Daily, Monthly }

public sealed record DashboardTrendPointViewModel(string PeriodKey, string AxisLabel, string TooltipLabel, int PrimaryCount, int SecondaryCount);

public sealed record DashboardTrendViewModel(
    IReadOnlyList<DashboardTrendPointViewModel> Points,
    string Title,
    string Subtitle,
    string PeriodLabel,
    string SummaryPeriod,
    string Primary,
    string Secondary,
    DashboardTrendGranularity Granularity,
    bool IsValid)
{
    public static DashboardTrendViewModel Create(
        IReadOnlyList<DashboardTrendPointContract> points,
        string title,
        string primary,
        string secondary,
        DashboardTrendGranularity granularity)
    {
        var mapped = new List<DashboardTrendPointViewModel>(points.Count);
        foreach (DashboardTrendPointContract point in points)
        {
            bool valid = granularity == DashboardTrendGranularity.Daily
                ? UiDisplay.TryDailyPeriodLabels(point.PeriodKey, out string axis, out string tooltip)
                : UiDisplay.TryMonthlyPeriodLabels(point.PeriodKey, out axis, out tooltip);
            if (!valid)
                return Invalid(title, primary, secondary, granularity);
            mapped.Add(new(point.PeriodKey, axis, tooltip, point.PrimaryCount, point.SecondaryCount));
        }
        int expectedPointCount = granularity == DashboardTrendGranularity.Daily ? 30 : 12;
        bool aligned = mapped.Count == expectedPointCount
            && mapped.Zip(mapped.Skip(1), static (left, right) => string.CompareOrdinal(left.PeriodKey, right.PeriodKey) < 0).All(static ordered => ordered);
        return aligned
            ? new(mapped, title, granularity == DashboardTrendGranularity.Daily ? "Günlük talep adetleri" : "Aylık talep adetleri", granularity == DashboardTrendGranularity.Daily ? "Son 30 Gün" : "Son 12 Ay", granularity == DashboardTrendGranularity.Daily ? "Son 30 günde" : "Son 12 ayda", primary, secondary, granularity, true)
            : Invalid(title, primary, secondary, granularity);
    }

    private static DashboardTrendViewModel Invalid(string title, string primary, string secondary, DashboardTrendGranularity granularity) =>
        new([], title, granularity == DashboardTrendGranularity.Daily ? "Günlük talep adetleri" : "Aylık talep adetleri", granularity == DashboardTrendGranularity.Daily ? "Son 30 Gün" : "Son 12 Ay", granularity == DashboardTrendGranularity.Daily ? "Son 30 günde" : "Son 12 ayda", primary, secondary, granularity, false);
}
