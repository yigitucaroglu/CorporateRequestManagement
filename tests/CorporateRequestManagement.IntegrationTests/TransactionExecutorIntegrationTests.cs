using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Infrastructure;
using CorporateRequestManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class TransactionExecutorIntegrationTests(SqlServerDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_WhenOperationSucceeds_ShouldCommitAllChanges()
    {
        (Guid firstId, Guid secondId) = (Guid.NewGuid(), Guid.NewGuid());
        await using ServiceProvider provider = BuildProvider();
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ITransactionExecutor executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
            await executor.ExecuteAsync(async cancellationToken =>
            {
                context.Departments.AddRange(CreateDepartment(firstId, "Commit A"), CreateDepartment(secondId, "Commit B"));
                await context.SaveChangesAsync(cancellationToken);
            });
        }

        await using ApplicationDbContext verification = fixture.CreateContext();
        Assert.Equal(2, await verification.Departments.CountAsync(item => item.Id == firstId || item.Id == secondId));
    }

    [Fact]
    public async Task ExecuteAsyncOfT_WhenOperationSucceeds_ShouldReturnResultAndCommit()
    {
        Guid id = Guid.NewGuid();
        await using ServiceProvider provider = BuildProvider();
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ITransactionExecutor executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
            string result = await executor.ExecuteAsync(async cancellationToken =>
            {
                context.Departments.Add(CreateDepartment(id, "Generic result"));
                await context.SaveChangesAsync(cancellationToken);
                return "expected-result";
            });
            Assert.Equal("expected-result", result);
        }

        await using ApplicationDbContext verification = fixture.CreateContext();
        Assert.True(await verification.Departments.AnyAsync(item => item.Id == id));
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationIsNull_ShouldRejectBothOverloads()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ITransactionExecutor executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        ArgumentNullException nonGeneric = await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ExecuteAsync(null!));
        ArgumentNullException generic = await Assert.ThrowsAsync<ArgumentNullException>(() => executor.ExecuteAsync<string>(null!));
        Assert.Equal("operation", nonGeneric.ParamName);
        Assert.Equal("operation", generic.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationFails_ShouldRollbackAndPreserveOriginalException()
    {
        Guid id = Guid.NewGuid();
        await using ServiceProvider provider = BuildProvider();
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ITransactionExecutor executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
            TestOperationException exception = await Assert.ThrowsAsync<TestOperationException>(() => executor.ExecuteAsync(async cancellationToken =>
            {
                context.Departments.Add(CreateDepartment(id, "Rollback original"));
                await context.SaveChangesAsync(cancellationToken);
                throw new TestOperationException("original-operation-message");
            }));
            Assert.Equal("original-operation-message", exception.Message);
            Assert.False(string.IsNullOrWhiteSpace(exception.StackTrace));
        }

        await using ApplicationDbContext verification = fixture.CreateContext();
        Assert.False(await verification.Departments.AnyAsync(item => item.Id == id));
    }

    [Fact]
    public async Task ExecuteAsync_WhenNestedOperationSucceedsThenOuterFails_ShouldRollbackBothWrites()
    {
        (Guid outerId, Guid innerId) = (Guid.NewGuid(), Guid.NewGuid());
        await using ServiceProvider provider = BuildProvider();
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ITransactionExecutor executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
            await Assert.ThrowsAsync<TestOperationException>(() => executor.ExecuteAsync(async cancellationToken =>
            {
                context.Departments.Add(CreateDepartment(outerId, "Nested outer"));
                await context.SaveChangesAsync(cancellationToken);
                await executor.ExecuteAsync(async innerToken =>
                {
                    context.Departments.Add(CreateDepartment(innerId, "Nested inner"));
                    await context.SaveChangesAsync(innerToken);
                }, cancellationToken);
                throw new TestOperationException("outer-failure");
            }));
        }

        await using ApplicationDbContext verification = fixture.CreateContext();
        Assert.False(await verification.Departments.AnyAsync(item => item.Id == outerId || item.Id == innerId));
    }

    [Fact]
    public async Task ExecuteAsync_WhenExternalTransactionExists_ShouldNotOwnIt()
    {
        Guid id = Guid.NewGuid();
        await using ServiceProvider provider = BuildProvider();
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            ITransactionExecutor executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
            await using var external = await context.Database.BeginTransactionAsync();
            await executor.ExecuteAsync(async cancellationToken =>
            {
                context.Departments.Add(CreateDepartment(id, "External transaction"));
                await context.SaveChangesAsync(cancellationToken);
            });
            Assert.Same(external, context.Database.CurrentTransaction);
            await external.RollbackAsync();
        }

        await using ApplicationDbContext verification = fixture.CreateContext();
        Assert.False(await verification.Departments.AnyAsync(item => item.Id == id));
    }

    [Fact]
    public async Task ExecuteAsync_WhenRollbackAlsoFails_ShouldStillExposeOriginalException()
    {
        await using ServiceProvider provider = BuildProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        ApplicationDbContext context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        ITransactionExecutor executor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();

        TestOperationException exception = await Assert.ThrowsAsync<TestOperationException>(() => executor.ExecuteAsync(async _ =>
        {
            await context.Database.CurrentTransaction!.DisposeAsync();
            throw new TestOperationException("original-after-dispose");
        }));
        Assert.Equal("original-after-dispose", exception.Message);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(fixture.ConnectionString);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static Department CreateDepartment(Guid id, string prefix) =>
        Department.Create(id, prefix + " " + id.ToString("N"), null, Now);

    private sealed class TestOperationException(string message) : Exception(message);
}
