using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CorporateRequestManagement.IntegrationTests;

internal sealed class Phase7ApiTestHost(SqlServerDatabaseFixture fixture) : IAsyncDisposable
{
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public async Task StartAsync()
    {
        await CleanOwnedUsersAsync();
        await fixture.ResetDevelopmentSeedAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { b.UseEnvironment("Development"); b.UseSetting("ConnectionStrings:DefaultConnection", fixture.ConnectionString); b.UseSetting("Jwt:Issuer", "CorporateRequestManagement.Api"); b.UseSetting("Jwt:Audience", "CorporateRequestManagement.Tests"); b.UseSetting("Jwt:SigningKey", "integration-test-signing-material-32-bytes-minimum"); b.UseSetting("RateLimiting:AuthPermitLimit", "100"); b.ConfigureLogging(x => x.ClearProviders()); b.ConfigureServices(s => s.AddDataProtection().UseEphemeralDataProtectionProvider()); });
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
    }
    public async Task<HttpClient> LoginAsync(string email)
    {
        HttpClient client = Factory.CreateClient(); using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DevelopmentSeedData.DemoPassword }); Assert.Equal(HttpStatusCode.OK, response.StatusCode); JsonElement json = await response.Content.ReadFromJsonAsync<JsonElement>(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString()); return client;
    }
    public ApplicationDbContext Db() => fixture.CreateContext();
    public async ValueTask DisposeAsync() { if (Factory is not null) await Factory.DisposeAsync(); await using ApplicationDbContext db = fixture.CreateContext(); await db.RequestApprovals.ExecuteDeleteAsync(); await db.CorporateRequests.IgnoreQueryFilters().ExecuteDeleteAsync(); await CleanOwnedUsersAsync(); await fixture.ResetDevelopmentSeedAsync(); }
    private async Task CleanOwnedUsersAsync() { await using ApplicationDbContext db = fixture.CreateContext(); Department? seed = await db.Departments.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == DevelopmentSeedData.DepartmentId); if (seed is not null && seed.ManagerId != DevelopmentSeedData.ManagerUserId) { seed.AssignManager(DevelopmentSeedData.ManagerUserId); await db.SaveChangesAsync(); } await db.Users.IgnoreQueryFilters().Where(x => x.Email.StartsWith("inactive-") || x.Email.StartsWith("wrong-") || x.Email.StartsWith("deleted-") || x.Email.StartsWith("replacement-") || x.Email.StartsWith("positive-")).ExecuteDeleteAsync(); }
}

[Collection(SqlServerTestGroup.Name)]
public sealed class Phase7MutationAuthorizationTests(SqlServerDatabaseFixture fixture)
{
    [Fact]
    public async Task Every_phase7_mutation_should_enforce_exact_actor_matrix_over_real_http()
    {
        await using var host = new Phase7ApiTestHost(fixture); await host.StartAsync(); using HttpClient anonymous = host.Factory.CreateClient(); using HttpClient employee = await host.LoginAsync(DevelopmentSeedData.EmployeeEmail); using HttpClient manager = await host.LoginAsync(DevelopmentSeedData.ManagerEmail); using HttpClient admin = await host.LoginAsync(DevelopmentSeedData.AdminEmail);
        Guid id = Guid.NewGuid(); var mutations = new (HttpMethod Method, string Path, object Body)[] { (HttpMethod.Post, "/api/admin/users", new { firstName = "", lastName = "", email = "bad", initialPassword = "x", role = 1, departmentId = (Guid?)null, isActive = true }), (HttpMethod.Put, $"/api/admin/users/{id}", new { firstName = "", lastName = "", email = "bad", role = 1, departmentId = (Guid?)null, isActive = true }), (HttpMethod.Post, $"/api/admin/users/{id}/reset-password", new { newPassword = "x", confirmPassword = "y" }), (HttpMethod.Delete, $"/api/admin/users/{id}", new { }), (HttpMethod.Post, "/api/admin/departments", new { name = "" }), (HttpMethod.Put, $"/api/admin/departments/{id}", new { name = "" }), (HttpMethod.Put, $"/api/admin/departments/{id}/manager", new { managerId = (Guid?)null }), (HttpMethod.Delete, $"/api/admin/departments/{id}", new { }), (HttpMethod.Delete, $"/api/admin/requests/{id}", new { rowVersion = "invalid" }) };
        foreach (var m in mutations) { Assert.Equal(HttpStatusCode.Unauthorized, (await Send(anonymous, m)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Send(employee, m)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Send(manager, m)).StatusCode); HttpStatusCode adminStatus = (await Send(admin, m)).StatusCode; Assert.DoesNotContain(adminStatus, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden }); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/account/change-password", new { })).StatusCode);
        foreach (HttpClient actor in new[] { employee, manager, admin }) { HttpStatusCode status = (await actor.PostAsJsonAsync("/api/account/change-password", new { currentPassword = "x", newPassword = "x", confirmNewPassword = "y" })).StatusCode; Assert.Equal(HttpStatusCode.BadRequest, status); }
    }
    private static Task<HttpResponseMessage> Send(HttpClient client, (HttpMethod Method, string Path, object Body) m) { var request = new HttpRequestMessage(m.Method, m.Path) { Content = JsonContent.Create(m.Body) }; return client.SendAsync(request); }
}

