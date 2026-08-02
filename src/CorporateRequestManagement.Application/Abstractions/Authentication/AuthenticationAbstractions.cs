using CorporateRequestManagement.Application.Features.Authentication.Common;
using CorporateRequestManagement.Domain.Entities;

namespace CorporateRequestManagement.Application.Abstractions.Authentication;

public interface IPasswordService
{
    string HashPassword(User user, string password);
    bool VerifyPassword(User user, string providedPassword);
}

public interface IAccessTokenService
{
    AccessTokenResult CreateToken(User user, DateTimeOffset issuedAt);
}

public interface IRefreshTokenService
{
    RefreshTokenMaterial Generate(DateTimeOffset issuedAt);
    string Hash(string rawToken);
}
