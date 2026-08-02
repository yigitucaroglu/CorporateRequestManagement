using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CorporateRequestManagement.Web.Authentication;

public static class WebAuthenticationDefaults { public const string Scheme = "CorporateRequestManagement.WebCookie"; public const string SessionKey = "crm.auth.tokens"; }

public sealed class WebUserSession
{
    public string AccessToken { get; init; } = string.Empty; public DateTimeOffset AccessTokenExpiresAt { get; init; }
    public string RefreshToken { get; init; } = string.Empty; public DateTimeOffset RefreshTokenExpiresAt { get; init; }
    public bool IsValid => !string.IsNullOrWhiteSpace(AccessToken) && !string.IsNullOrWhiteSpace(RefreshToken) && RefreshTokenExpiresAt > DateTimeOffset.UtcNow;
    public override string ToString() => nameof(WebUserSession);
}
public interface IUserTokenStore { Task<WebUserSession?> GetAsync(CancellationToken cancellationToken = default); Task SetAsync(WebUserSession session, CancellationToken cancellationToken = default); Task ClearAsync(CancellationToken cancellationToken = default); }
internal sealed class SessionUserTokenStore(IHttpContextAccessor accessor) : IUserTokenStore
{
    public Task<WebUserSession?> GetAsync(CancellationToken cancellationToken = default)
    {
        string? json = accessor.HttpContext?.Session.GetString(WebAuthenticationDefaults.SessionKey);
        if (string.IsNullOrWhiteSpace(json)) return Task.FromResult<WebUserSession?>(null);
        try { WebUserSession? value = JsonSerializer.Deserialize<WebUserSession>(json); return Task.FromResult(value?.IsValid == true ? value : null); }
        catch (JsonException) { accessor.HttpContext?.Session.Remove(WebAuthenticationDefaults.SessionKey); return Task.FromResult<WebUserSession?>(null); }
    }
    public Task SetAsync(WebUserSession session, CancellationToken cancellationToken = default) { ArgumentNullException.ThrowIfNull(session); if (!session.IsValid) throw new ArgumentException("Session token material is invalid.", nameof(session)); accessor.HttpContext!.Session.SetString(WebAuthenticationDefaults.SessionKey, JsonSerializer.Serialize(session)); return Task.CompletedTask; }
    public Task ClearAsync(CancellationToken cancellationToken = default) { accessor.HttpContext?.Session.Remove(WebAuthenticationDefaults.SessionKey); return Task.CompletedTask; }
}

public sealed class AuthenticatedUserContract { public Guid Id { get; init; } public string FirstName { get; init; } = string.Empty; public string LastName { get; init; } = string.Empty; public string Email { get; init; } = string.Empty; public string Role { get; init; } = string.Empty; public Guid? DepartmentId { get; init; } }
public sealed class AuthenticationContract
{
    public string AccessToken { get; init; } = string.Empty; public DateTimeOffset AccessTokenExpiresAt { get; init; }
    public string RefreshToken { get; init; } = string.Empty; public DateTimeOffset RefreshTokenExpiresAt { get; init; }
    public AuthenticatedUserContract User { get; init; } = new(); public override string ToString() => nameof(AuthenticationContract);
}
public sealed record ApiCallResult<T>(HttpStatusCode StatusCode, T? Value, string? Code = null);
public interface IAuthenticationApiClient
{
    Task<ApiCallResult<AuthenticationContract>> LoginAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<ApiCallResult<AuthenticationContract>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default);
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default);
}
internal sealed class AuthenticationApiClient(HttpClient client) : IAuthenticationApiClient
{
    public Task<ApiCallResult<AuthenticationContract>> LoginAsync(string email, string password, CancellationToken cancellationToken = default) => SendAsync("api/auth/login", new { email, password }, cancellationToken);
    public Task<ApiCallResult<AuthenticationContract>> RefreshAsync(string refreshToken, CancellationToken cancellationToken = default) => SendAsync("api/auth/refresh", new { refreshToken }, cancellationToken);
    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken = default) { using HttpResponseMessage _ = await client.PostAsJsonAsync("api/auth/revoke", new { refreshToken }, cancellationToken); }
    private async Task<ApiCallResult<AuthenticationContract>> SendAsync(string path, object body, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, body, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            AuthenticationContract? value = await response.Content.ReadFromJsonAsync<AuthenticationContract>(cancellationToken: cancellationToken);
            return value is null ? new(HttpStatusCode.BadGateway, null, "backend.invalid_response") : new(response.StatusCode, value);
        }
        string? code = null;
        try { using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken); if (json.RootElement.TryGetProperty("code", out JsonElement element)) code = element.GetString(); } catch (JsonException) { }
        return new(response.StatusCode, null, code);
    }
}

