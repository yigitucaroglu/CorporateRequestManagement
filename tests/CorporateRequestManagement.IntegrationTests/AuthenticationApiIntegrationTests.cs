using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure.Persistence;
using CorporateRequestManagement.Infrastructure.Persistence.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class AuthenticationApiIntegrationTests(SqlServerDatabaseFixture fixture) : IAsyncLifetime
{
    private WebApplicationFactory<Program>? factory;

    public async Task InitializeAsync()
    {
        await fixture.ResetDevelopmentSeedAsync();
        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", fixture.ConnectionString);
            builder.UseSetting("Jwt:Issuer", "CorporateRequestManagement.Api");
            builder.UseSetting("Jwt:Audience", "CorporateRequestManagement.Tests");
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-material-32-bytes-minimum");
            builder.UseSetting("RateLimiting:AuthPermitLimit", "20");
            builder.UseSetting("RateLimiting:GeneralPermitLimit", "100");
        });
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>().SeedAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        await fixture.ResetDevelopmentSeedAsync();
    }

    [Fact]
    public async Task AuthenticationFlow_ShouldLoginAuthorizeRotateRejectReuseAndRevoke()
    {
        using HttpClient client = factory!.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync("/api/auth/login", new { email = DevelopmentSeedData.AdminEmail, password = DevelopmentSeedData.DemoPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        JsonElement loginBody = await login.Content.ReadFromJsonAsync<JsonElement>();
        string access = loginBody.GetProperty("accessToken").GetString()!;
        string refresh = loginBody.GetProperty("refreshToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        HttpResponseMessage rotation = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.OK, rotation.StatusCode);
        JsonElement rotationBody = await rotation.Content.ReadFromJsonAsync<JsonElement>();
        string replacement = rotationBody.GetProperty("refreshToken").GetString()!;
        HttpResponseMessage reuse = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        JsonElement reuseProblem = await reuse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("auth.refresh_token_invalid", reuseProblem.GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/revoke", new { refreshToken = replacement })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = replacement })).StatusCode);
    }

    [Fact]
    public async Task Swagger_ShouldExposeAuthRoutesAndBearerScheme()
    {
        using HttpClient client = factory!.CreateClient();
        string json = await client.GetStringAsync("/swagger/v1/swagger.json");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.Contains("/api/auth/login", json, StringComparison.Ordinal);
        Assert.Contains("/api/auth/refresh", json, StringComparison.Ordinal);
        Assert.Contains("/api/auth/revoke", json, StringComparison.Ordinal);
        Assert.Contains("/api/auth/me", json, StringComparison.Ordinal);
        Assert.Contains("Bearer", json, StringComparison.Ordinal);
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", json, StringComparison.OrdinalIgnoreCase);
        JsonElement bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        AssertBearerRequired(root, "/api/auth/me", "get");
        AssertBearerRequired(root, "/api/requests", "get");
        AssertBearerRequired(root, "/api/requests", "post");
        AssertBearerRequired(root, "/api/requests/{id}", "get");
        AssertAnonymous(root, "/api/auth/login", "post");
        AssertAnonymous(root, "/api/auth/refresh", "post");
    }

    private static void AssertBearerRequired(JsonElement root, string path, string method)
    {
        JsonElement operation = root.GetProperty("paths").GetProperty(path).GetProperty(method);
        JsonElement security = operation.TryGetProperty("security", out JsonElement operationSecurity)
            ? operationSecurity
            : root.GetProperty("security");
        Assert.Contains(security.EnumerateArray(), requirement => requirement.TryGetProperty("Bearer", out _));
    }

    private static void AssertAnonymous(JsonElement root, string path, string method)
    {
        JsonElement operation = root.GetProperty("paths").GetProperty(path).GetProperty(method);
        Assert.True(operation.TryGetProperty("security", out JsonElement security));
        Assert.Empty(security.EnumerateArray());
    }

    [Fact]
    public async Task ConcurrentRefresh_ShouldAllowExactlyOneRotation()
    {
        using HttpClient client = factory!.CreateClient();
        JsonElement login = await (await client.PostAsJsonAsync("/api/auth/login", new { email = DevelopmentSeedData.EmployeeEmail, password = DevelopmentSeedData.DemoPassword })).Content.ReadFromJsonAsync<JsonElement>();
        string refresh = login.GetProperty("refreshToken").GetString()!;
        Task<HttpResponseMessage> first = client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Task<HttpResponseMessage> second = client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        HttpResponseMessage[] responses = await Task.WhenAll(first, second);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AuthRateLimit_ShouldReturnStableProblemDetailsOnSixthRequest()
    {
        await using WebApplicationFactory<Program> limited = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:DefaultConnection", fixture.ConnectionString);
            builder.UseSetting("Jwt:Issuer", "CorporateRequestManagement.Api");
            builder.UseSetting("Jwt:Audience", "CorporateRequestManagement.Tests");
            builder.UseSetting("Jwt:SigningKey", "integration-test-signing-material-32-bytes-minimum");
            builder.UseSetting("RateLimiting:AuthPermitLimit", "5");
            builder.UseSetting("RateLimiting:GeneralPermitLimit", "100");
        });
        using HttpClient client = limited.CreateClient();
        HttpResponseMessage response = null!;
        for (int index = 0; index < 6; index++)
            response = await client.PostAsJsonAsync("/api/auth/login", new { email = "unknown@crm.local", password = "invalid" });
        Assert.Equal((HttpStatusCode)429, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limit.exceeded", problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task InvalidLoginRequest_ShouldReturnValidationProblemDetails()
    {
        using HttpClient client = factory!.CreateClient();
        HttpResponseMessage response = await client.PostAsJsonAsync("/api/auth/login", new { email = "invalid", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation.failed", problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("errors", out _));
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task RefreshRotation_WhenAuditInsertFails_ShouldRollbackAndRemainUsable()
    {
        using HttpClient client = factory!.CreateClient();
        (string _, string refresh) = await LoginAsync(client, DevelopmentSeedData.AdminEmail);
        string hash = Hash(refresh);
        const string trigger = "TR_AuthRotationRollback_Test";
        await using (ApplicationDbContext setup = fixture.CreateContext())
            await setup.Database.ExecuteSqlRawAsync($"CREATE TRIGGER [{trigger}] ON [AuditLogs] AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE [Action] = 'Authentication.RefreshTokenRotated') THROW 51000, 'Test rotation failure', 1;");
        try
        {
            HttpResponseMessage failed = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            await using ApplicationDbContext verify = fixture.CreateContext();
            RefreshToken old = await verify.RefreshTokens.SingleAsync(x => x.TokenHash == hash);
            Assert.Null(old.RevokedAt); Assert.Null(old.RevokedByIp); Assert.Null(old.ReplacedByTokenId);
            Assert.DoesNotContain(await verify.AuditLogs.ToListAsync(), x => x.Action == "Authentication.RefreshTokenRotated" && x.EntityId == old.Id);
        }
        finally
        {
            await using ApplicationDbContext cleanup = fixture.CreateContext();
            await cleanup.Database.ExecuteSqlRawAsync($"DROP TRIGGER IF EXISTS [{trigger}]");
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh })).StatusCode);
    }

    [Fact]
    public async Task ExpiredRefreshAndRevoke_ShouldReturnGenericUnauthorizedWithoutMutation()
    {
        using HttpClient client = factory!.CreateClient();
        string raw = "expired-token-material";
        await using (ApplicationDbContext setup = fixture.CreateContext())
        {
            setup.RefreshTokens.Add(RefreshToken.Create(Guid.NewGuid(), DevelopmentSeedData.AdminUserId, Hash(raw), DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(-1), "127.0.0.1"));
            await setup.SaveChangesAsync();
        }
        await AssertRefreshUnauthorizedAsync(client, raw, "/api/auth/refresh");
        await AssertRefreshUnauthorizedAsync(client, raw, "/api/auth/revoke");
        await using ApplicationDbContext verify = fixture.CreateContext();
        RefreshToken token = await verify.RefreshTokens.SingleAsync(x => x.TokenHash == Hash(raw));
        Assert.Null(token.RevokedAt); Assert.Null(token.ReplacedByTokenId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_WhenUserInactiveOrDeleted_ShouldRejectWithoutTokenMutation(bool deleted)
    {
        using HttpClient client = factory!.CreateClient();
        (_, string refresh) = await LoginAsync(client, DevelopmentSeedData.EmployeeEmail);
        await ChangeUserStateAsync(DevelopmentSeedData.EmployeeUserId, deleted);
        await AssertRefreshUnauthorizedAsync(client, refresh, "/api/auth/refresh");
        await using ApplicationDbContext verify = fixture.CreateContext();
        RefreshToken old = await verify.RefreshTokens.IgnoreQueryFilters().SingleAsync(x => x.TokenHash == Hash(refresh));
        Assert.Null(old.RevokedAt); Assert.Null(old.ReplacedByTokenId);
        Assert.DoesNotContain(await verify.AuditLogs.ToListAsync(), x => x.Action == "Authentication.RefreshTokenRotated" && x.EntityId == old.Id);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("role")]
    public async Task Me_WhenDatabaseUserStateNoLongerMatchesJwt_ShouldReturnGenericUnauthorized(string change)
    {
        using HttpClient client = factory!.CreateClient();
        (string access, _) = await LoginAsync(client, DevelopmentSeedData.EmployeeEmail);
        await using (ApplicationDbContext setup = fixture.CreateContext())
        {
            User user = await setup.Users.SingleAsync(x => x.Id == DevelopmentSeedData.EmployeeUserId);
            if (change == "inactive") setup.Entry(user).Property(nameof(User.IsActive)).CurrentValue = false;
            else if (change == "deleted") user.SoftDelete(DateTimeOffset.UtcNow, DevelopmentSeedData.AdminUserId);
            else setup.Entry(user).Property(nameof(User.Role)).CurrentValue = UserRole.Manager;
            await setup.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        HttpResponseMessage response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("auth.unauthorized", problem.GetProperty("code").GetString());
        Assert.DoesNotContain(change, problem.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GeneralRateLimit_ShouldRejectRequest101AndKeepOtherUserPartitionIndependent()
    {
        using HttpClient admin = factory!.CreateClient();
        using HttpClient manager = factory.CreateClient();
        (string adminAccess, _) = await LoginAsync(admin, DevelopmentSeedData.AdminEmail);
        (string managerAccess, _) = await LoginAsync(manager, DevelopmentSeedData.ManagerEmail);
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminAccess);
        manager.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerAccess);
        for (int index = 0; index < 100; index++) Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/auth/me")).StatusCode);
        HttpResponseMessage limited = await admin.GetAsync("/api/auth/me");
        Assert.Equal((HttpStatusCode)429, limited.StatusCode);
        JsonElement problem = await limited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limit.exceeded", problem.GetProperty("code").GetString());
        Assert.True(problem.TryGetProperty("traceId", out _));
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/auth/me")).StatusCode);
    }

    private async Task ChangeUserStateAsync(Guid id, bool deleted)
    {
        await using ApplicationDbContext context = fixture.CreateContext();
        User user = await context.Users.SingleAsync(x => x.Id == id);
        if (deleted) user.SoftDelete(DateTimeOffset.UtcNow, DevelopmentSeedData.AdminUserId);
        else context.Entry(user).Property(nameof(User.IsActive)).CurrentValue = false;
        await context.SaveChangesAsync();
    }

    private static async Task<(string Access, string Refresh)> LoginAsync(HttpClient client, string email)
    {
        JsonElement body = await (await client.PostAsJsonAsync("/api/auth/login", new { email, password = DevelopmentSeedData.DemoPassword })).Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("accessToken").GetString()!, body.GetProperty("refreshToken").GetString()!);
    }

    private static async Task AssertRefreshUnauthorizedAsync(HttpClient client, string raw, string path)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(path, new { refreshToken = raw });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        JsonElement problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("auth.refresh_token_invalid", problem.GetProperty("code").GetString());
        Assert.DoesNotContain(raw, problem.ToString(), StringComparison.Ordinal);
    }

    private static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
}
