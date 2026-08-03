using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using MediatR;

namespace CorporateRequestManagement.Application.Features.CorporateRequests.GetById;

public sealed record GetCorporateRequestByIdQuery(Guid Id) : IRequest<CorporateRequestDetailDto>;
internal sealed class GetCorporateRequestByIdQueryHandler(ICurrentUserContext currentUser, ICorporateRequestReadRepository requests)
    : IRequestHandler<GetCorporateRequestByIdQuery, CorporateRequestDetailDto>
{
    public async Task<CorporateRequestDetailDto> Handle(GetCorporateRequestByIdQuery query, CancellationToken cancellationToken)
    {
        Guid userId = currentUser.IsAuthenticated && currentUser.UserId.HasValue ? currentUser.UserId.Value : throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
        return await requests.GetOwnedDetailAsync(query.Id, userId, cancellationToken)
            ?? throw new NotFoundException("request.not_found", "Talep bulunamadı.");
    }
}
