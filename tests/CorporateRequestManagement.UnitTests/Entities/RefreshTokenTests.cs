using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Exceptions;

namespace CorporateRequestManagement.UnitTests.Entities;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 8, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsActive_WhenTokenHasNotExpiredOrBeenRevoked_ShouldReturnTrue()
    {
        RefreshToken token = CreateToken();

        Assert.True(token.IsActive(CreatedAt.AddHours(1)));
        Assert.False(token.IsExpired(CreatedAt.AddHours(1)));
    }

    [Fact]
    public void IsExpired_WhenCurrentTimeEqualsExpiry_ShouldReturnTrue()
    {
        RefreshToken token = CreateToken();

        Assert.True(token.IsExpired(CreatedAt.AddDays(7)));
        Assert.False(token.IsActive(CreatedAt.AddDays(7)));
    }

    [Fact]
    public void Revoke_WhenTokenIsActive_ShouldMakeTokenInactive()
    {
        RefreshToken token = CreateToken();
        Guid replacementId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        token.Revoke(CreatedAt.AddHours(1), " 127.0.0.2 ", replacementId);

        Assert.False(token.IsActive(CreatedAt.AddHours(2)));
        Assert.Equal(CreatedAt.AddHours(1), token.RevokedAt);
        Assert.Equal("127.0.0.2", token.RevokedByIp);
        Assert.Equal(replacementId, token.ReplacedByTokenId);
    }

    [Fact]
    public void Revoke_WhenTokenWasAlreadyRevoked_ShouldRejectSecondRevocation()
    {
        RefreshToken token = CreateToken();
        token.Revoke(CreatedAt.AddHours(1), "127.0.0.2", null);

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => token.Revoke(CreatedAt.AddHours(2), "127.0.0.3", null));

        Assert.Equal("refresh_token.already_revoked", exception.Code);
    }

    [Fact]
    public void Create_WhenExpiryIsNotAfterCreation_ShouldRejectToken()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => RefreshToken.Create(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "token-hash",
                CreatedAt,
                CreatedAt,
                "127.0.0.1"));

        Assert.Equal("refresh_token.expiry_invalid", exception.Code);
    }

    [Fact]
    public void Revoke_WhenIpIsBlank_ShouldPreserveStateAndAllowLaterRevoke()
    {
        RefreshToken token = CreateToken();
        Guid replacementId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => token.Revoke(CreatedAt.AddHours(1), "  ", replacementId));

        Assert.Equal("refresh_token.revoked_ip_required", exception.Code);
        Assert.Null(token.RevokedAt);
        Assert.Null(token.RevokedByIp);
        Assert.Null(token.ReplacedByTokenId);
        Assert.True(token.IsActive(CreatedAt.AddHours(2)));

        token.Revoke(CreatedAt.AddHours(2), "127.0.0.2", replacementId);
        Assert.False(token.IsActive(CreatedAt.AddHours(3)));
    }

    [Fact]
    public void Revoke_WhenReplacementIsSelf_ShouldPreserveState()
    {
        RefreshToken token = CreateToken();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => token.Revoke(CreatedAt.AddHours(1), "127.0.0.2", token.Id));

        Assert.Equal("refresh_token.self_replacement_forbidden", exception.Code);
        Assert.Null(token.RevokedAt);
        Assert.Null(token.RevokedByIp);
        Assert.Null(token.ReplacedByTokenId);
        Assert.True(token.IsActive(CreatedAt.AddHours(2)));
    }

    [Fact]
    public void Revoke_WhenReplacementIsEmpty_ShouldPreserveState()
    {
        RefreshToken token = CreateToken();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => token.Revoke(CreatedAt.AddHours(1), "127.0.0.2", Guid.Empty));

        Assert.Equal("refresh_token.replacement_invalid", exception.Code);
        Assert.Null(token.RevokedAt);
        Assert.Null(token.RevokedByIp);
        Assert.Null(token.ReplacedByTokenId);
        Assert.True(token.IsActive(CreatedAt.AddHours(2)));
    }

    [Fact]
    public void Revoke_WhenTokenIsExpired_ShouldRecordRevocation()
    {
        RefreshToken token = CreateToken();
        DateTimeOffset revokedAt = CreatedAt.AddDays(8);

        token.Revoke(revokedAt, " 127.0.0.2 ", null);

        Assert.Equal(revokedAt, token.RevokedAt);
        Assert.Equal("127.0.0.2", token.RevokedByIp);
        Assert.False(token.IsActive(revokedAt));
    }

    [Fact]
    public void Revoke_WhenDateIsBeforeCreation_ShouldPreserveState()
    {
        RefreshToken token = CreateToken();

        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => token.Revoke(CreatedAt.AddTicks(-1), "127.0.0.2", null));

        Assert.Equal("refresh_token.revocation_date_invalid", exception.Code);
        Assert.Null(token.RevokedAt);
        Assert.Null(token.RevokedByIp);
        Assert.Null(token.ReplacedByTokenId);
        Assert.True(token.IsActive(CreatedAt.AddHours(1)));
    }

    [Fact]
    public void Create_WhenTokenHashExceedsMaximumLength_ShouldRejectToken()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => RefreshToken.Create(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                new string('h', RefreshToken.TokenHashMaxLength + 1),
                CreatedAt.AddDays(7),
                CreatedAt,
                "127.0.0.1"));

        Assert.Equal("refresh_token.hash_too_long", exception.Code);
    }

    [Fact]
    public void Create_WhenTokenHashIsAtMaximumLength_ShouldAcceptToken()
    {
        RefreshToken token = RefreshToken.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            new string('h', RefreshToken.TokenHashMaxLength),
            CreatedAt.AddDays(7),
            CreatedAt,
            "127.0.0.1");

        Assert.Equal(RefreshToken.TokenHashMaxLength, token.TokenHash.Length);
    }

    [Fact]
    public void Create_WhenIpIsAtMaximumLength_ShouldAcceptToken()
    {
        RefreshToken token = RefreshToken.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "token-hash",
            CreatedAt.AddDays(7),
            CreatedAt,
            new string('i', RefreshToken.IpAddressMaxLength));

        Assert.Equal(RefreshToken.IpAddressMaxLength, token.CreatedByIp.Length);
    }

    [Fact]
    public void Create_WhenIpExceedsMaximumLength_ShouldRejectToken()
    {
        DomainRuleException exception = Assert.Throws<DomainRuleException>(
            () => RefreshToken.Create(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "token-hash",
                CreatedAt.AddDays(7),
                CreatedAt,
                new string('i', RefreshToken.IpAddressMaxLength + 1)));

        Assert.Equal("refresh_token.created_ip_too_long", exception.Code);
    }

    private static RefreshToken CreateToken()
    {
        return RefreshToken.Create(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "token-hash",
            CreatedAt.AddDays(7),
            CreatedAt,
            " 127.0.0.1 ");
    }
}
