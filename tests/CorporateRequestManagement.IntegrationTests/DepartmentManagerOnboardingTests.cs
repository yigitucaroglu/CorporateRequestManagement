using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class DepartmentManagerOnboardingTests(SqlServerDatabaseFixture fixture)
{
    [Theory]
    [InlineData("exact", "employee@company.com")]
    [InlineData("case-insensitive", "Employee@Company.com")]
    [InlineData("trimmed", " employee@company.com ")]
    public async Task CreateManager_WhenEmailMatchesExistingUserVariant_ShouldReturnConflictWithoutSideEffects(string caseName, string email)
    {
        Assert.NotEmpty(caseName);
        await using var host = new AdministrationApiTestHost(fixture);
        await host.StartAsync();
        using HttpClient admin = await host.LoginAsync(DevelopmentSeedData.AdminEmail);
        await using ApplicationDbContext before = host.Db();
        int users = await before.Users.CountAsync();
        int audits = await before.AuditLogs.CountAsync();

        using HttpResponseMessage response = await admin.PostAsJsonAsync($"/api/admin/departments/{DevelopmentSeedData.FinanceDepartmentId}/manager/create-and-assign",
            new { firstName = "Aynı", lastName = "Kullanıcı", email, initialPassword = "StrongPass!2026", confirmPassword = "StrongPass!2026", isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("user.email_exists", problem.GetProperty("code").GetString());

        await using ApplicationDbContext after = host.Db();
        Assert.Equal(users, await after.Users.CountAsync());
        Assert.Equal(audits, await after.AuditLogs.CountAsync());
        Assert.Null((await after.Departments.SingleAsync(x => x.Id == DevelopmentSeedData.FinanceDepartmentId)).ManagerId);
    }

    [Fact]
    public async Task Sql_unique_email_violation_should_translate_to_existing_safe_conflict_code()
    {
        await using var host = new AdministrationApiTestHost(fixture);
        await host.StartAsync();
        string email = $"positive-race-{Guid.NewGuid():N}@company.com";
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using ApplicationDbContext first = host.Db();
        await using ApplicationDbContext second = host.Db();
        first.Users.Add(CorporateRequestManagement.Domain.Entities.User.Create(Guid.NewGuid(), "Race", "One", email, "hash", UserRole.Employee, DevelopmentSeedData.DepartmentId, now));
        second.Users.Add(CorporateRequestManagement.Domain.Entities.User.Create(Guid.NewGuid(), "Race", "Two", email, "hash", UserRole.Employee, DevelopmentSeedData.DepartmentId, now));
        await first.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<CorporateRequestManagement.Application.Common.Exceptions.ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("user.email_exists", exception.Code);
        Assert.Equal("Bu e-posta adresiyle kayıtlı bir kullanıcı zaten bulunuyor.", exception.Message);
    }

    [Fact]
    public async Task Create_and_assign_should_be_admin_only_atomic_and_return_no_password_material()
    {
        await using var host = new AdministrationApiTestHost(fixture);
        await host.StartAsync();
        using HttpClient anonymous = host.Factory.CreateClient();
        using HttpClient employee = await host.LoginAsync(DevelopmentSeedData.EmployeeEmail);
        using HttpClient manager = await host.LoginAsync(DevelopmentSeedData.ManagerEmail);
        using HttpClient admin = await host.LoginAsync(DevelopmentSeedData.AdminEmail);
        Guid departmentId = DevelopmentSeedData.FinanceDepartmentId;
        string email = $"positive-onboarding-{Guid.NewGuid():N}@company.com";
        var body = new { firstName = "Yeni", lastName = "Yönetici", email, initialPassword = "StrongPass!2026", confirmPassword = "StrongPass!2026", isActive = true };

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/admin/departments/{departmentId}/manager/create-and-assign", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync($"/api/admin/departments/{departmentId}/manager/create-and-assign", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync($"/api/admin/departments/{departmentId}/manager/create-and-assign", body)).StatusCode);

        using HttpResponseMessage response = await admin.PostAsJsonAsync($"/api/admin/departments/{departmentId}/manager/create-and-assign", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", payload, StringComparison.OrdinalIgnoreCase);

        await using ApplicationDbContext db = host.Db();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        var department = await db.Departments.SingleAsync(x => x.Id == departmentId);
        Assert.Equal(UserRole.Manager, user.Role);
        Assert.Equal(departmentId, user.DepartmentId);
        Assert.True(user.IsActive);
        Assert.False(user.IsDeleted);
        Assert.Equal(user.Id, department.ManagerId);
        Assert.True(await db.AuditLogs.AnyAsync(x => x.EntityId == user.Id && x.Action == "User.Created"));
        Assert.True(await db.AuditLogs.AnyAsync(x => x.EntityId == departmentId && x.Action == "Department.ManagerAssigned"));

        using HttpResponseMessage second = await admin.PostAsJsonAsync($"/api/admin/departments/{departmentId}/manager/create-and-assign",
            new { firstName = "Başka", lastName = "Yönetici", email = $"positive-onboarding-{Guid.NewGuid():N}@company.com", initialPassword = "StrongPass!2026", confirmPassword = "StrongPass!2026", isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        department.AssignManager(null);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("short", "short")]
    [InlineData("StrongPass!2026", "DifferentPass!2026")]
    public async Task Invalid_password_should_leave_department_user_and_audit_unchanged(string password, string confirmation)
    {
        await using var host = new AdministrationApiTestHost(fixture);
        await host.StartAsync();
        using HttpClient admin = await host.LoginAsync(DevelopmentSeedData.AdminEmail);
        string email = $"positive-invalid-{Guid.NewGuid():N}@company.com";
        await using ApplicationDbContext before = host.Db();
        int auditCount = await before.AuditLogs.CountAsync();

        using HttpResponseMessage response = await admin.PostAsJsonAsync($"/api/admin/departments/{DevelopmentSeedData.FinanceDepartmentId}/manager/create-and-assign",
            new { firstName = "Hatalı", lastName = "Yönetici", email, initialPassword = password, confirmPassword = confirmation, isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using ApplicationDbContext after = host.Db();
        Assert.False(await after.Users.AnyAsync(x => x.Email == email));
        Assert.Null((await after.Departments.SingleAsync(x => x.Id == DevelopmentSeedData.FinanceDepartmentId)).ManagerId);
        Assert.Equal(auditCount, await after.AuditLogs.CountAsync());
    }
}
