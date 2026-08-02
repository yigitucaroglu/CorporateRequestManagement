using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SqlServerTestGroup : ICollectionFixture<SqlServerDatabaseFixture>
{
    public const string Name = "SQL Server persistence";
}

public sealed class SqlServerDatabaseFixture : IAsyncLifetime
{
    private const string DatabasePrefix = "CorporateRequestManagement_IntegrationTests_";

    public SqlServerDatabaseFixture()
    {
        string source = Environment.GetEnvironmentVariable("CRM_TEST_SQLSERVER_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true";
        var builder = new SqlConnectionStringBuilder(source)
        {
            InitialCatalog = DatabasePrefix + Guid.NewGuid().ToString("N"),
            MultipleActiveResultSets = true
        };
        DatabaseName = builder.InitialCatalog;
        ConnectionString = builder.ConnectionString;
    }

    public string ConnectionString { get; }

    public string DatabaseName { get; }

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    public async Task InitializeAsync()
    {
        await using ApplicationDbContext context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        ValidateDatabaseName(DatabaseName);

        await using ApplicationDbContext context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    internal static void ValidateDatabaseName(string? databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName)
            || !databaseName.StartsWith(DatabasePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Integration database safety validation failed.");
        }

        string suffix = databaseName[DatabasePrefix.Length..];
        if (suffix.Length != 32 || !Guid.TryParseExact(suffix, "N", out _))
        {
            throw new InvalidOperationException("Integration database safety suffix validation failed.");
        }
    }

    internal async Task ResetDevelopmentSeedAsync()
    {
        await using ApplicationDbContext context = CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE [Departments] SET [ManagerId] = NULL WHERE [Id] = {0}",
            DevelopmentSeedData.DepartmentId);
        await context.Users.IgnoreQueryFilters().Where(user =>
                user.Id == DevelopmentSeedData.AdminUserId
                || user.Id == DevelopmentSeedData.ManagerUserId
                || user.Id == DevelopmentSeedData.EmployeeUserId)
            .ExecuteDeleteAsync();
        await context.Departments.IgnoreQueryFilters()
            .Where(item => item.Id == DevelopmentSeedData.DepartmentId)
            .ExecuteDeleteAsync();
    }

    public static async Task<(User Employee, User Manager, Department Department)> AddPrincipalsAsync(
        ApplicationDbContext context,
        string discriminator)
    {
        DateTimeOffset now = new(2026, 8, 2, 10, 0, 0, TimeSpan.Zero);
        Department department = Department.Create(Guid.NewGuid(), "Department " + discriminator, null, now);
        context.Departments.Add(department);
        await context.SaveChangesAsync();

        User employee = User.Create(Guid.NewGuid(), "Test", "Employee", $"employee-{discriminator}@crm.local", "hash", UserRole.Employee, department.Id, now);
        User manager = User.Create(Guid.NewGuid(), "Test", "Manager", $"manager-{discriminator}@crm.local", "hash", UserRole.Manager, department.Id, now);
        context.Users.AddRange(employee, manager);
        await context.SaveChangesAsync();
        context.Entry(department).Property(item => item.ManagerId).CurrentValue = manager.Id;
        await context.SaveChangesAsync();
        return (employee, manager, department);
    }
}
