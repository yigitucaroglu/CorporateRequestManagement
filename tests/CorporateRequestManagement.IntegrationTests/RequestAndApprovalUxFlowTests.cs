extern alias webapp;
using System.Net;
using System.Text.RegularExpressions;
namespace CorporateRequestManagement.WebTests;
using A = webapp::CorporateRequestManagement.Web.Authentication;
using W = webapp::CorporateRequestManagement.Web.Api;

public sealed class RequestAndApprovalUxFlowTests(AuthenticationWebFactory factory) : IClassFixture<AuthenticationWebFactory>
{
    [Fact]
    public async Task Request_form_should_publish_required_hidden_and_optional_amount_modes()
    {
        using HttpClient client = await LoginAsync("Employee");
        string html = await client.GetStringAsync("/Requests/Create");
        Assert.Contains("value=\"1\" data-amount-mode=\"required\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"2\" data-amount-mode=\"required\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"3\" data-amount-mode=\"hidden\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"4\" data-amount-mode=\"optional\"", html, StringComparison.Ordinal);
        Assert.Contains("data-amount-input", html, StringComparison.Ordinal);
        Assert.Contains("data-amount-help", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Manager", false, true, W.RequestStatusContract.Approved, "/Approvals/Manager", "Talep onaylandı.")]
    [InlineData("Manager", false, true, W.RequestStatusContract.PendingAdminApproval, "/Approvals/Manager", "Talep yönetici tarafından onaylandı ve yönetim onayına gönderildi.")]
    [InlineData("Manager", false, false, W.RequestStatusContract.Rejected, "/Approvals/Manager", "Talep reddedildi.")]
    [InlineData("Admin", true, true, W.RequestStatusContract.Approved, "/Approvals/Admin", "Talep onaylandı.")]
    [InlineData("Admin", true, false, W.RequestStatusContract.Rejected, "/Approvals/Admin", "Talep reddedildi.")]
    public async Task Successful_approval_should_redirect_to_actionable_queue(string role, bool admin, bool approve, W.RequestStatusContract status, string expectedLocation, string expectedMessage)
    {
        Guid id = Guid.NewGuid();
        var detail = Detail(id, status);
        factory.Approvals.Detail = detail;
        factory.Approvals.Decision = detail;
        factory.Approvals.DecisionStatus = HttpStatusCode.OK;
        using HttpClient client = await LoginAsync(role);
        string path = admin ? $"/Approvals/Admin/{id}" : $"/Approvals/Manager/{id}";
        string token = Token(await client.GetStringAsync(path));
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>{{"RowVersion",detail.RowVersion},{"Reason","Güvenli ret gerekçesi"},{"__RequestVerificationToken",token}});
        using HttpResponseMessage response = await client.PostAsync(path + (approve?"/Approve":"/Reject"), form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedLocation, response.Headers.Location?.OriginalString);
        string queue=WebUtility.HtmlDecode(await client.GetStringAsync(expectedLocation));
        Assert.Contains(expectedMessage,queue,StringComparison.Ordinal);
        Assert.DoesNotContain("Talep bulunamadı",queue,StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Manager", "/Approvals/Manager", "Onay bekleyen talep bulunmuyor", "Departman Taleplerine Git")]
    [InlineData("Admin", "/Approvals/Admin", "Yönetim onayı bekleyen talep bulunmuyor", "Tüm Taleplere Git")]
    public async Task Empty_approval_queue_should_explain_history_navigation(string role, string path, string title, string action)
    {
        factory.Approvals.Items=[];factory.Approvals.TotalCount=0;
        using HttpClient client=await LoginAsync(role);
        string html=await client.GetStringAsync(path);
        Assert.Contains(title,html,StringComparison.Ordinal);
        Assert.Contains(action,html,StringComparison.Ordinal);
        Assert.DoesNotContain("Talep bulunamadı",html,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Password_reset_validation_should_render_same_safe_form_without_password_value()
    {
        Guid id=Guid.NewGuid();
        factory.AdminUsers.Detail=new(id,"Test","Kullanıcı","test@example.test",W.UserRoleContract.Employee,Guid.NewGuid(),"Finans",true,DateTimeOffset.UtcNow);
        factory.AdminUsers.StatusCode=HttpStatusCode.BadRequest;
        factory.AdminUsers.Problem=new("validation.failed","raw-api-detail-must-not-render","trace-secret",new Dictionary<string,string[]>{{"Password",["Parola en az 12 karakter olmalıdır."]},{"ConfirmPassword",["Parolalar birbiriyle eşleşmiyor."]}});
        using HttpClient client=await LoginAsync("Admin");
        string token=Token(await client.GetStringAsync($"/Admin/Users/{id}"));
        const string unsafePassword="Short1!";
        using var form=new FormUrlEncodedContent(new Dictionary<string,string>{{"newPassword",unsafePassword},{"confirmPassword","Different1!"},{"__RequestVerificationToken",token}});
        using HttpResponseMessage response=await client.PostAsync($"/Admin/Users/{id}/ResetPassword",form);
        string html=await response.Content.ReadAsStringAsync();
        string decoded=WebUtility.HtmlDecode(html);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Contains("Parola en az 12 karakter olmalıdır.",decoded,StringComparison.Ordinal);
        Assert.Contains("Parolalar birbiriyle eşleşmiyor.",decoded,StringComparison.Ordinal);
        Assert.DoesNotContain(unsafePassword,html,StringComparison.Ordinal);
        Assert.DoesNotContain("raw-api-detail-must-not-render",html,StringComparison.Ordinal);
        Assert.DoesNotContain("İşlem tamamlanamadı",html,StringComparison.Ordinal);
    }

    private static W.ApprovalDetailContract Detail(Guid id,W.RequestStatusContract status)=>new(id,"Test talebi","Açıklama",W.RequestTypeContract.Expense,12500,status,Guid.NewGuid(),"Talep Sahibi",Guid.NewGuid(),"Finans",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null,"AQIDBAUGBwg=",[]);
    private static string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
    private async Task<HttpClient> LoginAsync(string role){factory.Authentication.Result=new A.AuthenticationContract{AccessToken=AuthenticationWebFactory.AccessToken,RefreshToken=AuthenticationWebFactory.RefreshToken,AccessTokenExpiresAt=DateTimeOffset.UtcNow.AddMinutes(10),RefreshTokenExpiresAt=DateTimeOffset.UtcNow.AddDays(1),User=new A.AuthenticatedUserContract{Id=Guid.NewGuid(),FirstName="Test",LastName=role,Email=$"{role.ToLowerInvariant()}@example.test",Role=role}};HttpClient client=factory.CreateSecureClient();string page=await client.GetStringAsync("/account/login");using var form=new FormUrlEncodedContent(new Dictionary<string,string>{{"Email",$"{role.ToLowerInvariant()}@example.test"},{"Password","password"},{"__RequestVerificationToken",Token(page)}});Assert.Equal(HttpStatusCode.Redirect,(await client.PostAsync("/account/login",form)).StatusCode);return client;}
}