[Collection(SqlServerTestGroup.Name)]
public sealed class Phase7AdminRequestConcurrencyTests(SqlServerDatabaseFixture fixture)
{
    [Fact]
    public async Task Admin_soft_delete_with_stale_rowversion_should_preserve_non_empty_approval_history_then_accept_fresh_version()
    {
        await using var host = new Phase7ApiTestHost(fixture);
        await host.StartAsync();
        using HttpClient employee = await host.LoginAsync(DevelopmentSeedData.EmployeeEmail);
        using HttpClient manager = await host.LoginAsync(DevelopmentSeedData.ManagerEmail);
        using HttpClient admin = await host.LoginAsync(DevelopmentSeedData.AdminEmail);

        JsonElement created = await (await employee.PostAsJsonAsync("/api/requests", new { title = "P7 history", description = "Concurrency", requestType = 1, amount = 10000.01m })).Content.ReadFromJsonAsync<JsonElement>();
        Guid id = created.GetProperty("id").GetGuid();
        JsonElement submitted = await (await employee.PostAsJsonAsync($"/api/requests/{id}/submit", new { rowVersion = created.GetProperty("rowVersion").GetString() })).Content.ReadFromJsonAsync<JsonElement>();
        string stale = submitted.GetProperty("rowVersion").GetString()!;
        JsonElement managerApproved = await (await manager.PostAsJsonAsync($"/api/approvals/manager/{id}/approve", new { rowVersion = stale })).Content.ReadFromJsonAsync<JsonElement>();
        JsonElement adminApproved = await (await admin.PostAsJsonAsync($"/api/approvals/admin/{id}/approve", new { rowVersion = managerApproved.GetProperty("rowVersion").GetString() })).Content.ReadFromJsonAsync<JsonElement>();
        string fresh = adminApproved.GetProperty("rowVersion").GetString()!;

        await using ApplicationDbContext snapshotContext = host.Db();
        var approvalSnapshot = await snapshotContext.RequestApprovals
            .Where(x => x.RequestId == id)
            .OrderBy(x => x.ApprovalLevel)
            .Select(x => new { x.Id, x.ApprovalLevel, x.Status, x.Description, x.ActionDate })
            .ToListAsync();
        Assert.Equal(2, approvalSnapshot.Count);

        using HttpResponseMessage conflict = await Delete(admin, id, stale);
        JsonElement problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("concurrency.conflict", problem.GetProperty("code").GetString());
        await AssertStateAndHistoryAsync(host, id, approvalSnapshot, isDeleted: false, expectedDeleteAudits: 0);

        using HttpResponseMessage deleted = await Delete(admin, id, fresh);
        using HttpResponseMessage repeated = await Delete(admin, id, fresh);
        using HttpResponseMessage normalDetail = await admin.GetAsync($"/api/admin/requests/{id}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, repeated.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, normalDetail.StatusCode);
        await AssertStateAndHistoryAsync(host, id, approvalSnapshot, isDeleted: true, expectedDeleteAudits: 1);
    }

    private static async Task AssertStateAndHistoryAsync(Phase7ApiTestHost host, Guid id, System.Collections.IList expected, bool isDeleted, int expectedDeleteAudits)
    {
        await using ApplicationDbContext db = host.Db();
        CorporateRequest row = await db.CorporateRequests.IgnoreQueryFilters().SingleAsync(x => x.Id == id);
        var actual = await db.RequestApprovals.Where(x => x.RequestId == id).OrderBy(x => x.ApprovalLevel).Select(x => new { x.Id, x.ApprovalLevel, x.Status, x.Description, x.ActionDate }).ToListAsync();

        Assert.Equal(isDeleted, row.IsDeleted);
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < actual.Count; index++) Assert.Equal(expected[index], actual[index]);
        Assert.Equal(expectedDeleteAudits, await db.AuditLogs.CountAsync(x => x.EntityId == id && x.Action == "CorporateRequest.SoftDeleted"));
    }
    private static Task<HttpResponseMessage> Delete(HttpClient c, Guid id, string rowVersion) => c.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/requests/{id}") { Content = JsonContent.Create(new { rowVersion }) });
}

