using CorporateRequestManagement.Application.Common.Exceptions;

namespace CorporateRequestManagement.Application.Common.Models;

public sealed class PagedResult<T>
{
    public PagedResult(
        IEnumerable<T> items,
        int totalCount,
        PageRequest pageRequest)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(pageRequest);

        if (totalCount < 0)
        {
            throw new ApplicationValidationException(
                "pagination.total_count_invalid",
                "Toplam kayıt sayısı negatif olamaz.");
        }

        T[] materializedItems = items.ToArray();
        Items = Array.AsReadOnly(materializedItems);
        TotalCount = totalCount;
        Page = pageRequest.Page;
        PageSize = pageRequest.PageSize;
        TotalPages = totalCount == 0
            ? 0
            : (totalCount / PageSize) + (totalCount % PageSize == 0 ? 0 : 1);
        HasPreviousPage = Page > 1;
        HasNextPage = Page < TotalPages;
    }

    public IReadOnlyList<T> Items { get; }

    public int TotalCount { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int TotalPages { get; }

    public bool HasPreviousPage { get; }

    public bool HasNextPage { get; }

}
