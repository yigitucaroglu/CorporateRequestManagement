using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.Infrastructure.Persistence.Repositories;

internal sealed class CorporateRequestRepository(ApplicationDbContext context) : ICorporateRequestRepository
{
    public Task<CorporateRequest?> GetByIdForUpdateAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        return context.CorporateRequests
            .Include(request => request.Approvals)
            .SingleOrDefaultAsync(request => request.Id == requestId, cancellationToken);
    }

    public async Task AddAsync(CorporateRequest request, CancellationToken cancellationToken = default)
    {
        await context.CorporateRequests.AddAsync(request, cancellationToken);
    }

    public Task<CorporateRequest?> GetForManagerDecisionAsync(Guid requestId, Guid managerUserId, CancellationToken cancellationToken = default)
    {
        return (from request in context.CorporateRequests.Include(item => item.Approvals)
                join department in context.Departments on request.DepartmentId equals department.Id
                where request.Id == requestId && department.ManagerId == managerUserId
                select request).SingleOrDefaultAsync(cancellationToken);
    }

    public Task<CorporateRequest?> GetForAdminDecisionAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        return context.CorporateRequests.Include(item => item.Approvals)
            .SingleOrDefaultAsync(request => request.Id == requestId, cancellationToken);
    }
}
