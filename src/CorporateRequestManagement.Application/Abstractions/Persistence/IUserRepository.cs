using CorporateRequestManagement.Domain.Entities;

namespace CorporateRequestManagement.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<User?> GetByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(
        string normalizedEmail,
        Guid? excludingUserId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        User user,
        CancellationToken cancellationToken = default);
}
