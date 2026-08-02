using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CorporateRequestManagement.Application.Abstractions.Authentication;
using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Features.Authentication.Common;
using CorporateRequestManagement.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CorporateRequestManagement.Infrastructure.Authentication;

internal sealed class PasswordService(IPasswordHasher<User> hasher) : IPasswordService
{
    public string HashPassword(User user, string password) => hasher.HashPassword(user, password);
    public bool VerifyPassword(User user, string providedPassword) =>
        hasher.VerifyHashedPassword(user, user.PasswordHash, providedPassword) is not PasswordVerificationResult.Failed;
}

internal sealed class RefreshTokenService(IOptions<JwtOptions> options) : IRefreshTokenService
{
    public RefreshTokenMaterial Generate(DateTimeOffset issuedAt)
    {
        string raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        return new(raw, Hash(raw), issuedAt.AddDays(options.Value.RefreshTokenDays));
    }
    public string Hash(string rawToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}

internal sealed class JwtAccessTokenService(IOptions<JwtOptions> options, IGuidGenerator guids) : IAccessTokenService
{
    public AccessTokenResult CreateToken(User user, DateTimeOffset issuedAt)
    {
        JwtOptions value = options.Value;
        DateTimeOffset expires = issuedAt.AddMinutes(value.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, guids.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };
        var credentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(value.SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(value.Issuer, value.Audience, claims, issuedAt.UtcDateTime, expires.UtcDateTime, credentials);
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}

internal sealed class SystemGuidGenerator : IGuidGenerator
{
    public Guid NewGuid() => Guid.NewGuid();
}
