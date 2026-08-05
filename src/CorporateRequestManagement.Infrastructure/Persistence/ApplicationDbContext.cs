using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Application.Common.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext, IUnitOfWork
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<CorporateRequest> CorporateRequests => Set<CorporateRequest>();
    public DbSet<RequestApproval> RequestApprovals => Set<RequestApproval>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUserEmailUniqueConflict(exception))
        {
            throw new ConflictException("user.email_exists", "Bu e-posta adresiyle kayıtlı bir kullanıcı zaten bulunuyor.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    private static bool IsUserEmailUniqueConflict(DbUpdateException exception) =>
        exception.Entries.Any(entry => entry.Entity is User)
        && exception.InnerException is SqlException { Number: 2601 or 2627 };
}