[Collection(SqlServerTestGroup.Name)]
public sealed class Phase7AdminSelfProtectionTests(SqlServerDatabaseFixture fixture)
{
    [Fact]
    public async Task Current_admin_cannot_deactivate_demote_or_delete_self_and_failures_have_no_side_effects()
    {
        await using var host = new Phase7ApiTestHost(fixture); await host.StartAsync(); using HttpClient admin = await host.LoginAsync(DevelopmentSeedData.AdminEmail); await using ApplicationDbContext before = host.Db(); int tokenCount = await before.RefreshTokens.CountAsync(x => x.UserId == DevelopmentSeedData.AdminUserId && x.RevokedAt == null); int auditCount = await before.AuditLogs.CountAsync(x => x.EntityId == DevelopmentSeedData.AdminUserId && (x.Action == "User.Updated" || x.Action == "User.SoftDeleted"));
        object Body(int role, bool active) => new { firstName = "System", lastName = "Admin", email = DevelopmentSeedData.AdminEmail, role, departmentId = (Guid?)null, isActive = active };
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/admin/users/{DevelopmentSeedData.AdminUserId}", Body(3, false))).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/admin/users/{DevelopmentSeedData.AdminUserId}", Body(1, true))).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/admin/users/{DevelopmentSeedData.AdminUserId}", Body(2, true))).StatusCode); Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/users/{DevelopmentSeedData.AdminUserId}")).StatusCode);
        await using ApplicationDbContext after = host.Db(); User user = await after.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == DevelopmentSeedData.AdminUserId); Assert.True(user.IsActive); Assert.False(user.IsDeleted); Assert.Equal(UserRole.Admin, user.Role); Assert.Equal(tokenCount, await after.RefreshTokens.CountAsync(x => x.UserId == user.Id && x.RevokedAt == null)); Assert.Equal(auditCount, await after.AuditLogs.CountAsync(x => x.EntityId == user.Id && (x.Action == "User.Updated" || x.Action == "User.SoftDeleted")));
    }

    [Fact]
    public async Task Self_protection_should_allow_updating_another_employee_and_persist_state_and_audit()
    {
        await using var host = new Phase7ApiTestHost(fixture);
        await host.StartAsync();
        using HttpClient admin = await host.LoginAsync(DevelopmentSeedData.AdminEmail);
        string email = $"positive-{Guid.NewGuid():N}@company.com";
        JsonElement created = await (await admin.PostAsJsonAsync("/api/admin/users", new { firstName = "Positive", lastName = "Employee", email, initialPassword = "EmployeePass!2026", confirmInitialPassword = "EmployeePass!2026", role = 1, departmentId = DevelopmentSeedData.DepartmentId, isActive = true })).Content.ReadFromJsonAsync<JsonElement>();
        Guid id = created.GetProperty("id").GetGuid();

        using HttpResponseMessage response = await admin.PutAsJsonAsync($"/api/admin/users/{id}", new { firstName = "Updated", lastName = "Employee", email, role = 1, departmentId = DevelopmentSeedData.DepartmentId, isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using ApplicationDbContext db = host.Db();
        User updated = await db.Users.SingleAsync(x => x.Id == id);
        User currentAdmin = await db.Users.SingleAsync(x => x.Id == DevelopmentSeedData.AdminUserId);
        Assert.Equal("Updated", updated.FirstName);
        Assert.Equal(DevelopmentSeedData.DepartmentId, updated.DepartmentId);
        Assert.True(await db.AuditLogs.AnyAsync(x => x.EntityId == id && x.Action == "User.Updated"));
        Assert.False(await db.RefreshTokens.AnyAsync(x => x.UserId == id && x.RevokedAt != null));
        Assert.True(currentAdmin.IsActive);
        Assert.Equal(UserRole.Admin, currentAdmin.Role);
        Assert.False(currentAdmin.IsDeleted);
    }
}

[Collection(SqlServerTestGroup.Name)]
public sealed class Phase7InvalidManagerAssignmentTests(SqlServerDatabaseFixture fixture)
{
    [Fact]
    public async Task Manager_assignment_should_reject_every_invalid_actor_without_state_or_audit_and_allow_valid_replacement()
    {
        await using var host = new Phase7ApiTestHost(fixture); await host.StartAsync(); using HttpClient admin = await host.LoginAsync(DevelopmentSeedData.AdminEmail); Guid departmentId = DevelopmentSeedData.DepartmentId; Guid original = DevelopmentSeedData.ManagerUserId;
        async Task<Guid> Create(string label, int role, Guid? dep, bool active) { JsonElement json = await (await admin.PostAsJsonAsync("/api/admin/users", new { firstName = label, lastName = "Manager", email = $"{label}-{Guid.NewGuid():N}@company.com", initialPassword = "ManagerPass!2026", confirmInitialPassword = "ManagerPass!2026", role, departmentId = dep, isActive = active })).Content.ReadFromJsonAsync<JsonElement>(); return json.GetProperty("id").GetGuid(); }
        Guid inactive = await Create("inactive", 2, departmentId, false), wrongDepartment = await Create("wrong", 2, DevelopmentSeedData.FinanceDepartmentId, true), deleted = await Create("deleted", 2, departmentId, true), replacement = await Create("replacement", 2, departmentId, true); Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/users/{deleted}")).StatusCode);
        await using ApplicationDbContext before = host.Db(); int audits = await before.AuditLogs.CountAsync(x => x.EntityId == departmentId && x.Action == "Department.ManagerChanged");
        foreach (Guid invalid in new[] { DevelopmentSeedData.EmployeeUserId, DevelopmentSeedData.AdminUserId, wrongDepartment, inactive, deleted, Guid.NewGuid() }) { HttpStatusCode status = (await admin.PutAsJsonAsync($"/api/admin/departments/{departmentId}/manager", new { managerId = invalid })).StatusCode; Assert.True(status is HttpStatusCode.Conflict or HttpStatusCode.NotFound); await using ApplicationDbContext check = host.Db(); Assert.Equal(original, (await check.Departments.SingleAsync(x => x.Id == departmentId)).ManagerId); Assert.Equal(audits, await check.AuditLogs.CountAsync(x => x.EntityId == departmentId && x.Action == "Department.ManagerChanged")); }
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/departments/{departmentId}/manager", new { managerId = original })).StatusCode); Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/departments/{departmentId}/manager", new { managerId = replacement })).StatusCode);
        await using ApplicationDbContext verify = host.Db(); Department department = await verify.Departments.SingleAsync(x => x.Id == departmentId); User assigned = await verify.Users.SingleAsync(x => x.Id == replacement); Assert.Equal(replacement, department.ManagerId); Assert.True(assigned.IsActive); Assert.False(assigned.IsDeleted); Assert.Equal(UserRole.Manager, assigned.Role); Assert.Equal(departmentId, assigned.DepartmentId); Assert.Equal(audits + 2, await verify.AuditLogs.CountAsync(x => x.EntityId == departmentId && x.Action == "Department.ManagerChanged"));
    }
}

