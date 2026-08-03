using System.ComponentModel.DataAnnotations;
using System.Globalization;
using CorporateRequestManagement.Web.Api;

namespace CorporateRequestManagement.Web.Models;

public static class ProductIdentity
{
    public const string Name = "Kurumsal Talep Yönetimi";
    public const string ShortName = "Kurumsal Talep";
}

public sealed record DisplayValue(string Label, string Tone);
public static class UiDisplay
{
    public static DisplayValue Status(RequestStatusContract value) => value switch
    {
        RequestStatusContract.Draft => new("Taslak", "neutral"), RequestStatusContract.PendingManagerApproval => new("Yönetici Onayı Bekliyor", "warning"),
        RequestStatusContract.PendingAdminApproval => new("Yönetim Onayı Bekliyor", "warning"), RequestStatusContract.Approved => new("Onaylandı", "success"),
        RequestStatusContract.Rejected => new("Reddedildi", "danger"), RequestStatusContract.Cancelled => new("İptal Edildi", "neutral"), _ => new("Bilinmiyor", "neutral")
    };
    public static string Type(RequestTypeContract value) => value switch { RequestTypeContract.Expense => "Masraf", RequestTypeContract.Equipment => "Ekipman", RequestTypeContract.Leave => "İzin", RequestTypeContract.Other => "Diğer", _ => "Bilinmiyor" };
    public static string Role(string? value) => value switch { "Employee" => "Çalışan", "Manager" => "Yönetici", "Admin" => "Sistem Yöneticisi", _ => "Bilinmiyor" };
    public static string AnalyticsLabel(string? value) => value switch
    {
        "Draft" => "Taslak", "PendingManagerApproval" => "Yönetici Onayı Bekliyor",
        "PendingAdminApproval" => "Yönetim Onayı Bekliyor", "Approved" => "Onaylandı",
        "Rejected" => "Reddedildi", "Cancelled" => "İptal Edildi", _ => "Bilinmiyor"
    };
    public static bool TryDailyPeriodLabels(string periodKey, out string axisLabel, out string tooltipLabel)
    {
        if (DateOnly.TryParseExact(periodKey, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly value))
        {
            axisLabel = value.ToString("dd.MM", CultureInfo.InvariantCulture);
            tooltipLabel = value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            return true;
        }
        axisLabel = tooltipLabel = string.Empty;
        return false;
    }
    public static bool TryMonthlyPeriodLabels(string periodKey, out string axisLabel, out string tooltipLabel)
    {
        if (DateOnly.TryParseExact($"{periodKey}-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly value))
        {
            CultureInfo culture = CultureInfo.GetCultureInfo("tr-TR");
            axisLabel = value.ToString("MMM yy", culture);
            tooltipLabel = value.ToString("MMMM yyyy", culture);
            return true;
        }
        axisLabel = tooltipLabel = string.Empty;
        return false;
    }
    public static string Level(ApprovalLevelContract value) => value switch { ApprovalLevelContract.Manager => "Yönetici", ApprovalLevelContract.Admin => "Yönetim", _ => "Bilinmiyor" };
    public static DisplayValue Approval(ApprovalStatusContract value) => value switch { ApprovalStatusContract.Approved => new("Onaylandı", "success"), ApprovalStatusContract.Rejected => new("Reddedildi", "danger"), _ => new("Bilinmiyor", "neutral") };
}

public sealed class RequestInputModel
{
    public Guid Id { get; set; }
    [Required(ErrorMessage = "Başlık zorunludur."), StringLength(200, ErrorMessage = "Başlık en fazla 200 karakter olabilir.")]
    public string Title { get; set; } = string.Empty;
    [Required(ErrorMessage = "Açıklama zorunludur."), StringLength(2000, ErrorMessage = "Açıklama en fazla 2000 karakter olabilir.")]
    public string Description { get; set; } = string.Empty;
    [Required(ErrorMessage = "Talep türü zorunludur.")]
    public RequestTypeContract RequestType { get; set; }
    [PositiveDecimal(ErrorMessage = "Tutar sıfırdan büyük olmalıdır.")]
    public decimal? Amount { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
internal sealed class PositiveDecimalAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is null || value is decimal amount && amount > decimal.Zero;
}
public sealed record RequestListPage(PagedContract<RequestListItemContract> Result, RequestListQuery Query);
public sealed record RequestDetailsPage(RequestDetailContract Request, IReadOnlyList<ApprovalHistoryContract> History);
public sealed record ApprovalQueuePage(PagedContract<ApprovalQueueItemContract> Result, bool IsAdmin, ApprovalListQuery Query);

public sealed record PaginationViewModel(
    int CurrentPage,
    int PageSize,
    int TotalCount,
    int TotalPages,
    bool HasPreviousPage,
    bool HasNextPage,
    IReadOnlyDictionary<string, string?> QueryParameters)
{
    public static readonly int[] AllowedPageSizes = [10, 20, 50, 100];

    public IEnumerable<int?> PageWindow()
    {
        if (TotalPages <= 0)
        {
            yield break;
        }

        var pages = new SortedSet<int> { 1, TotalPages };
        for (int page = Math.Max(1, CurrentPage - 2); page <= Math.Min(TotalPages, CurrentPage + 2); page++)
        {
            pages.Add(page);
        }

        int previous = 0;
        foreach (int page in pages)
        {
            if (previous != 0 && page - previous > 1)
            {
                yield return null;
            }

            yield return page;
            previous = page;
        }
    }
}

public static class WebPaging
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;

    public static int Page(int value) => value < DefaultPage ? DefaultPage : value;
    public static int PageSize(int value) => PaginationViewModel.AllowedPageSizes.Contains(value) ? value : DefaultPageSize;
}
public sealed record ApprovalDetailsPage(ApprovalDetailContract Request, bool IsAdmin);
public sealed class DecisionInputModel
{
    public Guid Id { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    [StringLength(1000, ErrorMessage = "Ret gerekçesi en fazla 1000 karakter olabilir.")] public string? Reason { get; set; }
}
