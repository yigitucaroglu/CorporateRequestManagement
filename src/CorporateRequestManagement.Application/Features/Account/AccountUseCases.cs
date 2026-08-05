using CorporateRequestManagement.Application.Abstractions.Authentication;
using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.Administration;
using CorporateRequestManagement.Domain.Entities;
using MediatR;
using FluentValidation;

namespace CorporateRequestManagement.Application.Features.Account;

public sealed record GetProfileQuery : IRequest<AdminUserDto>;
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword, string ConfirmNewPassword) : IRequest;

internal sealed class ChangePasswordValidator : FluentValidation.AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.ConfirmNewPassword).NotEmpty().WithMessage("Yeni parola tekrarı zorunludur.")
            .Equal(x => x.NewPassword).WithMessage("Yeni parola tekrarı yeni parola ile eşleşmelidir.");
    }
}

internal sealed class GetProfileHandler(ICurrentUserContext current, IAdministrationQueryService queries) : IRequestHandler<GetProfileQuery, AdminUserDto>
{
    public async Task<AdminUserDto> Handle(GetProfileQuery query, CancellationToken ct)
    {
        Guid actor = current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw Unauthorized();
        return await queries.GetUserAsync(actor, ct) ?? throw new NotFoundException("user.not_found", "Kullanıcı bulunamadı.");
    }
    private static AuthenticationFailedException Unauthorized() => new("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
}

internal sealed class ChangePasswordHandler(ICurrentUserContext current, IUserRepository users, IPasswordService passwords,
    IUserTokenRevocationRepository tokens, IAuditLogRepository audits, IUnitOfWork unit, ITransactionExecutor transactions,
    IGuidGenerator guids, TimeProvider time) : IRequestHandler<ChangePasswordCommand>
{
    public async Task Handle(ChangePasswordCommand command, CancellationToken ct)
    {
        User user = await users.GetByIdAsync(Actor(), ct) ?? throw new AuthenticationFailedException("auth.invalid_credentials", "Kimlik bilgileri doğrulanamadı.");
        if (!user.IsActive || !passwords.VerifyPassword(user, command.CurrentPassword)) throw new AuthenticationFailedException("auth.invalid_credentials", "Kimlik bilgileri doğrulanamadı.");
        PasswordRules.Ensure(command.NewPassword);
        if (passwords.VerifyPassword(user, command.NewPassword)) throw new ConflictException("password.same_as_current", "Yeni parola mevcut paroladan farklı olmalıdır.");
        DateTimeOffset now = time.GetUtcNow();
        await transactions.ExecuteAsync(async token => { user.ChangePasswordHash(passwords.HashPassword(user, command.NewPassword)); await tokens.RevokeActiveForUserAsync(user.Id, now, token); await audits.AddAsync(AuditLog.Create(guids.NewGuid(), user.Id, "User", user.Id, "User.PasswordChanged", null, null, now), token); await unit.SaveChangesAsync(token); }, ct);
    }
    private Guid Actor() => current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
}
