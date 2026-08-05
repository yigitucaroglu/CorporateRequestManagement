using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.Infrastructure.Persistence.Seeding;

public sealed class DevelopmentDataSeeder(
    ApplicationDbContext context,
    IUnitOfWork unitOfWork,
    ITransactionExecutor transactionExecutor,
    IPasswordHasher<User> passwordHasher)
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);

    public Task SeedAsync(CancellationToken cancellationToken = default)
    {
        return transactionExecutor.ExecuteAsync(ReconcileAsync, cancellationToken);
    }

    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        Department? department = await FindDepartmentAsync(DevelopmentSeedData.DepartmentId, DevelopmentSeedData.DepartmentName, DevelopmentSeedData.ManagerUserId, cancellationToken);
        Department? finance = await FindDepartmentAsync(DevelopmentSeedData.FinanceDepartmentId, DevelopmentSeedData.FinanceDepartmentName, null, cancellationToken);
        User? admin = await FindUserAsync(
            DevelopmentSeedData.AdminUserId,
            DevelopmentSeedData.AdminEmail,
            UserRole.Admin,
            null,
            cancellationToken);

        if (department is null)
        {
            department = Department.Create(
                DevelopmentSeedData.DepartmentId,
                DevelopmentSeedData.DepartmentName,
                null,
                CreatedAt);
            context.Departments.Add(department);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (finance is null)
        {
            finance = Department.Create(DevelopmentSeedData.FinanceDepartmentId, DevelopmentSeedData.FinanceDepartmentName, null, CreatedAt);
            context.Departments.Add(finance);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        User? manager = await FindUserAsync(
            DevelopmentSeedData.ManagerUserId,
            DevelopmentSeedData.ManagerEmail,
            UserRole.Manager,
            department.Id,
            cancellationToken);
        User? employee = await FindUserAsync(
            DevelopmentSeedData.EmployeeUserId,
            DevelopmentSeedData.EmployeeEmail,
            UserRole.Employee,
            department.Id,
            cancellationToken);

        if (admin is null)
        {
            admin = CreateUser(DevelopmentSeedData.AdminUserId, "Sistem", "Yöneticisi", DevelopmentSeedData.AdminEmail, UserRole.Admin, null);
            context.Users.Add(admin);
        }

        if (manager is null)
        {
            manager = CreateUser(DevelopmentSeedData.ManagerUserId, "Demo", "Yönetici", DevelopmentSeedData.ManagerEmail, UserRole.Manager, department.Id);
            context.Users.Add(manager);
        }

        if (employee is null)
        {
            employee = CreateUser(DevelopmentSeedData.EmployeeUserId, "Demo", "Çalışan", DevelopmentSeedData.EmployeeEmail, UserRole.Employee, department.Id);
            context.Users.Add(employee);
        }

        if (context.ChangeTracker.HasChanges())
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        ValidateManager(manager, department.Id);
        if (department.ManagerId is null)
        {
            context.Entry(department).Property(item => item.ManagerId).CurrentValue = manager.Id;
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else if (department.ManagerId != manager.Id)
        {
            throw SeedConflict("Development department is assigned to an unexpected manager.");
        }
    }

    private async Task<Department?> FindDepartmentAsync(Guid expectedId, string expectedName, Guid? expectedManagerId, CancellationToken cancellationToken)
    {
        Department? byId = await context.Departments.IgnoreQueryFilters().SingleOrDefaultAsync(
            item => item.Id == expectedId,
            cancellationToken);
        Department? byName = await context.Departments.IgnoreQueryFilters().SingleOrDefaultAsync(
            item => item.Name == expectedName,
            cancellationToken);

        if (byName is not null && byName.Id != expectedId)
        {
            throw SeedConflict("Development department name belongs to an unexpected record.");
        }

        if (byId is null)
        {
            return null;
        }

        if (byId.Name != expectedName || byId.IsDeleted)
        {
            throw SeedConflict("Development department record is inconsistent.");
        }

        if (byId.ManagerId is not null && byId.ManagerId != expectedManagerId)
        {
            throw SeedConflict("Development department is assigned to an unexpected manager.");
        }

        return byId;
    }

    private async Task<User?> FindUserAsync(
        Guid expectedId,
        string expectedEmail,
        UserRole expectedRole,
        Guid? expectedDepartmentId,
        CancellationToken cancellationToken)
    {
        User? byId = await context.Users.IgnoreQueryFilters().SingleOrDefaultAsync(
            item => item.Id == expectedId,
            cancellationToken);
        User? byEmail = await context.Users.IgnoreQueryFilters().SingleOrDefaultAsync(
            item => item.Email == expectedEmail,
            cancellationToken);

        if (byEmail is not null && byEmail.Id != expectedId)
        {
            throw SeedConflict("A development seed email belongs to an unexpected record.");
        }

        if (byId is null)
        {
            return null;
        }

        if (byId.Email != expectedEmail
            || byId.Role != expectedRole
            || byId.DepartmentId != expectedDepartmentId
            || !byId.IsActive
            || byId.IsDeleted)
        {
            throw SeedConflict("A development seed user record is inconsistent.");
        }

        return byId;
    }

    private static void ValidateManager(User manager, Guid departmentId)
    {
        if (manager.Role != UserRole.Manager
            || !manager.IsActive
            || manager.IsDeleted
            || manager.DepartmentId != departmentId)
        {
            throw SeedConflict("Development manager invariants are not satisfied.");
        }
    }

    private User CreateUser(Guid id, string firstName, string lastName, string email, UserRole role, Guid? departmentId)
    {
        User hashSubject = User.Create(id, firstName, lastName, email, "temporary-hash", role, departmentId, CreatedAt);
        string passwordHash = passwordHasher.HashPassword(hashSubject, DevelopmentSeedData.DemoPassword);
        return User.Create(id, firstName, lastName, email, passwordHash, role, departmentId, CreatedAt);
    }

    private static InvalidOperationException SeedConflict(string message) => new(message);
}
