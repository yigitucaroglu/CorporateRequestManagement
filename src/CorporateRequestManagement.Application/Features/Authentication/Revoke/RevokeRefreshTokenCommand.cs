using CorporateRequestManagement.Application.Abstractions.Authentication;
using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Features.Authentication.Common;
using CorporateRequestManagement.Application.Features.Authentication.Refresh;
using CorporateRequestManagement.Domain.Entities;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.Authentication.Revoke;

public sealed record RevokeRefreshTokenCommand(string RawRefreshToken, string ClientIpAddress) : IRequest;
internal sealed class RevokeRefreshTokenCommandValidator : AbstractValidator<RevokeRefreshTokenCommand>
{
    public RevokeRefreshTokenCommandValidator()
    {
        RuleFor(x => x.RawRefreshToken).NotEmpty().MaximumLength(2048);
        RuleFor(x => x.ClientIpAddress).NotEmpty().MaximumLength(RefreshToken.IpAddressMaxLength);
    }
}
internal sealed class RevokeRefreshTokenCommandHandler(
    IRefreshTokenService refreshTokens, IRefreshTokenRepository tokens, IUserRepository users,
    IAuditLogRepository audits, IUnitOfWork unitOfWork, ITransactionExecutor transactions,
    IGuidGenerator guids, TimeProvider timeProvider) : IRequestHandler<RevokeRefreshTokenCommand>
{
    public Task Handle(RevokeRefreshTokenCommand request, CancellationToken cancellationToken) => transactions.ExecuteAsync(async ct =>
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        RefreshToken? token = await tokens.GetByHashForUpdateAsync(refreshTokens.Hash(request.RawRefreshToken), ct);
        if (token is null || !token.IsActive(now)) throw RefreshTokenCommandHandler.Invalid();
        User? user = await users.GetByIdAsync(token.UserId, ct);
        if (user is null || user.IsDeleted || !user.IsActive) throw RefreshTokenCommandHandler.Invalid();
        token.Revoke(now, request.ClientIpAddress, null);
        await audits.AddAsync(AuditLog.Create(guids.NewGuid(), user.Id, nameof(RefreshToken), token.Id, AuthenticationAuditActions.RefreshTokenRevoked, null, null, now), ct);
        await unitOfWork.SaveChangesAsync(ct);
    }, cancellationToken);
}
