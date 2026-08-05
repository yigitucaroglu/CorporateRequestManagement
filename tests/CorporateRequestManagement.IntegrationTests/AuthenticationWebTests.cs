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
using IRequestsApiClient = webapp::CorporateRequestManagement.Web.Api.IRequestsApiClient;
using IApprovalsApiClient = webapp::CorporateRequestManagement.Web.Api.IApprovalsApiClient;
using IDashboardApiClient = webapp::CorporateRequestManagement.Web.Api.IDashboardApiClient;
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

    [Fact]
    public async Task Rejected_server_session_clears_local_authentication_and_login_page_terminates_redirect_chain()
    {
        factory.Authentication.Result = SuccessfulAuthentication();
        using HttpClient client = factory.CreateSecureClient();
        _ = await PostLoginAsync(client, "/");
        factory.Authentication.Result = null;

        using HttpResponseMessage protectedResponse = await client.GetAsync("/");
        using HttpResponseMessage loginResponse = await client.GetAsync(protectedResponse.Headers.Location);
        string headers = string.Join('\n', protectedResponse.Headers.Select(static pair => $"{pair.Key}:{string.Join(';', pair.Value)}"));

        Assert.Equal(HttpStatusCode.Redirect, protectedResponse.StatusCode);
        Assert.Equal("/account/login", protectedResponse.Headers.Location?.OriginalString);
        Assert.Contains("__Host-Crm.Web.Auth", headers, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Null(loginResponse.Headers.Location);
    }

    [Fact]
    public async Task Logout_post_remains_antiforgery_protected_and_clears_local_authentication()
    {
        factory.Authentication.Result = SuccessfulAuthentication();
        using HttpClient client = factory.CreateSecureClient();
        _ = await PostLoginAsync(client, "/");
        string antiforgery = await GetAntiforgeryTokenFromPageAsync(client, "/");
        using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = antiforgery });

        using HttpResponseMessage logout = await client.PostAsync("/account/logout", body);
        using HttpResponseMessage protectedResponse = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/account/login", logout.Headers.Location?.OriginalString);
        Assert.Equal(HttpStatusCode.Redirect, protectedResponse.StatusCode);
        Assert.Equal("/account/login", protectedResponse.Headers.Location?.AbsolutePath);
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client)
    {
        string html = await client.GetStringAsync("/account/login");
        Match match = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<string> GetAntiforgeryTokenFromPageAsync(HttpClient client, string path)
    {
        string html = await client.GetStringAsync(path);
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
    public FakeRequestsApiClient Requests { get; } = new();
    public FakeApprovalsApiClient Approvals { get; } = new();
    public FakeDashboardApiClient Dashboard { get; } = new();
    public FakeAdminUsersApiClient AdminUsers { get; } = new();
    public FakeAdminDepartmentsApiClient AdminDepartments { get; } = new();
    public FakeAdminRequestsApiClient AdminRequests { get; } = new();
    public FakeManagerRequestsApiClient ManagerRequests { get; } = new();
    public FakeProfileApiClient Profile { get; } = new();
    public FakeAuditLogsApiClient AuditLogs { get; } = new();

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
            services.RemoveAll<IRequestsApiClient>();
            services.RemoveAll<IApprovalsApiClient>();
            services.RemoveAll<IDashboardApiClient>();
            services.RemoveAll<webapp::CorporateRequestManagement.Web.Api.IAdminUsersApiClient>();
            services.RemoveAll<webapp::CorporateRequestManagement.Web.Api.IAdminDepartmentsApiClient>();
            services.RemoveAll<webapp::CorporateRequestManagement.Web.Api.IAdminRequestsApiClient>();
            services.RemoveAll<webapp::CorporateRequestManagement.Web.Api.IManagerRequestsApiClient>();
            services.RemoveAll<webapp::CorporateRequestManagement.Web.Api.IProfileApiClient>();
            services.RemoveAll<webapp::CorporateRequestManagement.Web.Api.IAuditLogsApiClient>();
            services.AddSingleton<IAuthenticationApiClient>(Authentication);
            services.AddSingleton<IAuthenticatedUserApiClient>(new FakeAuthenticatedUserApiClient(Authentication));
            services.AddSingleton<IRequestsApiClient>(Requests);
            services.AddSingleton<IApprovalsApiClient>(Approvals);
            services.AddSingleton<IDashboardApiClient>(Dashboard);
            services.AddSingleton<webapp::CorporateRequestManagement.Web.Api.IAdminUsersApiClient>(AdminUsers);
            services.AddSingleton<webapp::CorporateRequestManagement.Web.Api.IAdminDepartmentsApiClient>(AdminDepartments);
            services.AddSingleton<webapp::CorporateRequestManagement.Web.Api.IAdminRequestsApiClient>(AdminRequests);
            services.AddSingleton<webapp::CorporateRequestManagement.Web.Api.IManagerRequestsApiClient>(ManagerRequests);
            services.AddSingleton<webapp::CorporateRequestManagement.Web.Api.IProfileApiClient>(Profile);
            services.AddSingleton<webapp::CorporateRequestManagement.Web.Api.IAuditLogsApiClient>(AuditLogs);
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
    public Task<ApiCallResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) => Task.FromResult(new ApiCallResult(HttpStatusCode.Unauthorized, null));
    public Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class FakeAuthenticatedUserApiClient(FakeAuthenticationApiClient authentication) : IAuthenticatedUserApiClient
{
    public Task<AuthenticatedUserContract?> GetCurrentUserAsync(CancellationToken cancellationToken = default) => Task.FromResult(authentication.Result?.User);
}

public sealed class FakeRequestsApiClient : IRequestsApiClient
{
    public IReadOnlyList<webapp::CorporateRequestManagement.Web.Api.RequestListItemContract> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public webapp::CorporateRequestManagement.Web.Api.RequestListQuery? LastQuery { get; private set; }
    public webapp::CorporateRequestManagement.Web.Api.RequestDetailContract? Detail { get; set; }
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.PagedContract<webapp::CorporateRequestManagement.Web.Api.RequestListItemContract>>> GetMineAsync(webapp::CorporateRequestManagement.Web.Api.RequestListQuery query, CancellationToken cancellationToken = default) { LastQuery=query;int total=TotalCount==0?Items.Count:TotalCount;int pages=total==0?0:(int)Math.Ceiling(total/(double)query.PageSize);var page=new webapp::CorporateRequestManagement.Web.Api.PagedContract<webapp::CorporateRequestManagement.Web.Api.RequestListItemContract>(Items,total,query.PageNumber,query.PageSize,pages,query.PageNumber>1,query.PageNumber<pages);return Task.FromResult(new webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.PagedContract<webapp::CorporateRequestManagement.Web.Api.RequestListItemContract>>(HttpStatusCode.OK,page,webapp::CorporateRequestManagement.Web.Api.ApiProblem.Empty)); }
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.RequestDetailContract>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Detail is null ? Fail<webapp::CorporateRequestManagement.Web.Api.RequestDetailContract>() : Task.FromResult(new webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.RequestDetailContract>(HttpStatusCode.OK,Detail,webapp::CorporateRequestManagement.Web.Api.ApiProblem.Empty));
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.RequestDetailContract>> CreateAsync(string title,string description,webapp::CorporateRequestManagement.Web.Api.RequestTypeContract type,decimal? amount,CancellationToken cancellationToken=default)=>Fail<webapp::CorporateRequestManagement.Web.Api.RequestDetailContract>();
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.RequestDetailContract>> UpdateAsync(Guid id,string title,string description,webapp::CorporateRequestManagement.Web.Api.RequestTypeContract type,decimal? amount,string rowVersion,CancellationToken cancellationToken=default)=>Fail<webapp::CorporateRequestManagement.Web.Api.RequestDetailContract>();
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.RequestDetailContract>> SubmitAsync(Guid id,string rowVersion,CancellationToken cancellationToken=default)=>Fail<webapp::CorporateRequestManagement.Web.Api.RequestDetailContract>();
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.RequestDetailContract>> CancelAsync(Guid id,string rowVersion,CancellationToken cancellationToken=default)=>Fail<webapp::CorporateRequestManagement.Web.Api.RequestDetailContract>();
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<IReadOnlyList<webapp::CorporateRequestManagement.Web.Api.ApprovalHistoryContract>>> GetHistoryAsync(Guid id,CancellationToken cancellationToken=default)=>Fail<IReadOnlyList<webapp::CorporateRequestManagement.Web.Api.ApprovalHistoryContract>>();
    private static Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<T>> Fail<T>() => Task.FromResult(new webapp::CorporateRequestManagement.Web.Api.WebApiResult<T>(HttpStatusCode.NotFound, default, webapp::CorporateRequestManagement.Web.Api.ApiProblem.Empty));
}
public sealed class FakeDashboardApiClient : IDashboardApiClient
{
    public IReadOnlyList<webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract> EmployeeTrend { get; set; } = DailyTrend();
    public IReadOnlyList<webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract> ManagerTrend { get; set; } = DailyTrend();
    public IReadOnlyList<webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract> AdminTrend { get; set; } = MonthlyTrend();
    private static webapp::CorporateRequestManagement.Web.Api.WebApiResult<T> Ok<T>(T value) => new(HttpStatusCode.OK,value,webapp::CorporateRequestManagement.Web.Api.ApiProblem.Empty);
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.EmployeeDashboardContract>> GetEmployeeAsync(CancellationToken cancellationToken=default)
    {
        var value=new webapp::CorporateRequestManagement.Web.Api.EmployeeDashboardContract(6,2,1,1,Periods(),EmployeeTrend,Statuses(),[]);return Task.FromResult(Ok(value));
    }
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.ManagerDashboardContract>> GetManagerAsync(CancellationToken cancellationToken=default)
    {
        var value=new webapp::CorporateRequestManagement.Web.Api.ManagerDashboardContract(0,0,0,0,Periods(),ManagerTrend,[new("Approved",1),new("Rejected",1)],[],[]);return Task.FromResult(Ok(value));
    }
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.AdminDashboardContract>> GetAdminAsync(CancellationToken cancellationToken=default)
    {
        var value=new webapp::CorporateRequestManagement.Web.Api.AdminDashboardContract(0,0,0,0,Periods(),AdminTrend,Statuses(),[new("Operasyon",2)],[]);return Task.FromResult(Ok(value));
    }
    private static IReadOnlyList<webapp::CorporateRequestManagement.Web.Api.DashboardPeriodContract> Periods()=>[new("today",0,0),new("month",0,0),new("year",0,0)];
    private static List<webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract> DailyTrend()=>Enumerable.Range(0,30).Select(i=>new webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract(new DateOnly(2026,7,5).AddDays(i).ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture),i==0?1:0,0)).ToList();
    private static List<webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract> MonthlyTrend()=>Enumerable.Range(0,12).Select(i=>new webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract(new DateOnly(2025,9,1).AddMonths(i).ToString("yyyy-MM",System.Globalization.CultureInfo.InvariantCulture),i==0?1:0,0)).ToList();
    private static IReadOnlyList<webapp::CorporateRequestManagement.Web.Api.DashboardDistributionItemContract> Statuses()=>[new("Draft",1),new("PendingManagerApproval",1),new("PendingAdminApproval",1),new("Approved",1),new("Rejected",1),new("Cancelled",1)];
}
public sealed class FakeApprovalsApiClient : IApprovalsApiClient
{
    public IReadOnlyList<webapp::CorporateRequestManagement.Web.Api.ApprovalQueueItemContract> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public bool? LastAdmin { get; private set; }
    public webapp::CorporateRequestManagement.Web.Api.ApprovalListQuery? LastQuery { get; private set; }
    public webapp::CorporateRequestManagement.Web.Api.ApprovalDetailContract? Decision { get; set; }
    public HttpStatusCode DecisionStatus { get; set; } = HttpStatusCode.NotFound;
    public webapp::CorporateRequestManagement.Web.Api.ApprovalDetailContract? Detail { get; set; }
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.PagedContract<webapp::CorporateRequestManagement.Web.Api.ApprovalQueueItemContract>>> GetQueueAsync(bool admin,webapp::CorporateRequestManagement.Web.Api.ApprovalListQuery query,CancellationToken cancellationToken=default){LastAdmin=admin;LastQuery=query;int total=TotalCount==0?Items.Count:TotalCount;int pages=total==0?0:(int)Math.Ceiling(total/(double)query.PageSize);var page=new webapp::CorporateRequestManagement.Web.Api.PagedContract<webapp::CorporateRequestManagement.Web.Api.ApprovalQueueItemContract>(Items,total,query.PageNumber,query.PageSize,pages,query.PageNumber>1,query.PageNumber<pages);return Task.FromResult(new webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.PagedContract<webapp::CorporateRequestManagement.Web.Api.ApprovalQueueItemContract>>(HttpStatusCode.OK,page,webapp::CorporateRequestManagement.Web.Api.ApiProblem.Empty));}
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.ApprovalDetailContract>> GetDetailAsync(bool admin,Guid id,CancellationToken cancellationToken=default)=>Detail is null?Fail():Task.FromResult(new webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.ApprovalDetailContract>(HttpStatusCode.OK,Detail,webapp::CorporateRequestManagement.Web.Api.ApiProblem.Empty));
    public Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.ApprovalDetailContract>> DecideAsync(bool admin,Guid id,bool approve,string rowVersion,string? reason,CancellationToken cancellationToken=default)=>Task.FromResult(new webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.ApprovalDetailContract>(DecisionStatus,Decision,webapp::CorporateRequestManagement.Web.Api.ApiProblem.Empty));
    private static Task<webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.ApprovalDetailContract>> Fail()=>Task.FromResult(new webapp::CorporateRequestManagement.Web.Api.WebApiResult<webapp::CorporateRequestManagement.Web.Api.ApprovalDetailContract>(HttpStatusCode.NotFound,null,webapp::CorporateRequestManagement.Web.Api.ApiProblem.Empty));
}
