extern alias webapp;
using System.Net;
using System.Text.RegularExpressions;
namespace CorporateRequestManagement.WebTests;
using A=webapp::CorporateRequestManagement.Web.Authentication;
using W=webapp::CorporateRequestManagement.Web.Api;

public sealed class WebOutputEncodingTests(AuthenticationWebFactory factory):IClassFixture<AuthenticationWebFactory>
{
    [Fact]
    public async Task Approval_and_audit_details_should_html_encode_user_controlled_text_in_rendered_responses()
    {
        const string payload="<script>alert('unsafe')</script>";Guid requestId=Guid.NewGuid(),auditId=Guid.NewGuid();factory.AdminRequests.Detail=new(requestId,"Title","Description",Guid.NewGuid(),"User","user@example.test",Guid.NewGuid(),"Yazılım",W.RequestTypeContract.Other,null,W.RequestStatusContract.Rejected,DateTimeOffset.UtcNow,null,DateTimeOffset.UtcNow,"AQIDBAUGBwg=",[new(W.ApprovalLevelContract.Admin,W.ApprovalStatusContract.Rejected,payload,DateTimeOffset.UtcNow,"Admin")]);factory.AuditLogs.Detail=new(auditId,Guid.NewGuid(),"Admin",payload,Guid.NewGuid(),"Safe.Action",DateTimeOffset.UtcNow,payload,payload);
        using HttpClient client=await LoginAsync();string requestHtml=await client.GetStringAsync($"/Admin/Requests/{requestId}");string auditHtml=await client.GetStringAsync($"/Admin/AuditLogs/{auditId}");
        Assert.DoesNotContain(payload, requestHtml, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", requestHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(payload, auditHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("&lt;script&gt;", auditHtml, StringComparison.OrdinalIgnoreCase);
        foreach (string html in new[] { requestHtml, auditHtml })
        {
            Assert.DoesNotContain(" onerror=", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(" onload=", html, StringComparison.OrdinalIgnoreCase);
        }
    }
    private async Task<HttpClient> LoginAsync(){factory.Authentication.Result=new A.AuthenticationContract{AccessToken=AuthenticationWebFactory.AccessToken,RefreshToken=AuthenticationWebFactory.RefreshToken,AccessTokenExpiresAt=DateTimeOffset.UtcNow.AddMinutes(10),RefreshTokenExpiresAt=DateTimeOffset.UtcNow.AddDays(1),User=new A.AuthenticatedUserContract{Id=Guid.NewGuid(),FirstName="Admin",LastName="User",Email="admin@example.test",Role="Admin"}};HttpClient client=factory.CreateSecureClient();string page=await client.GetStringAsync("/account/login");string token=WebUtility.HtmlDecode(Regex.Match(page,"name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);using var form=new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","admin@example.test"},{"Password","password"},{"__RequestVerificationToken",token}});Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/account/login",form)).StatusCode);return client;}
}
