extern alias webapp;
using System.Net;
using System.Text.RegularExpressions;
using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
namespace CorporateRequestManagement.WebTests;

public sealed class Phase6WebTests(AuthenticationWebFactory factory) : IClassFixture<AuthenticationWebFactory>
{
    [Fact]
    public async Task Login_should_use_product_identity_without_visible_template_branding_or_tokens()
    {
        using HttpClient client = factory.CreateSecureClient(); string html = WebUtility.HtmlDecode(await client.GetStringAsync("/account/login"));
        Assert.Contains("Talep süreçlerinizi tek merkezden, güvenle yönetin.", html, StringComparison.Ordinal);
        Assert.Contains("Hoş geldiniz", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Tekrar hoş geldiniz", html, StringComparison.Ordinal);
        Assert.DoesNotContain("tarayıcıda token", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kurumsal Talep Yönetimi", html, StringComparison.Ordinal);
        Assert.Contains("/css/crm-ui.css", html, StringComparison.Ordinal);
        Assert.Contains("/favicon.svg", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Metronic", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("KeenThemes", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AuthenticationWebFactory.AccessToken, html, StringComparison.Ordinal);
        Assert.DoesNotContain(AuthenticationWebFactory.RefreshToken, html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/css/crm-ui.css")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/js/crm-ui.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/favicon.svg")).StatusCode);
    }

    [Theory]
    [InlineData("Employee", true, false, false)]
    [InlineData("Manager", true, true, false)]
    [InlineData("Admin", false, false, true)]
    public async Task Authenticated_shell_should_render_exact_role_navigation(string role, bool selfServiceMenu, bool managerMenu, bool adminMenu)
    {
        factory.Authentication.Result = AuthenticationFor(role); using HttpClient client = factory.CreateSecureClient();
        string token = await Antiforgery(client); using var body = new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","user@example.test"},{"Password","password"},{"__RequestVerificationToken",token}});
        using HttpResponseMessage login = await client.PostAsync("/account/login", body); Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        string html = await client.GetStringAsync("/");
        Assert.Equal(selfServiceMenu, html.Contains("Taleplerim", StringComparison.Ordinal));
        Assert.Equal(selfServiceMenu, html.Contains("Yeni Talep", StringComparison.Ordinal));
        Assert.Equal(managerMenu, html.Contains("Yönetici Onayları", StringComparison.Ordinal));
        Assert.Equal(adminMenu, html.Contains("Yönetim Onayları", StringComparison.Ordinal));
        Assert.DoesNotContain(AuthenticationWebFactory.AccessToken, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_self_service_routes_should_redirect_to_access_denied_and_hide_create_actions()
    {
        factory.Authentication.Result=AuthenticationFor("Admin");using HttpClient client=factory.CreateSecureClient();await Login(client);
        string dashboard=WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        Assert.DoesNotContain("Taleplerim",dashboard,StringComparison.Ordinal);Assert.DoesNotContain("Yeni Talep",dashboard,StringComparison.Ordinal);

        foreach(string path in new[]{"/Requests","/Requests/Create",$"/Requests/{Guid.NewGuid()}",$"/Requests/{Guid.NewGuid()}/Edit"})
        {
            using HttpResponseMessage response=await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Equal("/account/access-denied",response.Headers.Location?.AbsolutePath);
        }

        foreach(string path in new[]{"/Requests/Create",$"/Requests/{Guid.NewGuid()}/Edit",$"/Requests/{Guid.NewGuid()}/Submit",$"/Requests/{Guid.NewGuid()}/Cancel"})
        {
            using HttpResponseMessage response=await client.PostAsync(path,new FormUrlEncodedContent([]));
            Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Equal("/account/access-denied",response.Headers.Location?.AbsolutePath);
        }
    }

    [Fact]
    public async Task Mutation_routes_should_require_antiforgery()
    {
        factory.Authentication.Result = AuthenticationFor("Employee"); using HttpClient client = factory.CreateSecureClient();
        string token = await Antiforgery(client); using var loginBody = new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","user@example.test"},{"Password","password"},{"__RequestVerificationToken",token}}); await client.PostAsync("/account/login",loginBody);
        foreach(string path in new[]{"/Requests/Create",$"/Requests/{Guid.NewGuid()}/Submit",$"/Requests/{Guid.NewGuid()}/Cancel"})
        { using HttpResponseMessage response=await client.PostAsync(path,new FormUrlEncodedContent([]));Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode); }
    }
    [Theory]
    [InlineData("Employee", "/Approvals/Manager")]
    [InlineData("Manager", "/Approvals/Admin")]
    [InlineData("Admin", "/Approvals/Manager")]
    public async Task Wrong_role_routes_should_redirect_to_safe_access_denied(string role, string path)
    {
        factory.Authentication.Result=AuthenticationFor(role);using HttpClient client=factory.CreateSecureClient();string token=await Antiforgery(client);
        using var body=new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","user@example.test"},{"Password","password"},{"__RequestVerificationToken",token}});await client.PostAsync("/account/login",body);
        using HttpResponseMessage response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Equal("/account/access-denied",response.Headers.Location?.AbsolutePath);
    }
    [Fact]
    public void Display_mapper_should_translate_known_values_and_safely_handle_unknown_values()
    {
        Assert.Equal("Yönetici Onayı Bekliyor",webapp::CorporateRequestManagement.Web.Models.UiDisplay.Status(webapp::CorporateRequestManagement.Web.Api.RequestStatusContract.PendingManagerApproval).Label);
        Assert.Equal("Bilinmiyor",webapp::CorporateRequestManagement.Web.Models.UiDisplay.Status((webapp::CorporateRequestManagement.Web.Api.RequestStatusContract)999).Label);
        Assert.Equal("Bilinmiyor",webapp::CorporateRequestManagement.Web.Models.UiDisplay.Type((webapp::CorporateRequestManagement.Web.Api.RequestTypeContract)999));
    }

    [Theory]
    [InlineData("2026-08-03", "03.08", "03.08.2026")]
    [InlineData("2026-07-31", "31.07", "31.07.2026")]
    [InlineData("2026-01-01", "01.01", "01.01.2026")]
    [InlineData("2025-12-31", "31.12", "31.12.2025")]
    public void Daily_period_formatter_should_use_canonical_key_for_axis_and_tooltip(string key,string axis,string tooltip)
    {
        Assert.True(webapp::CorporateRequestManagement.Web.Models.UiDisplay.TryDailyPeriodLabels(key,out string actualAxis,out string actualTooltip));Assert.Equal(axis,actualAxis);Assert.Equal(tooltip,actualTooltip);
    }

    [Theory]
    [InlineData("2025-09", "Eyl 25", "Eylül 2025")]
    [InlineData("2025-12", "Ara 25", "Aralık 2025")]
    [InlineData("2026-01", "Oca 26", "Ocak 2026")]
    public void Monthly_period_formatter_should_use_turkish_axis_and_tooltip(string key,string axis,string tooltip)
    {
        Assert.True(webapp::CorporateRequestManagement.Web.Models.UiDisplay.TryMonthlyPeriodLabels(key,out string actualAxis,out string actualTooltip));Assert.Equal(axis,actualAxis);Assert.Equal(tooltip,actualTooltip);
    }

    [Fact]
    public void Trend_mapping_should_reject_wrong_granularity_and_return_safe_invalid_state_without_throwing()
    {
        Assert.False(webapp::CorporateRequestManagement.Web.Models.UiDisplay.TryDailyPeriodLabels("2025-09",out _,out _));Assert.False(webapp::CorporateRequestManagement.Web.Models.UiDisplay.TryMonthlyPeriodLabels("2026-08-03",out _,out _));
        var points=new[]{new webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract("geçersiz",1,0)};var model=webapp::CorporateRequestManagement.Web.Models.Dashboard.DashboardTrendViewModel.Create(points,"Grafik","Talep","",webapp::CorporateRequestManagement.Web.Models.Dashboard.DashboardTrendGranularity.Daily);Assert.False(model.IsValid);Assert.Empty(model.Points);
    }

    [Fact]
    public async Task Admin_dashboard_should_render_safe_chart_state_instead_of_exception_for_malformed_monthly_contract()
    {
        IReadOnlyList<webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract> original=factory.Dashboard.AdminTrend;
        try
        {
            factory.Dashboard.AdminTrend=[new("geçersiz",1,0)];factory.Authentication.Result=AuthenticationFor("Admin");using HttpClient client=factory.CreateSecureClient();await Login(client);using HttpResponseMessage response=await client.GetAsync("/");string html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("Grafik verisi şu anda görüntülenemiyor.",html,StringComparison.Ordinal);Assert.DoesNotContain("FormatException",html,StringComparison.Ordinal);Assert.DoesNotContain("Developer Exception",html,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("geçersiz",html,StringComparison.Ordinal);
        }
        finally { factory.Dashboard.AdminTrend=original; }
    }

    [Fact]
    public async Task Turkish_culture_create_and_edit_get_should_render_decimal_input_without_range_conversion_failure()
    {
        factory.Authentication.Result=AuthenticationFor("Employee");Guid id=Guid.NewGuid();factory.Requests.Detail=new(id,"Taslak talep","Açıklama",webapp::CorporateRequestManagement.Web.Api.RequestTypeContract.Expense,17500.50m,webapp::CorporateRequestManagement.Web.Api.RequestStatusContract.Draft,Guid.NewGuid(),"Operasyon",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null,null,"AQIDBAUGBwg=");
        using HttpClient client=factory.CreateSecureClient();await Login(client);CultureInfo prior=CultureInfo.CurrentCulture;CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo("tr-TR");
        using HttpResponseMessage create=await client.GetAsync("/Requests/Create",new CancellationToken());string createHtml=WebUtility.HtmlDecode(await create.Content.ReadAsStringAsync());
        using HttpResponseMessage edit=await client.GetAsync($"/Requests/{id}/Edit",new CancellationToken());string editHtml=WebUtility.HtmlDecode(await edit.Content.ReadAsStringAsync());CultureInfo.CurrentCulture=prior;
        Assert.Equal(HttpStatusCode.OK,create.StatusCode);Assert.Equal(HttpStatusCode.OK,edit.StatusCode);
        Assert.Contains("inputmode=\"decimal\"",createHtml,StringComparison.Ordinal);Assert.Contains("step=\"0.01\"",createHtml,StringComparison.Ordinal);
        Assert.Contains("name=\"Amount\"",editHtml,StringComparison.Ordinal);Assert.DoesNotContain("data-val-range",editHtml,StringComparison.OrdinalIgnoreCase);
        foreach(string html in new[]{createHtml,editHtml}){Assert.Contains("crm-form-page",html,StringComparison.Ordinal);Assert.Contains("crm-form-page__container",html,StringComparison.Ordinal);Assert.Contains("crm-form-page__heading",html,StringComparison.Ordinal);Assert.Contains("crm-form-card--premium",html,StringComparison.Ordinal);Assert.Matches("crm-form-page__container[\\s\\S]*crm-form-page__heading[\\s\\S]*crm-form-card--premium",html);Assert.Contains("__RequestVerificationToken",html,StringComparison.Ordinal);Assert.Contains("data-submit-form",html,StringComparison.Ordinal);}
    }
    [Theory]
    [InlineData(null,true)]
    [InlineData("0",false)]
    [InlineData("-0.01",false)]
    [InlineData("0.01",true)]
    [InlineData("17500.50",true)]
    public void Amount_validation_should_be_nullable_positive_and_culture_independent(string? text,bool expected)
    {
        decimal? amount=text is null?null:decimal.Parse(text,CultureInfo.InvariantCulture);var model=new webapp::CorporateRequestManagement.Web.Models.RequestInputModel{Amount=amount};var context=new ValidationContext(model){MemberName=nameof(model.Amount)};var results=new List<ValidationResult>();
        Assert.Equal(expected,Validator.TryValidateProperty(model.Amount,context,results));if(!expected)Assert.Contains(results,x=>x.ErrorMessage=="Tutar sıfırdan büyük olmalıdır.");
    }

    [Fact]
    public async Task Employee_dashboard_and_request_list_should_render_polished_actions_filters_and_semantic_table()
    {
        factory.Authentication.Result=AuthenticationFor("Employee");Guid draftId=Guid.NewGuid(),submittedId=Guid.NewGuid();DateTimeOffset now=DateTimeOffset.UtcNow;
        factory.Requests.Items=[new(draftId,"<script>alert(1)</script>",webapp::CorporateRequestManagement.Web.Api.RequestTypeContract.Expense,1500,webapp::CorporateRequestManagement.Web.Api.RequestStatusContract.Draft,now,now,null,"AQIDBAUGBwg="),new(submittedId,"Gönderilen talep",webapp::CorporateRequestManagement.Web.Api.RequestTypeContract.Leave,null,webapp::CorporateRequestManagement.Web.Api.RequestStatusContract.PendingManagerApproval,now,now,now,"AQIDBAUGBwg=")];factory.Requests.TotalCount=2;
        using HttpClient client=factory.CreateSecureClient();await Login(client);string dashboard=WebUtility.HtmlDecode(await client.GetStringAsync("/"));string rawList=await client.GetStringAsync("/Requests?Status=Draft");string list=WebUtility.HtmlDecode(rawList);
        Assert.Contains("crm-recent-card",dashboard,StringComparison.Ordinal);Assert.Single(Regex.Matches(dashboard,"Yeni Talep Oluştur"));Assert.Single(Regex.Matches(dashboard,"Taleplerimi Görüntüle"));
        Assert.Contains("<th scope=\"col\" class=\"crm-text-end\">İşlemler</th>",list,StringComparison.Ordinal);Assert.Contains("data-crm-filter-toggle",list,StringComparison.Ordinal);Assert.Contains("aria-expanded=\"true\"",list,StringComparison.Ordinal);
        Assert.Collection(Regex.Matches(list,">Detay<").Cast<Match>(),_=>{},_=>{});Assert.Single(Regex.Matches(list,">Düzenle<"));Assert.Contains("1 / 1",list,StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>",rawList,StringComparison.Ordinal);Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;",rawList,StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dashboard_should_follow_locked_composition_and_never_render_raw_status_or_role_names()
    {
        factory.Authentication.Result=AuthenticationFor("Employee");using HttpClient client=factory.CreateSecureClient();await Login(client);string html=WebUtility.HtmlDecode(await client.GetStringAsync("/"));string chartScript=await client.GetStringAsync("/js/crm-dashboard-charts.js");
        Assert.Contains("crm-dashboard-top",html,StringComparison.Ordinal);Assert.Contains("crm-dashboard-hero",html,StringComparison.Ordinal);Assert.Contains("crm-period-card",html,StringComparison.Ordinal);Assert.Contains("crm-kpi-grid",html,StringComparison.Ordinal);Assert.Contains("crm-analytics-grid",html,StringComparison.Ordinal);
        Assert.Contains("Son 30 Gün Talep Grafiği",html,StringComparison.Ordinal);Assert.Contains("Günlük talep adetleri",html,StringComparison.Ordinal);Assert.Contains("data-request-chart",html,StringComparison.Ordinal);Assert.Contains("data-request-chart-data",html,StringComparison.Ordinal);Assert.Contains("data-distribution-chart",html,StringComparison.Ordinal);Assert.Contains("crm-distribution-breakdown",html,StringComparison.Ordinal);
        Assert.Matches("/js/crm-ui\\.js\\?v=[^\"]+",html);Assert.Matches("/js/crm-dashboard-charts\\.js\\?v=[^\"]+",html);
        Assert.Contains("type: \"area\"",chartScript,StringComparison.Ordinal);Assert.Contains("type: \"donut\"",chartScript,StringComparison.Ordinal);Assert.Contains("row.axisLabel",chartScript,StringComparison.Ordinal);Assert.Contains("tooltipLabel",chartScript,StringComparison.Ordinal);Assert.Contains("`${name}: `",chartScript,StringComparison.Ordinal);Assert.DoesNotContain("Satış",chartScript,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("Bilin",chartScript,StringComparison.Ordinal);
        Assert.Contains("Number.isFinite(value)",chartScript,StringComparison.Ordinal);Assert.Contains("try { renderRequestChart(element); }",chartScript,StringComparison.Ordinal);Assert.Contains("try { renderDistributionChart(element); }",chartScript,StringComparison.Ordinal);Assert.DoesNotContain("fetch(",chartScript,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("axios",chartScript,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@if",html,StringComparison.Ordinal);Assert.DoesNotContain("string.IsNullOrEmpty",html,StringComparison.Ordinal);
        Assert.DoesNotContain("Satış",html,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("Gerçek SQL",html,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("database data",html,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("backend data",html,StringComparison.OrdinalIgnoreCase);
        foreach(string value in new[]{"Draft","PendingManagerApproval","PendingAdminApproval","Approved","Rejected","Cancelled","Employee"})Assert.DoesNotContain(value,html,StringComparison.Ordinal);
        foreach(string value in new[]{"Taslak","Yönetici Onayı Bekliyor","Yönetim Onayı Bekliyor","Onaylandı","Reddedildi","İptal Edildi","Çalışan"})Assert.Contains(value,html,StringComparison.Ordinal);
        Match payload=Regex.Match(html,"<script type=\"application/json\" data-request-chart-data>(.*?)</script>",RegexOptions.Singleline);Assert.True(payload.Success);using JsonDocument chart=JsonDocument.Parse(payload.Groups[1].Value);JsonElement root=chart.RootElement;
        Assert.Equal(30,root.GetProperty("categories").GetArrayLength());Assert.Equal(30,root.GetProperty("axisLabels").GetArrayLength());Assert.Equal(30,root.GetProperty("tooltipLabels").GetArrayLength());Assert.Equal("05.07",root.GetProperty("axisLabels")[0].GetString());Assert.Equal("03.08",root.GetProperty("axisLabels")[29].GetString());Assert.Equal("03.08.2026",root.GetProperty("tooltipLabels")[29].GetString());foreach(JsonElement series in root.GetProperty("series").EnumerateArray())Assert.Equal(30,series.GetProperty("data").GetArrayLength());Assert.DoesNotContain("Bilin",payload.Groups[1].Value,StringComparison.Ordinal);Assert.DoesNotContain("Invalid Date",payload.Groups[1].Value,StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_list_should_render_safe_title_links_filter_footer_and_query_preserving_pagination()
    {
        factory.Authentication.Result=AuthenticationFor("Employee");Guid id=Guid.NewGuid();DateTimeOffset now=DateTimeOffset.UtcNow;
        factory.Requests.Items=[new(id,"<b>Güvenli başlık</b>",webapp::CorporateRequestManagement.Web.Api.RequestTypeContract.Other,null,webapp::CorporateRequestManagement.Web.Api.RequestStatusContract.Approved,now,now,now,"AQIDBAUGBwg=")];factory.Requests.TotalCount=205;
        using HttpClient client=factory.CreateSecureClient();await Login(client);string raw=await client.GetStringAsync("/Requests?PageNumber=6&PageSize=20&Status=Approved&RequestType=Other&CreatedFrom=2026-07-01&CreatedTo=2026-07-31&SortBy=title&SortDirection=Ascending");string html=WebUtility.HtmlDecode(raw);
        Assert.Equal(6,factory.Requests.LastQuery?.PageNumber);Assert.Equal(20,factory.Requests.LastQuery?.PageSize);Assert.Equal(webapp::CorporateRequestManagement.Web.Api.RequestStatusContract.Approved,factory.Requests.LastQuery?.Status);
        Assert.Contains($"href=\"/Requests/{id}\"",html,StringComparison.Ordinal);Assert.DoesNotContain("<b>Güvenli başlık</b>",raw,StringComparison.Ordinal);Assert.Contains("&lt;b&gt;",raw,StringComparison.Ordinal);Assert.Contains("&lt;/b&gt;",raw,StringComparison.Ordinal);
        Assert.Contains("crm-filter-footer",html,StringComparison.Ordinal);Assert.Contains("crm-btn crm-btn-secondary\" href=\"/Requests\">Temizle",html,StringComparison.Ordinal);Assert.Contains("crm-btn crm-btn-primary\" type=\"submit\">Filtrele",html,StringComparison.Ordinal);
        Assert.Contains("Sayfa başına",html,StringComparison.Ordinal);foreach(int size in new[]{10,20,50,100})Assert.Contains($"value=\"{size}\"",html,StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\">6",html,StringComparison.Ordinal);Assert.Contains(">Önceki<",html,StringComparison.Ordinal);Assert.Contains(">Sonraki<",html,StringComparison.Ordinal);Assert.Contains(">…<",html,StringComparison.Ordinal);Assert.DoesNotContain("0 / 1",html,StringComparison.Ordinal);
        Assert.Contains("Status=4",html,StringComparison.Ordinal);Assert.Contains("RequestType=Other",html,StringComparison.Ordinal);Assert.Contains("SortBy=title",html,StringComparison.Ordinal);Assert.Contains("SortDirection=Ascending",html,StringComparison.Ordinal);Assert.Contains("name=\"PageNumber\" value=\"1\"",html,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Manager", "/Approvals/Manager", false)]
    [InlineData("Admin", "/Approvals/Admin", true)]
    public async Task Approval_queues_should_forward_normalized_server_paging_and_render_shared_navigation(string role,string path,bool admin)
    {
        factory.Authentication.Result=AuthenticationFor(role);Guid id=Guid.NewGuid();DateTimeOffset now=DateTimeOffset.UtcNow;
        factory.Approvals.Items=[new(id,"Onay talebi",webapp::CorporateRequestManagement.Web.Api.RequestTypeContract.Expense,1250,webapp::CorporateRequestManagement.Web.Api.RequestStatusContract.PendingManagerApproval,"Talep Sahibi",Guid.NewGuid(),"Operasyon",now,now,"AQIDBAUGBwg=")];factory.Approvals.TotalCount=55;
        using HttpClient client=factory.CreateSecureClient();await Login(client);string html=WebUtility.HtmlDecode(await client.GetStringAsync(path+"?SearchTerm=talep&PageNumber=2&PageSize=10&RequestType=Expense&CreatedFrom=2026-07-01&SortBy=amount&SortDirection=Descending"));
        Assert.Equal(admin,factory.Approvals.LastAdmin);Assert.Equal("talep",factory.Approvals.LastQuery?.SearchTerm);Assert.Equal(2,factory.Approvals.LastQuery?.PageNumber);Assert.Equal(10,factory.Approvals.LastQuery?.PageSize);Assert.Equal(webapp::CorporateRequestManagement.Web.Api.RequestTypeContract.Expense,factory.Approvals.LastQuery?.RequestType);
        Assert.Contains("Sayfa başına",html,StringComparison.Ordinal);Assert.Contains("aria-current=\"page\">2",html,StringComparison.Ordinal);Assert.Contains(">Önceki<",html,StringComparison.Ordinal);Assert.Contains(">Sonraki<",html,StringComparison.Ordinal);Assert.Contains("SearchTerm=talep",html,StringComparison.Ordinal);Assert.Contains("name=\"SearchTerm\" value=\"talep\"",html,StringComparison.Ordinal);Assert.Contains("RequestType=Expense",html,StringComparison.Ordinal);Assert.Contains("SortBy=amount",html,StringComparison.Ordinal);Assert.Contains("name=\"PageNumber\" value=\"1\"",html,StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html,"data-crm-filter-toggle"));Assert.Contains("type=\"button\" data-crm-filter-toggle",html,StringComparison.Ordinal);Assert.Single(Regex.Matches(html,"id=\"approval-filters\""));Assert.Contains("aria-controls=\"approval-filters\"",html,StringComparison.Ordinal);Assert.Contains("aria-expanded=\"true\"",html,StringComparison.Ordinal);Assert.DoesNotContain("hidden=\"hidden\"",html,StringComparison.Ordinal);
    }

    [Fact]
    public async Task Shared_filter_script_should_resolve_each_panel_from_its_trigger_contract_without_duplicate_listener()
    {
        using HttpClient client=factory.CreateSecureClient();string script=await client.GetStringAsync("/js/crm-ui.js");
        Assert.Contains("closest(\"[data-crm-filter-toggle]\")",script,StringComparison.Ordinal);Assert.Contains("getAttribute(\"aria-controls\")",script,StringComparison.Ordinal);Assert.Contains("getElementById(panelId)",script,StringComparison.Ordinal);
        Assert.DoesNotContain("getElementById(\"request-filters\")",script,StringComparison.Ordinal);Assert.Single(Regex.Matches(script,Regex.Escape("closest(\"[data-crm-filter-toggle]\")")));
    }

    [Theory]
    [InlineData("Employee", "/Requests", "request-filters")]
    [InlineData("Manager", "/Approvals/Manager", "approval-filters")]
    [InlineData("Admin", "/Approvals/Admin", "approval-filters")]
    public async Task List_filters_should_start_closed_with_unique_matching_trigger_and_panel_ids(string role,string path,string panelId)
    {
        factory.Authentication.Result=AuthenticationFor(role);using HttpClient client=factory.CreateSecureClient();await Login(client);string html=WebUtility.HtmlDecode(await client.GetStringAsync(path));
        Assert.Single(Regex.Matches(html,"data-crm-filter-toggle"));Assert.Contains("type=\"button\" data-crm-filter-toggle",html,StringComparison.Ordinal);Assert.Single(Regex.Matches(html,$"id=\"{panelId}\""));Assert.Contains($"aria-controls=\"{panelId}\"",html,StringComparison.Ordinal);Assert.Contains("aria-expanded=\"false\"",html,StringComparison.Ordinal);Assert.Contains($"id=\"{panelId}\" class=\"crm-filter-panel",html,StringComparison.Ordinal);Assert.Contains("hidden=\"hidden\"",html,StringComparison.Ordinal);string[] ids=Regex.Matches(html,"\\sid=\"([^\"]+)\"").Select(x=>x.Groups[1].Value).ToArray();Assert.Equal(ids.Length,ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("Employee", "Talep", null, 30, "05.07", "03.08.2026")]
    [InlineData("Manager", "Talep", "Karar", 30, "05.07", "03.08.2026")]
    [InlineData("Admin", "Oluşturulan", "Tamamlanan", 12, "Eyl 25", "Ağustos 2026")]
    public async Task Role_dashboards_should_render_aligned_period_payload_and_turkish_series(string role,string primary,string? secondary,int expectedCount,string firstAxis,string lastTooltip)
    {
        factory.Authentication.Result=AuthenticationFor(role);using HttpClient client=factory.CreateSecureClient();await Login(client);using HttpResponseMessage response=await client.GetAsync("/");string html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.DoesNotContain("FormatException",html,StringComparison.Ordinal);Assert.DoesNotContain("Developer Exception",html,StringComparison.OrdinalIgnoreCase);Match payload=Regex.Match(html,"<script type=\"application/json\" data-request-chart-data>(.*?)</script>",RegexOptions.Singleline);Assert.True(payload.Success);using JsonDocument chart=JsonDocument.Parse(payload.Groups[1].Value);JsonElement root=chart.RootElement;
        Assert.Equal(expectedCount,root.GetProperty("categories").GetArrayLength());Assert.Equal(expectedCount,root.GetProperty("axisLabels").GetArrayLength());Assert.Equal(expectedCount,root.GetProperty("tooltipLabels").GetArrayLength());Assert.Equal(firstAxis,root.GetProperty("axisLabels")[0].GetString());Assert.Equal(lastTooltip,root.GetProperty("tooltipLabels")[expectedCount-1].GetString());JsonElement.ArrayEnumerator series=root.GetProperty("series").EnumerateArray();List<JsonElement> values=series.ToList();Assert.Equal(secondary is null?1:2,values.Count);Assert.Equal(primary,values[0].GetProperty("name").GetString());if(secondary is not null)Assert.Equal(secondary,values[1].GetProperty("name").GetString());Assert.All(values,x=>Assert.Equal(expectedCount,x.GetProperty("data").GetArrayLength()));Assert.DoesNotContain("Bilin",payload.Groups[1].Value,StringComparison.Ordinal);Assert.DoesNotContain("Invalid Date",payload.Groups[1].Value,StringComparison.Ordinal);Assert.DoesNotContain(AuthenticationWebFactory.AccessToken,html,StringComparison.Ordinal);Assert.DoesNotContain(AuthenticationWebFactory.RefreshToken,html,StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Employee", 1)]
    [InlineData("Manager", 2)]
    public async Task Zero_filled_daily_dashboard_should_render_valid_chart_payload_instead_of_fallback(string role,int expectedSeries)
    {
        IReadOnlyList<webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract> zeroTrend=Enumerable.Range(0,30).Select(i=>new webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract(new DateOnly(2026,7,5).AddDays(i).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),0,0)).ToArray();
        factory.Dashboard.EmployeeTrend=zeroTrend;factory.Dashboard.ManagerTrend=zeroTrend;
        try
        {
            factory.Authentication.Result=AuthenticationFor(role);using HttpClient client=factory.CreateSecureClient();await Login(client);string html=WebUtility.HtmlDecode(await client.GetStringAsync("/"));
            Assert.DoesNotContain("Grafik verisi şu anda görüntülenemiyor.",html,StringComparison.Ordinal);Assert.Contains("data-request-chart",html,StringComparison.Ordinal);Match payload=Regex.Match(html,"<script type=\"application/json\" data-request-chart-data>(.*?)</script>",RegexOptions.Singleline);Assert.True(payload.Success);using JsonDocument chart=JsonDocument.Parse(payload.Groups[1].Value);JsonElement root=chart.RootElement;Assert.Equal(30,root.GetProperty("categories").GetArrayLength());Assert.Equal(expectedSeries,root.GetProperty("series").GetArrayLength());Assert.All(root.GetProperty("series").EnumerateArray(),series=>Assert.All(series.GetProperty("data").EnumerateArray(),value=>Assert.Equal(0,value.GetInt32())));
        }
        finally
        {
            factory.Dashboard.EmployeeTrend=Enumerable.Range(0,30).Select(i=>new webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract(new DateOnly(2026,7,5).AddDays(i).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),i==0?1:0,0)).ToArray();factory.Dashboard.ManagerTrend=factory.Dashboard.EmployeeTrend;
        }
    }

    [Fact]
    public void Daily_trend_mapping_should_require_exactly_thirty_aligned_points()
    {
        webapp::CorporateRequestManagement.Web.Models.Dashboard.DashboardTrendViewModel model=webapp::CorporateRequestManagement.Web.Models.Dashboard.DashboardTrendViewModel.Create(Enumerable.Range(0,29).Select(i=>new webapp::CorporateRequestManagement.Web.Api.DashboardTrendPointContract(new DateOnly(2026,7,6).AddDays(i).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),0,0)).ToArray(),"Grafik","Talep","",webapp::CorporateRequestManagement.Web.Models.Dashboard.DashboardTrendGranularity.Daily);Assert.False(model.IsValid);Assert.Empty(model.Points);
    }

    [Fact]
    public async Task Invalid_web_paging_should_use_safe_one_based_defaults_and_sidebar_should_have_new_request_icon()
    {
        factory.Authentication.Result=AuthenticationFor("Employee");using HttpClient client=factory.CreateSecureClient();await Login(client);
        string html=WebUtility.HtmlDecode(await client.GetStringAsync("/Requests?PageNumber=0&PageSize=999"));Assert.Equal(1,factory.Requests.LastQuery?.PageNumber);Assert.Equal(20,factory.Requests.LastQuery?.PageSize);
        string dashboard=WebUtility.HtmlDecode(await client.GetStringAsync("/"));string css=await client.GetStringAsync("/css/crm-ui.css");Assert.Contains("crm-nav-icon",dashboard,StringComparison.Ordinal);Assert.Contains("focusable=\"false\"",dashboard,StringComparison.Ordinal);Assert.Contains(">Yeni Talep</a>",dashboard,StringComparison.Ordinal);
        Assert.Single(Regex.Matches(dashboard,"Yeni Talep Oluştur"));Assert.Contains("crm-btn crm-btn-hero",dashboard,StringComparison.Ordinal);
        Assert.Contains(".crm-section-head .crm-btn-primary:visited",css,StringComparison.Ordinal);Assert.Contains("color:#fff",css,StringComparison.Ordinal);Assert.DoesNotContain("a:visited{",css,StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_search_should_render_encode_forward_and_preserve_value_across_filters_and_paging()
    {
        factory.Authentication.Result=AuthenticationFor("Employee");Guid id=Guid.NewGuid();DateTimeOffset now=DateTimeOffset.UtcNow;
        factory.Requests.Items=[new(id,"Arama sonucu",webapp::CorporateRequestManagement.Web.Api.RequestTypeContract.Expense,100,webapp::CorporateRequestManagement.Web.Api.RequestStatusContract.Draft,now,now,null,"AQIDBAUGBwg=")];factory.Requests.TotalCount=45;
        using HttpClient client=factory.CreateSecureClient();await Login(client);
        string raw=await client.GetStringAsync("/Requests?SearchTerm=%20%3Cscript%3Earama%3C%2Fscript%3E%20&Status=Draft&PageNumber=2&PageSize=20");string html=WebUtility.HtmlDecode(raw);
        Assert.Equal("<script>arama</script>",factory.Requests.LastQuery?.SearchTerm);Assert.Equal(2,factory.Requests.LastQuery?.PageNumber);
        Assert.Contains("role=\"search\"",html,StringComparison.Ordinal);Assert.Contains("placeholder=\"Talep başlığı veya açıklamasında ara\"",html,StringComparison.Ordinal);Assert.Contains("aria-label=\"Talep ara\"",html,StringComparison.Ordinal);
        int toolbar=html.IndexOf("class=\"crm-request-toolbar\"",StringComparison.Ordinal),search=html.IndexOf("crm-request-toolbar__search",toolbar,StringComparison.Ordinal),actions=html.IndexOf("crm-request-toolbar__actions",toolbar,StringComparison.Ordinal),filters=html.IndexOf(">Filtreler",actions,StringComparison.Ordinal),create=html.IndexOf(">Yeni Talep</a>",actions,StringComparison.Ordinal);Assert.True(toolbar>=0&&search>toolbar&&actions>search&&filters>actions&&create>filters);Assert.DoesNotContain("crm-request-tools",html,StringComparison.Ordinal);
        Assert.DoesNotContain("<script>arama</script>",raw,StringComparison.Ordinal);Assert.Contains("&lt;script&gt;",raw,StringComparison.Ordinal);
        Assert.Contains("name=\"SearchTerm\" value=\"<script>arama</script>\"",html,StringComparison.Ordinal);Assert.Contains("Status=1",html,StringComparison.Ordinal);Assert.Contains("SearchTerm=%3Cscript%3Earama%3C%2Fscript%3E",raw,StringComparison.OrdinalIgnoreCase);Assert.Contains("name=\"PageNumber\" value=\"1\"",html,StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employee_layout_css_should_center_forms_and_use_responsive_single_toolbar_contract()
    {
        using HttpClient client=factory.CreateSecureClient();string css=await client.GetStringAsync("/css/crm-ui.css");
        Assert.Contains(".crm-form-page__container{width:100%;max-width:1120px;margin-inline:auto}",css,StringComparison.Ordinal);
        Assert.Contains(".crm-form-page .crm-form-card{width:100%;max-width:none}",css,StringComparison.Ordinal);
        Assert.Contains(".crm-request-toolbar{width:100%;display:flex;align-items:center;justify-content:space-between",css,StringComparison.Ordinal);
        Assert.Contains(".crm-request-toolbar__search{flex:1 1 560px;max-width:600px",css,StringComparison.Ordinal);
        Assert.Contains("@media(max-width:575px)",css,StringComparison.Ordinal);Assert.Contains(".crm-request-toolbar__actions{width:100%;display:grid;grid-template-columns:1fr 1fr}",css,StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_search_without_results_should_render_distinct_clearable_empty_state()
    {
        factory.Authentication.Result=AuthenticationFor("Employee");factory.Requests.Items=[];factory.Requests.TotalCount=0;
        using HttpClient client=factory.CreateSecureClient();await Login(client);using HttpResponseMessage response=await client.GetAsync("/Requests?SearchTerm=bulunamayan");string html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("bulunamayan",factory.Requests.LastQuery?.SearchTerm);Assert.Contains("Aramanızla eşleşen talep bulunamadı.",html,StringComparison.Ordinal);Assert.Contains("Arama ve filtreleri temizle",html,StringComparison.Ordinal);Assert.Contains("href=\"/Requests\"",html,StringComparison.Ordinal);Assert.Contains("<form method=\"get\" class=\"crm-search-form crm-request-toolbar__search\"",html,StringComparison.Ordinal);
    }
    private static async Task<string> Antiforgery(HttpClient client){string html=await client.GetStringAsync("/account/login");Match m=Regex.Match(html,"name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");return WebUtility.HtmlDecode(m.Groups[1].Value);}
    private static async Task Login(HttpClient client){string token=await Antiforgery(client);using var body=new FormUrlEncodedContent(new Dictionary<string,string>{{"Email","user@example.test"},{"Password","password"},{"__RequestVerificationToken",token}});using HttpResponseMessage response=await client.PostAsync("/account/login",body);Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);}
    private static webapp::CorporateRequestManagement.Web.Authentication.AuthenticationContract AuthenticationFor(string role)=>new(){AccessToken=AuthenticationWebFactory.AccessToken,AccessTokenExpiresAt=DateTimeOffset.UtcNow.AddMinutes(5),RefreshToken=AuthenticationWebFactory.RefreshToken,RefreshTokenExpiresAt=DateTimeOffset.UtcNow.AddDays(1),User=new(){Id=Guid.NewGuid(),FirstName="Test",LastName="Kullanıcı",Email="user@example.test",Role=role}};
}
