using CorporateRequestManagement.Application.Abstractions.Authentication;
using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.Authentication.Common;
using CorporateRequestManagement.Domain.Entities;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.Authentication.Login;

public sealed record LoginCommand(string Email, string Password, string ClientIpAddress) : IRequest<AuthenticationResult>;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(User.EmailMaxLength).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ClientIpAddress).NotEmpty().MaximumLength(RefreshToken.IpAddressMaxLength);
    }
}

internal sealed class LoginCommandHandler(
    IUserRepository users, IPasswordService passwords, IAccessTokenService accessTokens,
    IRefreshTokenService refreshTokens, IRefreshTokenRepository tokenRepository,
    IAuditLogRepository audits, IUnitOfWork unitOfWork, IGuidGenerator guids, TimeProvider timeProvider)
    : IRequestHandler<LoginCommand, AuthenticationResult>
{
    public async Task<AuthenticationResult> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        User? user = await users.GetByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null || user.IsDeleted || !user.IsActive || !passwords.VerifyPassword(user, request.Password))
            throw InvalidCredentials();
        DateTimeOffset now = timeProvider.GetUtcNow();
        AccessTokenResult access = accessTokens.CreateToken(user, now);
        RefreshTokenMaterial material = refreshTokens.Generate(now);
        var token = RefreshToken.Create(guids.NewGuid(), user.Id, material.TokenHash, material.ExpiresAt, now, request.ClientIpAddress);
        await tokenRepository.AddAsync(token, cancellationToken);
        await audits.AddAsync(AuditLog.Create(guids.NewGuid(), user.Id, nameof(User), user.Id, AuthenticationAuditActions.LoginSucceeded, null, null, now), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result(user, access, material);
    }

    internal static AuthenticationFailedException InvalidCredentials() => new("auth.invalid_credentials", "E-posta veya parola geçersiz.");
    internal static AuthenticationResult Result(User user, AccessTokenResult access, RefreshTokenMaterial refresh) =>
        new(access, refresh, new(user.Id, user.FirstName, user.LastName, user.Email, user.Role.ToString(), user.DepartmentId));
}
