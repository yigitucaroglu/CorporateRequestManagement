extern alias webapp;

using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CorporateRequestManagement.WebTests;

using ApiCallResult = webapp::CorporateRequestManagement.Web.Authentication.ApiCallResult<webapp::CorporateRequestManagement.Web.Authentication.AuthenticationContract>;
using AuthenticatedUserContract = webapp::CorporateRequestManagement.Web.Authentication.AuthenticatedUserContract;
using AuthenticationContract = webapp::CorporateRequestManagement.Web.Authentication.AuthenticationContract;
using IAuthenticatedUserApiClient = webapp::CorporateRequestManagement.Web.Authentication.IAuthenticatedUserApiClient;
using IAuthenticationApiClient = webapp::CorporateRequestManagement.Web.Authentication.IAuthenticationApiClient;
using WebProgram = webapp::Program;

public sealed class AuthenticationWebTests : IClassFixture<AuthenticationWebFactory>
{
    private readonly AuthenticationWebFactory factory;

    public AuthenticationWebTests(AuthenticationWebFactory factory) => this.factory = factory;

    [Fact]
    public async Task Anonymous_dashboard_request_redirects_to_login()
    {
        using HttpClient client = factory.CreateSecureClient();

        using HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/login", response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Login_page_loads_required_minimal_assets()
    {
        using HttpClient client = factory.CreateSecureClient();
        using HttpResponseMessage page = await client.GetAsync("/account/login");
        string html = await page.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("/assets/css/style.bundle.css", html, StringComparison.Ordinal);
        Assert.Contains("/assets/plugins/global/plugins.bundle.js", html, StringComparison.Ordinal);
        Assert.Contains("__RequestVerificationToken", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"password\"", html, StringComparison.OrdinalIgnoreCase);

        foreach (string path in new[] { "/assets/css/style.bundle.css", "/assets/js/scripts.bundle.js", "/assets/media/misc/auth-bg.png" })
        {
            using HttpResponseMessage asset = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, asset.StatusCode);
        }
    }

    [Fact]
    public async Task Invalid_credentials_show_generic_safe_message()
    {
        factory.Authentication.Result = null;
        factory.Authentication.FailureStatusCode = HttpStatusCode.Unauthorized;
        using HttpClient client = factory.CreateSecureClient();

        string html = await PostLoginAsync(client, "/");

        Assert.Contains("E-posta veya parola geçersiz.", html, StringComparison.Ordinal);
        Assert.DoesNotContain(AuthenticationWebFactory.AccessToken, html, StringComparison.Ordinal);
        Assert.DoesNotContain(AuthenticationWebFactory.RefreshToken, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rate_limited_login_shows_safe_retry_message()
    {
        factory.Authentication.Result = null;
        factory.Authentication.FailureStatusCode = HttpStatusCode.TooManyRequests;
        using HttpClient client = factory.CreateSecureClient();

        string html = await PostLoginAsync(client, "/");

        Assert.Contains("Çok fazla giriş denemesi", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logout_get_is_not_an_endpoint()
    {
        using HttpClient client = factory.CreateSecureClient();

        using HttpResponseMessage response = await client.GetAsync("/account/logout");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task Login_post_requires_antiforgery_token()
    {
        using HttpClient client = factory.CreateSecureClient();
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "user@example.test",
            ["Password"] = "password"
        });

        using HttpResponseMessage response = await client.PostAsync("/account/login", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Successful_login_uses_secure_http_only_cookies_without_exposing_tokens()
    {
        factory.Authentication.Result = SuccessfulAuthentication();
        using HttpClient client = factory.CreateSecureClient();
        string antiforgery = await GetAntiforgeryTokenAsync(client);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "user@example.test",
            ["Password"] = "password",
            ["ReturnUrl"] = "/",
            ["__RequestVerificationToken"] = antiforgery
        });

        using HttpResponseMessage response = await client.PostAsync("/account/login", body);
        string headers = string.Join('\n', response.Headers.Select(static pair => $"{pair.Key}:{string.Join(';', pair.Value)}"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        Assert.Contains("__Host-Crm.Web.Auth", headers, StringComparison.Ordinal);
        Assert.Contains("__Host-Crm.Web.Session", headers, StringComparison.Ordinal);
        Assert.Contains("secure", headers, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", headers, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AuthenticationWebFactory.AccessToken, headers, StringComparison.Ordinal);
        Assert.DoesNotContain(AuthenticationWebFactory.RefreshToken, headers, StringComparison.Ordinal);
    }

    [Fact]
    public async Task External_return_url_is_not_followed()
    {
        factory.Authentication.Result = SuccessfulAuthentication();
        using HttpClient client = factory.CreateSecureClient();
        string antiforgery = await GetAntiforgeryTokenAsync(client);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "user@example.test",
            ["Password"] = "password",
            ["ReturnUrl"] = "https://attacker.example/path",
            ["__RequestVerificationToken"] = antiforgery
        });

        using HttpResponseMessage response = await client.PostAsync("/account/login", body);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        string html = await client.GetStringAsync("/account/login");
        Match match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<string> PostLoginAsync(HttpClient client, string returnUrl)
    {
        string antiforgery = await GetAntiforgeryTokenAsync(client);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "user@example.test",
            ["Password"] = "password",
            ["ReturnUrl"] = returnUrl,
            ["__RequestVerificationToken"] = antiforgery
        });
        using HttpResponseMessage response = await client.PostAsync("/account/login", body);
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    private static AuthenticationContract SuccessfulAuthentication() => new()
    {
        AccessToken = AuthenticationWebFactory.AccessToken,
        AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
        RefreshToken = AuthenticationWebFactory.RefreshToken,
        RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        User = new AuthenticatedUserContract
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "User",
            Email = "user@example.test",
            Role = "Employee"
        }
    };
}

public sealed class AuthenticationWebFactory : WebApplicationFactory<WebProgram>
{
    public const string AccessToken = "test-access-token-never-for-browser";
    public const string RefreshToken = "test-refresh-token-never-for-browser";
    public FakeAuthenticationApiClient Authentication { get; } = new();

    public HttpClient CreateSecureClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        BaseAddress = new Uri("https://localhost")
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.RemoveAll<IAuthenticationApiClient>();
            services.RemoveAll<IAuthenticatedUserApiClient>();
            services.AddSingleton<IAuthenticationApiClient>(Authentication);
            services.AddSingleton<IAuthenticatedUserApiClient, FakeAuthenticatedUserApiClient>();
        });
    }
}

public sealed class FakeAuthenticationApiClient : IAuthenticationApiClient
{
    public AuthenticationContract? Result { get; set; }
    public HttpStatusCode FailureStatusCode { get; set; } = HttpStatusCode.Unauthorized;
    public Task<ApiCallResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result is null
            ? new ApiCallResult(FailureStatusCode, null)
            : new ApiCallResult(HttpStatusCode.OK, Result));
    public Task<ApiCallResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class FakeAuthenticatedUserApiClient : IAuthenticatedUserApiClient
{
    public Task<AuthenticatedUserContract?> GetCurrentUserAsync(CancellationToken cancellationToken = default) => Task.FromResult<AuthenticatedUserContract?>(new AuthenticatedUserContract());
}
