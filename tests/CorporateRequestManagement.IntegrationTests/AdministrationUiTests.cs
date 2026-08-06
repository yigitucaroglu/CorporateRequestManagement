extern alias webapp;

using System.Net;
using System.Text.RegularExpressions;

namespace CorporateRequestManagement.WebTests;

using A = webapp::CorporateRequestManagement.Web.Authentication;
using W = webapp::CorporateRequestManagement.Web.Api;

public sealed class AdministrationUiTests(AuthenticationWebFactory factory) : IClassFixture<AuthenticationWebFactory>
{
    [Fact]
    public async Task Admin_request_list_should_render_canonical_toolbar_actions_pagination_and_safe_turkish_text()
    {
        Guid requestId = Guid.NewGuid();
        factory.AdminRequests.ListValue = new([
            new W.ScopedRequestContract(requestId, "Ekipman yenileme", "Açıklama", Guid.NewGuid(), "Ayşe Çalışan", "ayse@example.test", Guid.NewGuid(), "Yazılım", W.RequestTypeContract.Equipment, 25000m, W.RequestStatusContract.PendingAdminApproval, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, "AQIDBAUGBwg=", [])
        ], 21, 1, 20, 2, false, true);
        using HttpClient client = await LoginAsync();

        string html = await client.GetStringAsync("/Admin/Requests");
        string visible = VisibleText(html);

        Assert.Contains("Talep başlığı veya açıklamasında ara", html, StringComparison.Ordinal);
        Assert.Contains("Ara", visible, StringComparison.Ordinal);
        Assert.Contains("Filtreler", visible, StringComparison.Ordinal);
        Assert.Contains("İşlemler", visible, StringComparison.Ordinal);
        Assert.Contains("Toplam 21 kayıt", visible, StringComparison.Ordinal);
        Assert.Contains("Sonraki", visible, StringComparison.Ordinal);
        Assert.Contains("Yönetim Onayı Bekliyor", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("Soft Delete", visible, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RowVersion", visible, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(requestId.ToString(), visible, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task User_create_should_render_named_department_select_and_turkish_roles_without_visible_guid_input()
    {
        Guid departmentId = Guid.NewGuid();
        factory.AdminDepartments.ListValue = new([new W.DepartmentContract(departmentId, "Yazılım", null, null, 3, 4, DateTimeOffset.UtcNow)], 1, 1, 100, 1, false, false);
        using HttpClient client = await LoginAsync();

        string html = await client.GetStringAsync("/Admin/Users/Create");
        string visible = VisibleText(html);

        Assert.Contains("Yazılım", visible, StringComparison.Ordinal);
        Assert.Contains("Çalışan", visible, StringComparison.Ordinal);
        Assert.Contains("Yönetici", visible, StringComparison.Ordinal);
        Assert.Contains("Sistem Yöneticisi", visible, StringComparison.Ordinal);
        Assert.DoesNotContain("Departman kimliği", visible, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(departmentId.ToString(), visible, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">Employee<", html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Authentication.LoginSucceeded", "User", "Başarılı giriş", "Kullanıcı")]
    [InlineData("Unknown.SecretAction", "UnknownEntity", "Sistem işlemi", "Sistem Kaydı")]
    public async Task Audit_history_should_map_known_and_unknown_technical_values_without_rendering_raw_values(string action, string entity, string expectedAction, string expectedEntity)
    {
        factory.AuditLogs.ListValue = new([new W.AuditLogContract(Guid.NewGuid(), null, "Sistem", entity, Guid.NewGuid(), action, DateTimeOffset.UtcNow, null, null)], 1, 1, 20, 1, false, false);
        using HttpClient client = await LoginAsync();

        string html = await client.GetStringAsync("/Admin/AuditLogs");
        string visible = VisibleText(html);

        Assert.Contains(expectedAction, visible, StringComparison.Ordinal);
        Assert.Contains(expectedEntity, visible, StringComparison.Ordinal);
        Assert.DoesNotContain(action, visible, StringComparison.Ordinal);
        Assert.DoesNotContain(entity, visible, StringComparison.Ordinal);
        Assert.Contains("İşlemler", visible, StringComparison.Ordinal);
        Assert.Contains("Filtreler", visible, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audit_history_should_render_actor_identity_filter_and_deleted_badge_without_visible_guid()
    {
        Guid actorId = Guid.NewGuid();
        factory.AuditLogs.ActorsStatusCode = HttpStatusCode.OK;
        factory.AuditLogs.Actors = [new(actorId, "Ayşe Denetçi", "ayse.denetcı@example.test", W.UserRoleContract.Admin, true)];
        factory.AuditLogs.ListValue = new([new W.AuditLogContract(Guid.NewGuid(), actorId, "Ayşe Denetçi", "User", Guid.NewGuid(), "User.Updated", DateTimeOffset.UtcNow, null, null, "ayse.denetcı@example.test", W.UserRoleContract.Admin, true)], 1, 1, 20, 1, false, false);
        using HttpClient client = await LoginAsync();

        string html = await client.GetStringAsync($"/Admin/AuditLogs?UserId={actorId}");
        string visible = VisibleText(html);

        Assert.Contains("Ayşe Denetçi — ayse.denetcı@example.test", visible, StringComparison.Ordinal);
        Assert.Contains("Sistem Yöneticisi", visible, StringComparison.Ordinal);
        Assert.Contains("Silinmiş hesap", visible, StringComparison.Ordinal);
        Assert.Contains("Tüm kullanıcılar", visible, StringComparison.Ordinal);
        Assert.DoesNotContain(actorId.ToString(), visible, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Audit_actor_lookup_failure_should_keep_list_and_render_safe_warning()
    {
        factory.AuditLogs.ActorsStatusCode = HttpStatusCode.ServiceUnavailable;
        factory.AuditLogs.ListValue = new([new W.AuditLogContract(Guid.NewGuid(), null, "Sistem", "AuditLog", Guid.NewGuid(), "Unknown.Action", DateTimeOffset.UtcNow, null, null, IsSystemActor: true)], 1, 1, 20, 1, false, false);
        using HttpClient client = await LoginAsync();

        string visible = VisibleText(await client.GetStringAsync("/Admin/AuditLogs"));

        Assert.Contains("Kayıtlar görüntülenmeye devam ediyor", visible, StringComparison.Ordinal);
        Assert.Contains("Otomatik sistem işlemi", visible, StringComparison.Ordinal);
        factory.AuditLogs.ActorsStatusCode = HttpStatusCode.OK;
    }

    [Fact]
    public async Task Department_detail_should_render_eligible_manager_identity_without_visible_guid()
    {
        Guid departmentId = Guid.NewGuid();
        Guid managerId = Guid.NewGuid();
        factory.AdminDepartments.StatusCode = HttpStatusCode.OK;
        factory.AdminDepartments.EligibleStatusCode = HttpStatusCode.OK;
        factory.AdminDepartments.Detail = new(departmentId, "Finans", managerId, "Ayşe Yılmaz", 2, 0, DateTimeOffset.UtcNow);
        factory.AdminDepartments.EligibleManagers = [new(managerId, "Ayşe Yılmaz", "ayse.yilmaz@company.com")];
        using HttpClient client = await LoginAsync();

        string html = await client.GetStringAsync($"/Admin/Departments/{departmentId}");
        string visible = VisibleText(html);

        Assert.Contains("Ayşe Yılmaz — ayse.yilmaz@company.com", visible, StringComparison.Ordinal);
        Assert.DoesNotContain(managerId.ToString(), visible, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ORGANİZASYON", visible, StringComparison.Ordinal);
        Assert.Contains("← Departmanlara dön", visible, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Department_detail_without_candidate_should_explain_next_step_and_disable_save()
    {
        Guid departmentId = Guid.NewGuid();
        factory.AdminDepartments.StatusCode = HttpStatusCode.OK;
        factory.AdminDepartments.EligibleStatusCode = HttpStatusCode.OK;
        factory.AdminDepartments.Detail = new(departmentId, "Finans", null, null, 1, 0, DateTimeOffset.UtcNow);
        factory.AdminDepartments.EligibleManagers = [];
        using HttpClient client = await LoginAsync();

        string html = await client.GetStringAsync($"/Admin/Departments/{departmentId}");
        string visible = VisibleText(html);

        Assert.Contains("Bu departmanda yönetici rolüne sahip aktif bir kullanıcı bulunmuyor.", visible, StringComparison.Ordinal);
        Assert.Contains($"/Admin/Users?departmentId={departmentId}", html, StringComparison.OrdinalIgnoreCase);
        Assert.Matches("<button[^>]*disabled[^>]*>Yöneticiyi Kaydet</button>", html);
        Assert.DoesNotContain("ManagerId", visible, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DepartmentId", visible, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("crm-empty-actions", html, StringComparison.Ordinal);
        Assert.Contains("Yeni Yönetici Oluştur ve Ata", visible, StringComparison.Ordinal);
        Assert.Contains("Kullanıcıları Gör", visible, StringComparison.Ordinal);
        string css = await client.GetStringAsync("/css/crm-ui.css");
        Assert.Contains(".crm-empty-actions{display:flex;justify-content:center;align-items:center;flex-wrap:wrap", css, StringComparison.Ordinal);
        Assert.Contains("@media(max-width:575px){.crm-empty-actions{width:100%;flex-direction:column}", css, StringComparison.Ordinal);
        Assert.Contains(".crm-empty-actions .crm-btn{width:100%;min-height:48px}", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_manager_duplicate_email_should_render_one_field_error_without_password_or_generic_conflict()
    {
        Guid departmentId = Guid.NewGuid();
        factory.AdminDepartments.StatusCode = HttpStatusCode.Conflict;
        factory.AdminDepartments.Problem = new("user.email_exists", "technical detail", "trace", new Dictionary<string, string[]>());
        factory.AdminDepartments.Detail = new(departmentId, "Finans", null, null, 1, 0, DateTimeOffset.UtcNow);
        using HttpClient client = await LoginAsync();
        string page = await client.GetStringAsync($"/Admin/Departments/{departmentId}/CreateManager");
        string token = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
        const string duplicate = "Bu e-posta adresiyle kayıtlı bir kullanıcı zaten bulunuyor.";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["firstName"] = "Ayşe", ["lastName"] = "Yılmaz", ["email"] = " Existing@Company.com ",
            ["initialPassword"] = "SecretPass!2026", ["confirmPassword"] = "SecretPass!2026", ["isActive"] = "true",
            ["__RequestVerificationToken"] = token
        });

        string html = WebUtility.HtmlDecode(await (await client.PostAsync($"/Admin/Departments/{departmentId}/CreateManager", form)).Content.ReadAsStringAsync());
        Assert.Single(Regex.Matches(html, Regex.Escape(duplicate)).Cast<Match>());
        Assert.Contains("value=\"Ayşe\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"Yılmaz\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\" Existing@Company.com \"", html, StringComparison.Ordinal);
        Assert.Contains("aria-invalid=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("İşlem mevcut durumla çelişiyor", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"SecretPass!2026\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("technical detail", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Department_edit_should_not_depend_on_eligible_manager_lookup()
    {
        Guid departmentId = Guid.NewGuid();
        factory.AdminDepartments.StatusCode = HttpStatusCode.OK;
        factory.AdminDepartments.Detail = new(departmentId, "Finans", null, null, 0, 0, DateTimeOffset.UtcNow);
        factory.AdminDepartments.EligibleStatusCode = HttpStatusCode.NotFound;
        int callsBefore = factory.AdminDepartments.EligibleManagerCalls;
        using HttpClient client = await LoginAsync();

        using HttpResponseMessage response = await client.GetAsync($"/Admin/Departments/{departmentId}/Edit");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("value=\"Finans\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Talep bulunamadı", VisibleText(html), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(callsBefore, factory.AdminDepartments.EligibleManagerCalls);
    }

    [Fact]
    public async Task Missing_department_should_render_entity_aware_not_found_for_detail_and_edit()
    {
        Guid departmentId = Guid.NewGuid();
        factory.AdminDepartments.Detail = null;
        factory.AdminDepartments.StatusCode = HttpStatusCode.NotFound;
        factory.AdminDepartments.EligibleStatusCode = HttpStatusCode.OK;
        using HttpClient client = await LoginAsync();

        foreach (string path in new[] { $"/Admin/Departments/{departmentId}", $"/Admin/Departments/{departmentId}/Edit" })
        {
            string visible = VisibleText(await client.GetStringAsync(path));
            Assert.Contains("Departman bulunamadı", visible, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Departmanlara Dön", visible, StringComparison.Ordinal);
            Assert.DoesNotContain("Talep bulunamadı", visible, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(departmentId.ToString(), visible, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task<HttpClient> LoginAsync()
    {
        factory.Authentication.Result = new A.AuthenticationContract
        {
            AccessToken = AuthenticationWebFactory.AccessToken,
            RefreshToken = AuthenticationWebFactory.RefreshToken,
            AccessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10),
            RefreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            User = new A.AuthenticatedUserContract { Id = Guid.NewGuid(), FirstName = "Test", LastName = "Admin", Email = "admin@example.test", Role = "Admin" }
        };
        HttpClient client = factory.CreateSecureClient();
        string login = await client.GetStringAsync("/account/login");
        string token = WebUtility.HtmlDecode(Regex.Match(login, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["Email"] = "admin@example.test", ["Password"] = "password", ["__RequestVerificationToken"] = token });
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/account/login", form)).StatusCode);
        return client;
    }

    private static string VisibleText(string html)
    {
        string withoutCode = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return WebUtility.HtmlDecode(Regex.Replace(withoutCode, "<[^>]+>", " "));
    }
}
