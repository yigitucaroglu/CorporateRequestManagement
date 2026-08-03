using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class ApprovalsApiIntegrationTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly string[] RaceConflictCodes = ["concurrency.conflict", "approval.already_processed"];
    private WebApplicationFactory<Program>? factory;

    public async Task InitializeAsync()
    {
        await fixture.ResetDevelopmentSeedAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", fixture.ConnectionString);
            builder.UseSetting("Jwt:Issuer", "CorporateRequestManagement.Api");
            builder.UseSetting("Jwt:Audience", "CorporateRequestManagement.Tests");
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-material-32-bytes-minimum");
            builder.UseSetting("Approval:Threshold", "10000");
            builder.UseSetting("RateLimiting:AuthPermitLimit", "30");
            builder.UseSetting("RateLimiting:GeneralPermitLimit", "200");
            builder.ConfigureLogging(logging => logging.ClearProviders());
        });
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        await using ApplicationDbContext context = fixture.CreateContext();
        await context.RequestApprovals.ExecuteDeleteAsync();
        await context.CorporateRequests.IgnoreQueryFilters().ExecuteDeleteAsync();
        await context.Departments.IgnoreQueryFilters().Where(x => x.Id != DevelopmentSeedData.DepartmentId).ExecuteDeleteAsync();
        await fixture.ResetDevelopmentSeedAsync();
    }

    [Fact]
    public async Task Expense_above_threshold_should_flow_from_scoped_manager_to_admin_with_history_and_audits()
    {
        using HttpClient employee = await ClientAsync(DevelopmentSeedData.EmployeeEmail);
        using HttpClient manager = await ClientAsync(DevelopmentSeedData.ManagerEmail);
        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail);
        JsonElement submitted = await CreateAndSubmitAsync(employee, 10000.01m);
        Guid id = submitted.GetProperty("id").GetGuid();

        JsonElement managerQueue = await manager.GetFromJsonAsync<JsonElement>("/api/approvals/manager?pageSize=10&sortBy=amount&sortDirection=Descending");
        Assert.Contains(managerQueue.GetProperty("items").EnumerateArray(), x => x.GetProperty("requestId").GetGuid() == id);
        using HttpResponseMessage managerDecision = await manager.PostAsJsonAsync($"/api/approvals/manager/{id}/approve", new { rowVersion = Version(submitted) });
        Assert.Equal(HttpStatusCode.OK, managerDecision.StatusCode);
        JsonElement managerResult = await managerDecision.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, managerResult.GetProperty("requestStatus").GetInt32());
        Assert.Single(managerResult.GetProperty("approvalHistory").EnumerateArray());

        JsonElement adminQueue = await admin.GetFromJsonAsync<JsonElement>("/api/approvals/admin?pageSize=10");
        Assert.Contains(adminQueue.GetProperty("items").EnumerateArray(), x => x.GetProperty("requestId").GetGuid() == id);
        using HttpResponseMessage adminDecision = await admin.PostAsJsonAsync($"/api/approvals/admin/{id}/approve", new { rowVersion = Version(managerResult) });
        Assert.Equal(HttpStatusCode.OK, adminDecision.StatusCode);
        JsonElement approved = await adminDecision.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, approved.GetProperty("requestStatus").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, approved.GetProperty("completedDate").ValueKind);
        Assert.Equal(2, approved.GetProperty("approvalHistory").GetArrayLength());

        JsonElement history = await employee.GetFromJsonAsync<JsonElement>($"/api/requests/{id}/approval-history");
        Assert.Equal(2, history.GetArrayLength());
        await using ApplicationDbContext context = fixture.CreateContext();
        Assert.Equal(2, await context.RequestApprovals.CountAsync(x => x.RequestId == id));
        Assert.Equal(1, await context.AuditLogs.CountAsync(x => x.EntityId == id && x.Action == "CorporateRequest.ManagerApproved"));
        Assert.Equal(1, await context.AuditLogs.CountAsync(x => x.EntityId == id && x.Action == "CorporateRequest.AdminApproved"));
        Assert.DoesNotContain(await context.AuditLogs.Where(x => x.EntityId == id).Select(x => (x.OldValues ?? "") + (x.NewValues ?? "")).ToArrayAsync(),
            value => value.Contains("rowVersion", StringComparison.OrdinalIgnoreCase) || value.Contains("description", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Manager_reject_should_be_final_and_stale_or_repeat_decisions_should_conflict_without_duplicate_records()
    {
        using HttpClient employee = await ClientAsync(DevelopmentSeedData.EmployeeEmail);
        using HttpClient manager = await ClientAsync(DevelopmentSeedData.ManagerEmail);
        JsonElement submitted = await CreateAndSubmitAsync(employee, 25m);
        Guid id = submitted.GetProperty("id").GetGuid(); string version = Version(submitted);
        using HttpResponseMessage rejectedResponse = await manager.PostAsJsonAsync($"/api/approvals/manager/{id}/reject", new { rowVersion = version, reason = "Bütçe gerekçesi" });
        Assert.Equal(HttpStatusCode.OK, rejectedResponse.StatusCode);
        JsonElement rejected = await rejectedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, rejected.GetProperty("requestStatus").GetInt32());
        using HttpResponseMessage repeat = await manager.PostAsJsonAsync($"/api/approvals/manager/{id}/approve", new { rowVersion = Version(rejected) });
        JsonElement repeatProblem = await AssertProblemAsync(repeat, HttpStatusCode.Conflict);
        Assert.Equal("approval.already_processed", repeatProblem.GetProperty("code").GetString());
        await using ApplicationDbContext context = fixture.CreateContext();
        Assert.Equal(1, await context.RequestApprovals.CountAsync(x => x.RequestId == id));
        Assert.Equal(1, await context.AuditLogs.CountAsync(x => x.EntityId == id && x.Action == "CorporateRequest.ManagerRejected"));
    }

    [Fact]
    public async Task Threshold_boundary_should_finish_at_manager_and_admin_reject_should_be_final()
    {
        using HttpClient employee = await ClientAsync(DevelopmentSeedData.EmployeeEmail);
        using HttpClient manager = await ClientAsync(DevelopmentSeedData.ManagerEmail);
        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail);

        JsonElement boundary = await CreateAndSubmitAsync(employee, 10000m);
        Guid boundaryId = boundary.GetProperty("id").GetGuid();
        using HttpResponseMessage managerFinalResponse = await manager.PostAsJsonAsync($"/api/approvals/manager/{boundaryId}/approve", new { rowVersion = Version(boundary) });
        Assert.Equal(HttpStatusCode.OK, managerFinalResponse.StatusCode);
        JsonElement managerFinal = await managerFinalResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, managerFinal.GetProperty("requestStatus").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, managerFinal.GetProperty("completedDate").ValueKind);

        JsonElement escalated = await CreateAndSubmitAsync(employee, 10000.01m);
        Guid escalatedId = escalated.GetProperty("id").GetGuid();
        using HttpResponseMessage managerDecision = await manager.PostAsJsonAsync($"/api/approvals/manager/{escalatedId}/approve", new { rowVersion = Version(escalated) });
        JsonElement pendingAdmin = await managerDecision.Content.ReadFromJsonAsync<JsonElement>();
        using HttpResponseMessage adminRejectResponse = await admin.PostAsJsonAsync($"/api/approvals/admin/{escalatedId}/reject", new { rowVersion = Version(pendingAdmin), reason = "Admin bütçe reddi" });
        Assert.Equal(HttpStatusCode.OK, adminRejectResponse.StatusCode);
        JsonElement adminRejected = await adminRejectResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5, adminRejected.GetProperty("requestStatus").GetInt32());
        Assert.Equal(2, adminRejected.GetProperty("approvalHistory").GetArrayLength());
    }

    [Fact]
    public async Task Admin_should_not_decide_own_request_after_manager_escalation()
    {
        Guid id = Guid.NewGuid();
        await using (ApplicationDbContext context = fixture.CreateContext())
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            CorporateRequest request = CorporateRequest.Create(id, "Admin owned", "Separation of duties", RequestType.Expense,
                20000m, DevelopmentSeedData.AdminUserId, DevelopmentSeedData.DepartmentId, now);
            request.Submit(DevelopmentSeedData.AdminUserId, now.AddSeconds(1));
            context.CorporateRequests.Add(request);
            await context.SaveChangesAsync();
        }

        using HttpClient manager = await ClientAsync(DevelopmentSeedData.ManagerEmail);
        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail);
        JsonElement managerDetail = await manager.GetFromJsonAsync<JsonElement>($"/api/approvals/manager/{id}");
        using HttpResponseMessage managerApprove = await manager.PostAsJsonAsync($"/api/approvals/manager/{id}/approve", new { rowVersion = Version(managerDetail) });
        JsonElement pendingAdmin = await managerApprove.Content.ReadFromJsonAsync<JsonElement>();
        using HttpResponseMessage self = await admin.PostAsJsonAsync($"/api/approvals/admin/{id}/reject", new { rowVersion = Version(pendingAdmin), reason = "Forbidden" });
        JsonElement problem = await AssertProblemAsync(self, HttpStatusCode.Conflict);
        Assert.Equal("approval.self_action_forbidden", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Racing_manager_decisions_should_allow_one_winner_and_atomically_persist_one_approval_and_audit()
    {
        using HttpClient employee = await ClientAsync(DevelopmentSeedData.EmployeeEmail);
        using HttpClient approvingManager = await ClientAsync(DevelopmentSeedData.ManagerEmail);
        using HttpClient rejectingManager = await ClientAsync(DevelopmentSeedData.ManagerEmail);
        JsonElement submitted = await CreateAndSubmitAsync(employee, 500m);
        Guid id = submitted.GetProperty("id").GetGuid(); string staleVersion = Version(submitted);

        Task<HttpResponseMessage> approve = approvingManager.PostAsJsonAsync($"/api/approvals/manager/{id}/approve", new { rowVersion = staleVersion });
        Task<HttpResponseMessage> reject = rejectingManager.PostAsJsonAsync($"/api/approvals/manager/{id}/reject", new { rowVersion = staleVersion, reason = "Race rejection" });
        HttpResponseMessage[] results = await Task.WhenAll(approve, reject);
        using (results[0]) using (results[1])
        {
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
            HttpResponseMessage loser = Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
            JsonElement problem = await loser.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(problem.GetProperty("code").GetString(), RaceConflictCodes);
        }

        await using ApplicationDbContext context = fixture.CreateContext();
        Assert.Equal(1, await context.RequestApprovals.CountAsync(x => x.RequestId == id));
        Assert.Equal(1, await context.AuditLogs.CountAsync(x => x.EntityId == id &&
            (x.Action == "CorporateRequest.ManagerApproved" || x.Action == "CorporateRequest.ManagerRejected")));
    }

    [Fact]
    public async Task Approval_endpoints_should_enforce_exact_roles_validation_self_action_and_not_found_contracts()
    {
        using HttpClient employee = await ClientAsync(DevelopmentSeedData.EmployeeEmail);
        using HttpClient manager = await ClientAsync(DevelopmentSeedData.ManagerEmail);
        using HttpClient admin = await ClientAsync(DevelopmentSeedData.AdminEmail);
        JsonElement submitted = await CreateAndSubmitAsync(employee, 10m);
        Guid id = submitted.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/approvals/manager")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/approvals/manager")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/approvals/admin")).StatusCode);
        using HttpResponseMessage missing = await manager.GetAsync($"/api/approvals/manager/{Guid.NewGuid()}");
        JsonElement missingProblem = await AssertProblemAsync(missing, HttpStatusCode.NotFound);
        Guid otherDepartmentId = Guid.NewGuid(); Guid wrongScopeId = Guid.NewGuid();
        await using (ApplicationDbContext context = fixture.CreateContext())
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            context.Departments.Add(Department.Create(otherDepartmentId, "Other approval department", DevelopmentSeedData.AdminUserId, now));
            CorporateRequest wrongScope = CorporateRequest.Create(wrongScopeId, "Wrong scope", "Must remain hidden", RequestType.Expense,
                100m, DevelopmentSeedData.EmployeeUserId, otherDepartmentId, now);
            wrongScope.Submit(DevelopmentSeedData.EmployeeUserId, now.AddSeconds(1));
            context.CorporateRequests.Add(wrongScope);
            await context.SaveChangesAsync();
        }
        using HttpResponseMessage wrongScopeResponse = await manager.GetAsync($"/api/approvals/manager/{wrongScopeId}");
        JsonElement wrongScopeProblem = await AssertProblemAsync(wrongScopeResponse, HttpStatusCode.NotFound);
        Assert.Equal(missingProblem.GetProperty("code").GetString(), wrongScopeProblem.GetProperty("code").GetString());
        Assert.Equal(missingProblem.GetProperty("detail").GetString(), wrongScopeProblem.GetProperty("detail").GetString());
        using HttpResponseMessage invalid = await manager.PostAsJsonAsync($"/api/approvals/manager/{id}/reject", new { rowVersion = "invalid", reason = "" });
        await AssertProblemAsync(invalid, HttpStatusCode.BadRequest);

        JsonElement own = await CreateAndSubmitAsync(manager, 10m);
        Guid ownId = own.GetProperty("id").GetGuid();
        using HttpResponseMessage self = await manager.PostAsJsonAsync($"/api/approvals/manager/{ownId}/approve", new { rowVersion = Version(own) });
        JsonElement selfProblem = await AssertProblemAsync(self, HttpStatusCode.Conflict);
        Assert.Equal("approval.self_action_forbidden", selfProblem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/api/requests/{ownId}/approval-history")).StatusCode);
    }

    [Fact]
    public async Task Swagger_should_publish_all_approval_routes_as_bearer_protected()
    {
        string swagger = await factory!.CreateClient().GetStringAsync("/swagger/v1/swagger.json");
        foreach (string route in new[]
        {
            "/api/approvals/manager", "/api/approvals/manager/{requestId}", "/api/approvals/manager/{requestId}/approve",
            "/api/approvals/manager/{requestId}/reject", "/api/approvals/admin", "/api/approvals/admin/{requestId}",
            "/api/approvals/admin/{requestId}/approve", "/api/approvals/admin/{requestId}/reject", "/api/requests/{id}/approval-history"
        }) Assert.Contains(route, swagger, StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(swagger);
        Assert.Equal("Bearer", document.RootElement.GetProperty("security")[0].EnumerateObject().Single().Name);
    }

    private async Task<HttpClient> ClientAsync(string email)
    {
        HttpClient client = factory!.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DevelopmentSeedData.DemoPassword });
        response.EnsureSuccessStatusCode(); JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }

    private static async Task<JsonElement> CreateAndSubmitAsync(HttpClient client, decimal amount)
    {
        using HttpResponseMessage create = await client.PostAsJsonAsync("/api/requests", new { title = "Approval flow", description = "Safe approval integration request", requestType = 1, amount });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode); JsonElement created = await create.Content.ReadFromJsonAsync<JsonElement>();
        using HttpResponseMessage submit = await client.PostAsJsonAsync($"/api/requests/{created.GetProperty("id").GetGuid()}/submit", new { rowVersion = Version(created) });
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode); return await submit.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static string Version(JsonElement value)
    {
        string version = value.GetProperty("rowVersion").GetString()!;
        Assert.Equal(8, Convert.FromBase64String(version).Length); return version;
    }

    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        Assert.Equal(status, response.StatusCode); JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("code", out _)); Assert.True(body.TryGetProperty("traceId", out _)); return body;
    }
}
