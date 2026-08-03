using CorporateRequestManagement.Application.Abstractions.Approvals;
using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.Approvals.Common;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.Approvals.Decisions;

public sealed record ManagerApproveCommand(Guid RequestId, string RowVersion) : IRequest<ApprovalRequestDetailDto>;
public sealed record ManagerRejectCommand(Guid RequestId, string RowVersion, string Reason) : IRequest<ApprovalRequestDetailDto>;
public sealed record AdminApproveCommand(Guid RequestId, string RowVersion) : IRequest<ApprovalRequestDetailDto>;
public sealed record AdminRejectCommand(Guid RequestId, string RowVersion, string Reason) : IRequest<ApprovalRequestDetailDto>;

internal sealed class ManagerApproveCommandValidator : AbstractValidator<ManagerApproveCommand>
{ public ManagerApproveCommandValidator() { RuleFor(x => x.RequestId).NotEmpty(); RuleFor(x => x.RowVersion).Must(ValidVersion); } private static bool ValidVersion(string value) => RowVersionCodec.TryDecode(value, out _); }
internal sealed class AdminApproveCommandValidator : AbstractValidator<AdminApproveCommand>
{ public AdminApproveCommandValidator() { RuleFor(x => x.RequestId).NotEmpty(); RuleFor(x => x.RowVersion).Must(ValidVersion); } private static bool ValidVersion(string value) => RowVersionCodec.TryDecode(value, out _); }
internal sealed class ManagerRejectCommandValidator : AbstractValidator<ManagerRejectCommand>
{ public ManagerRejectCommandValidator() { RuleFor(x => x.RequestId).NotEmpty(); RuleFor(x => x.RowVersion).Must(ValidVersion); RuleFor(x => x.Reason).NotEmpty().MaximumLength(CorporateRequest.RejectionDescriptionMaxLength); } private static bool ValidVersion(string value) => RowVersionCodec.TryDecode(value, out _); }
internal sealed class AdminRejectCommandValidator : AbstractValidator<AdminRejectCommand>
{ public AdminRejectCommandValidator() { RuleFor(x => x.RequestId).NotEmpty(); RuleFor(x => x.RowVersion).Must(ValidVersion); RuleFor(x => x.Reason).NotEmpty().MaximumLength(CorporateRequest.RejectionDescriptionMaxLength); } private static bool ValidVersion(string value) => RowVersionCodec.TryDecode(value, out _); }

