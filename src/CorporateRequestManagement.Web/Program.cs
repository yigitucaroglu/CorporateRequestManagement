using CorporateRequestManagement.Web.Authentication;
using CorporateRequestManagement.Web.Configuration;
using CorporateRequestManagement.Web.Api;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "__Host-Crm.Web.Session";
    options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax; options.Cookie.IsEssential = true; options.Cookie.Path = "/";
    options.IdleTimeout = TimeSpan.FromDays(7);
});
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-Crm.Web.Antiforgery"; options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.SameSite = SameSiteMode.Lax; options.Cookie.Path = "/";
});
builder.Services.AddAuthentication(WebAuthenticationDefaults.Scheme).AddCookie(WebAuthenticationDefaults.Scheme, options =>
{
    options.Cookie.Name = "__Host-Crm.Web.Auth"; options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax; options.Cookie.IsEssential = true; options.Cookie.Path = "/";
    options.LoginPath = "/account/login"; options.AccessDeniedPath = "/account/access-denied"; options.SlidingExpiration = false;
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Requests.SelfService", policy => policy.RequireAuthenticatedUser().RequireRole("Employee", "Manager"));
    options.AddPolicy("Admin.Exact", policy => policy.RequireAuthenticatedUser().RequireRole("Admin"));
    options.AddPolicy("Manager.Exact", policy => policy.RequireAuthenticatedUser().RequireRole("Manager"));
});
builder.Services.AddOptions<BackendApiOptions>().Bind(builder.Configuration.GetSection(BackendApiOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps), "BackendApi BaseUrl geçersiz.")
    .Validate(options => options.RequestTimeoutSeconds is >= 1 and <= 120, "BackendApi timeout geçersiz.").ValidateOnStart();
builder.Services.AddScoped<IUserTokenStore, SessionUserTokenStore>();
builder.Services.AddScoped<ILocalAuthenticationState, LocalAuthenticationState>();
builder.Services.AddSingleton<IRefreshCoordinator, RefreshCoordinator>();
builder.Services.AddHttpClient<IAuthenticationApiClient, AuthenticationApiClient>((provider, client) =>
{
    BackendApiOptions options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl); client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});
builder.Services.AddHttpClient<IAuthenticatedUserApiClient, AuthenticatedUserApiClient>((provider, client) =>
{
    BackendApiOptions options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl); client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});
builder.Services.AddHttpClient<IBackendApiGateway, BackendApiGateway>((provider, client) =>
{
    BackendApiOptions options = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<BackendApiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl); client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
});
builder.Services.AddScoped<IRequestsApiClient, RequestsApiClient>();
builder.Services.AddScoped<IApprovalsApiClient, ApprovalsApiClient>();
builder.Services.AddScoped<IDashboardApiClient, DashboardApiClient>();
builder.Services.AddScoped<IAdminUsersApiClient, AdminUsersApiClient>();
builder.Services.AddScoped<IAdminDepartmentsApiClient, AdminDepartmentsApiClient>();
builder.Services.AddScoped<IAdminRequestsApiClient, AdminRequestsApiClient>();
builder.Services.AddScoped<IManagerRequestsApiClient, ManagerRequestsApiClient>();
builder.Services.AddScoped<IAuditLogsApiClient, AuditLogsApiClient>();
builder.Services.AddScoped<IProfileApiClient, ProfileApiClient>();
builder.Services.AddScoped<IWebApiErrorMapper, WebApiErrorMapper>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/account/error");
app.UseStaticFiles(); app.UseRouting(); app.UseSession(); app.UseAuthentication(); app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");
app.Run();
public partial class Program;
