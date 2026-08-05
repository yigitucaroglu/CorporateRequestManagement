using System.Net;
using System.Net.Http.Json;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class Phase7EligibleManagerLookupTests(SqlServerDatabaseFixture fixture)
{
    [Fact]
    public async Task Eligible_manager_lookup_should_filter_in_sql_sort_deterministically_and_not_page_candidates()
    {
        await using var host = new Phase7ApiTestHost(fixture);
        await host.StartAsync();
        using HttpClient admin = await host.LoginAsync(DevelopmentSeedData.AdminEmail);
        Guid departmentId = DevelopmentSeedData.FinanceDepartmentId;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var expected = Enumerable.Range(0, 105).Select(index => User.Create(
            Guid.NewGuid(), $"Aday{index:D3}", "Yönetici", $"positive-lookup-{index:D3}-{Guid.NewGuid():N}@company.com",
            "test-only-hash", UserRole.Manager, departmentId, now.AddTicks(index))).ToList();

        await using (ApplicationDbContext db = host.Db())
        {
            Assert.Equal(0, await db.Users.CountAsync(user => user.DepartmentId == departmentId && user.IsActive));
            Assert.Equal(0, await db.Users.CountAsync(user => user.DepartmentId == departmentId && user.IsActive && user.Role == UserRole.Manager));
            Assert.Equal(0, await db.Users.CountAsync(user => user.DepartmentId == departmentId));
            db.Users.AddRange(expected);
            db.Users.Add(User.Create(Guid.NewGuid(), "Çalışan", "Aday", $"positive-employee-{Guid.NewGuid():N}@company.com", "hash", UserRole.Employee, departmentId, now));
            db.Users.Add(User.Create(Guid.NewGuid(), "Başka", "Yönetici", $"positive-other-{Guid.NewGuid():N}@company.com", "hash", UserRole.Manager, DevelopmentSeedData.DepartmentId, now));
            User inactive = User.Create(Guid.NewGuid(), "Pasif", "Yönetici", $"positive-inactive-{Guid.NewGuid():N}@company.com", "hash", UserRole.Manager, departmentId, now);
            inactive.UpdateProfile(inactive.FirstName, inactive.LastName, inactive.Email, inactive.Role, inactive.DepartmentId, false);
            User deleted = User.Create(Guid.NewGuid(), "Silinmiş", "Yönetici", $"positive-deleted-{Guid.NewGuid():N}@company.com", "hash", UserRole.Manager, departmentId, now);
            deleted.SoftDelete(now.AddMinutes(1), DevelopmentSeedData.AdminUserId);
            db.Users.AddRange(inactive, deleted);
            await db.SaveChangesAsync();
        }

        using HttpResponseMessage response = await admin.GetAsync($"/api/admin/departments/{departmentId}/eligible-managers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        EligibleManagerResponse[] items = (await response.Content.ReadFromJsonAsync<EligibleManagerResponse[]>())!;
        Assert.Equal(105, items.Length);
        Assert.Equal(expected.Select(user => user.Id), items.Select(item => item.Id));
        Assert.All(items, item => Assert.Contains("positive-lookup-", item.Email, StringComparison.Ordinal));
    }

    private sealed record EligibleManagerResponse(Guid Id, string FullName, string Email);
}
