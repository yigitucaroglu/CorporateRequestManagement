using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using CorporateRequestManagement.Application.Abstractions.Authentication;
using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;
using CorporateRequestManagement.Infrastructure;
using CorporateRequestManagement.Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace CorporateRequestManagement.IntegrationTests;

[Collection(SqlServerTestGroup.Name)]
public sealed class AuthenticationServiceTests(SqlServerDatabaseFixture fixture)
{
    [Fact]
    public void PasswordService_ShouldHashAndVerifyWithoutStoringPlaintext()
    {
        using ServiceProvider provider = Provider();
        IPasswordService service = provider.GetRequiredService<IPasswordService>();
        User placeholder = User.Create(Guid.NewGuid(), "Test", "User", "password@crm.local", "placeholder", UserRole.Admin, null, Now);
        string hash = service.HashPassword(placeholder, "Strong!Password1");
        User user = User.Create(placeholder.Id, "Test", "User", placeholder.Email, hash, UserRole.Admin, null, Now);
        Assert.NotEqual("Strong!Password1", hash);
        Assert.True(service.VerifyPassword(user, "Strong!Password1"));
        Assert.False(service.VerifyPassword(user, "wrong"));
    }

    [Fact]
    public void RefreshTokenService_ShouldGenerateUrlSafeUniqueMaterialAndStableHash()
    {
        using ServiceProvider provider = Provider();
        IRefreshTokenService service = provider.GetRequiredService<IRefreshTokenService>();
        var first = service.Generate(Now); var second = service.Generate(Now);
        Assert.NotEqual(first.RawToken, second.RawToken);
        Assert.Matches(new Regex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant), first.RawToken);
        Assert.True(first.RawToken.Length >= 80);
        Assert.Equal(64, first.TokenHash.Length);
        Assert.Matches("^[0-9a-f]{64}$", first.TokenHash);
        Assert.Equal(first.TokenHash, service.Hash(first.RawToken));
        Assert.NotEqual(first.RawToken, first.TokenHash);
        Assert.Equal(Now.AddDays(7), first.ExpiresAt);
        Assert.DoesNotContain(first.RawToken, first.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void JwtService_ShouldProduceValidMinimalClaimsAndSafeResult()
    {
        using ServiceProvider provider = Provider();
        IAccessTokenService service = provider.GetRequiredService<IAccessTokenService>();
        User user = User.Create(Guid.NewGuid(), "JWT", "User", "jwt@crm.local", "password-hash-secret", UserRole.Manager, Guid.NewGuid(), Now);
        DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
        var result = service.CreateToken(user, issuedAt);
        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        JwtSecurityToken rawJwt = tokenHandler.ReadJwtToken(result.Token);
        Assert.Contains(rawJwt.Claims, claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == user.Id.ToString());
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = Issuer, ValidateAudience = true, ValidAudience = Audience,
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
            NameClaimType = JwtRegisteredClaimNames.Sub, RoleClaimType = ClaimTypes.Role
        };
        ClaimsPrincipal principal = tokenHandler.ValidateToken(result.Token, parameters, out SecurityToken validated);
        var jwt = Assert.IsType<JwtSecurityToken>(validated);
        Assert.Equal(user.Id.ToString(), principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.Equal(user.Email, principal.FindFirstValue(JwtRegisteredClaimNames.Email));
        Assert.Equal(user.Role.ToString(), principal.FindFirstValue(ClaimTypes.Role));
        Assert.False(string.IsNullOrWhiteSpace(principal.FindFirstValue(JwtRegisteredClaimNames.Jti)));
        Assert.Equal(issuedAt.AddMinutes(15), result.ExpiresAt);
        Assert.DoesNotContain("department", result.Token, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.PasswordHash, result.Token, StringComparison.Ordinal);
        Assert.DoesNotContain(result.Token, result.ToString(), StringComparison.Ordinal);
        Assert.Equal(SecurityAlgorithms.HmacSha256, jwt.SignatureAlgorithm);
    }

    private ServiceProvider Provider()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = fixture.ConnectionString, ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = Audience, ["Jwt:SigningKey"] = Key, ["Jwt:AccessTokenMinutes"] = "15", ["Jwt:RefreshTokenDays"] = "7"
        }).Build();
        var services = new ServiceCollection(); services.AddInfrastructure(configuration); return services.BuildServiceProvider();
    }

    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    private const string Issuer = "CorporateRequestManagement.Api";
    private const string Audience = "CorporateRequestManagement.Tests";
    private const string Key = "integration-test-signing-material-32-bytes-minimum";
}
