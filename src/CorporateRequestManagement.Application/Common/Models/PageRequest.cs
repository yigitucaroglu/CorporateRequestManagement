using CorporateRequestManagement.Application.Common.Exceptions;

namespace CorporateRequestManagement.Application.Common.Models;

public sealed record PageRequest
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private PageRequest(int page, int pageSize, int skip)
    {
        Page = page;
        PageSize = pageSize;
        Skip = skip;
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip { get; }

    public static PageRequest Create(
        int page = DefaultPage,
        int pageSize = DefaultPageSize)
    {
        if (page < 1)
        {
            throw new ApplicationValidationException(
                "pagination.page_invalid",
                "Sayfa numarası en az 1 olmalıdır.");
        }

        if (pageSize < 1 || pageSize > MaxPageSize)
        {
            throw new ApplicationValidationException(
                "pagination.page_size_invalid",
                "Sayfa boyutu izin verilen aralıkta olmalıdır.");
        }

        int skip;

        try
        {
            skip = checked((page - 1) * pageSize);
        }
        catch (OverflowException)
        {
            throw new ApplicationValidationException(
                "pagination.skip_overflow",
                "Sayfalama değeri izin verilen aralığı aşıyor.");
        }

        return new PageRequest(page, pageSize, skip);
    }
}
