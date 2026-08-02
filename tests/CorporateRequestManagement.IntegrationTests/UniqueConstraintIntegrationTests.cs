using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class UniqueConstraintIntegrationTests(SqlServerDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 13, 0, 0, TimeSpan.Zero);
    private static readonly int[] UniqueViolationNumbers = [2601, 2627];

    [Fact]
    public async Task UserEmail_WhenOriginalIsInactiveButNotDeleted_ShouldStillRejectDuplicate()
    {
        await using ApplicationDbContext context = fixture.CreateContext();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));
        context.Entry(principals.Employee).Property(user => user.IsActive).CurrentValue = false;
        await context.SaveChangesAsync();
        context.Users.Add(User.Create(Guid.NewGuid(), "Other", "Employee", principals.Employee.Email, "hash", UserRole.Employee, principals.Department.Id, Now));

        await AssertUniqueViolationAsync(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DepartmentName_WhenBothAreActive_ShouldRejectDuplicate()
    {
        await using ApplicationDbContext context = fixture.CreateContext();
        string name = "Unique Department " + Guid.NewGuid().ToString("N");
        context.Departments.Add(Department.Create(Guid.NewGuid(), name, null, Now));
        await context.SaveChangesAsync();
        context.Departments.Add(Department.Create(Guid.NewGuid(), name, null, Now));

        await AssertUniqueViolationAsync(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DepartmentName_WhenOriginalIsSoftDeleted_ShouldAllowReuse()
    {
        await using ApplicationDbContext context = fixture.CreateContext();
        string name = "Reusable Department " + Guid.NewGuid().ToString("N");
        Guid actor = Guid.NewGuid();
        Department original = Department.Create(Guid.NewGuid(), name, null, Now);
        context.Departments.Add(original);
        await context.SaveChangesAsync();
        original.SoftDelete(Now.AddMinutes(1), actor);
        await context.SaveChangesAsync();
        context.Departments.Add(Department.Create(Guid.NewGuid(), name, null, Now.AddMinutes(2)));

        Assert.Equal(1, await context.SaveChangesAsync());
    }

    [Fact]
    public async Task RefreshTokenHash_WhenDuplicated_ShouldRejectSecondHash()
    {
        await using ApplicationDbContext context = fixture.CreateContext();
        var principals = await SqlServerDatabaseFixture.AddPrincipalsAsync(context, Guid.NewGuid().ToString("N"));
        string hash = "hash-" + Guid.NewGuid().ToString("N");
        context.RefreshTokens.Add(CreateToken(Guid.NewGuid(), principals.Employee.Id, hash));
        await context.SaveChangesAsync();
        context.RefreshTokens.Add(CreateToken(Guid.NewGuid(), principals.Employee.Id, hash));

        await AssertUniqueViolationAsync(() => context.SaveChangesAsync());
    }

    private static RefreshToken CreateToken(Guid id, Guid userId, string hash) =>
        RefreshToken.Create(id, userId, hash, Now.AddDays(1), Now, "127.0.0.1");

    private static async Task AssertUniqueViolationAsync(Func<Task> operation)
    {
        DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(operation);
        SqlException sqlException = Assert.IsType<SqlException>(exception.InnerException);
        Assert.Contains(sqlException.Number, UniqueViolationNumbers);
    }
}
