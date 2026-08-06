extern alias webapp;

using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WApi = webapp::CorporateRequestManagement.Web.Api;
using WAuth = webapp::CorporateRequestManagement.Web.Authentication;

namespace CorporateRequestManagement.WebTests;

public sealed class BackendTransportResilienceTests(AuthenticationWebFactory webFactory) : IClassFixture<AuthenticationWebFactory>
{
    [Fact]
    public async Task Connection_failure_should_return_safe_503_without_transport_detail()
    {
        WApi.BackendApiGateway gateway = CreateGateway(new ThrowingHandler(
            _ => throw new HttpRequestException("connection refused api.internal.example")));

        WApi.WebApiResult<object> result = await gateway.SendAsync<object>(HttpMethod.Get, "api/admin/requests");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.Equal("backend.service_unavailable", result.Problem.Code);
        Assert.Contains("Servise şu anda ulaşılamıyor", result.Problem.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("connection refused", result.Problem.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task Premature_response_end_should_return_safe_503()
    {
        WApi.BackendApiGateway gateway = CreateGateway(new ThrowingHandler(
            _ => throw new HttpIOException(HttpRequestError.ResponseEnded, "ResponseEnded secret")));

        WApi.WebApiResult<object> result = await gateway.SendAsync<object>(HttpMethod.Get, "api/admin/requests");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        Assert.DoesNotContain("ResponseEnded", result.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Http_client_timeout_should_return_safe_504()
    {
        WApi.BackendApiGateway gateway = CreateGateway(new ThrowingHandler(
            _ => throw new TaskCanceledException("timeout with host detail")));

        WApi.WebApiResult<object> result = await gateway.SendAsync<object>(HttpMethod.Get, "api/admin/requests");

        Assert.Equal(HttpStatusCode.GatewayTimeout, result.StatusCode);
        Assert.Equal("backend.gateway_timeout", result.Problem.Code);
        Assert.DoesNotContain("host detail", result.Problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_should_not_be_mapped_to_503_or_504()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        WApi.BackendApiGateway gateway = CreateGateway(new ThrowingHandler(
            token => throw new OperationCanceledException(token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => gateway.SendAsync<object>(HttpMethod.Get, "api/admin/requests", cancellationToken: source.Token));
    }

    [Fact]
    public async Task Gateway_should_recover_on_next_request_without_recreation()
    {
        int calls = 0;
        WApi.BackendApiGateway gateway = CreateGateway(new ThrowingHandler(_ =>
        {
            calls++;
            if (calls == 1) throw new HttpRequestException("temporary failure");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { value = "recovered" }) };
        }));

        WApi.WebApiResult<RecoveryContract> failed = await gateway.SendAsync<RecoveryContract>(HttpMethod.Get, "api/admin/requests");
        WApi.WebApiResult<RecoveryContract> recovered = await gateway.SendAsync<RecoveryContract>(HttpMethod.Get, "api/admin/requests");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.True(recovered.IsSuccess);
        Assert.Equal("recovered", recovered.Value?.Value);
    }

    [Fact]
    public async Task Web_detail_should_render_safe_503_without_developer_exception_content()
    {
        using WebApplicationFactory<webapp::Program> factory = TransportFactory(new ThrowingHandler(
            _ => throw new HttpRequestException("connection refused backend.internal")));
        using HttpClient client = await LoginAdminAsync(factory);

        using HttpResponseMessage response = await client.GetAsync($"/Admin/Requests/{Guid.NewGuid()}");
        string html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Servise şu anda ulaşılamıyor", html, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpRequestException", html, StringComparison.Ordinal);
        Assert.DoesNotContain("backend.internal", html, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_mutation_transport_failure_should_not_report_success_or_retry_mutation()
    {
        int calls = 0;
        using WebApplicationFactory<webapp::Program> factory = TransportFactory(new ThrowingHandler(_ =>
        {
            calls++;
            throw new HttpRequestException("connection reset secret");
        }));
        using HttpClient client = await LoginAdminAsync(factory);
        string token = await AntiforgeryAsync(client, "/Admin/Users/Create");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["rowVersion"] = "AQIDBAUGBwg=",
            ["__RequestVerificationToken"] = token
        });

        using HttpResponseMessage response = await client.PostAsync($"/Admin/Requests/{Guid.NewGuid()}/Delete", form);
        string html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, calls);
        Assert.Contains("Servise şu anda ulaşılamıyor", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Talep silindi", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("connection reset", html, StringComparison.OrdinalIgnoreCase);
    }

    private static WApi.BackendApiGateway CreateGateway(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://backend.invalid/") };
        return new WApi.BackendApiGateway(client, new TokenStore(), new AuthenticationClient(), new Coordinator(),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, new LocalAuthenticationState());
    }

    private WebApplicationFactory<webapp::Program> TransportFactory(HttpMessageHandler handler) =>
        webFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<WApi.IBackendApiGateway>();
            services.RemoveAll<WApi.IAdminRequestsApiClient>();
            services.AddHttpClient<WApi.IBackendApiGateway, WApi.BackendApiGateway>(client =>
                client.BaseAddress = new Uri("https://backend.invalid/"))
                .ConfigurePrimaryHttpMessageHandler(() => handler);
            services.AddScoped<WApi.IAdminRequestsApiClient, WApi.AdminRequestsApiClient>();
        }));

    private async Task<HttpClient> LoginAdminAsync(WebApplicationFactory<webapp::Program> factory)
    {
        webFactory.Authentication.Result = new WAuth.AuthenticationContract
        {
            AccessToken = AuthenticationWebFactory.AccessToken,
            RefreshToken = AuthenticationWebFactory.RefreshToken,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            User = new WAuth.AuthenticatedUserContract { Id = Guid.NewGuid(), FirstName = "Admin", LastName = "User", Email = "admin@example.test", Role = "Admin" }
        };
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        string token = await AntiforgeryAsync(client, "/account/login");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "admin@example.test",
            ["Password"] = "password",
            ["__RequestVerificationToken"] = token
        });
        using HttpResponseMessage response = await client.PostAsync("/account/login", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private static async Task<string> AntiforgeryAsync(HttpClient client, string path)
    {
        string html = await client.GetStringAsync(path);
        Match match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed record RecoveryContract(string Value);

    private sealed class ThrowingHandler(Func<CancellationToken, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response(cancellationToken));
    }

    private sealed class TokenStore : WAuth.IUserTokenStore
    {
        private readonly WAuth.WebUserSession session = new() { AccessToken = "access", RefreshToken = "refresh", AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5), RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        public Task<WAuth.WebUserSession?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<WAuth.WebUserSession?>(session);
        public Task SetAsync(WAuth.WebUserSession value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AuthenticationClient : WAuth.IAuthenticationApiClient
    {
        public Task<WAuth.ApiCallResult<WAuth.AuthenticationContract>> LoginAsync(string email, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WAuth.ApiCallResult<WAuth.AuthenticationContract>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Coordinator : WAuth.IRefreshCoordinator
    {
        public Task<T> ExecuteAsync<T>(string sessionId, Func<Task<T>> operation, CancellationToken cancellationToken = default) => operation();
    }

    private sealed class LocalAuthenticationState : WAuth.ILocalAuthenticationState
    {
        public Task<bool> HasValidSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
