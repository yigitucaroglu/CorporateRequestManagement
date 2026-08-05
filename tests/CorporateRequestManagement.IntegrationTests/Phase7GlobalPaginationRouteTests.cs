extern alias webapp;

using System.Net;
using System.Text.RegularExpressions;

namespace CorporateRequestManagement.WebTests;

using A = webapp::CorporateRequestManagement.Web.Authentication;
using W = webapp::CorporateRequestManagement.Web.Api;

public sealed class Phase7GlobalPaginationRouteTests(AuthenticationWebFactory factory) : IClassFixture<AuthenticationWebFactory>
{
    [Fact]
    public async Task Audit_pagination_should_preserve_canonical_route_filters_and_follow_generated_link()
    {
        Guid actorId = Guid.NewGuid();
        factory.AuditLogs.Actors = [new(actorId, "Ayşe Denetçi", "ayse@example.test", W.UserRoleContract.Admin, false)];
        factory.AuditLogs.ListValue = new([new W.AuditLogContract(Guid.NewGuid(), actorId, "Ayşe Denetçi", "User", Guid.NewGuid(), "User.Updated", DateTimeOffset.UtcNow, null, null, "ayse@example.test", W.UserRoleContract.Admin)], 11, 1, 10, 2, false, true);
        using HttpClient client = await LoginAsync("Admin");

        string html = await client.GetStringAsync($"/Admin/AuditLogs?UserId={actorId}&Action=User.Updated&CreatedFrom=2026-08-01&PageSize=10&PageNumber=1");
        string next = Link(html, "next");

        Assert.StartsWith("/Admin/AuditLogs?", next, StringComparison.Ordinal);
        Assert.DoesNotContain("/AdminAuditLogs", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PageNumber=2", next, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PageSize=10", next, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"UserId={actorId}", next, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Action=User.Updated", next, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CreatedFrom=2026-08-01", next, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/Admin/AuditLogs", FormAction(html));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(next)).StatusCode);
    }

    [Fact]
    public async Task Every_admin_paginated_route_should_render_and_follow_its_canonical_next_link()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        factory.AdminUsers.ListValue = new([new(Guid.NewGuid(), "Test", "Admin", "admin@example.test", W.UserRoleContract.Admin, null, null, true, now)], 11, 1, 10, 2, false, true);
        factory.AdminDepartments.ListValue = new([new(Guid.NewGuid(), "Yazılım", null, null, 1, 1, now)], 11, 1, 10, 2, false, true);
        factory.AdminRequests.ListValue = new([Scoped(now)], 11, 1, 10, 2, false, true);
        factory.ManagerRequests.ListValue = new([Scoped(now)], 11, 1, 10, 2, false, true);
        factory.Approvals.Items = [Approval(now)]; factory.Approvals.TotalCount = 11;
        using HttpClient client = await LoginAsync("Admin");

        foreach (string path in new[] { "/Admin/Users", "/Admin/Departments", "/Admin/Requests", "/Approvals/Admin" })
            await AssertCanonicalAndFollowAsync(client, path);
    }

    [Theory]
    [InlineData("Employee", "/Requests")]
    [InlineData("Manager", "/Manager/Requests")]
    [InlineData("Manager", "/Approvals/Manager")]
    public async Task Employee_and_manager_paginated_routes_should_render_and_follow_canonical_next_link(string role, string path)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        factory.Requests.Items = [new(Guid.NewGuid(), "Birinci talep", W.RequestTypeContract.Other, null, W.RequestStatusContract.Draft, now, now, null, "AQIDBAUGBwg=")]; factory.Requests.TotalCount = 11;
        factory.AdminRequests.ListValue = new([Scoped(now)], 11, 1, 10, 2, false, true);
        factory.ManagerRequests.ListValue = new([Scoped(now)], 11, 1, 10, 2, false, true);
        factory.Approvals.Items = [Approval(now)]; factory.Approvals.TotalCount = 11;
        using HttpClient client = await LoginAsync(role);

        await AssertCanonicalAndFollowAsync(client, path);
    }

    private static async Task AssertCanonicalAndFollowAsync(HttpClient client, string path)
    {
        string html = await client.GetStringAsync(path + "?PageSize=10&PageNumber=1&SearchTerm=ekipman&SortDirection=Ascending");
        string next = Link(html, "next");
        Assert.StartsWith(path + "?", next, StringComparison.Ordinal);
        Assert.Contains("PageNumber=2", next, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PageSize=10", next, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SearchTerm=ekipman", next, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(path, FormAction(html));
        using HttpResponseMessage followed = await client.GetAsync(next);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
    }

    private async Task<HttpClient> LoginAsync(string role)
    {
        factory.Authentication.Result = new A.AuthenticationContract
        {
            AccessToken = AuthenticationWebFactory.AccessToken, RefreshToken = AuthenticationWebFactory.RefreshToken,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10), RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            User = new A.AuthenticatedUserContract { Id = Guid.NewGuid(), FirstName = "Test", LastName = role, Email = $"{role.ToLowerInvariant()}@example.test", Role = role }
        };
        HttpClient client = factory.CreateSecureClient();
        string login = await client.GetStringAsync("/account/login");
        string token = WebUtility.HtmlDecode(Regex.Match(login, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = factory.Authentication.Result.User.Email, ["Password"] = "password", ["__RequestVerificationToken"] = token });
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/account/login", form)).StatusCode);
        return client;
    }

    private static string Link(string html, string rel) => WebUtility.HtmlDecode(Regex.Match(html, $"<a[^>]*href=\"([^\"]+)\"[^>]*rel=\"{rel}\"", RegexOptions.IgnoreCase).Groups[1].Value);
    private static string FormAction(string html) => WebUtility.HtmlDecode(Regex.Match(html, "<form[^>]*action=\"([^\"]+)\"[^>]*class=\"crm-page-size-form\"", RegexOptions.IgnoreCase).Groups[1].Value);
    private static W.ScopedRequestContract Scoped(DateTimeOffset now) => new(Guid.NewGuid(), "Ekipman", "Açıklama", Guid.NewGuid(), "Talep Sahibi", "user@example.test", Guid.NewGuid(), "Yazılım", W.RequestTypeContract.Equipment, 100m, W.RequestStatusContract.PendingManagerApproval, now, now, null, "AQIDBAUGBwg=", []);
    private static W.ApprovalQueueItemContract Approval(DateTimeOffset now) => new(Guid.NewGuid(), "Ekipman", W.RequestTypeContract.Equipment, 100m, W.RequestStatusContract.PendingManagerApproval, "Talep Sahibi", Guid.NewGuid(), "Yazılım", now, now, "AQIDBAUGBwg=");
}
