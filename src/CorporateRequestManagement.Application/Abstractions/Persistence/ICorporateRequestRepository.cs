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

    Task<CorporateRequest?> GetForManagerDecisionAsync(Guid requestId, Guid managerUserId, CancellationToken cancellationToken = default);

    Task<CorporateRequest?> GetForAdminDecisionAsync(Guid requestId, CancellationToken cancellationToken = default);
}
