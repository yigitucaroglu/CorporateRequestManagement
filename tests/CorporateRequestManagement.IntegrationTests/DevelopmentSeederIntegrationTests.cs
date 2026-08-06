using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class DevelopmentSeederIntegrationTests(SqlServerDatabaseFixture fixture)
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 2, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SeedAsync_WhenRunTwice_ShouldRemainCompleteAndPreservePasswordHashes()
    {
        await fixture.ResetDevelopmentSeedAsync();
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        DevelopmentDataSeeder seeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await seeder.SeedAsync();
        string[] hashes = await context.Users.Where(IsSeedUser).OrderBy(user => user.Id).Select(user => user.PasswordHash).ToArrayAsync();
        await seeder.SeedAsync();
        context.ChangeTracker.Clear();

        User[] users = await context.Users.Where(IsSeedUser).OrderBy(user => user.Id).ToArrayAsync();
        Department department = await context.Departments.SingleAsync(item => item.Id == DevelopmentSeedData.DepartmentId);
        Assert.Equal(3, users.Length);
        Assert.Single(await context.Departments.Where(item => item.Id == DevelopmentSeedData.DepartmentId).ToArrayAsync());
        Assert.Equal(hashes, users.Select(user => user.PasswordHash));
        Assert.Equal(DevelopmentSeedData.ManagerUserId, department.ManagerId);
        Assert.Collection(users,
            admin => { Assert.Equal(UserRole.Admin, admin.Role); Assert.Null(admin.DepartmentId); Assert.True(admin.IsActive); },
            manager => { Assert.Equal(UserRole.Manager, manager.Role); Assert.Equal(department.Id, manager.DepartmentId); Assert.True(manager.IsActive); },
            employee => { Assert.Equal(UserRole.Employee, employee.Role); Assert.Equal(department.Id, employee.DepartmentId); Assert.True(employee.IsActive); });
    }

    [Fact]
    public async Task SeedAsync_WhenOnlyAdminExists_ShouldRecoverMissingRecords()
    {
        await fixture.ResetDevelopmentSeedAsync();
        await using (ApplicationDbContext setup = fixture.CreateContext())
        {
            setup.Users.Add(CreateAdmin("existing-admin-hash"));
            await setup.SaveChangesAsync();
        }

        await RunSeederAsync();
        await using ApplicationDbContext verification = fixture.CreateContext();
        Assert.Equal(3, await verification.Users.CountAsync(IsSeedUser));
        Assert.Equal("existing-admin-hash", (await verification.Users.SingleAsync(user => user.Id == DevelopmentSeedData.AdminUserId)).PasswordHash);
        Assert.Equal(DevelopmentSeedData.ManagerUserId, (await verification.Departments.SingleAsync(item => item.Id == DevelopmentSeedData.DepartmentId)).ManagerId);
    }

    [Fact]
    public async Task SeedAsync_WhenOnlyManagerLinkIsMissing_ShouldRepairLinkWithoutChangingHashes()
    {
        await fixture.ResetDevelopmentSeedAsync();
        string[] expectedHashes = ["admin-existing-hash", "manager-existing-hash", "employee-existing-hash"];
        await using (ApplicationDbContext setup = fixture.CreateContext())
        {
            Department department = Department.Create(DevelopmentSeedData.DepartmentId, DevelopmentSeedData.DepartmentName, null, CreatedAt);
            setup.Departments.Add(department);
            await setup.SaveChangesAsync();
            setup.Users.AddRange(
                CreateAdmin(expectedHashes[0]),
                CreateDepartmentUser(DevelopmentSeedData.ManagerUserId, DevelopmentSeedData.ManagerEmail, UserRole.Manager, expectedHashes[1]),
                CreateDepartmentUser(DevelopmentSeedData.EmployeeUserId, DevelopmentSeedData.EmployeeEmail, UserRole.Employee, expectedHashes[2]));
            await setup.SaveChangesAsync();
        }

        await RunSeederAsync();
        await using ApplicationDbContext verification = fixture.CreateContext();
        Assert.Equal(DevelopmentSeedData.ManagerUserId, (await verification.Departments.SingleAsync(item => item.Id == DevelopmentSeedData.DepartmentId)).ManagerId);
        Assert.Equal(expectedHashes, await verification.Users.Where(IsSeedUser).OrderBy(user => user.Id).Select(user => user.PasswordHash).ToArrayAsync());
    }

    [Fact]
    public async Task SeedAsync_WhenDeterministicManagerIsInconsistent_ShouldRejectWithoutOverwritingOrAddingRecords()
    {
        await fixture.ResetDevelopmentSeedAsync();
        await using (ApplicationDbContext setup = fixture.CreateContext())
        {
            setup.Departments.Add(Department.Create(DevelopmentSeedData.DepartmentId, DevelopmentSeedData.DepartmentName, null, CreatedAt));
            await setup.SaveChangesAsync();
            setup.Users.Add(CreateDepartmentUser(DevelopmentSeedData.ManagerUserId, DevelopmentSeedData.ManagerEmail, UserRole.Employee, "wrong-role-hash"));
            await setup.SaveChangesAsync();
        }

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(RunSeederAsync);
        Assert.DoesNotContain(DevelopmentSeedData.DemoPassword, exception.Message, StringComparison.Ordinal);
        await using ApplicationDbContext verification = fixture.CreateContext();
        User manager = await verification.Users.SingleAsync(user => user.Id == DevelopmentSeedData.ManagerUserId);
        Assert.Equal(UserRole.Employee, manager.Role);
        Assert.Equal("wrong-role-hash", manager.PasswordHash);
        Assert.Equal(1, await verification.Users.CountAsync(IsSeedUser));
    }

    [Fact]
    public async Task SeedAsync_WhenManagerAssignmentSaveFails_ShouldRollbackEverySeedWrite()
    {
        await fixture.ResetDevelopmentSeedAsync();
        const string triggerName = "TR_IntegrationTests_BlockSeedManagerUpdate";
        await using ApplicationDbContext setup = fixture.CreateContext();
        await setup.Database.ExecuteSqlRawAsync($$"""
            CREATE TRIGGER [{{triggerName}}] ON [Departments]
            AFTER UPDATE AS
            BEGIN
                IF EXISTS (SELECT 1 FROM inserted WHERE [ManagerId] IS NOT NULL)
                    THROW 51000, 'Test-only manager update failure.', 1;
            END
            """);

        try
        {
            await Assert.ThrowsAsync<DbUpdateException>(RunSeederAsync);
            await using ApplicationDbContext verification = fixture.CreateContext();
            Assert.False(await verification.Departments.IgnoreQueryFilters().AnyAsync(item => item.Id == DevelopmentSeedData.DepartmentId));
            Assert.False(await verification.Users.IgnoreQueryFilters().AnyAsync(IsSeedUser));
        }
        finally
        {
            await setup.Database.ExecuteSqlRawAsync($"DROP TRIGGER IF EXISTS [{triggerName}]");
        }
    }

    private async Task RunSeederAsync()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(fixture.ConnectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static User CreateAdmin(string hash) =>
        User.Create(DevelopmentSeedData.AdminUserId, "Sistem", "Yöneticisi", DevelopmentSeedData.AdminEmail, hash, UserRole.Admin, null, CreatedAt);

    private static User CreateDepartmentUser(Guid id, string email, UserRole role, string hash) =>
        User.Create(id, "Demo", "User", email, hash, role, DevelopmentSeedData.DepartmentId, CreatedAt);

    private static System.Linq.Expressions.Expression<Func<User, bool>> IsSeedUser => user =>
        user.Id == DevelopmentSeedData.AdminUserId
        || user.Id == DevelopmentSeedData.ManagerUserId
        || user.Id == DevelopmentSeedData.EmployeeUserId;
}
