using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Common.Models;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using FluentValidation;
using MediatR;

namespace CorporateRequestManagement.Application.Features.Administration;

public sealed record GetAdminRequestsQuery(int PageNumber = 1, int PageSize = 20, string? SearchTerm = null,
    RequestStatus? Status = null, RequestType? RequestType = null, Guid? DepartmentId = null,
    DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null, decimal? MinAmount = null,
    decimal? MaxAmount = null, string SortBy = "createdDate", SortDirection SortDirection = SortDirection.Descending) : IRequest<PagedResult<ScopedRequestDto>>;
public sealed record GetManagerDepartmentRequestsQuery(int PageNumber = 1, int PageSize = 20, string? SearchTerm = null,
    RequestStatus? Status = null, RequestType? RequestType = null, DateTimeOffset? CreatedFrom = null,
    DateTimeOffset? CreatedTo = null, decimal? MinAmount = null, decimal? MaxAmount = null,
    string SortBy = "createdDate", SortDirection SortDirection = SortDirection.Descending) : IRequest<PagedResult<ScopedRequestDto>>;
public sealed record GetAdminRequestQuery(Guid Id) : IRequest<ScopedRequestDto>;
public sealed record GetManagerDepartmentRequestQuery(Guid Id) : IRequest<ScopedRequestDto>;
public sealed record DeleteCorporateRequestCommand(Guid Id, string RowVersion) : IRequest;
public sealed record GetAuditLogsQuery(int PageNumber = 1, int PageSize = 20, Guid? UserId = null, string? EntityName = null,
    Guid? EntityId = null, string? Action = null, DateTimeOffset? CreatedFrom = null, DateTimeOffset? CreatedTo = null,
    SortDirection SortDirection = SortDirection.Descending) : IRequest<PagedResult<AuditLogDto>>;
public sealed record GetAuditLogQuery(Guid Id) : IRequest<AuditLogDto>;
public sealed record GetAuditActorsQuery : IRequest<IReadOnlyList<AuditActorDto>>;

internal sealed class RequestScopeValidator : AbstractValidator<GetAdminRequestsQuery>
{
    public RequestScopeValidator() { RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize); RuleFor(x => x.SearchTerm).MaximumLength(100); RuleFor(x => x.CreatedTo).GreaterThanOrEqualTo(x => x.CreatedFrom).When(x => x.CreatedFrom.HasValue && x.CreatedTo.HasValue); RuleFor(x => x.MaxAmount).GreaterThanOrEqualTo(x => x.MinAmount).When(x => x.MinAmount.HasValue && x.MaxAmount.HasValue); RuleFor(x => x.MinAmount).GreaterThan(0).When(x => x.MinAmount.HasValue); RuleFor(x => x.MaxAmount).GreaterThan(0).When(x => x.MaxAmount.HasValue); RuleFor(x => x.SortBy).Must(x => x.Equals("createdDate", StringComparison.OrdinalIgnoreCase) || x.Equals("amount", StringComparison.OrdinalIgnoreCase)); }
}
internal sealed class ManagerRequestScopeValidator : AbstractValidator<GetManagerDepartmentRequestsQuery>
{
    public ManagerRequestScopeValidator() { RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize); RuleFor(x => x.SearchTerm).MaximumLength(100); RuleFor(x => x.CreatedTo).GreaterThanOrEqualTo(x => x.CreatedFrom).When(x => x.CreatedFrom.HasValue && x.CreatedTo.HasValue); RuleFor(x => x.MaxAmount).GreaterThanOrEqualTo(x => x.MinAmount).When(x => x.MinAmount.HasValue && x.MaxAmount.HasValue); RuleFor(x => x.MinAmount).GreaterThan(0).When(x => x.MinAmount.HasValue); RuleFor(x => x.MaxAmount).GreaterThan(0).When(x => x.MaxAmount.HasValue); RuleFor(x => x.SortBy).Must(x => x.Equals("createdDate", StringComparison.OrdinalIgnoreCase) || x.Equals("amount", StringComparison.OrdinalIgnoreCase)); }
}
internal sealed class AuditLogScopeValidator : AbstractValidator<GetAuditLogsQuery>
{
    public AuditLogScopeValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageRequest.MaxPageSize);
        RuleFor(x => x.EntityName).MaximumLength(AuditLog.EntityNameMaxLength);
        RuleFor(x => x.Action).MaximumLength(AuditLog.ActionMaxLength);
        RuleFor(x => x.CreatedTo).GreaterThanOrEqualTo(x => x.CreatedFrom)
            .When(x => x.CreatedFrom.HasValue && x.CreatedTo.HasValue);
        RuleFor(x => x.SortDirection).IsInEnum();
    }
}
internal sealed class DeleteCorporateRequestValidator : AbstractValidator<DeleteCorporateRequestCommand>
{ public DeleteCorporateRequestValidator() { RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.RowVersion).Must(x => RowVersionCodec.TryDecode(x, out _)); } }

