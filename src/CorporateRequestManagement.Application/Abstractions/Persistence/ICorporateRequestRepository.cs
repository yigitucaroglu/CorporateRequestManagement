using CorporateRequestManagement.Domain.Entities;

namespace CorporateRequestManagement.Application.Abstractions.Persistence;

public interface ICorporateRequestRepository
{
    Task<CorporateRequest?> GetByIdForUpdateAsync(
        Guid requestId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        CorporateRequest request,
        CancellationToken cancellationToken = default);
}