[Collection(SqlServerTestGroup.Name)]
public sealed class Phase7OpenApiContractTests(SqlServerDatabaseFixture fixture)
{
    [Fact]
    public async Task Generated_openapi_should_expose_phase7_routes_security_and_forbid_spoof_or_sensitive_request_fields()
    {
        await using var host = new Phase7ApiTestHost(fixture);
        await host.StartAsync();
        JsonElement root = await host.Factory.CreateClient().GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        Assert.True(root.GetProperty("security").GetArrayLength() > 0);
        JsonElement paths = root.GetProperty("paths");
        string[] required = { "/api/admin/users", "/api/admin/users/{id}", "/api/admin/users/{id}/reset-password", "/api/admin/departments", "/api/admin/departments/{id}", "/api/admin/departments/{id}/manager", "/api/admin/departments/{id}/eligible-managers", "/api/admin/requests", "/api/admin/requests/{id}", "/api/manager/requests", "/api/manager/requests/{id}", "/api/audit-logs", "/api/audit-logs/actors", "/api/audit-logs/{id}", "/api/account/profile", "/api/account/change-password" };
        foreach (string path in required) { Assert.True(paths.TryGetProperty(path, out JsonElement item), path); foreach (JsonProperty operation in item.EnumerateObject()) Assert.False(operation.Value.TryGetProperty("security", out JsonElement security) && security.GetArrayLength() == 0, $"{path} {operation.Name}"); }
        Assert.Equal(0, paths.GetProperty("/api/auth/login").GetProperty("post").GetProperty("security").GetArrayLength());
        JsonElement schemas = root.GetProperty("components").GetProperty("schemas");
        AssertOperationSchema(paths, schemas, "/api/admin/users", "post", ["firstName", "lastName", "email", "initialPassword", "confirmInitialPassword", "role", "departmentId", "isActive"], ["passwordHash", "isDeleted", "deletedDate", "createdDate", "updatedDate", "refreshToken", "accessToken", "tokenHash", "auditLog", "createdByUserId"]);
        AssertOperationSchema(paths, schemas, "/api/admin/users/{id}", "put", ["firstName", "lastName", "email", "role", "departmentId", "isActive"], ["password", "passwordHash", "initialPassword", "isDeleted", "deletedDate", "createdDate", "refreshToken", "accessToken", "tokenHash"]);
        AssertOperationSchema(paths, schemas, "/api/admin/users/{id}/reset-password", "post", ["newPassword", "confirmPassword"], ["passwordHash", "refreshToken", "accessToken", "userId"]);
        AssertOperationSchema(paths, schemas, "/api/account/change-password", "post", ["currentPassword", "newPassword", "confirmNewPassword"], ["passwordHash", "accessToken", "refreshToken"]);
        AssertOperationSchema(paths, schemas, "/api/admin/requests/{id}", "delete", ["rowVersion"], ["status", "owner", "ownerId", "createdByUserId", "departmentId", "isDeleted", "deletedDate", "approvalStatus"]);
    }
    private static void AssertOperationSchema(JsonElement paths, JsonElement schemas, string path, string method, string[] required, string[] forbidden)
    {
        JsonElement operation = paths.GetProperty(path).GetProperty(method);
        JsonElement mediaType = operation.GetProperty("requestBody").GetProperty("content").EnumerateObject().First().Value;
        string reference = mediaType.GetProperty("schema").GetProperty("$ref").GetString()!;
        string schemaName = reference[(reference.LastIndexOf('/') + 1)..];
        JsonElement properties = schemas.GetProperty(schemaName).GetProperty("properties");

        foreach (string name in required) Assert.True(properties.TryGetProperty(name, out _), $"{path} {method} requires {name}");
        foreach (string name in forbidden) Assert.False(properties.TryGetProperty(name, out _), $"{path} {method} forbids {name}");
        Assert.False(operation.TryGetProperty("security", out JsonElement security) && security.GetArrayLength() == 0, $"{path} {method} requires Bearer security");
    }
}
