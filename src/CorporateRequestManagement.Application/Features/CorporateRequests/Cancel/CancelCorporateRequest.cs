using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Domain.Entities;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.CorporateRequests.Cancel;

public sealed record CancelCorporateRequestCommand(Guid Id, string RowVersion) : IRequest<CorporateRequestDetailDto>;
internal sealed class CancelCorporateRequestCommandValidator : AbstractValidator<CancelCorporateRequestCommand>
{
    public CancelCorporateRequestCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.RowVersion).Must(value => RowVersionCodec.TryDecode(value, out _)); }
}
internal sealed class CancelCorporateRequestCommandHandler(ICurrentUserContext currentUser, ICorporateRequestRepository requests,
    IAuditLogRepository audits, IUnitOfWork unitOfWork, IGuidGenerator guids, TimeProvider timeProvider)
    : IRequestHandler<CancelCorporateRequestCommand, CorporateRequestDetailDto>
{
    public async Task<CorporateRequestDetailDto> Handle(CancelCorporateRequestCommand command, CancellationToken cancellationToken)
    {
        Guid userId = RequireUser(); CorporateRequest entity = await FindOwned(command.Id, userId, cancellationToken);
        RowVersionCodec.TryDecode(command.RowVersion, out byte[] expected); if (!entity.RowVersion.SequenceEqual(expected)) throw Concurrency();
        DateTimeOffset now = timeProvider.GetUtcNow(); entity.CancelByOwner(userId, now);
        await audits.AddAsync(AuditLog.Create(guids.NewGuid(), userId, nameof(CorporateRequest), entity.Id, CorporateRequestAuditActions.Cancelled, null, null, now), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken); return entity.ToDetail();
    }
    private Guid RequireUser() => currentUser.IsAuthenticated && currentUser.UserId.HasValue ? currentUser.UserId.Value : throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
    private async Task<CorporateRequest> FindOwned(Guid id, Guid userId, CancellationToken ct) { CorporateRequest? entity = await requests.GetByIdForUpdateAsync(id, ct); return entity is not null && entity.CreatedByUserId == userId ? entity : throw new NotFoundException("request.not_found", "Talep bulunamadı."); }
    private static ConflictException Concurrency() => new("concurrency.conflict", "Kayıt başka bir işlem tarafından değiştirildi.");
}
