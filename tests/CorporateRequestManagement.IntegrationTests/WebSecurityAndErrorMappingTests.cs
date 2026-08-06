extern alias webapp;
using System.Net;
using System.Text.RegularExpressions;
namespace CorporateRequestManagement.WebTests;
using A = webapp::CorporateRequestManagement.Web.Authentication;

public sealed class WebSecurityAndErrorMappingTests(AuthenticationWebFactory factory) : IClassFixture<AuthenticationWebFactory>
{
    [Theory]
    [InlineData("user-create", "/Admin/Users/Create", "AdminUsers")]
    [InlineData("user-edit", "/Admin/Users/11111111-1111-1111-1111-111111111111/Edit", "AdminUsers")]
    [InlineData("user-password", "/Admin/Users/11111111-1111-1111-1111-111111111111/ResetPassword", "AdminUsers")]
    [InlineData("user-delete", "/Admin/Users/11111111-1111-1111-1111-111111111111/Delete", "AdminUsers")]
    [InlineData("department-create", "/Admin/Departments/Create", "Departments")]
    [InlineData("department-edit", "/Admin/Departments/11111111-1111-1111-1111-111111111111/Edit", "Departments")]
    [InlineData("department-manager", "/Admin/Departments/11111111-1111-1111-1111-111111111111/Manager", "Departments")]
    [InlineData("department-delete", "/Admin/Departments/11111111-1111-1111-1111-111111111111/Delete", "Departments")]
    [InlineData("request-delete", "/Admin/Requests/11111111-1111-1111-1111-111111111111/Delete", "Requests")]
    [InlineData("profile-password", "/Profile/ChangePassword", "Profile")]
    public async Task ProtectedMutation_WhenAntiforgeryTokenIsMissing_ShouldRejectBeforeApiCall(string caseName, string path, string clientName)
    {
        Assert.NotEmpty(caseName);
        using HttpClient client = await LoginAsync("Admin"); int before = Calls(clientName);
        using HttpResponseMessage response = await client.PostAsync(path, new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Equal(before, Calls(clientName));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, HttpStatusCode.OK, null)]
    [InlineData(HttpStatusCode.Unauthorized, HttpStatusCode.Redirect, "/account/login")]
    [InlineData(HttpStatusCode.Forbidden, HttpStatusCode.Redirect, "/account/access-denied")]
    [InlineData(HttpStatusCode.NotFound, HttpStatusCode.OK, null)]
    [InlineData(HttpStatusCode.Conflict, HttpStatusCode.OK, null)]
    [InlineData(HttpStatusCode.TooManyRequests, HttpStatusCode.OK, null)]
    [InlineData(HttpStatusCode.InternalServerError, HttpStatusCode.OK, null)]
    public async Task Failed_password_reset_should_map_safely_without_raw_detail_or_success(HttpStatusCode apiStatus, HttpStatusCode expected, string? redirect)
    {
        factory.AdminUsers.StatusCode = apiStatus; using HttpClient client = await LoginAsync("Admin"); string token = await TokenAsync(client, "/Admin/Users/Create");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { { "newPassword", "SafePass!2026" }, { "confirmPassword", "SafePass!2026" }, { "__RequestVerificationToken", token } });
        using HttpResponseMessage response = await client.PostAsync($"/Admin/Users/{Guid.NewGuid()}/ResetPassword", form); string html = await response.Content.ReadAsStringAsync();
        Assert.Equal(expected, response.StatusCode); if (redirect is not null) Assert.StartsWith(redirect, response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-api-detail-must-not-render", html, StringComparison.Ordinal); Assert.DoesNotContain("Parola sıfırlandı", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Valid_antiforgery_token_should_reach_action_and_failed_manager_assignment_should_not_report_success()
    {
        factory.AdminDepartments.StatusCode = HttpStatusCode.Conflict; using HttpClient client = await LoginAsync("Admin"); string token = await TokenAsync(client, "/Admin/Departments/Create"); int before = factory.AdminDepartments.MutationCalls;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { { "managerId", Guid.NewGuid().ToString() }, { "__RequestVerificationToken", token } });
        using HttpResponseMessage response = await client.PostAsync($"/Admin/Departments/{Guid.NewGuid()}/Manager", form); string html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(before + 1, factory.AdminDepartments.MutationCalls); Assert.Contains("sayfayı yenileyip", html, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("güncellendi", html, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("raw-api-detail", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backend_401_should_clear_real_web_session_and_old_cookies_should_not_authenticate_again()
    {
        factory.AdminUsers.StatusCode = HttpStatusCode.Unauthorized;
        using HttpClient client = await LoginAsync("Admin");
        string token = await TokenAsync(client, "/Admin/Users/Create");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["newPassword"] = "SafePass!2026",
            ["confirmPassword"] = "SafePass!2026",
            ["__RequestVerificationToken"] = token
        });

        using HttpResponseMessage rejected = await client.PostAsync($"/Admin/Users/{Guid.NewGuid()}/ResetPassword?source=local", form);
        string setCookies = string.Join(';', rejected.Headers.GetValues("Set-Cookie"));

        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.StartsWith("/account/login", rejected.Headers.Location?.OriginalString, StringComparison.Ordinal);
        Assert.Contains("returnUrl=%2FAdmin%2FUsers", rejected.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http%3A", rejected.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("__Host-Crm.Web.Auth", setCookies, StringComparison.Ordinal);

        using HttpResponseMessage second = await client.GetAsync("/Admin/Users");

        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal("/account/login", second.Headers.Location?.AbsolutePath);
        Assert.NotEqual(rejected.Headers.Location?.OriginalString, second.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "bulunamadı")]
    [InlineData(HttpStatusCode.Conflict, "sayfayı yenileyip")]
    [InlineData(HttpStatusCode.TooManyRequests, "Çok fazla")]
    [InlineData(HttpStatusCode.InternalServerError, "tamamlanamıyor")]
    public async Task Department_delete_failure_should_map_safely_without_fake_success(HttpStatusCode apiStatus, string safeMessage)
    {
        factory.AdminDepartments.StatusCode = apiStatus;
        using HttpClient client = await LoginAsync("Admin");
        string token = await TokenAsync(client, "/Admin/Departments/Create");
        int callsBefore = factory.AdminDepartments.MutationCalls;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        });

        using HttpResponseMessage response = await client.PostAsync($"/Admin/Departments/{Guid.NewGuid()}/Delete", form);
        string html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(callsBefore + 1, factory.AdminDepartments.MutationCalls);
        Assert.Contains(safeMessage, html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Departman silindi", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw-api-detail", html, StringComparison.OrdinalIgnoreCase);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "bulunamadı")]
    [InlineData(HttpStatusCode.Conflict, "sayfayı yenileyip")]
    [InlineData(HttpStatusCode.TooManyRequests, "Çok fazla")]
    [InlineData(HttpStatusCode.InternalServerError, "tamamlanamıyor")]
    public async Task Admin_request_soft_delete_failure_should_map_safely_without_fake_success(HttpStatusCode apiStatus, string safeMessage)
    {
        factory.AdminRequests.StatusCode = apiStatus;
        using HttpClient client = await LoginAsync("Admin");
        string token = await TokenAsync(client, "/Admin/Users/Create");
        int callsBefore = factory.AdminRequests.MutationCalls;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["rowVersion"] = "AQIDBAUGBwg=",
            ["__RequestVerificationToken"] = token
        });

        using HttpResponseMessage response = await client.PostAsync($"/Admin/Requests/{Guid.NewGuid()}/Delete", form);
        string html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(callsBefore + 1, factory.AdminRequests.MutationCalls);
        Assert.Contains(safeMessage, html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Talep silindi", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw-api-detail", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQIDBAUGBwg=", html, StringComparison.Ordinal);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("department")]
    [InlineData("request")]
    public async Task Successful_delete_should_use_prg_and_report_success_only_after_success(string target)
    {
        factory.AdminDepartments.StatusCode = HttpStatusCode.NoContent;
        factory.AdminRequests.StatusCode = HttpStatusCode.NoContent;
        using HttpClient client = await LoginAsync("Admin");
        string token = await TokenAsync(client, "/Admin/Users/Create");
        Guid id = Guid.NewGuid();
        string path = target == "department" ? $"/Admin/Departments/{id}/Delete" : $"/Admin/Requests/{id}/Delete";
        int callsBefore = target == "department" ? factory.AdminDepartments.MutationCalls : factory.AdminRequests.MutationCalls;
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["rowVersion"] = "AQIDBAUGBwg=",
            ["__RequestVerificationToken"] = token
        });

        using HttpResponseMessage response = await client.PostAsync(path, form);
        using HttpResponseMessage landing = await client.GetAsync(response.Headers.Location);
        string html = WebUtility.HtmlDecode(await landing.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(target == "department" ? "/Admin/Departments" : "/Admin/Requests", response.Headers.Location?.OriginalString);
        Assert.Equal(callsBefore + 1, target == "department" ? factory.AdminDepartments.MutationCalls : factory.AdminRequests.MutationCalls);
        Assert.Equal(HttpStatusCode.OK, landing.StatusCode);
        Assert.Contains(target == "department" ? "Departman silindi" : "Talep silindi", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQIDBAUGBwg=", html, StringComparison.Ordinal);
    }

    private int Calls(string name) => name switch { "AdminUsers" => factory.AdminUsers.MutationCalls, "Departments" => factory.AdminDepartments.MutationCalls, "Requests" => factory.AdminRequests.MutationCalls, "Profile" => factory.Profile.MutationCalls, _ => -1 };
    private async Task<HttpClient> LoginAsync(string role) { factory.Authentication.Result = new A.AuthenticationContract { AccessToken = AuthenticationWebFactory.AccessToken, RefreshToken = AuthenticationWebFactory.RefreshToken, AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1), User = new A.AuthenticatedUserContract { Id = Guid.NewGuid(), FirstName = "Test", LastName = "User", Email = "user@example.test", Role = role } }; HttpClient client = factory.CreateSecureClient(); string token = await TokenAsync(client, "/account/login"); using var form = new FormUrlEncodedContent(new Dictionary<string, string> { { "Email", "user@example.test" }, { "Password", "password" }, { "__RequestVerificationToken", token } }); using HttpResponseMessage response = await client.PostAsync("/account/login", form); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); return client; }
    private static async Task<string> TokenAsync(HttpClient client, string path) { string html = await client.GetStringAsync(path); Match m = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\""); Assert.True(m.Success); return WebUtility.HtmlDecode(m.Groups[1].Value); }
}
