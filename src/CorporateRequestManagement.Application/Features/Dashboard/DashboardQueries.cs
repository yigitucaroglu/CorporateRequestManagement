using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using MediatR;

namespace CorporateRequestManagement.Application.Features.Dashboard;

public sealed record GetEmployeeDashboardQuery : IRequest<EmployeeDashboardDto>;
public sealed record GetManagerDashboardQuery : IRequest<ManagerDashboardDto>;
public sealed record GetAdminDashboardQuery : IRequest<AdminDashboardDto>;

internal static class DashboardActor
{
    public static async Task<Guid> RequireAsync(ICurrentUserContext current, IUserRepository users, UserRole role, CancellationToken ct)
    {
        if (!current.IsAuthenticated || current.UserId is null) throw new AuthenticationFailedException("auth.unauthorized", "Kimlik doğrulaması gereklidir.");
        User? user = await users.GetByIdAsync(current.UserId.Value, ct);
        if (user is null || user.IsDeleted || !user.IsActive || user.Role != role)
            throw new ForbiddenAccessException("dashboard.forbidden", "Dashboard verisine erişim yetkiniz bulunmuyor.");
        return user.Id;
    }
}

internal sealed class GetEmployeeDashboardQueryHandler(ICurrentUserContext current, IUserRepository users, IDashboardReadRepository reads, TimeProvider time)
    : IRequestHandler<GetEmployeeDashboardQuery, EmployeeDashboardDto>
{ public async Task<EmployeeDashboardDto> Handle(GetEmployeeDashboardQuery _, CancellationToken ct) => await reads.GetEmployeeAsync(await DashboardActor.RequireAsync(current, users, UserRole.Employee, ct), time.GetUtcNow(), ct); }
internal sealed class GetManagerDashboardQueryHandler(ICurrentUserContext current, IUserRepository users, IDashboardReadRepository reads, TimeProvider time)
    : IRequestHandler<GetManagerDashboardQuery, ManagerDashboardDto>
{ public async Task<ManagerDashboardDto> Handle(GetManagerDashboardQuery _, CancellationToken ct) => await reads.GetManagerAsync(await DashboardActor.RequireAsync(current, users, UserRole.Manager, ct), time.GetUtcNow(), ct); }
internal sealed class GetAdminDashboardQueryHandler(ICurrentUserContext current, IUserRepository users, IDashboardReadRepository reads, TimeProvider time)
    : IRequestHandler<GetAdminDashboardQuery, AdminDashboardDto>
{ public async Task<AdminDashboardDto> Handle(GetAdminDashboardQuery _, CancellationToken ct) => await reads.GetAdminAsync(await DashboardActor.RequireAsync(current, users, UserRole.Admin, ct), time.GetUtcNow(), ct); }
