using CorporateRequestManagement.Application.Features.Authentication.Login;
using CorporateRequestManagement.Application.Features.Authentication.Refresh;
using CorporateRequestManagement.Application.Features.Authentication.Revoke;
using CorporateRequestManagement.Domain.Entities;
using FluentValidation;

namespace CorporateRequestManagement.UnitTests.Application.Authentication;

public sealed class AuthenticationValidatorTests
{
    [Fact]
    public async Task LoginValidator_WhenModelValid_ShouldSucceed() => Assert.True((await ((IValidator<LoginCommand>)new LoginCommandValidator()).ValidateAsync(new LoginCommand("a@b.com", "password", "127.0.0.1"))).IsValid);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")] [InlineData("invalid")]
    public async Task LoginValidator_WhenEmailInvalid_ShouldFail(string? email) => Assert.False((await ((IValidator<LoginCommand>)new LoginCommandValidator()).ValidateAsync(new LoginCommand(email!, "password", "127.0.0.1"))).IsValid);

    [Fact]
    public async Task LoginValidator_WhenEmailAtMaximum_ShouldSucceed()
    {
        string email = new string('a', User.EmailMaxLength - "@b.com".Length) + "@b.com";
        Assert.True((await ((IValidator<LoginCommand>)new LoginCommandValidator()).ValidateAsync(new LoginCommand(email, "password", "127.0.0.1"))).IsValid);
    }

    [Fact]
    public async Task LoginValidator_WhenEmailOverMaximum_ShouldFail() => Assert.False((await ((IValidator<LoginCommand>)new LoginCommandValidator()).ValidateAsync(new LoginCommand(new string('a', User.EmailMaxLength + 1), "password", "127.0.0.1"))).IsValid);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task LoginValidator_WhenPasswordInvalid_ShouldFail(string? password) => Assert.False((await ((IValidator<LoginCommand>)new LoginCommandValidator()).ValidateAsync(new LoginCommand("a@b.com", password!, "127.0.0.1"))).IsValid);

    [Fact] public async Task LoginValidator_WhenPasswordAtMaximum_ShouldSucceed() => Assert.True((await ((IValidator<LoginCommand>)new LoginCommandValidator()).ValidateAsync(new LoginCommand("a@b.com", new string('p', 256), "127.0.0.1"))).IsValid);
    [Fact] public async Task LoginValidator_WhenPasswordOverMaximum_ShouldFail() => Assert.False((await ((IValidator<LoginCommand>)new LoginCommandValidator()).ValidateAsync(new LoginCommand("a@b.com", new string('p', 257), "127.0.0.1"))).IsValid);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task LoginValidator_WhenIpInvalid_ShouldFail(string? ip) => Assert.False((await ((IValidator<LoginCommand>)new LoginCommandValidator()).ValidateAsync(new LoginCommand("a@b.com", "password", ip!))).IsValid);

    [Fact] public async Task LoginValidator_WhenIpAtMaximum_ShouldSucceed() => Assert.True((await ((IValidator<LoginCommand>)new LoginCommandValidator()).ValidateAsync(new LoginCommand("a@b.com", "password", new string('1', RefreshToken.IpAddressMaxLength)))).IsValid);
    [Fact] public async Task LoginValidator_WhenIpOverMaximum_ShouldFail() => Assert.False((await ((IValidator<LoginCommand>)new LoginCommandValidator()).ValidateAsync(new LoginCommand("a@b.com", "password", new string('1', RefreshToken.IpAddressMaxLength + 1)))).IsValid);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task RefreshValidator_WhenTokenInvalid_ShouldFail(string? token) => Assert.False((await ((IValidator<RefreshTokenCommand>)new RefreshTokenCommandValidator()).ValidateAsync(new RefreshTokenCommand(token!, "127.0.0.1"))).IsValid);
    [Fact] public async Task RefreshValidator_WhenValid_ShouldSucceed() => Assert.True((await ((IValidator<RefreshTokenCommand>)new RefreshTokenCommandValidator()).ValidateAsync(new RefreshTokenCommand("token", "127.0.0.1"))).IsValid);
    [Fact] public async Task RefreshValidator_WhenTokenAtMaximum_ShouldSucceed() => Assert.True((await ((IValidator<RefreshTokenCommand>)new RefreshTokenCommandValidator()).ValidateAsync(new RefreshTokenCommand(new string('t', 2048), "127.0.0.1"))).IsValid);
    [Fact] public async Task RefreshValidator_WhenTokenOverMaximum_ShouldFail() => Assert.False((await ((IValidator<RefreshTokenCommand>)new RefreshTokenCommandValidator()).ValidateAsync(new RefreshTokenCommand(new string('t', 2049), "127.0.0.1"))).IsValid);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task RefreshValidator_WhenIpInvalid_ShouldFail(string? ip) => Assert.False((await ((IValidator<RefreshTokenCommand>)new RefreshTokenCommandValidator()).ValidateAsync(new RefreshTokenCommand("token", ip!))).IsValid);
    [Fact] public async Task RefreshValidator_WhenIpOverMaximum_ShouldFail() => Assert.False((await ((IValidator<RefreshTokenCommand>)new RefreshTokenCommandValidator()).ValidateAsync(new RefreshTokenCommand("token", new string('1', RefreshToken.IpAddressMaxLength + 1)))).IsValid);

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task RevokeValidator_WhenTokenInvalid_ShouldFail(string? token) => Assert.False((await ((IValidator<RevokeRefreshTokenCommand>)new RevokeRefreshTokenCommandValidator()).ValidateAsync(new RevokeRefreshTokenCommand(token!, "127.0.0.1"))).IsValid);
    [Fact] public async Task RevokeValidator_WhenValid_ShouldSucceed() => Assert.True((await ((IValidator<RevokeRefreshTokenCommand>)new RevokeRefreshTokenCommandValidator()).ValidateAsync(new RevokeRefreshTokenCommand("token", "127.0.0.1"))).IsValid);
    [Fact] public async Task RevokeValidator_WhenTokenAtMaximum_ShouldSucceed() => Assert.True((await ((IValidator<RevokeRefreshTokenCommand>)new RevokeRefreshTokenCommandValidator()).ValidateAsync(new RevokeRefreshTokenCommand(new string('t', 2048), "127.0.0.1"))).IsValid);
    [Fact] public async Task RevokeValidator_WhenTokenOverMaximum_ShouldFail() => Assert.False((await ((IValidator<RevokeRefreshTokenCommand>)new RevokeRefreshTokenCommandValidator()).ValidateAsync(new RevokeRefreshTokenCommand(new string('t', 2049), "127.0.0.1"))).IsValid);
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task RevokeValidator_WhenIpInvalid_ShouldFail(string? ip) => Assert.False((await ((IValidator<RevokeRefreshTokenCommand>)new RevokeRefreshTokenCommandValidator()).ValidateAsync(new RevokeRefreshTokenCommand("token", ip!))).IsValid);
    [Fact] public async Task RevokeValidator_WhenIpOverMaximum_ShouldFail() => Assert.False((await ((IValidator<RevokeRefreshTokenCommand>)new RevokeRefreshTokenCommandValidator()).ValidateAsync(new RevokeRefreshTokenCommand("token", new string('1', RefreshToken.IpAddressMaxLength + 1)))).IsValid);
}


