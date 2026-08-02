namespace CorporateRequestManagement.Api.Contracts.Authentication;

public sealed class LoginRequest { public string Email { get; init; } = string.Empty; public string Password { get; init; } = string.Empty; public override string ToString() => nameof(LoginRequest); }
public sealed class RefreshTokenRequest { public string RefreshToken { get; init; } = string.Empty; public override string ToString() => nameof(RefreshTokenRequest); }
public sealed class RevokeRefreshTokenRequest { public string RefreshToken { get; init; } = string.Empty; public override string ToString() => nameof(RevokeRefreshTokenRequest); }