internal sealed class ManagerApproveCommandHandler(ICurrentUserContext current, IUserRepository users, ICorporateRequestRepository requests,
    IApprovalReadRepository reads, IApprovalThresholdProvider threshold, IAuditLogRepository audits, IUnitOfWork unitOfWork,
    IGuidGenerator guids, TimeProvider time) : IRequestHandler<ManagerApproveCommand, ApprovalRequestDetailDto>
{
    public async Task<ApprovalRequestDetailDto> Handle(ManagerApproveCommand command, CancellationToken ct)
    {
        Guid actor = await ApprovalDecisionGuard.RequireActorAsync(current, users, UserRole.Manager, ct);
        CorporateRequest request = await ApprovalDecisionGuard.ManagerRequestAsync(requests, command.RequestId, actor, ct);
        ApprovalDecisionGuard.Check(request, actor, ApprovalLevel.Manager, command.RowVersion);
        DateTimeOffset now = time.GetUtcNow(); request.ApproveByManager(guids.NewGuid(), actor, threshold.Threshold, now);
        await ApprovalDecisionGuard.AuditAsync(audits, guids, actor, request.Id, ApprovalAuditActions.ManagerApproved, now, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return await reads.GetDecisionResultAsync(request.Id, ct) ?? throw ApprovalDecisionGuard.NotFound();
    }
}

internal sealed class ManagerRejectCommandHandler(ICurrentUserContext current, IUserRepository users, ICorporateRequestRepository requests,
    IApprovalReadRepository reads, IAuditLogRepository audits, IUnitOfWork unitOfWork, IGuidGenerator guids, TimeProvider time)
    : IRequestHandler<ManagerRejectCommand, ApprovalRequestDetailDto>
{
    public async Task<ApprovalRequestDetailDto> Handle(ManagerRejectCommand command, CancellationToken ct)
    {
        Guid actor = await ApprovalDecisionGuard.RequireActorAsync(current, users, UserRole.Manager, ct);
        CorporateRequest request = await ApprovalDecisionGuard.ManagerRequestAsync(requests, command.RequestId, actor, ct);
        ApprovalDecisionGuard.Check(request, actor, ApprovalLevel.Manager, command.RowVersion);
        DateTimeOffset now = time.GetUtcNow(); request.RejectByManager(guids.NewGuid(), actor, command.Reason, now);
        await ApprovalDecisionGuard.AuditAsync(audits, guids, actor, request.Id, ApprovalAuditActions.ManagerRejected, now, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return await reads.GetDecisionResultAsync(request.Id, ct) ?? throw ApprovalDecisionGuard.NotFound();
    }
}

internal sealed class AdminApproveCommandHandler(ICurrentUserContext current, IUserRepository users, ICorporateRequestRepository requests,
    IApprovalReadRepository reads, IAuditLogRepository audits, IUnitOfWork unitOfWork, IGuidGenerator guids, TimeProvider time)
    : IRequestHandler<AdminApproveCommand, ApprovalRequestDetailDto>
{
    public async Task<ApprovalRequestDetailDto> Handle(AdminApproveCommand command, CancellationToken ct)
    {
        Guid actor = await ApprovalDecisionGuard.RequireActorAsync(current, users, UserRole.Admin, ct);
        CorporateRequest request = await ApprovalDecisionGuard.AdminRequestAsync(requests, command.RequestId, ct);
        ApprovalDecisionGuard.Check(request, actor, ApprovalLevel.Admin, command.RowVersion);
        DateTimeOffset now = time.GetUtcNow(); request.ApproveByAdmin(guids.NewGuid(), actor, now);
        await ApprovalDecisionGuard.AuditAsync(audits, guids, actor, request.Id, ApprovalAuditActions.AdminApproved, now, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return await reads.GetDecisionResultAsync(request.Id, ct) ?? throw ApprovalDecisionGuard.NotFound();
    }
}

internal sealed class AdminRejectCommandHandler(ICurrentUserContext current, IUserRepository users, ICorporateRequestRepository requests,
    IApprovalReadRepository reads, IAuditLogRepository audits, IUnitOfWork unitOfWork, IGuidGenerator guids, TimeProvider time)
    : IRequestHandler<AdminRejectCommand, ApprovalRequestDetailDto>
{
    public async Task<ApprovalRequestDetailDto> Handle(AdminRejectCommand command, CancellationToken ct)
    {
        Guid actor = await ApprovalDecisionGuard.RequireActorAsync(current, users, UserRole.Admin, ct);
        CorporateRequest request = await ApprovalDecisionGuard.AdminRequestAsync(requests, command.RequestId, ct);
        ApprovalDecisionGuard.Check(request, actor, ApprovalLevel.Admin, command.RowVersion);
        DateTimeOffset now = time.GetUtcNow(); request.RejectByAdmin(guids.NewGuid(), actor, command.Reason, now);
        await ApprovalDecisionGuard.AuditAsync(audits, guids, actor, request.Id, ApprovalAuditActions.AdminRejected, now, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return await reads.GetDecisionResultAsync(request.Id, ct) ?? throw ApprovalDecisionGuard.NotFound();
    }
}

internal static class ApprovalDecisionGuard
{
    public static async Task<Guid> RequireActorAsync(ICurrentUserContext current, IUserRepository users, UserRole role, CancellationToken ct)
    {
        if (!current.IsAuthenticated || !current.UserId.HasValue) throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
        User? user = await users.GetByIdAsync(current.UserId.Value, ct);
        if (user is null || user.IsDeleted || !user.IsActive || user.Role != role) throw new ForbiddenAccessException("approval.role_required", "Bu onay işlemi için gerekli role sahip değilsiniz.");
        return user.Id;
    }
    public static async Task<CorporateRequest> ManagerRequestAsync(ICorporateRequestRepository repository, Guid id, Guid actor, CancellationToken ct) =>
        await repository.GetForManagerDecisionAsync(id, actor, ct) ?? throw NotFound();
    public static async Task<CorporateRequest> AdminRequestAsync(ICorporateRequestRepository repository, Guid id, CancellationToken ct) =>
        await repository.GetForAdminDecisionAsync(id, ct) ?? throw NotFound();
    public static void Check(CorporateRequest request, Guid actor, ApprovalLevel level, string encodedVersion)
    {
        if (request.CreatedByUserId == actor) throw new ConflictException("approval.self_action_forbidden", "Kullanıcı kendi talebi üzerinde karar veremez.");
        RowVersionCodec.TryDecode(encodedVersion, out byte[] version);
        if (!request.RowVersion.SequenceEqual(version)) throw new ConflictException("concurrency.conflict", "Kayıt başka bir işlem tarafından değiştirilmiş.");
        if (request.Approvals.Any(x => x.ApprovalLevel == level)) throw new ConflictException("approval.already_processed", "Bu onay seviyesi daha önce işlenmiş.");
    }
    public static Task AuditAsync(IAuditLogRepository audits, IGuidGenerator guids, Guid actor, Guid requestId, string action, DateTimeOffset now, CancellationToken ct) =>
        audits.AddAsync(AuditLog.Create(guids.NewGuid(), actor, nameof(CorporateRequest), requestId, action, null, null, now), ct);
    public static NotFoundException NotFound() => new("approval.request_not_found", "Onay talebi bulunamadı.");
}
