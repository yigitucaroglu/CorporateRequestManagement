using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class PersistenceBehaviorTests(SqlServerDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SoftDeletedEntities_AreHiddenButRemainStored()
    {
        await using ApplicationDbContext context = fixture.CreateContext();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));
        principals.Employee.SoftDelete(Now.AddMinutes(1), principals.Manager.Id);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.Null(await context.Users.SingleOrDefaultAsync(user => user.Id == principals.Employee.Id));
        Assert.NotNull(await context.Users.IgnoreQueryFilters().SingleOrDefaultAsync(user => user.Id == principals.Employee.Id));
    }

    [Fact]
    public async Task FilteredUniqueEmail_AllowsReuseAfterSoftDelete()
    {
        await using ApplicationDbContext context = fixture.CreateContext();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));
        string email = principals.Employee.Email;
        principals.Employee.SoftDelete(Now.AddMinutes(1), principals.Manager.Id);
        await context.SaveChangesAsync();

        User replacement = User.Create(Guid.NewGuid(), "New", "Employee", email, "hash", UserRole.Employee, principals.Department.Id, Now.AddMinutes(2));
        context.Users.Add(replacement);

        Assert.Equal(1, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task ActiveDuplicateEmail_IsRejectedBySqlServerUniqueIndex()
    {
        await using ApplicationDbContext context = fixture.CreateContext();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));
        context.Users.Add(User.Create(Guid.NewGuid(), "Duplicate", "User", principals.Employee.Email, "hash", UserRole.Employee, principals.Department.Id, Now));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task UnitOfWork_AndRepositoriesShareTheScopedDbContext()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IUserRepository repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));

        User? loaded = await repository.GetByIdAsync(principals.Employee.Id);

        Assert.Same(context, unitOfWork);
        Assert.Same(principals.Employee, loaded);
    }

    [Fact]
    public async Task UserEmailLookup_IsNoTrackingAndHonorsSoftDelete()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IUserRepository repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));
        context.ChangeTracker.Clear();

        User? loaded = await repository.GetByEmailAsync(principals.Employee.Email);

        Assert.NotNull(loaded);
        Assert.Empty(context.ChangeTracker.Entries<User>());
    }

    [Fact]
    public async Task RequestRepository_LoadsApprovalHistoryForUpdate()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ICorporateRequestRepository repository = scope.ServiceProvider.GetRequiredService<ICorporateRequestRepository>();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));
        CorporateRequest request = CreateRequest(principals.Employee, principals.Department, 5000m);
        request.Submit(principals.Employee.Id, Now.AddMinutes(1));
        request.ApproveByManager(Guid.NewGuid(), principals.Manager.Id, 1000m, Now.AddMinutes(2));
        context.CorporateRequests.Add(request);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        CorporateRequest? loaded = await repository.GetByIdForUpdateAsync(request.Id);

        Assert.NotNull(loaded);
        Assert.Single(loaded.Approvals);
        Assert.Equal(EntityState.Unchanged, context.Entry(loaded).State);
    }

    [Fact]
    public async Task TransactionExecutor_RollsBackAllChangesWhenOperationFails()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ITransactionExecutor transaction = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
        string discriminator = Guid.NewGuid().ToString("N");

        await Assert.ThrowsAsync<InvalidOperationException>(() => transaction.ExecuteAsync(async cancellationToken =>
        {
            context.Departments.Add(Department.Create(Guid.NewGuid(), "Rollback " + discriminator, null, Now));
            await context.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("expected");
        }));
        context.ChangeTracker.Clear();

        Assert.False(await context.Departments.AnyAsync(department => department.Name == "Rollback " + discriminator));
    }

    [Fact]
    public async Task TransactionExecutor_ReusesAnExistingTransaction()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ITransactionExecutor transaction = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
        await using var outer = await context.Database.BeginTransactionAsync();

        await transaction.ExecuteAsync(_ => Task.CompletedTask);

        Assert.Same(outer, context.Database.CurrentTransaction);
        await outer.RollbackAsync();
    }

    [Fact]
    public async Task SqlServerRowVersion_RejectsStaleConcurrentUpdate()
    {
        Guid requestId;
        Guid employeeId;
        byte[] insertedVersion;
        await using (ApplicationDbContext setup = fixture.CreateContext())
        {
            var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(setup, Guid.NewGuid().ToString("N"));
            CorporateRequest request = CreateRequest(principals.Employee, principals.Department, 100m);
            requestId = request.Id;
            employeeId = principals.Employee.Id;
            setup.CorporateRequests.Add(request);
            await setup.SaveChangesAsync();
            Assert.NotEmpty(request.RowVersion);
            insertedVersion = request.RowVersion;
        }

        await using ApplicationDbContext first = fixture.CreateContext();
        await using ApplicationDbContext second = fixture.CreateContext();
        CorporateRequest firstCopy = await first.CorporateRequests.SingleAsync(item => item.Id == requestId);
        CorporateRequest staleCopy = await second.CorporateRequests.SingleAsync(item => item.Id == requestId);
        Assert.Equal(insertedVersion, firstCopy.RowVersion);
        Assert.Equal(firstCopy.RowVersion, staleCopy.RowVersion);
        byte[] defensiveCopy = firstCopy.RowVersion;
        defensiveCopy[0] ^= byte.MaxValue;
        Assert.Equal(insertedVersion, firstCopy.RowVersion);
        firstCopy.UpdateDraft(employeeId, "First update", "description", RequestType.Expense, 100m, Now.AddMinutes(1));
        staleCopy.UpdateDraft(employeeId, "Stale update", "description", RequestType.Expense, 100m, Now.AddMinutes(2));
        await first.SaveChangesAsync();
        Assert.False(insertedVersion.SequenceEqual(firstCopy.RowVersion));

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        await using ApplicationDbContext verification = fixture.CreateContext();
        CorporateRequest persisted = await verification.CorporateRequests.SingleAsync(item => item.Id == requestId);
        Assert.Equal("First update", persisted.Title);
        Assert.NotEqual("Stale update", persisted.Title);
        Assert.Equal(firstCopy.RowVersion, persisted.RowVersion);
    }

    [Fact]
    public async Task DevelopmentSeeder_IsIdempotentAndHashesDemoPasswords()
    {
        await fixture.ResetDevelopmentSeedAsync();
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var hasher = new PasswordHasher<User>();
        DevelopmentDataSeeder seeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>();

        await seeder.SeedAsync();
        await seeder.SeedAsync();
        User admin = await context.Users.SingleAsync(user => user.Id == DevelopmentSeedData.AdminUserId);

        Assert.Equal(3, await context.Users.CountAsync(user => user.Id == DevelopmentSeedData.AdminUserId || user.Id == DevelopmentSeedData.ManagerUserId || user.Id == DevelopmentSeedData.EmployeeUserId));
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(admin, admin.PasswordHash, DevelopmentSeedData.DemoPassword));
        Assert.DoesNotContain(DevelopmentSeedData.DemoPassword, admin.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DevelopmentSeeder_WiresManagerToSeedDepartment()
    {
        await fixture.ResetDevelopmentSeedAsync();
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        DevelopmentDataSeeder seeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>();
        await seeder.SeedAsync();
        Department department = await context.Departments.SingleAsync(item => item.Id == DevelopmentSeedData.DepartmentId);
        User manager = await context.Users.SingleAsync(item => item.Id == DevelopmentSeedData.ManagerUserId);

        Assert.Equal(manager.Id, department.ManagerId);
        Assert.Equal(department.Id, manager.DepartmentId);
        Assert.Equal(UserRole.Manager, manager.Role);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(fixture.ConnectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static CorporateRequest CreateRequest(User employee, Department department, decimal amount) =>
        CorporateRequest.Create(Guid.NewGuid(), "Test request", "description", RequestType.Expense, amount, employee.Id, department.Id, Now);
}