public interface IAuthenticatedUserApiClient { Task<AuthenticatedUserContract?> GetCurrentUserAsync(CancellationToken cancellationToken = default); }
public interface IRefreshCoordinator
{
    Task<T> ExecuteAsync<T>(string sessionId, Func<Task<T>> operation, CancellationToken cancellationToken = default);
}

public sealed class RefreshCoordinator : IRefreshCoordinator
{
    private readonly object sync = new();
    private readonly Dictionary<string, LockEntry> locks = new(StringComparer.Ordinal);

    public int ActiveLockCount { get { lock (sync) return locks.Count; } }

    public async Task<T> ExecuteAsync<T>(string sessionId, Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(operation);

        LockEntry entry;
        lock (sync)
        {
            if (!locks.TryGetValue(sessionId, out entry!))
            {
                entry = new LockEntry();
                locks.Add(sessionId, entry);
            }
            entry.ReferenceCount++;
        }

        SemaphoreSlim gate = entry.Gate;
        bool acquired = false;
        try
        {
            await gate.WaitAsync(cancellationToken);
            acquired = true;
            return await operation();
        }
        finally
        {
            if (acquired) gate.Release();
            lock (sync)
            {
                entry.ReferenceCount--;
                if (entry.ReferenceCount == 0) locks.Remove(sessionId);
            }
        }
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public int ReferenceCount { get; set; }
    }
}

internal sealed class AuthenticatedUserApiClient(
    HttpClient client,
    IUserTokenStore store,
    IAuthenticationApiClient authentication,
    IRefreshCoordinator refreshCoordinator,
    IHttpContextAccessor httpContextAccessor) : IAuthenticatedUserApiClient
{
    public async Task<AuthenticatedUserContract?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        WebUserSession? session = await store.GetAsync(cancellationToken); if (session is null) return null;
        using HttpResponseMessage first = await SendAsync(session.AccessToken, cancellationToken);
        if (first.StatusCode != HttpStatusCode.Unauthorized) return first.IsSuccessStatusCode ? await first.Content.ReadFromJsonAsync<AuthenticatedUserContract>(cancellationToken: cancellationToken) : null;
        string sessionId = httpContextAccessor.HttpContext?.Session.Id ?? throw new InvalidOperationException("An active Web session is required.");
        WebUserSession? current = await refreshCoordinator.ExecuteAsync(
            sessionId,
            () => RefreshIfRequiredAsync(session, cancellationToken),
            cancellationToken);
        if (current is null) return null;

        using HttpResponseMessage retry = await SendAsync(current.AccessToken, cancellationToken);
        if (!retry.IsSuccessStatusCode) { if (retry.StatusCode == HttpStatusCode.Unauthorized) await store.ClearAsync(cancellationToken); return null; }
        return await retry.Content.ReadFromJsonAsync<AuthenticatedUserContract>(cancellationToken: cancellationToken);
    }

    private async Task<WebUserSession?> RefreshIfRequiredAsync(WebUserSession failedSession, CancellationToken cancellationToken)
    {
        WebUserSession? latest = await store.GetAsync(cancellationToken);
        if (latest is null) return null;

        if (!string.Equals(latest.AccessToken, failedSession.AccessToken, StringComparison.Ordinal))
            return latest;

        ApiCallResult<AuthenticationContract> refreshed = await authentication.RefreshAsync(latest.RefreshToken, cancellationToken);
        if (refreshed.Value is null)
        {
            await store.ClearAsync(cancellationToken);
            return null;
        }

        var rotated = new WebUserSession
        {
            AccessToken = refreshed.Value.AccessToken,
            AccessTokenExpiresAt = refreshed.Value.AccessTokenExpiresAt,
            RefreshToken = refreshed.Value.RefreshToken,
            RefreshTokenExpiresAt = refreshed.Value.RefreshTokenExpiresAt
        };
        await store.SetAsync(rotated, cancellationToken);
        return rotated;
    }
    private Task<HttpResponseMessage> SendAsync(string accessToken, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/me"); request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken); return client.SendAsync(request, cancellationToken);
    }
}
