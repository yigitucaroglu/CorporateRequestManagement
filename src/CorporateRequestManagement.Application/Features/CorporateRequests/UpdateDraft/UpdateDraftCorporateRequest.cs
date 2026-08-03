using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.CorporateRequests.UpdateDraft;

public sealed record UpdateDraftCorporateRequestCommand(Guid Id, string Title, string Description, RequestType RequestType, decimal? Amount, string RowVersion)
    : IRequest<CorporateRequestDetailDto>;

internal sealed class UpdateDraftCorporateRequestCommandValidator : AbstractValidator<UpdateDraftCorporateRequestCommand>
{
    public UpdateDraftCorporateRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(CorporateRequest.TitleMaxLength);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(CorporateRequest.DescriptionMaxLength);
        RuleFor(x => x.RequestType).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0).When(x => x.Amount.HasValue);
        RuleFor(x => x.Amount).NotNull().When(x => x.RequestType is RequestType.Expense or RequestType.Equipment);
        RuleFor(x => x.Amount).PrecisionScale(18, 2, false).When(x => x.Amount.HasValue);
        RuleFor(x => x.RowVersion).Must(value => RowVersionCodec.TryDecode(value, out _)).WithMessage("RowVersion geçerli 8-byte Base64 olmalıdır.");
    }
}

internal sealed class UpdateDraftCorporateRequestCommandHandler(
    ICurrentUserContext currentUser, ICorporateRequestRepository requests, IAuditLogRepository audits,
    IUnitOfWork unitOfWork, Application.Abstractions.Common.IGuidGenerator guids, TimeProvider timeProvider)
    : IRequestHandler<UpdateDraftCorporateRequestCommand, CorporateRequestDetailDto>
{
    public async Task<CorporateRequestDetailDto> Handle(UpdateDraftCorporateRequestCommand command, CancellationToken cancellationToken)
    {
        Guid userId = RequireUser();
        CorporateRequest entity = await FindOwned(command.Id, userId, cancellationToken);
        RowVersionCodec.TryDecode(command.RowVersion, out byte[] expected);
        if (!entity.RowVersion.SequenceEqual(expected)) throw Concurrency();
        DateTimeOffset now = timeProvider.GetUtcNow();
        entity.UpdateDraft(userId, command.Title, command.Description, command.RequestType, command.Amount, now);
        await audits.AddAsync(AuditLog.Create(guids.NewGuid(), userId, nameof(CorporateRequest), entity.Id,
            CorporateRequestAuditActions.DraftUpdated, null, null, now), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity.ToDetail();
    }
    private Guid RequireUser() => currentUser.IsAuthenticated && currentUser.UserId.HasValue ? currentUser.UserId.Value : throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
    private async Task<CorporateRequest> FindOwned(Guid id, Guid userId, CancellationToken ct)
    { CorporateRequest? entity = await requests.GetByIdForUpdateAsync(id, ct); return entity is not null && entity.CreatedByUserId == userId ? entity : throw new NotFoundException("request.not_found", "Talep bulunamadı."); }
    private static ConflictException Concurrency() => new("concurrency.conflict", "Kayıt başka bir işlem tarafından değiştirildi.");
}
