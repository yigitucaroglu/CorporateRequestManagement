using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CorporateRequestManagement.Application.Features.Administration;
using CorporateRequestManagement.Application.Common.Enums;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class AdministrationApiIntegrationTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program>? factory;
    public async Task InitializeAsync()
    {
        await fixture.ResetDevelopmentSeedAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development"); builder.UseSetting("ConnectionStrings:DefaultConnection", fixture.ConnectionString);
            builder.UseSetting("Jwt:Issuer", "CorporateRequestManagement.Api"); builder.UseSetting("Jwt:Audience", "CorporateRequestManagement.Tests");
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-material-32-bytes-minimum"); builder.UseSetting("RateLimiting:AuthPermitLimit", "100");
            builder.ConfigureLogging(logging => { logging.ClearProviders(); logging.AddConsole(); });
        });
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
    }
    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync(); await using ApplicationDbContext db = fixture.CreateContext();
        await db.RequestApprovals.ExecuteDeleteAsync(); await db.CorporateRequests.IgnoreQueryFilters().ExecuteDeleteAsync();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [Departments] SET [ManagerId] = NULL WHERE [Id] = {0}",
            DevelopmentSeedData.FinanceDepartmentId);
        await db.Departments.IgnoreQueryFilters().Where(x => x.Id == DevelopmentSeedData.FinanceDepartmentId).ExecuteDeleteAsync();
        await fixture.ResetDevelopmentSeedAsync();
    }

    [Fact]
    public async Task Admin_management_profile_and_audit_routes_should_enforce_exact_roles_and_safe_contracts()
    {
        await using (AsyncServiceScope scope = factory!.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAdministrationQueryService>().GetUsersAsync(new(1, 20, null, null, null, null, "createdDate", SortDirection.Descending));
        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail); using HttpClient employee = await ClientAsync(DevelopmentSeedData.EmployeeEmail);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/departments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/users")).StatusCode);
        JsonElement profile = await (await employee.GetAsync("/api/account/profile")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(DevelopmentSeedData.EmployeeEmail, profile.GetProperty("email").GetString()); Assert.False(profile.TryGetProperty("passwordHash", out _));
    }

    [Fact]
    public async Task Admin_and_manager_request_lists_should_apply_scope_amount_filter_and_soft_delete()
    {
        using HttpClient employee = await ClientAsync(DevelopmentSeedData.EmployeeEmail); using HttpClient manager = await ClientAsync(DevelopmentSeedData.ManagerEmail); using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail); string title = "Administration-" + Guid.NewGuid().ToString("N");
        using HttpResponseMessage createdResponse = await employee.PostAsJsonAsync("/api/requests", new { title, description = "Filter proof", requestType = 2, amount = 15000m });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode); JsonElement created = await createdResponse.Content.ReadFromJsonAsync<JsonElement>(); Guid id = created.GetProperty("id").GetGuid(); string rowVersion = created.GetProperty("rowVersion").GetString()!;
        await using (AsyncServiceScope scope = factory!.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IAdministrationQueryService>().GetAdminRequestsAsync(new(1, 20, null, null, null, DevelopmentSeedData.DepartmentId, null, null, 14000, 16000, "createdDate", SortDirection.Descending));
        using HttpResponseMessage adminResponse = await admin.GetAsync("/api/admin/requests?searchTerm=" + title + "&departmentId=" + DevelopmentSeedData.DepartmentId + "&minAmount=14000&maxAmount=16000"); Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode); JsonElement adminList = await adminResponse.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1, adminList.GetProperty("totalCount").GetInt32());
        using HttpResponseMessage managerResponse = await manager.GetAsync("/api/manager/requests?searchTerm=" + title + "&minAmount=14000&maxAmount=16000"); Assert.Equal(HttpStatusCode.OK, managerResponse.StatusCode); JsonElement managerList = await managerResponse.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1, managerList.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/manager/requests")).StatusCode);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/requests/{id}") { Content = JsonContent.Create(new { rowVersion }) };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(delete)).StatusCode);
        JsonElement after = await (await admin.GetAsync("/api/admin/requests?searchTerm=" + title)).Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(0, after.GetProperty("totalCount").GetInt32());
        await using ApplicationDbContext db = fixture.CreateContext(); Assert.True(await db.CorporateRequests.IgnoreQueryFilters().AnyAsync(x => x.Id == id && x.IsDeleted)); Assert.True(await db.AuditLogs.AnyAsync(x => x.EntityId == id && x.Action == "CorporateRequest.SoftDeleted"));
    }

    [Fact]
    public async Task Admin_user_department_and_password_lifecycle_should_hash_revoke_audit_and_soft_delete()
    {
        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail);
        using HttpResponseMessage departmentResponse = await admin.PostAsJsonAsync("/api/admin/departments", new { name = "Administration Test" });
        Assert.Equal(HttpStatusCode.Created, departmentResponse.StatusCode); Guid departmentId = (await departmentResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        const string initial = "AdminStart!2026"; const string reset = "AdminReset!2026"; string email = $"admin-test-{Guid.NewGuid():N}@company.com";
        using HttpResponseMessage createResponse = await admin.PostAsJsonAsync("/api/admin/users", new { firstName = "Phase", lastName = "Seven", email, initialPassword = initial, confirmInitialPassword = initial, role = 1, departmentId, isActive = true });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode); JsonElement created = await createResponse.Content.ReadFromJsonAsync<JsonElement>(); Guid userId = created.GetProperty("id").GetGuid(); Assert.False(created.TryGetProperty("passwordHash", out _));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/users", new { firstName = "Duplicate", lastName = "User", email = email.ToUpperInvariant(), initialPassword = initial, confirmInitialPassword = initial, role = 1, departmentId, isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, initial)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/users/{userId}/reset-password", new { newPassword = reset, confirmPassword = reset })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, initial)).StatusCode); Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, reset)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/users/{userId}")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, reset)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/departments/{departmentId}")).StatusCode);
        await using ApplicationDbContext db = fixture.CreateContext(); string audit = string.Join('|', await db.AuditLogs.Where(x => x.EntityId == userId).Select(x => x.Action + x.OldValues + x.NewValues).ToListAsync());
        Assert.Contains("User.Created", audit); Assert.Contains("User.PasswordReset", audit); Assert.Contains("User.SoftDeleted", audit); Assert.DoesNotContain(initial, audit); Assert.DoesNotContain(reset, audit);
    }

    [Fact]
    public async Task Change_password_should_require_current_password_revoke_refresh_and_write_secret_free_audit()
    {
        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail); Guid departmentId = (await (await admin.PostAsJsonAsync("/api/admin/departments", new { name = "Password Test" })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        string email = $"password-{Guid.NewGuid():N}@company.com"; const string original = "OriginalPass!2026";
        Guid userId = (await (await admin.PostAsJsonAsync("/api/admin/users", new { firstName = "Password", lastName = "Test", email, initialPassword = original, confirmInitialPassword = original, role = 1, departmentId, isActive = true })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        HttpClient client = factory!.CreateClient(); using HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = original });
        JsonElement auth = await login.Content.ReadFromJsonAsync<JsonElement>(); string refresh = auth.GetProperty("refreshToken").GetString()!; client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/account/change-password", new { currentPassword = "WrongCurrent!2026", newPassword = "ChangedPass!2026", confirmNewPassword = "ChangedPass!2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/account/change-password", new { currentPassword = original, newPassword = "ChangedPass!2026", confirmNewPassword = "ChangedPass!2026" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(email, original)).StatusCode); Assert.Equal(HttpStatusCode.OK, (await LoginAsync(email, "ChangedPass!2026")).StatusCode);
        await using ApplicationDbContext db = fixture.CreateContext(); var audits = db.AuditLogs.Where(x => x.EntityId == userId && x.Action == "User.PasswordChanged"); Assert.Equal(1, await audits.CountAsync());
        string value = string.Join('|', await audits.Select(x => (x.OldValues ?? "") + (x.NewValues ?? "")).ToListAsync()); Assert.DoesNotContain("ChangedPass", value, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("password", value, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/users/{userId}")).StatusCode); Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/departments/{departmentId}")).StatusCode);
    }

    [Fact]
    public async Task Active_manager_beyond_first_hundred_departments_should_not_be_demoted_moved_deactivated_or_deleted()
    {
        Guid managerId = Guid.NewGuid();
        Guid managedDepartmentId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow.AddDays(-2);
        await using (ApplicationDbContext db = fixture.CreateContext())
        {
            var departments = Enumerable.Range(0, 101)
                .Select(i => Department.Create(i == 0 ? managedDepartmentId : Guid.NewGuid(), $"Guard-{Guid.NewGuid():N}", null, now.AddMinutes(i)))
                .ToList();
            db.Departments.AddRange(departments);
            await db.SaveChangesAsync();
            User manager = User.Create(managerId, "Paging", "Manager", $"paging-{Guid.NewGuid():N}@company.com", "test-hash", UserRole.Manager, managedDepartmentId, now);
            db.Users.Add(manager);
            await db.SaveChangesAsync();
            departments[0].AssignManager(managerId);
            await db.SaveChangesAsync();
        }

        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail);
        object Base(UserRole role, Guid? departmentId, bool isActive) => new { firstName = "Paging", lastName = "Manager", email = $"paging-{managerId:N}@company.com", role = (int)role, departmentId, isActive };
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/admin/users/{managerId}", Base(UserRole.Manager, managedDepartmentId, false))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/admin/users/{managerId}", Base(UserRole.Employee, managedDepartmentId, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/admin/users/{managerId}", Base(UserRole.Manager, DevelopmentSeedData.DepartmentId, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/users/{managerId}")).StatusCode);

        await using ApplicationDbContext verify = fixture.CreateContext();
        User unchanged = await verify.Users.SingleAsync(x => x.Id == managerId);
        Assert.True(unchanged.IsActive); Assert.Equal(UserRole.Manager, unchanged.Role); Assert.Equal(managedDepartmentId, unchanged.DepartmentId);
        Assert.False(await verify.AuditLogs.AnyAsync(x => x.EntityId == managerId));
        Department managed = await verify.Departments.SingleAsync(x => x.Id == managedDepartmentId); managed.AssignManager(null); await verify.SaveChangesAsync();
        await verify.Users.Where(x => x.Id == managerId).ExecuteDeleteAsync();
        await verify.Departments.Where(x => x.Name.StartsWith("Guard-")).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Password_confirmation_mismatch_should_leave_hash_tokens_and_audit_unchanged()
    {
        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail);
        await using ApplicationDbContext before = fixture.CreateContext();
        string hash = await before.Users.Where(x => x.Id == DevelopmentSeedData.EmployeeUserId).Select(x => x.PasswordHash).SingleAsync();
        int tokenCount = await before.RefreshTokens.CountAsync(x => x.UserId == DevelopmentSeedData.EmployeeUserId && x.RevokedAt == null);
        int auditCount = await before.AuditLogs.CountAsync(x => x.EntityId == DevelopmentSeedData.EmployeeUserId);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/users/{DevelopmentSeedData.EmployeeUserId}/reset-password", new { newPassword = "MismatchPass!2026", confirmPassword = "DifferentPass!2026" })).StatusCode);
        using HttpClient employee = await ClientAsync(DevelopmentSeedData.EmployeeEmail);
        Assert.Equal(HttpStatusCode.BadRequest, (await employee.PostAsJsonAsync("/api/account/change-password", new { currentPassword = DevelopmentSeedData.DemoPassword, newPassword = "MismatchPass!2026", confirmNewPassword = "DifferentPass!2026" })).StatusCode);
        await using ApplicationDbContext after = fixture.CreateContext();
        Assert.Equal(hash, await after.Users.Where(x => x.Id == DevelopmentSeedData.EmployeeUserId).Select(x => x.PasswordHash).SingleAsync());
        Assert.Equal(tokenCount + 1, await after.RefreshTokens.CountAsync(x => x.UserId == DevelopmentSeedData.EmployeeUserId && x.RevokedAt == null));
        Assert.Equal(auditCount + 1, await after.AuditLogs.CountAsync(x => x.EntityId == DevelopmentSeedData.EmployeeUserId));
    }

    [Fact]
    public async Task Admin_and_manager_general_detail_should_return_deterministic_approval_history()
    {
        using HttpClient employee = await ClientAsync(DevelopmentSeedData.EmployeeEmail);
        using HttpClient manager = await ClientAsync(DevelopmentSeedData.ManagerEmail);
        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail);
        JsonElement created = await (await employee.PostAsJsonAsync("/api/requests", new { title = "History proof", description = "Ordered history", requestType = 2, amount = 15001m })).Content.ReadFromJsonAsync<JsonElement>();
        Guid id = created.GetProperty("id").GetGuid();
        JsonElement submitted = await (await employee.PostAsJsonAsync($"/api/requests/{id}/submit", new { rowVersion = created.GetProperty("rowVersion").GetString() })).Content.ReadFromJsonAsync<JsonElement>();
        JsonElement managerDecision = await (await manager.PostAsJsonAsync($"/api/approvals/manager/{id}/approve", new { rowVersion = submitted.GetProperty("rowVersion").GetString() })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/approvals/admin/{id}/reject", new { rowVersion = managerDecision.GetProperty("rowVersion").GetString(), reason = "Bütçe uygun değil <script>alert(1)</script>" })).StatusCode);

        JsonElement managerDetail = await (await manager.GetAsync($"/api/manager/requests/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        JsonElement adminDetail = await (await admin.GetAsync($"/api/admin/requests/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        foreach (JsonElement detail in new[] { managerDetail, adminDetail })
        {
            JsonElement.ArrayEnumerator history = detail.GetProperty("approvalHistory").EnumerateArray();
            List<JsonElement> items = history.ToList();
            Assert.Equal(2, items.Count);
            Assert.Equal(1, items[0].GetProperty("approvalLevel").GetInt32());
            Assert.Equal(2, items[1].GetProperty("approvalLevel").GetInt32());
        }
    }

    [Fact]
    public async Task ProtectedRoutes_WhenCalledByEachRole_ShouldEnforceExactAuthorizationMatrix()
    {
        using HttpClient anonymous = factory!.CreateClient();
        using HttpClient employee = await ClientAsync(DevelopmentSeedData.EmployeeEmail);
        using HttpClient manager = await ClientAsync(DevelopmentSeedData.ManagerEmail);
        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail);
        string[] adminRoutes = ["/api/admin/users", "/api/admin/departments", "/api/admin/requests", "/api/audit-logs"];
        foreach (string route in adminRoutes)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(route)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/manager/requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/manager/requests")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/manager/requests")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/manager/requests")).StatusCode);
        foreach (HttpClient authenticated in new[] { employee, manager, admin }) Assert.Equal(HttpStatusCode.OK, (await authenticated.GetAsync("/api/account/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/account/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/admin/users/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.DeleteAsync($"/api/admin/users/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.DeleteAsync($"/api/admin/departments/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Audit_projection_should_fail_closed_for_unknown_nested_sensitive_and_malformed_payloads()
    {
        Guid safeId = Guid.NewGuid(), nestedId = Guid.NewGuid(), malformedId = Guid.NewGuid();
        await using (ApplicationDbContext db = fixture.CreateContext())
        {
            db.AuditLogs.AddRange(
                AuditLog.Create(safeId, DevelopmentSeedData.AdminUserId, "User", Guid.NewGuid(), "Security.Safe", null, "{\"Role\":3,\"UnknownAlias\":\"must-hide\"}", DateTimeOffset.UtcNow),
                AuditLog.Create(nestedId, DevelopmentSeedData.AdminUserId, "User", Guid.NewGuid(), "Security.Nested", null, "{\"Name\":\"safe\",\"Metadata\":{\"Password\":\"secret-value\"}}", DateTimeOffset.UtcNow.AddMilliseconds(1)),
                AuditLog.Create(malformedId, DevelopmentSeedData.AdminUserId, "User", Guid.NewGuid(), "Security.Malformed", null, "<script>secret-value</script>", DateTimeOffset.UtcNow.AddMilliseconds(2)));
            await db.SaveChangesAsync();
        }
        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail);
        string safe = await (await admin.GetAsync($"/api/audit-logs/{safeId}")).Content.ReadAsStringAsync();
        string nested = await (await admin.GetAsync($"/api/audit-logs/{nestedId}")).Content.ReadAsStringAsync();
        string malformed = await (await admin.GetAsync($"/api/audit-logs/{malformedId}")).Content.ReadAsStringAsync();
        Assert.Contains("Role", safe); Assert.DoesNotContain("must-hide", safe);
        Assert.Contains("safe", nested); Assert.DoesNotContain("secret-value", nested);
        Assert.DoesNotContain("<script>", malformed); Assert.DoesNotContain("secret-value", malformed);
    }

    private async Task<HttpClient> ClientAsync(string email)
    {
        HttpClient client = factory!.CreateClient(); using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DevelopmentSeedData.DemoPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString()); return client;
    }
    private Task<HttpResponseMessage> LoginAsync(string email, string password) => factory!.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
}
