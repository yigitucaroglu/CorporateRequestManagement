using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure;
using CorporateRequestManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class RepositoryIntegrationTests(SqlServerDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CorporateRequestAddAsync_ShouldPersistOnlyAfterUnitOfWorkSave()
    {
        Guid requestId;
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));
        CorporateRequest request = CorporateRequest.Create(Guid.NewGuid(), "Boundary", "Description", RequestType.Expense, 10m, principals.Employee.Id, principals.Department.Id, Now);
        requestId = request.Id;
        await scope.ServiceProvider.GetRequiredService<ICorporateRequestRepository>().AddAsync(request);

        await using (ApplicationDbContext beforeSave = fixture.CreateContext())
        {
            Assert.False(await beforeSave.CorporateRequests.AnyAsync(item => item.Id == requestId));
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        await using ApplicationDbContext afterSave = fixture.CreateContext();
        Assert.True(await afterSave.CorporateRequests.AnyAsync(item => item.Id == requestId));
    }

    [Fact]
    public async Task AuditAddAsync_ShouldPersistOnlyAfterUnitOfWorkSave()
    {
        Guid auditId = Guid.NewGuid();
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        AuditLog audit = AuditLog.Create(auditId, null, "CorporateRequest", Guid.NewGuid(), "Created", null, "{}", Now);
        await scope.ServiceProvider.GetRequiredService<IAuditLogRepository>().AddAsync(audit);

        await using (ApplicationDbContext beforeSave = fixture.CreateContext())
        {
            Assert.False(await beforeSave.AuditLogs.AnyAsync(item => item.Id == auditId));
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        await using ApplicationDbContext afterSave = fixture.CreateContext();
        Assert.True(await afterSave.AuditLogs.AnyAsync(item => item.Id == auditId));
    }

    [Fact]
    public async Task UserEmailExistsAsync_ShouldHonorExcludeSoftDeleteAndInactiveState()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IUserRepository repository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));

        Assert.True(await repository.EmailExistsAsync(principals.Employee.Email));
        Assert.False(await repository.EmailExistsAsync("missing-" + principals.Employee.Email));
        Assert.False(await repository.EmailExistsAsync(principals.Employee.Email, principals.Employee.Id));
        context.Entry(principals.Employee).Property(user => user.IsActive).CurrentValue = false;
        await context.SaveChangesAsync();
        Assert.True(await repository.EmailExistsAsync(principals.Employee.Email));
        principals.Employee.SoftDelete(Now.AddMinutes(1), principals.Manager.Id);
        await context.SaveChangesAsync();
        Assert.False(await repository.EmailExistsAsync(principals.Employee.Email));
    }

    [Fact]
    public async Task DepartmentNameExistsAsync_ShouldHonorExcludeAndSoftDelete()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        IDepartmentRepository repository = scope.ServiceProvider.GetRequiredService<IDepartmentRepository>();
        Department department = Department.Create(Guid.NewGuid(), "Repository department " + Guid.NewGuid().ToString("N"), null, Now);
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        Assert.True(await repository.NameExistsAsync(department.Name));
        Assert.False(await repository.NameExistsAsync("Missing department"));
        Assert.False(await repository.NameExistsAsync(department.Name, department.Id));
        department.SoftDelete(Now.AddMinutes(1), Guid.NewGuid());
        await context.SaveChangesAsync();
        Assert.False(await repository.NameExistsAsync(department.Name));
    }

    [Fact]
    public async Task RefreshTokenLookup_ShouldBeTrackedAndPersistRevocation()
    {
        Guid tokenId = Guid.NewGuid();
        string hash = "repository-hash-" + Guid.NewGuid().ToString("N");
        await using ServiceProvider provider = BuildProvider();
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));
            IRefreshTokenRepository repository = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();
            RefreshToken token = RefreshToken.Create(tokenId, principals.Employee.Id, hash, Now.AddDays(1), Now, "127.0.0.1");
            await repository.AddAsync(token);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            context.ChangeTracker.Clear();
            RefreshToken loaded = Assert.IsType<RefreshToken>(await repository.GetByHashAsync(hash));
            Assert.Equal(EntityState.Unchanged, context.Entry(loaded).State);
            loaded.Revoke(Now.AddMinutes(1), "127.0.0.2", null);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using ApplicationDbContext verification = fixture.CreateContext();
        Assert.NotNull((await verification.RefreshTokens.SingleAsync(item => item.Id == tokenId)).RevokedAt);
    }

    [Fact]
    public async Task CorporateRequestLookup_ShouldTrackIncludeApprovalsAndHideSoftDeletedRequest()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ICorporateRequestRepository repository = scope.ServiceProvider.GetRequiredService<ICorporateRequestRepository>();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));
        CorporateRequest request = CorporateRequest.Create(Guid.NewGuid(), "Lookup", "Description", RequestType.Expense, 5000m, principals.Employee.Id, principals.Department.Id, Now);
        request.Submit(principals.Employee.Id, Now.AddMinutes(1));
        request.ApproveByManager(Guid.NewGuid(), principals.Manager.Id, 1000m, Now.AddMinutes(2));
        context.CorporateRequests.Add(request);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        CorporateRequest loaded = Assert.IsType<CorporateRequest>(await repository.GetByIdForUpdateAsync(request.Id));
        Assert.Single(loaded.Approvals);
        Assert.Equal(EntityState.Unchanged, context.Entry(loaded).State);
        loaded.SoftDelete(Now.AddMinutes(3), principals.Manager.Id);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        Assert.Null(await repository.GetByIdForUpdateAsync(request.Id));
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(fixture.ConnectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
