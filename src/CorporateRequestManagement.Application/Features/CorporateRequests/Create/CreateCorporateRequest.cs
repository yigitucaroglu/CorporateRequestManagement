using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.CorporateRequests.Create;

public sealed record CreateCorporateRequestCommand(string Title, string Description, RequestType RequestType, decimal? Amount)
    : IRequest<CorporateRequestDetailDto>;

internal sealed class CreateCorporateRequestCommandValidator : AbstractValidator<CreateCorporateRequestCommand>
{
    public CreateCorporateRequestCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(CorporateRequest.TitleMaxLength);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(CorporateRequest.DescriptionMaxLength);
        RuleFor(x => x.RequestType).IsInEnum();
        RuleFor(x => x.Amount).GreaterThan(0).When(x => x.Amount.HasValue);
        RuleFor(x => x.Amount).NotNull().When(x => x.RequestType is RequestType.Expense or RequestType.Equipment);
        RuleFor(x => x.Amount).PrecisionScale(18, 2, false).When(x => x.Amount.HasValue);
    }
}

internal sealed class CreateCorporateRequestCommandHandler(
    ICurrentUserContext currentUser, IUserRepository users, IDepartmentRepository departments,
    ICorporateRequestRepository requests, IAuditLogRepository audits, IUnitOfWork unitOfWork,
    IGuidGenerator guids, TimeProvider timeProvider) : IRequestHandler<CreateCorporateRequestCommand, CorporateRequestDetailDto>
{
    public async Task<CorporateRequestDetailDto> Handle(CreateCorporateRequestCommand command, CancellationToken cancellationToken)
    {
        Guid userId = RequireUser();
        User? user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null || user.IsDeleted || !user.IsActive)
            throw new AuthenticationFailedException("auth.user_invalid", "Kimliği doğrulanmış kullanıcı artık kullanılamıyor.");
        if (!user.DepartmentId.HasValue)
            throw new ConflictException("request.department_required", "Talep oluşturmak için geçerli bir departman gereklidir.");
        Department? department = await departments.GetByIdAsync(user.DepartmentId.Value, cancellationToken);
        if (department is null || department.IsDeleted)
            throw new ConflictException("request.department_unavailable", "Talep departmanı kullanılamıyor.");
        DateTimeOffset now = timeProvider.GetUtcNow();
        CorporateRequest entity = CorporateRequest.Create(guids.NewGuid(), command.Title, command.Description,
            command.RequestType, command.Amount, userId, department.Id, now);
        await requests.AddAsync(entity, cancellationToken);
        await audits.AddAsync(AuditLog.Create(guids.NewGuid(), userId, nameof(CorporateRequest), entity.Id,
            CorporateRequestAuditActions.Created, null, null, now), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return entity.ToDetail(department.Name);
    }

    private Guid RequireUser() => currentUser.IsAuthenticated && currentUser.UserId.HasValue
        ? currentUser.UserId.Value
        : throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
}
