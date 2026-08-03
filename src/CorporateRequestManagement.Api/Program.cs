using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using CorporateRequestManagement.Api.Authentication;
using CorporateRequestManagement.Api.Errors;
using CorporateRequestManagement.Api.OpenApi;
using CorporateRequestManagement.Application;
using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Infrastructure;
using CorporateRequestManagement.Infrastructure.Authentication;
using CorporateRequestManagement.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
{
    var errors = context.ModelState.Where(x => x.Value?.Errors.Count > 0).ToDictionary(x => x.Key, x => x.Value!.Errors.Select(_ => "İstek alanı geçersiz.").ToArray());
    var problem = new ProblemDetails { Status = 400, Title = "Doğrulama hatası", Detail = "İstek doğrulaması başarısız.", Instance = context.HttpContext.Request.Path, Type = "https://httpstatuses.com/400" };
    problem.Extensions["code"] = "validation.failed"; problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier; problem.Extensions["errors"] = errors;
    return new BadRequestObjectResult(problem);
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();
builder.Services.AddScoped<JwtBearerEventsHandler>();
JwtOptions jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false; options.SaveToken = false; options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer, ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateLifetime = true, ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = JwtRegisteredClaimNames.Sub, RoleClaimType = ClaimTypes.Role
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = context => context.HttpContext.RequestServices.GetRequiredService<JwtBearerEventsHandler>().TokenValidated(context),
        OnChallenge = JwtBearerEventsHandler.Challenge,
        OnForbidden = JwtBearerEventsHandler.Forbidden
    };
});
builder.Services.AddAuthorization();
int authLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 5);
int generalLimit = builder.Configuration.GetValue("RateLimiting:GeneralPermitLimit", 100);
int windowSeconds = builder.Configuration.GetValue("RateLimiting:WindowSeconds", 60);
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = authLimit, Window = TimeSpan.FromSeconds(windowSeconds), QueueLimit = 0, AutoReplenishment = true }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = generalLimit, Window = TimeSpan.FromSeconds(windowSeconds), QueueLimit = 0, AutoReplenishment = true }));
    options.OnRejected = (context, _) => new ValueTask(ProblemDetailsResponse.WriteAsync(context.HttpContext, 429, "rate_limit.exceeded", "İstek sınırı aşıldı", "Çok fazla istek gönderildi."));
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    const string bearerScheme = "Bearer";
    options.AddSecurityDefinition(bearerScheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(bearerScheme, document)] = []
    });
    options.OperationFilter<AllowAnonymousOperationFilter>();
});
var app = builder.Build();
if (args.Contains("--initialize-database", StringComparer.Ordinal))
{
    if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Database initialization is allowed only in Development.");
    await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync(); return;
}
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.Run();
public partial class Program;
