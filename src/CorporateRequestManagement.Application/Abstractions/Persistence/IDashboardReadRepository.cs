using CorporateRequestManagement.Application.Features.Dashboard;

namespace CorporateRequestManagement.Application.Abstractions.Persistence;

public interface IDashboardReadRepository
{
    Task<EmployeeDashboardDto> GetEmployeeAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<ManagerDashboardDto> GetManagerAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task<AdminDashboardDto> GetAdminAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken = default);
}