internal sealed class GetAdminRequestsHandler(IAdministrationQueryService queries) : IRequestHandler<GetAdminRequestsQuery, PagedResult<ScopedRequestDto>> { public Task<PagedResult<ScopedRequestDto>> Handle(GetAdminRequestsQuery q, CancellationToken ct) => queries.GetAdminRequestsAsync(new(q.PageNumber,q.PageSize,q.SearchTerm,q.Status,q.RequestType,q.DepartmentId,q.CreatedFrom,q.CreatedTo,q.MinAmount,q.MaxAmount,q.SortBy,q.SortDirection),ct); }
internal sealed class GetManagerDepartmentRequestsHandler(IAdministrationQueryService queries, ICurrentUserContext current) : IRequestHandler<GetManagerDepartmentRequestsQuery, PagedResult<ScopedRequestDto>> { public Task<PagedResult<ScopedRequestDto>> Handle(GetManagerDepartmentRequestsQuery q, CancellationToken ct) => queries.GetManagerRequestsAsync(Actor(),new(q.PageNumber,q.PageSize,q.SearchTerm,q.Status,q.RequestType,null,q.CreatedFrom,q.CreatedTo,q.MinAmount,q.MaxAmount,q.SortBy,q.SortDirection),ct); private Guid Actor()=>current.IsAuthenticated&&current.UserId.HasValue?current.UserId.Value:throw new AuthenticationFailedException("auth.unauthorized","Kimlik doğrulaması gereklidir."); }
internal sealed class GetAdminRequestHandler(IAdministrationQueryService queries) : IRequestHandler<GetAdminRequestQuery, ScopedRequestDto> { public async Task<ScopedRequestDto> Handle(GetAdminRequestQuery q, CancellationToken ct) => await queries.GetAdminRequestAsync(q.Id,ct)??throw Missing(); private static NotFoundException Missing()=>new("request.not_found","Talep bulunamadı."); }
internal sealed class GetManagerDepartmentRequestHandler(IAdministrationQueryService queries, ICurrentUserContext current) : IRequestHandler<GetManagerDepartmentRequestQuery, ScopedRequestDto> { public async Task<ScopedRequestDto> Handle(GetManagerDepartmentRequestQuery q,CancellationToken ct)=>await queries.GetManagerRequestAsync(q.Id,Actor(),ct)??throw new NotFoundException("request.not_found","Talep bulunamadı."); private Guid Actor()=>current.IsAuthenticated&&current.UserId.HasValue?current.UserId.Value:throw new AuthenticationFailedException("auth.unauthorized","Kimlik doğrulaması gereklidir."); }
internal sealed class GetAuditLogsHandler(IAdministrationQueryService queries) : IRequestHandler<GetAuditLogsQuery, PagedResult<AuditLogDto>> { public Task<PagedResult<AuditLogDto>> Handle(GetAuditLogsQuery q,CancellationToken ct)=>queries.GetAuditLogsAsync(new(q.PageNumber,q.PageSize,q.UserId,q.EntityName,q.EntityId,q.Action,q.CreatedFrom,q.CreatedTo,q.SortDirection),ct); }
internal sealed class GetAuditLogHandler(IAdministrationQueryService queries) : IRequestHandler<GetAuditLogQuery, AuditLogDto> { public async Task<AuditLogDto> Handle(GetAuditLogQuery q,CancellationToken ct)=>await queries.GetAuditLogAsync(q.Id,ct)??throw new NotFoundException("audit.not_found","Audit kaydı bulunamadı."); }
internal sealed class GetAuditActorsHandler(IAdministrationQueryService queries) : IRequestHandler<GetAuditActorsQuery, IReadOnlyList<AuditActorDto>> { public Task<IReadOnlyList<AuditActorDto>> Handle(GetAuditActorsQuery q,CancellationToken ct)=>queries.GetAuditActorsAsync(ct); }

internal sealed class DeleteCorporateRequestHandler(ICorporateRequestRepository requests, IAuditLogRepository audits,
    IUnitOfWork unit, ITransactionExecutor transactions, ICurrentUserContext current, IGuidGenerator guids, TimeProvider time)
    : IRequestHandler<DeleteCorporateRequestCommand>
{
    public async Task Handle(DeleteCorporateRequestCommand c, CancellationToken ct)
    {
        Guid actor = current.IsAuthenticated && current.UserId.HasValue ? current.UserId.Value : throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
        CorporateRequest entity = await requests.GetForAdminDecisionAsync(c.Id, ct) ?? throw new NotFoundException("request.not_found", "Talep bulunamadı.");
        RowVersionCodec.TryDecode(c.RowVersion, out byte[] expected); if (!entity.RowVersion.SequenceEqual(expected)) throw new ConflictException("concurrency.conflict", "Kayıt başka bir işlem tarafından değiştirildi.");
        DateTimeOffset now = time.GetUtcNow(); await transactions.ExecuteAsync(async token => { entity.SoftDelete(now, actor); await audits.AddAsync(AuditLog.Create(guids.NewGuid(), actor, nameof(CorporateRequest), entity.Id, "CorporateRequest.SoftDeleted", null, null, now), token); await unit.SaveChangesAsync(token); }, ct);
    }
}
