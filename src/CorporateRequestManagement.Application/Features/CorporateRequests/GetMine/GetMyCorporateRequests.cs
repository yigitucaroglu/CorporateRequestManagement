using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Domain.Enums;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.CorporateRequests.GetMine;

public sealed record GetMyCorporateRequestsQuery(int PageNumber = PageRequest.DefaultPage, int PageSize = PageRequest.DefaultPageSize,
    RequestStatus? Status = null, RequestType? RequestType = null, DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null, string? SearchTerm = null, string SortBy = CorporateRequestSortFields.CreatedDate,
    SortDirection SortDirection = SortDirection.Descending) : IRequest<PagedResult<CorporateRequestListItemDto>>;

internal sealed class GetMyCorporateRequestsQueryValidator : AbstractValidator<GetMyCorporateRequestsQuery>
{
    public GetMyCorporateRequestsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(x => x.Status).Must(value => !value.HasValue || Enum.IsDefined(value.Value));
        RuleFor(x => x.RequestType).Must(value => !value.HasValue || Enum.IsDefined(value.Value));
        RuleFor(x => x.CreatedTo).GreaterThanOrEqualTo(x => x.CreatedFrom).When(x => x.CreatedFrom.HasValue && x.CreatedTo.HasValue);
        RuleFor(x => x.SearchTerm).Must(value => CorporateRequestSearch.Normalize(value) is not { } normalized || normalized.Length <= CorporateRequestSearch.MaxLength)
            .WithMessage($"Arama metni en fazla {CorporateRequestSearch.MaxLength} karakter olabilir.");
        RuleFor(x => x.SortBy).Must(CorporateRequestSortFields.All.Contains);
        RuleFor(x => x.SortDirection).IsInEnum();
    }
}
internal sealed class GetMyCorporateRequestsQueryHandler(ICurrentUserContext currentUser, ICorporateRequestReadRepository requests)
    : IRequestHandler<GetMyCorporateRequestsQuery, PagedResult<CorporateRequestListItemDto>>
{
    public Task<PagedResult<CorporateRequestListItemDto>> Handle(GetMyCorporateRequestsQuery query, CancellationToken cancellationToken)
    {
        Guid userId = currentUser.IsAuthenticated && currentUser.UserId.HasValue ? currentUser.UserId.Value : throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
        return requests.GetOwnedPageAsync(userId, new CorporateRequestListCriteria(query.PageNumber, query.PageSize, query.Status,
            query.RequestType, query.CreatedFrom, query.CreatedTo, CorporateRequestSearch.Normalize(query.SearchTerm), query.SortBy, query.SortDirection), cancellationToken);
    }
}
