using CorporateRequestManagement.Application.Abstractions.Authentication;
using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.Authentication.Common;
using CorporateRequestManagement.Application.Features.Authentication.Login;
using CorporateRequestManagement.Domain.Entities;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.Authentication.Refresh;

public sealed record RefreshTokenCommand(string RawRefreshToken, string ClientIpAddress) : IRequest<AuthenticationResult>;

internal sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RawRefreshToken).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.ClientIpAddress).NotEmpty().MaximumLength(RefreshToken.IpAddressMaxLength);
    }
}

internal sealed class RefreshTokenCommandHandler(
    IRefreshTokenService refreshTokens, IRefreshTokenRepository tokenRepository, IUserRepository users,
    IAccessTokenService accessTokens, IAuditLogRepository audits, IUnitOfWork unitOfWork,
    ITransactionExecutor transactions, IGuidGenerator guids, TimeProvider timeProvider)
    : IRequestHandler<RefreshTokenCommand, AuthenticationResult>
{
    public Task<AuthenticationResult> Handle(RefreshTokenCommand request, CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(async ct =>
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            RefreshToken? old = await tokenRepository.GetByHashForUpdateAsync(refreshTokens.Hash(request.RawRefreshToken), ct);
            if (old is null || !old.IsActive(now)) throw Invalid();
            User? user = await users.GetByIdAsync(old.UserId, ct);
            if (user is null || user.IsDeleted || !user.IsActive) throw Invalid();
            RefreshTokenMaterial material = refreshTokens.Generate(now);
            Guid newId = guids.NewGuid();
            old.Revoke(now, request.ClientIpAddress, newId);
            await tokenRepository.AddAsync(RefreshToken.Create(newId, user.Id, material.TokenHash, material.ExpiresAt, now, request.ClientIpAddress), ct);
            await audits.AddAsync(AuditLog.Create(guids.NewGuid(), user.Id, nameof(RefreshToken), old.Id, AuthenticationAuditActions.RefreshTokenRotated, null, null, now), ct);
            await unitOfWork.SaveChangesAsync(ct);
            return LoginCommandHandler.Result(user, accessTokens.CreateToken(user, now), material);
        }, cancellationToken);

    internal static AuthenticationFailedException Invalid() => new("auth.refresh_token_invalid", "Refresh token geçersiz.");
}
