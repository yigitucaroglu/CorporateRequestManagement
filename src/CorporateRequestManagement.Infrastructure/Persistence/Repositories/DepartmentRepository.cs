using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.Infrastructure.Persistence.Repositories;

internal sealed class DepartmentRepository(ApplicationDbContext context) : IDepartmentRepository
{
    public Task<Department?> GetByIdAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        return context.Departments.SingleOrDefaultAsync(department => department.Id == departmentId, cancellationToken);
    }

    public Task<bool> NameExistsAsync(string name, Guid? excludingDepartmentId = null, CancellationToken cancellationToken = default)
    {
        return context.Departments.AnyAsync(
            department => department.Name == name && (excludingDepartmentId == null || department.Id != excludingDepartmentId),
            cancellationToken);
    }

    public Task<bool> IsActiveDepartmentManagerAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return context.Departments.AnyAsync(department => department.ManagerId == userId, cancellationToken);
    }

    public async Task AddAsync(Department department, CancellationToken cancellationToken = default)
    {
        await context.Departments.AddAsync(department, cancellationToken);
    }
}
