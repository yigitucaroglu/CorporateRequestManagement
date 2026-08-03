using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;

namespace CorporateRequestManagement.Application.Abstractions.Persistence;

public interface ICorporateRequestReadRepository
{
    Task<CorporateRequestDetailDto?> GetOwnedDetailAsync(Guid requestId, Guid ownerId, CancellationToken cancellationToken = default);
    Task<PagedResult<CorporateRequestListItemDto>> GetOwnedPageAsync(Guid ownerId, CorporateRequestListCriteria criteria, CancellationToken cancellationToken = default);
}
