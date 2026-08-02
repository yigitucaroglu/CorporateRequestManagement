namespace CorporateRequestManagement.Application.Features.Authentication.Common;

public sealed class AccessTokenResult(string token, DateTimeOffset expiresAt)
{
    public string Token { get; } = token;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public override string ToString() => nameof(AccessTokenResult);
}

public sealed class RefreshTokenMaterial(string rawToken, string tokenHash, DateTimeOffset expiresAt)
{
    public string RawToken { get; } = rawToken;
    public string TokenHash { get; } = tokenHash;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;
    public override string ToString() => nameof(RefreshTokenMaterial);
}

public sealed record AuthenticatedUserDto(Guid Id, string FirstName, string LastName, string Email, string Role, Guid? DepartmentId);

public sealed class AuthenticationResult(AccessTokenResult access, RefreshTokenMaterial refresh, AuthenticatedUserDto user)
{
    private readonly string tokenType = "Bearer";
    public string TokenType => tokenType;
    public string AccessToken => access.Token;
    public DateTimeOffset AccessTokenExpiresAt => access.ExpiresAt;
    public string RefreshToken => refresh.RawToken;
    public DateTimeOffset RefreshTokenExpiresAt => refresh.ExpiresAt;
    public AuthenticatedUserDto User { get; } = user;
    public override string ToString() => nameof(AuthenticationResult);
}

public static class AuthenticationAuditActions
{
    public const string LoginSucceeded = "Authentication.LoginSucceeded";
    public const string RefreshTokenRotated = "Authentication.RefreshTokenRotated";
    public const string RefreshTokenRevoked = "Authentication.RefreshTokenRevoked";
}
