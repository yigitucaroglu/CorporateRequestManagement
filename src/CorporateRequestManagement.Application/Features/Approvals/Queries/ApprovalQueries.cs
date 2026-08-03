using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.Approvals.Common;
using CorporateRequestManagement.Application.Features.Approvals.Decisions;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.Approvals.Queries;

public sealed record GetManagerApprovalQueueQuery(int PageNumber = 1, int PageSize = 20, RequestType? RequestType = null,
    DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null, string SortBy = ApprovalSortFields.SubmittedDate,
    SortDirection SortDirection = SortDirection.Ascending) : IRequest<PagedResult<ApprovalQueueItemDto>>;
public sealed record GetAdminApprovalQueueQuery(int PageNumber = 1, int PageSize = 20, Guid? DepartmentId = null, RequestType? RequestType = null,
    DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null, string SortBy = ApprovalSortFields.SubmittedDate,
    SortDirection SortDirection = SortDirection.Ascending) : IRequest<PagedResult<ApprovalQueueItemDto>>;
public sealed record GetManagerApprovalDetailQuery(Guid RequestId) : IRequest<ApprovalRequestDetailDto>;
public sealed record GetAdminApprovalDetailQuery(Guid RequestId) : IRequest<ApprovalRequestDetailDto>;
public sealed record GetApprovalHistoryQuery(Guid RequestId) : IRequest<IReadOnlyList<ApprovalHistoryItemDto>>;

internal sealed class GetManagerApprovalQueueQueryValidator : AbstractValidator<GetManagerApprovalQueueQuery>
{
    public GetManagerApprovalQueueQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1); RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(x => x.SortBy).Must(ApprovalSortFields.All.Contains); RuleFor(x => x.SortDirection).IsInEnum();
        RuleFor(x => x.RequestType).Must(x => !x.HasValue || Enum.IsDefined(x.Value));
        RuleFor(x => x.CreatedTo).GreaterThanOrEqualTo(x => x.CreatedFrom).When(x => x.CreatedFrom.HasValue && x.CreatedTo.HasValue);
    }
}
internal sealed class GetAdminApprovalQueueQueryValidator : AbstractValidator<GetAdminApprovalQueueQuery>
{
    public GetAdminApprovalQueueQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1); RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(x => x.SortBy).Must(ApprovalSortFields.All.Contains); RuleFor(x => x.SortDirection).IsInEnum();
        RuleFor(x => x.RequestType).Must(x => !x.HasValue || Enum.IsDefined(x.Value));
        RuleFor(x => x.CreatedTo).GreaterThanOrEqualTo(x => x.CreatedFrom).When(x => x.CreatedFrom.HasValue && x.CreatedTo.HasValue);
        RuleFor(x => x.DepartmentId).NotEqual(Guid.Empty).When(x => x.DepartmentId.HasValue);
    }
}
internal sealed class GetManagerApprovalDetailQueryValidator : AbstractValidator<GetManagerApprovalDetailQuery>
{ public GetManagerApprovalDetailQueryValidator() => RuleFor(x => x.RequestId).NotEmpty(); }
internal sealed class GetAdminApprovalDetailQueryValidator : AbstractValidator<GetAdminApprovalDetailQuery>
{ public GetAdminApprovalDetailQueryValidator() => RuleFor(x => x.RequestId).NotEmpty(); }
internal sealed class GetApprovalHistoryQueryValidator : AbstractValidator<GetApprovalHistoryQuery>
{ public GetApprovalHistoryQueryValidator() => RuleFor(x => x.RequestId).NotEmpty(); }

internal sealed class GetManagerApprovalQueueQueryHandler(ICurrentUserContext current, IUserRepository users, IApprovalReadRepository reads)
    : IRequestHandler<GetManagerApprovalQueueQuery, PagedResult<ApprovalQueueItemDto>>
{
    public async Task<PagedResult<ApprovalQueueItemDto>> Handle(GetManagerApprovalQueueQuery q, CancellationToken ct)
    { Guid actor = await ApprovalDecisionGuard.RequireActorAsync(current, users, UserRole.Manager, ct); return await reads.GetManagerQueueAsync(actor, new(q.PageNumber, q.PageSize, q.RequestType, null, q.CreatedFrom, q.CreatedTo, q.SortBy, q.SortDirection), ct); }
}
internal sealed class GetAdminApprovalQueueQueryHandler(ICurrentUserContext current, IUserRepository users, IApprovalReadRepository reads)
    : IRequestHandler<GetAdminApprovalQueueQuery, PagedResult<ApprovalQueueItemDto>>
{
    public async Task<PagedResult<ApprovalQueueItemDto>> Handle(GetAdminApprovalQueueQuery q, CancellationToken ct)
    { Guid actor = await ApprovalDecisionGuard.RequireActorAsync(current, users, UserRole.Admin, ct); return await reads.GetAdminQueueAsync(actor, new(q.PageNumber, q.PageSize, q.RequestType, q.DepartmentId, q.CreatedFrom, q.CreatedTo, q.SortBy, q.SortDirection), ct); }
}
internal sealed class GetManagerApprovalDetailQueryHandler(ICurrentUserContext current, IUserRepository users, IApprovalReadRepository reads)
    : IRequestHandler<GetManagerApprovalDetailQuery, ApprovalRequestDetailDto>
{
    public async Task<ApprovalRequestDetailDto> Handle(GetManagerApprovalDetailQuery q, CancellationToken ct)
    { Guid actor = await ApprovalDecisionGuard.RequireActorAsync(current, users, UserRole.Manager, ct); return await reads.GetManagerDetailAsync(q.RequestId, actor, ct) ?? throw ApprovalDecisionGuard.NotFound(); }
}
internal sealed class GetAdminApprovalDetailQueryHandler(ICurrentUserContext current, IUserRepository users, IApprovalReadRepository reads)
    : IRequestHandler<GetAdminApprovalDetailQuery, ApprovalRequestDetailDto>
{
    public async Task<ApprovalRequestDetailDto> Handle(GetAdminApprovalDetailQuery q, CancellationToken ct)
    { Guid actor = await ApprovalDecisionGuard.RequireActorAsync(current, users, UserRole.Admin, ct); return await reads.GetAdminDetailAsync(q.RequestId, actor, ct) ?? throw ApprovalDecisionGuard.NotFound(); }
}
internal sealed class GetApprovalHistoryQueryHandler(ICurrentUserContext current, IUserRepository users, IApprovalReadRepository reads)
    : IRequestHandler<GetApprovalHistoryQuery, IReadOnlyList<ApprovalHistoryItemDto>>
{
    public async Task<IReadOnlyList<ApprovalHistoryItemDto>> Handle(GetApprovalHistoryQuery q, CancellationToken ct)
    {
        if (!current.IsAuthenticated || !current.UserId.HasValue) throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
        User? user = await users.GetByIdAsync(current.UserId.Value, ct);
        if (user is null || user.IsDeleted || !user.IsActive) throw new ForbiddenAccessException("approval.history_forbidden", "Onay geçmişine erişilemiyor.");
        return await reads.GetHistoryAsync(q.RequestId, user.Id, user.Role, ct) ?? throw ApprovalDecisionGuard.NotFound();
    }
}
