using CorporateRequestManagement.Domain.Entities;

namespace CorporateRequestManagement.Application.Abstractions.Persistence;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByIdAsync(
        Guid refreshTokenId,
        CancellationToken cancellationToken = default);

    Task<RefreshToken?> GetByHashForUpdateAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        RefreshToken refreshToken,
        CancellationToken cancellationToken = default);
}
