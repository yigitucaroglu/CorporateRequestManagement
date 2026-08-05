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
public sealed class Phase7CleanDatabaseE2ETests
{
    [Fact]
    public async Task Clean_database_should_migrate_seed_and_complete_single_end_to_end_management_workflow()
    {
        var database = new SqlServerDatabaseFixture();
        WebApplicationFactory<Program>? factory = null;
        try
        {
            await database.InitializeAsync();
            factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("ConnectionStrings:DefaultConnection", database.ConnectionString);
                builder.UseSetting("Jwt:Issuer", "CorporateRequestManagement.Api");
                builder.UseSetting("Jwt:Audience", "CorporateRequestManagement.Tests");
                builder.UseSetting("Jwt:SigningKey", "integration-test-signing-material-32-bytes-minimum");
                builder.UseSetting("RateLimiting:AuthPermitLimit", "100");
                builder.ConfigureLogging(logging => logging.ClearProviders());
            });
            await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            {
                DevelopmentDataSeeder seeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>();
                await seeder.SeedAsync(); await seeder.SeedAsync();
            }
            await using (ApplicationDbContext seeded = database.CreateContext())
            {
                Assert.Equal(1, await seeded.Departments.CountAsync(x => x.Name == "Yazılım"));
                Assert.Equal(1, await seeded.Departments.CountAsync(x => x.Name == "Finans"));
                Assert.Equal(3, await seeded.Users.CountAsync(x => x.Email.EndsWith("@company.com")));
            }

            using HttpClient employee = await LoginAsync(factory, DevelopmentSeedData.EmployeeEmail);
            using HttpClient manager = await LoginAsync(factory, DevelopmentSeedData.ManagerEmail);
            using HttpClient admin = await LoginAsync(factory, DevelopmentSeedData.AdminEmail);
            JsonElement created = await (await employee.PostAsJsonAsync("/api/requests", new { title = "Clean E2E", description = "Migration to audit", requestType = 1, amount = 20000m })).Content.ReadFromJsonAsync<JsonElement>();
            Guid requestId = created.GetProperty("id").GetGuid();
            JsonElement submitted = await (await employee.PostAsJsonAsync($"/api/requests/{requestId}/submit", new { rowVersion = created.GetProperty("rowVersion").GetString() })).Content.ReadFromJsonAsync<JsonElement>();
            JsonElement managerApproved = await (await manager.PostAsJsonAsync($"/api/approvals/manager/{requestId}/approve", new { rowVersion = submitted.GetProperty("rowVersion").GetString() })).Content.ReadFromJsonAsync<JsonElement>();
            JsonElement adminApproved = await (await admin.PostAsJsonAsync($"/api/approvals/admin/{requestId}/approve", new { rowVersion = managerApproved.GetProperty("rowVersion").GetString() })).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/requests?minAmount=1&maxAmount=25000")).StatusCode);

            JsonElement department = await (await admin.PostAsJsonAsync("/api/admin/departments", new { name = "E2E Department" })).Content.ReadFromJsonAsync<JsonElement>();
            Guid departmentId = department.GetProperty("id").GetGuid();
            JsonElement user = await (await admin.PostAsJsonAsync("/api/admin/users", new { firstName = "E2E", lastName = "Manager", email = "e2e.manager@company.com", initialPassword = "E2EManager!2026", confirmInitialPassword = "E2EManager!2026", role = 2, departmentId, isActive = true })).Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/departments/{departmentId}/manager", new { managerId = user.GetProperty("id").GetGuid() })).StatusCode);

            using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/admin/requests/{requestId}") { Content = JsonContent.Create(new { rowVersion = adminApproved.GetProperty("rowVersion").GetString() }) };
            Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(delete)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await employee.GetAsync($"/api/requests/{requestId}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/audit-logs?action=CorporateRequest.SoftDeleted")).StatusCode);
            await using ApplicationDbContext verify = database.CreateContext();
            Assert.True(await verify.CorporateRequests.IgnoreQueryFilters().AnyAsync(x => x.Id == requestId && x.IsDeleted));
            Assert.Equal(2, await verify.RequestApprovals.CountAsync(x => x.RequestId == requestId));
            Assert.True(await verify.AuditLogs.AnyAsync(x => x.EntityId == requestId && x.Action == "CorporateRequest.SoftDeleted"));
        }
        finally
        {
            if (factory is not null) await factory.DisposeAsync();
            await database.DisposeAsync();
        }
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string email)
    {
        HttpClient client = factory.CreateClient();
        JsonElement body = await (await client.PostAsJsonAsync("/api/auth/login", new { email, password = DevelopmentSeedData.DemoPassword })).Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return client;
    }
}
