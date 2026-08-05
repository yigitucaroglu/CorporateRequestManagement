using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.Infrastructure.Persistence.Repositories;

internal sealed class RefreshTokenRepository(ApplicationDbContext context) : IRefreshTokenRepository, IUserTokenRevocationRepository
{
    public Task<RefreshToken?> GetByIdAsync(Guid refreshTokenId, CancellationToken cancellationToken = default)
    {
        return context.RefreshTokens.SingleOrDefaultAsync(token => token.Id == refreshTokenId, cancellationToken);
    }

    public Task<RefreshToken?> GetByHashForUpdateAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Refresh token update lock requires an active transaction.");
        return context.RefreshTokens
            .FromSqlInterpolated($"SELECT * FROM [RefreshTokens] WITH (UPDLOCK, HOLDLOCK, ROWLOCK) WHERE [TokenHash] = {tokenHash}")
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default)
    {
        await context.RefreshTokens.AddAsync(refreshToken, cancellationToken);
    }

    public async Task RevokeActiveForUserAsync(Guid userId, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
    {
        List<RefreshToken> tokens = await context.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (RefreshToken token in tokens) token.Revoke(revokedAt, "system", null);
    }
}
