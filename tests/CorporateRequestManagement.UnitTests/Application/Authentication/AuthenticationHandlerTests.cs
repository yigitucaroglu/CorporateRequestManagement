using System.Reflection;
using CorporateRequestManagement.Application.Abstractions.Authentication;
using CorporateRequestManagement.Application.Abstractions.Common;
using CorporateRequestManagement.Application.Abstractions.Identity;
using CorporateRequestManagement.Application.Abstractions.Persistence;
using CorporateRequestManagement.Application.Common.Exceptions;
using CorporateRequestManagement.Application.Features.Authentication.Common;
using CorporateRequestManagement.Application.Features.Authentication.CurrentUser;
using CorporateRequestManagement.Application.Features.Authentication.Login;
using CorporateRequestManagement.Application.Features.Authentication.Refresh;
using CorporateRequestManagement.Application.Features.Authentication.Revoke;
using CorporateRequestManagement.Domain.Entities;
using CorporateRequestManagement.Domain.Enums;

namespace CorporateRequestManagement.UnitTests.Application.Authentication;

public sealed class AuthenticationHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Login_WhenValid_ShouldNormalizePersistAuditSaveAndReturnSecrets()
    {
        var f = new Fakes { User = CreateUser(), PasswordValid = true };
        using var source = new CancellationTokenSource();
        AuthenticationResult result = await f.Login().Handle(new("  USER@CRM.LOCAL ", "password-secret", "127.0.0.1"), source.Token);
        Assert.Equal("user@crm.local", f.Users.LastEmail); Assert.Equal(1, f.Tokens.AddCalls); Assert.Equal(1, f.Audits.AddCalls); Assert.Equal(1, f.Unit.SaveCalls);
        Assert.Equal(Now, f.Access.IssuedAt); Assert.Equal(Now, f.Refresh.IssuedAt); Assert.True(f.Guids.Calls >= 2);
        Assert.Equal("raw-refresh-secret", result.RefreshToken); Assert.Equal(source.Token, f.Users.Token); Assert.Equal(source.Token, f.Tokens.Token); Assert.Equal(source.Token, f.Audits.Token); Assert.Equal(source.Token, f.Unit.Token);
        AuditLog audit = Assert.Single(f.Audits.Items); string auditText = $"{audit.OldValues}{audit.NewValues}";
        Assert.DoesNotContain("password-secret", auditText, StringComparison.Ordinal); Assert.DoesNotContain("raw-refresh-secret", auditText, StringComparison.Ordinal); Assert.DoesNotContain("hash-secret", auditText, StringComparison.Ordinal); Assert.DoesNotContain("access-secret", auditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_WhenUnknown_ShouldUseGenericErrorAndNotVerifyPassword()
    {
        var f = new Fakes();
        AuthenticationFailedException error = await Assert.ThrowsAsync<AuthenticationFailedException>(() => f.Login().Handle(new("missing@crm.local", "x", "ip"), default));
        AssertGenericLogin(error); Assert.Equal(0, f.Password.VerifyCalls); Assert.Equal(0, f.Unit.SaveCalls);
    }

    [Theory]
    [InlineData("wrong")] [InlineData("inactive")]
    public async Task Login_WhenCredentialStateInvalid_ShouldUseSameGenericErrorAndNotWrite(string state)
    {
        var user = CreateUser(); if (state == "inactive") SetInactive(user);
        var f = new Fakes { User = user, PasswordValid = state != "wrong" };
        AuthenticationFailedException error = await Assert.ThrowsAsync<AuthenticationFailedException>(() => f.Login().Handle(new(user.Email, "x", "ip"), default));
        AssertGenericLogin(error); Assert.Equal(0, f.Tokens.AddCalls); Assert.Equal(0, f.Audits.AddCalls); Assert.Equal(0, f.Unit.SaveCalls);
    }

    [Fact]
    public async Task Login_WhenSaveFails_ShouldPropagateFailure()
    {
        var f = new Fakes { User = CreateUser(), PasswordValid = true }; f.Unit.Exception = new IOException("save failed");
        IOException error = await Assert.ThrowsAsync<IOException>(() => f.Login().Handle(new(f.User.Email, "x", "ip"), default));
        Assert.Equal("save failed", error.Message);
    }

    [Theory]
    [InlineData("unknown")] [InlineData("expired")] [InlineData("revoked")] [InlineData("missing-user")] [InlineData("inactive-user")]
    public async Task Refresh_WhenInvalid_ShouldRejectWithoutGeneratingOrWriting(string state)
    {
        var f = new Fakes { User = CreateUser() };
        f.StoredToken = state == "unknown" ? null : Token(f.User.Id, state == "expired" ? Now.AddMinutes(-1) : Now.AddDays(1));
        if (state == "revoked") f.StoredToken!.Revoke(Now, "ip", null);
        if (state == "missing-user") f.User = null;
        if (state == "inactive-user") SetInactive(f.User!);
        AuthenticationFailedException error = await Assert.ThrowsAsync<AuthenticationFailedException>(() => f.RefreshHandler().Handle(new("raw-input", "ip"), default));
        AssertGenericRefresh(error); Assert.Equal(1, f.Transaction.Calls); Assert.Equal("lookup-hash", f.Tokens.LastHash); Assert.Equal(0, f.Refresh.GenerateCalls); Assert.Equal(0, f.Tokens.AddCalls); Assert.Equal(0, f.Audits.AddCalls); Assert.Equal(0, f.Unit.SaveCalls); Assert.Equal(0, f.Access.Calls);
    }

    [Fact]
    public async Task Refresh_WhenValid_ShouldRotateInsideTransactionAndPersistOnce()
    {
        var f = new Fakes { User = CreateUser() }; f.StoredToken = Token(f.User.Id, Now.AddDays(1));
        using var source = new CancellationTokenSource();
        AuthenticationResult result = await f.RefreshHandler().Handle(new("raw-input", "ip"), source.Token);
        Assert.Equal(1, f.Transaction.Calls); Assert.Equal(1, f.Tokens.AddCalls); Assert.Equal(1, f.Audits.AddCalls); Assert.Equal(1, f.Unit.SaveCalls); Assert.Equal(1, f.Access.Calls); Assert.Equal(1, f.Refresh.GenerateCalls);
        Assert.NotNull(f.StoredToken.ReplacedByTokenId); Assert.Equal(f.StoredToken.ReplacedByTokenId, f.Tokens.Added!.Id); Assert.Equal(f.User.Id, f.Tokens.Added.UserId); Assert.Equal("raw-refresh-secret", result.RefreshToken);
        Assert.Equal(source.Token, f.Transaction.Token); Assert.Equal(source.Token, f.Tokens.Token); Assert.Equal(source.Token, f.Users.Token); Assert.Equal(source.Token, f.Unit.Token);
        Assert.DoesNotContain("raw-input", AuditText(f.Audits.Items), StringComparison.Ordinal); Assert.DoesNotContain("lookup-hash", AuditText(f.Audits.Items), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown")] [InlineData("expired")] [InlineData("revoked")] [InlineData("missing-user")] [InlineData("inactive-user")]
    public async Task Revoke_WhenInvalid_ShouldNotMutateAuditOrSave(string state)
    {
        var f = new Fakes { User = CreateUser() };
        f.StoredToken = state == "unknown" ? null : Token(f.User.Id, state == "expired" ? Now.AddMinutes(-1) : Now.AddDays(1));
        if (state == "revoked") f.StoredToken!.Revoke(Now, "ip", null);
        if (state == "missing-user") f.User = null;
        if (state == "inactive-user") SetInactive(f.User!);
        AuthenticationFailedException error = await Assert.ThrowsAsync<AuthenticationFailedException>(() => f.RevokeHandler().Handle(new("raw-input", "ip"), default));
        AssertGenericRefresh(error); Assert.Equal(1, f.Transaction.Calls); Assert.Equal(0, f.Audits.AddCalls); Assert.Equal(0, f.Unit.SaveCalls);
    }

    [Fact]
    public async Task Revoke_WhenValid_ShouldRevokeWithoutReplacementAndPersistAuditOnce()
    {
        var f = new Fakes { User = CreateUser() }; f.StoredToken = Token(f.User.Id, Now.AddDays(1));
        await f.RevokeHandler().Handle(new("raw-input", "ip"), default);
        Assert.Equal(1, f.Transaction.Calls); Assert.Equal(1, f.Audits.AddCalls); Assert.Equal(1, f.Unit.SaveCalls); Assert.NotNull(f.StoredToken.RevokedAt); Assert.Null(f.StoredToken.ReplacedByTokenId);
        Assert.DoesNotContain("raw-input", AuditText(f.Audits.Items), StringComparison.Ordinal); Assert.DoesNotContain("lookup-hash", AuditText(f.Audits.Items), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false, "none")] [InlineData(true, false, "none")] [InlineData(true, true, "missing")] [InlineData(true, true, "inactive")]
    public async Task CurrentUser_WhenIdentityOrUserInvalid_ShouldReject(bool authenticated, bool hasId, string state)
    {
        var f = new Fakes { User = state == "missing" ? null : CreateUser() };
        if (state == "inactive") SetInactive(f.User!);
        f.Current.Authenticated = authenticated; f.Current.Id = hasId ? Guid.NewGuid() : null;
        AuthenticationFailedException error = await Assert.ThrowsAsync<AuthenticationFailedException>(() => f.CurrentHandler().Handle(new(), default));
        Assert.Equal("auth.unauthorized", error.Code);
    }

    [Fact]
    public async Task CurrentUser_WhenValid_ShouldReturnSafeDtoAndForwardCancellation()
    {
        User user = CreateUser(); var f = new Fakes { User = user }; f.Current.Authenticated = true; f.Current.Id = user.Id; using var source = new CancellationTokenSource();
        AuthenticatedUserDto dto = await f.CurrentHandler().Handle(new(), source.Token);
        Assert.Equal(user.Id, dto.Id); Assert.Equal(user.Role.ToString(), dto.Role); Assert.Equal(user.DepartmentId, dto.DepartmentId); Assert.DoesNotContain("Password", dto.GetType().GetProperties().Select(x => x.Name)); Assert.Equal(source.Token, f.Users.Token);
    }

    private static void AssertGenericLogin(AuthenticationFailedException error) { Assert.Equal("auth.invalid_credentials", error.Code); Assert.Equal("E-posta veya parola ge\u00e7ersiz.", error.Message); }
    private static void AssertGenericRefresh(AuthenticationFailedException error) { Assert.Equal("auth.refresh_token_invalid", error.Code); Assert.Equal("Refresh token ge\u00e7ersiz.", error.Message); }
    private static string AuditText(IEnumerable<AuditLog> audits) => string.Join('|', audits.Select(x => $"{x.Action}{x.OldValues}{x.NewValues}"));
    private static User CreateUser() => User.Create(Guid.NewGuid(), "Test", "User", "user@crm.local", "stored-password-hash", UserRole.Employee, Guid.NewGuid(), Now.AddDays(-1));
    private static RefreshToken Token(Guid userId, DateTimeOffset expiry) => RefreshToken.Create(Guid.NewGuid(), userId, "lookup-hash", expiry, Now.AddDays(-1), "ip");
    private static void SetInactive(User user) => typeof(User).GetProperty(nameof(User.IsActive), BindingFlags.Instance | BindingFlags.Public)!.SetValue(user, false);

    private sealed class Fakes
    {
        public User? User { get => Users.Value; set => Users.Value = value; }
        public RefreshToken? StoredToken { get => Tokens.Value; set => Tokens.Value = value; }
        public bool PasswordValid { set => Password.Valid = value; }
        public UserRepository Users { get; } = new(); public TokenRepository Tokens { get; } = new(); public AuditRepository Audits { get; } = new(); public Unit Unit { get; } = new();
        public Password Password { get; } = new(); public Access Access { get; } = new(); public Refresh Refresh { get; } = new(); public Guids Guids { get; } = new(); public Transaction Transaction { get; } = new(); public Current Current { get; } = new();
        private readonly FixedTime time = new(Now);
        public LoginCommandHandler Login() => new(Users, Password, Access, Refresh, Tokens, Audits, Unit, Guids, time);
        public RefreshTokenCommandHandler RefreshHandler() => new(Refresh, Tokens, Users, Access, Audits, Unit, Transaction, Guids, time);
        public RevokeRefreshTokenCommandHandler RevokeHandler() => new(Refresh, Tokens, Users, Audits, Unit, Transaction, Guids, time);
        public GetCurrentUserQueryHandler CurrentHandler() => new(Current, Users);
    }

    private sealed class UserRepository : IUserRepository
    {
        public User? Value; public string? LastEmail; public CancellationToken Token;
        public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) { Token = cancellationToken; return Task.FromResult(Value); }
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) { LastEmail = email; Token = cancellationToken; return Task.FromResult(Value); }
        public Task<bool> EmailExistsAsync(string email, Guid? excludingUserId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task AddAsync(User user, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class TokenRepository : IRefreshTokenRepository
    {
        public RefreshToken? Value; public RefreshToken? Added; public int AddCalls; public string? LastHash; public CancellationToken Token;
        public Task<RefreshToken?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public Task<RefreshToken?> GetByHashForUpdateAsync(string hash, CancellationToken cancellationToken = default) { LastHash = hash; Token = cancellationToken; return Task.FromResult(Value); }
        public Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default) { Added = token; AddCalls++; Token = cancellationToken; return Task.CompletedTask; }
    }
    private sealed class AuditRepository : IAuditLogRepository { public int AddCalls; public CancellationToken Token; public List<AuditLog> Items { get; } = []; public Task AddAsync(AuditLog item, CancellationToken cancellationToken = default) { AddCalls++; Token = cancellationToken; Items.Add(item); return Task.CompletedTask; } }
    private sealed class Unit : IUnitOfWork { public int SaveCalls; public CancellationToken Token; public Exception? Exception; public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) { SaveCalls++; Token = cancellationToken; return Exception is null ? Task.FromResult(1) : Task.FromException<int>(Exception); } }
    private sealed class Password : IPasswordService { public bool Valid; public int VerifyCalls; public string HashPassword(User user, string password) => "hash"; public bool VerifyPassword(User user, string password) { VerifyCalls++; return Valid; } }
    private sealed class Access : IAccessTokenService { public int Calls; public DateTimeOffset IssuedAt; public AccessTokenResult CreateToken(User user, DateTimeOffset issuedAt) { Calls++; IssuedAt = issuedAt; return new("access-secret", issuedAt.AddMinutes(15)); } }
    private sealed class Refresh : IRefreshTokenService { public int GenerateCalls; public DateTimeOffset IssuedAt; public RefreshTokenMaterial Generate(DateTimeOffset issuedAt) { GenerateCalls++; IssuedAt = issuedAt; return new("raw-refresh-secret", "new-hash-secret", issuedAt.AddDays(7)); } public string Hash(string rawToken) => "lookup-hash"; }
    private sealed class Guids : IGuidGenerator { public int Calls; public Guid NewGuid() { Calls++; return Guid.NewGuid(); } }
    private sealed class Transaction : ITransactionExecutor
    {
        public int Calls; public CancellationToken Token;
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default) { Calls++; Token = cancellationToken; await operation(cancellationToken); }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) { Calls++; Token = cancellationToken; return await operation(cancellationToken); }
    }
    private sealed class Current : ICurrentUserContext { public bool Authenticated; public Guid? Id; public bool IsAuthenticated => Authenticated; public Guid? UserId => Id; }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
}
