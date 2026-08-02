using CorporateRequestManagement.Domain.Entities;

namespace CorporateRequestManagement.Application.Abstractions.Persistence;

public interface IAuditLogRepository
{
    Task AddAsync(
        AuditLog auditLog,
        CancellationToken cancellationToken = default);
}
