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

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class CorporateRequestsApiIntegrationTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly string[] ExpectedLifecycleActions =
    [
        "CorporateRequest.Created",
        "CorporateRequest.DraftUpdated",
        "CorporateRequest.Submitted",
        "CorporateRequest.Cancelled"
    ];
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
            builder.UseSetting("RateLimiting:AuthPermitLimit", "20");
            builder.UseSetting("RateLimiting:GeneralPermitLimit", "100");
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
        await fixture.ResetDevelopmentSeedAsync();
    }

    [Fact]
    public async Task Owner_lifecycle_should_create_list_update_submit_and_cancel_with_safe_audits()
    {
        using HttpClient employee = await AuthenticatedClientAsync(DevelopmentSeedData.EmployeeEmail);
        JsonElement created = await CreateAsync(employee, "Travel equipment");
        Guid id = created.GetProperty("id").GetGuid();
        string initialVersion = AssertRowVersion(created);

        using HttpResponseMessage listResponse = await employee.GetAsync("/api/requests?pageNumber=1&pageSize=10&sortBy=createdDate&sortDirection=Descending");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        JsonElement list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(id, list.GetProperty("items")[0].GetProperty("id").GetGuid());

        using HttpResponseMessage detailResponse = await employee.GetAsync($"/api/requests/{id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        using HttpResponseMessage updateResponse = await employee.PutAsJsonAsync($"/api/requests/{id}", new
        {
            title = "Updated title", description = "Updated but not audited description", requestType = 1, amount = 125.50m, rowVersion = initialVersion
        });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        JsonElement updated = await updateResponse.Content.ReadFromJsonAsync<JsonElement>();
        string updatedVersion = AssertRowVersion(updated);
        Assert.NotEqual(initialVersion, updatedVersion);

        using HttpResponseMessage submitResponse = await employee.PostAsJsonAsync($"/api/requests/{id}/submit", new { rowVersion = updatedVersion });
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        JsonElement submitted = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        string submittedVersion = AssertRowVersion(submitted);
        Assert.Equal(2, submitted.GetProperty("status").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, submitted.GetProperty("submittedDate").ValueKind);

        using HttpResponseMessage secondSubmit = await employee.PostAsJsonAsync($"/api/requests/{id}/submit", new { rowVersion = submittedVersion });
        await AssertProblemAsync(secondSubmit, HttpStatusCode.Conflict, "request.submit_requires_draft");

        using HttpResponseMessage cancelResponse = await employee.PostAsJsonAsync($"/api/requests/{id}/cancel", new { rowVersion = submittedVersion });
        Assert.Equal(HttpStatusCode.OK, cancelResponse.StatusCode);
        JsonElement cancelled = await cancelResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(6, cancelled.GetProperty("status").GetInt32());
        AssertRowVersion(cancelled);

        await using ApplicationDbContext context = fixture.CreateContext();
        string[] actions = await context.AuditLogs.Where(x => x.EntityId == id).OrderBy(x => x.CreatedDate).Select(x => x.Action).ToArrayAsync();
        Assert.Equal(ExpectedLifecycleActions, actions);
        Assert.DoesNotContain(await context.AuditLogs.Where(x => x.EntityId == id).Select(x => (x.OldValues ?? "") + (x.NewValues ?? "")).ToArrayAsync(), value => value.Contains("description", StringComparison.OrdinalIgnoreCase) || value.Contains("rowVersion", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(context.RequestApprovals.Where(x => x.RequestId == id));
    }

    [Fact]
    public async Task Wrong_owner_and_random_id_should_return_same_not_found_without_existence_leak()
    {
        using HttpClient employee = await AuthenticatedClientAsync(DevelopmentSeedData.EmployeeEmail);
        JsonElement created = await CreateAsync(employee, "Owned request");
        Guid id = created.GetProperty("id").GetGuid();
        using HttpClient manager = await AuthenticatedClientAsync(DevelopmentSeedData.ManagerEmail);

        using HttpResponseMessage wrongOwner = await manager.GetAsync($"/api/requests/{id}");
        using HttpResponseMessage missing = await manager.GetAsync($"/api/requests/{Guid.NewGuid()}");
        JsonElement wrongBody = await AssertProblemAsync(wrongOwner, HttpStatusCode.NotFound, "request.not_found");
        JsonElement missingBody = await AssertProblemAsync(missing, HttpStatusCode.NotFound, "request.not_found");
        Assert.Equal(wrongBody.GetProperty("detail").GetString(), missingBody.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Stale_update_should_return_concurrency_conflict_preserve_first_write_and_not_add_second_audit()
    {
        using HttpClient employee = await AuthenticatedClientAsync(DevelopmentSeedData.EmployeeEmail);
        JsonElement created = await CreateAsync(employee, "Concurrent request");
        Guid id = created.GetProperty("id").GetGuid(); string stale = AssertRowVersion(created);
        object firstBody = new { title = "First write", description = "First", requestType = 3, amount = (decimal?)null, rowVersion = stale };
        using HttpResponseMessage first = await employee.PutAsJsonAsync($"/api/requests/{id}", firstBody);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using HttpResponseMessage second = await employee.PutAsJsonAsync($"/api/requests/{id}", new { title = "Second write", description = "Second", requestType = 3, amount = (decimal?)null, rowVersion = stale });
        JsonElement problem = await AssertProblemAsync(second, HttpStatusCode.Conflict, "concurrency.conflict");
        Assert.DoesNotContain("Sql", problem.ToString(), StringComparison.OrdinalIgnoreCase);
        using HttpResponseMessage detailResponse = await employee.GetAsync($"/api/requests/{id}");
        JsonElement detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("First write", detail.GetProperty("title").GetString());
        await using ApplicationDbContext context = fixture.CreateContext();
        Assert.Equal(1, await context.AuditLogs.CountAsync(x => x.EntityId == id && x.Action == "CorporateRequest.DraftUpdated"));
    }

    [Fact]
    public async Task Submit_and_cancel_should_reject_stale_versions_and_wrong_owner_without_audit()
    {
        using HttpClient employee = await AuthenticatedClientAsync(DevelopmentSeedData.EmployeeEmail);
        JsonElement created = await CreateAsync(employee, "Mutation guards");
        Guid id = created.GetProperty("id").GetGuid();
        string initial = AssertRowVersion(created);
        using HttpClient manager = await AuthenticatedClientAsync(DevelopmentSeedData.ManagerEmail);
        using HttpResponseMessage wrongOwner = await manager.PostAsJsonAsync($"/api/requests/{id}/submit", new { rowVersion = initial });
        await AssertProblemAsync(wrongOwner, HttpStatusCode.NotFound, "request.not_found");

        using HttpResponseMessage update = await employee.PutAsJsonAsync($"/api/requests/{id}", new
        { title = "Mutation guards updated", description = "Safe", requestType = 3, amount = (decimal?)null, rowVersion = initial });
        JsonElement updated = await update.Content.ReadFromJsonAsync<JsonElement>();
        string current = AssertRowVersion(updated);
        using HttpResponseMessage staleSubmit = await employee.PostAsJsonAsync($"/api/requests/{id}/submit", new { rowVersion = initial });
        await AssertProblemAsync(staleSubmit, HttpStatusCode.Conflict, "concurrency.conflict");

        using HttpResponseMessage submit = await employee.PostAsJsonAsync($"/api/requests/{id}/submit", new { rowVersion = current });
        JsonElement submitted = await submit.Content.ReadFromJsonAsync<JsonElement>();
        string submittedVersion = AssertRowVersion(submitted);
        using HttpResponseMessage staleCancel = await employee.PostAsJsonAsync($"/api/requests/{id}/cancel", new { rowVersion = current });
        await AssertProblemAsync(staleCancel, HttpStatusCode.Conflict, "concurrency.conflict");
        await using ApplicationDbContext context = fixture.CreateContext();
        Assert.Equal(1, await context.AuditLogs.CountAsync(x => x.EntityId == id && x.Action == "CorporateRequest.Submitted"));
        Assert.NotEqual(current, submittedVersion);
    }

    [Fact]
    public async Task Invalid_row_version_should_return_validation_problem()
    {
        using HttpClient employee = await AuthenticatedClientAsync(DevelopmentSeedData.EmployeeEmail);
        JsonElement created = await CreateAsync(employee, "Invalid version");
        Guid id = created.GetProperty("id").GetGuid();
        using HttpResponseMessage response = await employee.PutAsJsonAsync($"/api/requests/{id}", new
        { title = "Title", description = "Description", requestType = 3, amount = (decimal?)null, rowVersion = "not-base64" });
        JsonElement problem = await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed");
        Assert.True(problem.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Validation_unauthorized_and_mass_assignment_contracts_should_be_safe()
    {
        using HttpClient anonymous = factory!.CreateClient();
        using HttpResponseMessage unauthorized = await anonymous.PostAsJsonAsync("/api/requests", new { title = "T", description = "D", requestType = "Leave" });
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using HttpClient employee = await AuthenticatedClientAsync(DevelopmentSeedData.EmployeeEmail);
        using HttpResponseMessage invalid = await employee.PostAsJsonAsync("/api/requests", new { title = "", description = "", requestType = 999, amount = -1 });
        JsonElement invalidProblem = await AssertProblemAsync(invalid, HttpStatusCode.BadRequest, "validation.failed");
        Assert.True(invalidProblem.TryGetProperty("errors", out _));
        using HttpResponseMessage spoof = await employee.PostAsJsonAsync("/api/requests", new
        {
            title = "Safe", description = "Safe request", requestType = 3, createdByUserId = DevelopmentSeedData.ManagerUserId,
            departmentId = Guid.NewGuid(), status = "Approved"
        });
        Assert.Equal(HttpStatusCode.Created, spoof.StatusCode);
        JsonElement body = await spoof.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(DevelopmentSeedData.DepartmentId, body.GetProperty("departmentId").GetGuid());
        Assert.Equal(1, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Swagger_should_publish_safe_request_routes_and_row_version_contract()
    {
        using HttpClient client = factory!.CreateClient();
        string swagger = await client.GetStringAsync("/swagger/v1/swagger.json");
        foreach (string route in new[] { "/api/requests", "/api/requests/{id}", "/api/requests/{id}/submit", "/api/requests/{id}/cancel" }) Assert.Contains(route, swagger, StringComparison.Ordinal);
        Assert.Contains("rowVersion", swagger, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", swagger, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", swagger, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpClient> AuthenticatedClientAsync(string email)
    {
        HttpClient client = factory!.CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = DevelopmentSeedData.DemoPassword });
        response.EnsureSuccessStatusCode(); JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }
    private static async Task<JsonElement> CreateAsync(HttpClient client, string title)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/requests", new { title, description = "Safe integration request", requestType = 3, amount = (decimal?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.NotNull(response.Headers.Location);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static string AssertRowVersion(JsonElement body)
    { string value = body.GetProperty("rowVersion").GetString()!; Assert.Equal(8, Convert.FromBase64String(value).Length); return value; }
    private static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    { Assert.Equal(status, response.StatusCode); JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(code, body.GetProperty("code").GetString()); Assert.True(body.TryGetProperty("traceId", out _)); return body; }
}
