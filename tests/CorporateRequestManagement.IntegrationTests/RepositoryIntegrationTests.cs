using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Application.Features.CorporateRequests.Common;
using CorporateRequestManagement.Application.Features.Dashboard;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Repositories;
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
            await scope.ServiceProvider.GetRequiredService<ITransactionExecutor>().ExecuteAsync(async cancellationToken =>
            {
                RefreshToken loaded = Assert.IsType<RefreshToken>(await repository.GetByHashForUpdateAsync(hash, cancellationToken));
                Assert.Equal(EntityState.Unchanged, context.Entry(loaded).State);
                loaded.Revoke(Now.AddMinutes(1), "127.0.0.2", null);
                await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(cancellationToken);
            });
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

    [Fact]
    public async Task CorporateRequestReadSearch_ShouldFilterBeforePagingAndHonorOwnerSoftDeleteAndCombinedCriteria()
    {
        await using ApplicationDbContext context = fixture.CreateContext();
        string discriminator = Guid.NewGuid().ToString("N");
        var owner = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, discriminator + "-owner");
        var other = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, discriminator + "-other");
        CorporateRequest titleMatch = CorporateRequest.Create(Guid.NewGuid(), "Bilgisayar talebi", "Yeni donanım", RequestType.Expense, 100m, owner.Employee.Id, owner.Department.Id, Now);
        CorporateRequest descriptionMatch = CorporateRequest.Create(Guid.NewGuid(), "Seyahat ekipmanı", "Koruyucu bilgisayar çantası", RequestType.Leave, null, owner.Employee.Id, owner.Department.Id, Now.AddDays(1));
        CorporateRequest submittedMatch = CorporateRequest.Create(Guid.NewGuid(), "Eski bilgisayar yenileme", "Performans ihtiyacı", RequestType.Expense, 200m, owner.Employee.Id, owner.Department.Id, Now.AddDays(2));
        submittedMatch.Submit(owner.Employee.Id, Now.AddDays(2).AddMinutes(1));
        CorporateRequest noMatch = CorporateRequest.Create(Guid.NewGuid(), "Telefon talebi", "Mobil cihaz", RequestType.Equipment, 300m, owner.Employee.Id, owner.Department.Id, Now.AddDays(3));
        CorporateRequest wrongOwner = CorporateRequest.Create(Guid.NewGuid(), "Bilgisayar gizli", "Başka kullanıcı", RequestType.Expense, 400m, other.Employee.Id, other.Department.Id, Now);
        CorporateRequest deleted = CorporateRequest.Create(Guid.NewGuid(), "Bilgisayar silinmiş", "Görünmemeli", RequestType.Expense, 500m, owner.Employee.Id, owner.Department.Id, Now);
        deleted.SoftDelete(Now.AddMinutes(1), owner.Manager.Id);
        context.CorporateRequests.AddRange(titleMatch, descriptionMatch, submittedMatch, noMatch, wrongOwner, deleted);
        await context.SaveChangesAsync();
        var repository = new CorporateRequestReadRepository(context);

        static CorporateRequestListCriteria Criteria(string? search, int pageSize = 20, RequestStatus? status = null,
            RequestType? type = null, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
            new(1, pageSize, status, type, from, to, search, CorporateRequestSortFields.CreatedDate, SortDirection.Ascending);

        var title = await repository.GetOwnedPageAsync(owner.Employee.Id, Criteria("yenileme"));
        Assert.Equal([submittedMatch.Id], title.Items.Select(item => item.Id));
        var description = await repository.GetOwnedPageAsync(owner.Employee.Id, Criteria("Koruyucu"));
        Assert.Equal([descriptionMatch.Id], description.Items.Select(item => item.Id));

        var searched = await repository.GetOwnedPageAsync(owner.Employee.Id, Criteria("  bilgisayar  ", pageSize: 2));
        Assert.Equal(3, searched.TotalCount); Assert.Equal(2, searched.Items.Count); Assert.DoesNotContain(searched.Items, item => item.Id == wrongOwner.Id || item.Id == deleted.Id);
        Assert.Equal(2, (await repository.GetOwnedPageAsync(owner.Employee.Id, Criteria("bilgisayar", status: RequestStatus.Draft))).TotalCount);
        Assert.Equal(2, (await repository.GetOwnedPageAsync(owner.Employee.Id, Criteria("bilgisayar", type: RequestType.Expense))).TotalCount);
        var dated = await repository.GetOwnedPageAsync(owner.Employee.Id, Criteria("bilgisayar", from: Now.AddDays(1), to: Now.AddDays(1)));
        Assert.Equal([descriptionMatch.Id], dated.Items.Select(item => item.Id));
        Assert.Equal(4, (await repository.GetOwnedPageAsync(owner.Employee.Id, Criteria("   "))).TotalCount);
        Assert.Empty((await repository.GetOwnedPageAsync(owner.Employee.Id, Criteria("eşleşmeyen"))).Items);
    }

    [Fact]
    public async Task DashboardReadRepository_ShouldAggregateRoleScopesAndZeroFillTrendOnSqlServer()
    {
        await using ApplicationDbContext context = fixture.CreateContext();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));
        CorporateRequest completed = CorporateRequest.Create(Guid.NewGuid(), "Tamamlanan talep", "Dashboard", RequestType.Expense, 100m, principals.Employee.Id, principals.Department.Id, Now);
        completed.Submit(principals.Employee.Id, Now.AddMinutes(1));
        completed.ApproveByManager(Guid.NewGuid(), principals.Manager.Id, 1000m, Now.AddMinutes(2));
        CorporateRequest pending = CorporateRequest.Create(Guid.NewGuid(), "Bekleyen talep", "Dashboard", RequestType.Equipment, 200m, principals.Employee.Id, principals.Department.Id, Now.AddMinutes(3));
        pending.Submit(principals.Employee.Id, Now.AddMinutes(4));
        context.CorporateRequests.AddRange(completed, pending);
        await context.SaveChangesAsync();
        var repository = new DashboardReadRepository(context);

        EmployeeDashboardDto employee = await repository.GetEmployeeAsync(principals.Employee.Id, Now.AddDays(1));
        Assert.Equal(2, employee.TotalRequests); Assert.Equal(1, employee.PendingRequests); Assert.Equal(1, employee.ApprovedRequests);
        Assert.Equal(30, employee.Trend.Count); Assert.Equal("2026-07-05", employee.Trend[0].PeriodKey); Assert.Equal("2026-08-03", employee.Trend[^1].PeriodKey); Assert.Equal(employee.Trend.OrderBy(x => x.PeriodKey).Select(x => x.PeriodKey), employee.Trend.Select(x => x.PeriodKey)); Assert.Contains(employee.Trend, x => x.PrimaryCount == 0); Assert.Equal(2, employee.RecentRequests.Count);

        ManagerDashboardDto manager = await repository.GetManagerAsync(principals.Manager.Id, Now.AddDays(1));
        Assert.Equal(1, manager.PendingApprovals); Assert.Equal(2, manager.MonthlyIncoming); Assert.Equal(1, manager.MonthlyApproved);
        Assert.Single(manager.PendingPreview); Assert.Equal(30, manager.Trend.Count); Assert.All(manager.Trend, x => Assert.Matches("^\\d{4}-\\d{2}-\\d{2}$", x.PeriodKey)); Assert.Contains(manager.Trend, x => x.PrimaryCount == 0 && x.SecondaryCount == 0);

        AdminDashboardDto admin = await repository.GetAdminAsync(Guid.NewGuid(), Now.AddDays(1));
        Assert.Equal(1, admin.MonthlyApproved); Assert.Contains(admin.StatusDistribution, x => x.Label == nameof(RequestStatus.Approved) && x.Count >= 1);
        Assert.NotEmpty(admin.DepartmentDistribution); Assert.Equal(12, admin.Trend.Count); Assert.Equal("2025-09", admin.Trend[0].PeriodKey); Assert.Equal("2026-08", admin.Trend[^1].PeriodKey); Assert.All(admin.Trend, x => Assert.Matches("^\\d{4}-\\d{2}$", x.PeriodKey)); Assert.Equal(admin.Trend.OrderBy(x => x.PeriodKey).Select(x => x.PeriodKey), admin.Trend.Select(x => x.PeriodKey));
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(fixture.ConnectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }
}
