using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Domain.Entities;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.CorporateRequests.Submit;

public sealed record SubmitCorporateRequestCommand(Guid Id, string RowVersion) : IRequest<CorporateRequestDetailDto>;
internal sealed class SubmitCorporateRequestCommandValidator : AbstractValidator<SubmitCorporateRequestCommand>
{
    public SubmitCorporateRequestCommandValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.RowVersion).Must(value => RowVersionCodec.TryDecode(value, out _)); }
}
internal sealed class SubmitCorporateRequestCommandHandler(ICurrentUserContext currentUser, ICorporateRequestRepository requests,
    IAuditLogRepository audits, IUnitOfWork unitOfWork, IGuidGenerator guids, TimeProvider timeProvider)
    : IRequestHandler<SubmitCorporateRequestCommand, CorporateRequestDetailDto>
{
    public async Task<CorporateRequestDetailDto> Handle(SubmitCorporateRequestCommand command, CancellationToken cancellationToken)
    {
        Guid userId = RequireUser(); CorporateRequest entity = await FindOwned(command.Id, userId, cancellationToken);
        RowVersionCodec.TryDecode(command.RowVersion, out byte[] expected); if (!entity.RowVersion.SequenceEqual(expected)) throw Concurrency();
        DateTimeOffset now = timeProvider.GetUtcNow(); entity.Submit(userId, now);
        await audits.AddAsync(AuditLog.Create(guids.NewGuid(), userId, nameof(CorporateRequest), entity.Id, CorporateRequestAuditActions.Submitted, null, null, now), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken); return entity.ToDetail();
    }
    private Guid RequireUser() => currentUser.IsAuthenticated && currentUser.UserId.HasValue ? currentUser.UserId.Value : throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
    private async Task<CorporateRequest> FindOwned(Guid id, Guid userId, CancellationToken ct) { CorporateRequest? entity = await requests.GetByIdForUpdateAsync(id, ct); return entity is not null && entity.CreatedByUserId == userId ? entity : throw new NotFoundException("request.not_found", "Talep bulunamadı."); }
    private static ConflictException Concurrency() => new("concurrency.conflict", "Kayıt başka bir işlem tarafından değiştirildi.");
}
