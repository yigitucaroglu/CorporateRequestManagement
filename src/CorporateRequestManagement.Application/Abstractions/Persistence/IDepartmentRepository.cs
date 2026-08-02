using CorporateRequestManagement.Domain.Entities;

namespace CorporateRequestManagement.Application.Abstractions.Persistence;

public interface IDepartmentRepository
{
    Task<Department?> GetByIdAsync(
        Guid departmentId,
        CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(
        string name,
        Guid? excludingDepartmentId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Department department,
        CancellationToken cancellationToken = default);
}
