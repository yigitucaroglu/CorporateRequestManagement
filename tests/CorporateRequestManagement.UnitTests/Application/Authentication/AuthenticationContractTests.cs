using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.Authentication.Common;

namespace CorporateRequestManagement.UnitTests.Application.Authentication;

public sealed class AuthenticationContractTests
{
    [Fact]
    public void ValidationErrors_ShouldDefensivelyCopyInput()
    {
        string[] messages = ["ilk"];
        var source = new Dictionary<string, string[]> { ["Email"] = messages };
        var exception = new ApplicationValidationException("validation.failed", "İstek doğrulaması başarısız.", source);
        messages[0] = "değişti";
        source["Email"] = ["başka"];
        Assert.Equal("ilk", exception.Errors["Email"][0]);
        string[] exposed = exception.Errors["Email"];
        exposed[0] = "consumer mutation";
        Assert.Equal("ilk", exception.Errors["Email"][0]);
        var exposedDictionary = Assert.IsAssignableFrom<IDictionary<string, string[]>>(exception.Errors);
        Assert.Throws<NotSupportedException>(() => exposedDictionary["Email"] = ["dictionary mutation"]);
        Assert.Equal("ilk", exception.Errors["Email"][0]);
    }

    [Fact]
    public void AuthenticationFailedException_ShouldPreserveSafeCodeAndMessage()
    {
        var exception = new AuthenticationFailedException("auth.invalid_credentials", "E-posta veya parola geçersiz.");
        Assert.Equal("auth.invalid_credentials", exception.Code);
        Assert.Equal("E-posta veya parola geçersiz.", exception.Message);
        Assert.True(exception.GetType().IsSealed);
    }

    [Fact]
    public void SensitiveModels_ToString_ShouldNotExposeTokens()
    {
        var access = new AccessTokenResult("access-secret", DateTimeOffset.UtcNow);
        var refresh = new RefreshTokenMaterial("refresh-secret", "hash-secret", DateTimeOffset.UtcNow);
        var result = new AuthenticationResult(access, refresh, new(Guid.NewGuid(), "A", "B", "a@b.com", "Admin", null));
        Assert.DoesNotContain("secret", access.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", refresh.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("secret", result.ToString(), StringComparison.Ordinal);
    }
}
