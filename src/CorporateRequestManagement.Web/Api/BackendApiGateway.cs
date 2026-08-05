using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CorporateRequestManagement.Web.Authentication;

namespace CorporateRequestManagement.Web.Api;

public sealed record ApiProblem(string? Code, string? Detail, string? TraceId, IReadOnlyDictionary<string, string[]> Errors)
{
    public static ApiProblem Empty { get; } = new(null, null, null, new Dictionary<string, string[]>());
    public static ApiProblem ServiceUnavailable { get; } = new("backend.service_unavailable", "Servise şu anda ulaşılamıyor. Lütfen kısa bir süre sonra yeniden deneyin.", null, new Dictionary<string, string[]>());
    public static ApiProblem GatewayTimeout { get; } = new("backend.gateway_timeout", "İşlem zamanında tamamlanamadı. Lütfen yeniden deneyin.", null, new Dictionary<string, string[]>());
}

public sealed record WebApiResult<T>(HttpStatusCode StatusCode, T? Value, ApiProblem Problem)
{
    public bool IsSuccess => (int)StatusCode is >= 200 and < 300 && Value is not null;
}

public interface IBackendApiGateway
{
    Task<WebApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default);
}

internal sealed class BackendApiGateway(HttpClient client, IUserTokenStore store, IAuthenticationApiClient authentication,
    IRefreshCoordinator refreshCoordinator, IHttpContextAccessor accessor, ILocalAuthenticationState localAuthenticationState) : IBackendApiGateway
{
    public async Task<WebApiResult<T>> SendAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        try
        {
            WebUserSession? session = await store.GetAsync(cancellationToken);
            if (session is null) { await localAuthenticationState.ClearAsync(cancellationToken); return new(HttpStatusCode.Unauthorized, default, ApiProblem.Empty); }
            using HttpResponseMessage first = await SendCoreAsync(method, path, body, session.AccessToken, cancellationToken);
            if (first.StatusCode != HttpStatusCode.Unauthorized) return await ReadAsync<T>(first, cancellationToken);

            string sessionId = accessor.HttpContext?.Session.Id ?? throw new InvalidOperationException("Active session required.");
            WebUserSession? current = await refreshCoordinator.ExecuteAsync(sessionId, () => RefreshAsync(session, cancellationToken), cancellationToken);
            if (current is null) return new(HttpStatusCode.Unauthorized, default, ApiProblem.Empty);
            using HttpResponseMessage retry = await SendCoreAsync(method, path, body, current.AccessToken, cancellationToken);
            if (retry.StatusCode == HttpStatusCode.Unauthorized) await localAuthenticationState.ClearAsync(cancellationToken);
            return await ReadAsync<T>(retry, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(HttpStatusCode.GatewayTimeout, default, ApiProblem.GatewayTimeout);
        }
        catch (HttpRequestException)
        {
            return new(HttpStatusCode.ServiceUnavailable, default, ApiProblem.ServiceUnavailable);
        }
        catch (HttpIOException)
        {
            return new(HttpStatusCode.ServiceUnavailable, default, ApiProblem.ServiceUnavailable);
        }
    }

    private async Task<WebUserSession?> RefreshAsync(WebUserSession failed, CancellationToken cancellationToken)
    {
        WebUserSession? latest = await store.GetAsync(cancellationToken);
        if (latest is null) return null;
        if (!string.Equals(latest.AccessToken, failed.AccessToken, StringComparison.Ordinal)) return latest;
        ApiCallResult<AuthenticationContract> result = await authentication.RefreshAsync(latest.RefreshToken, cancellationToken);
        if (result.Value is null) { await localAuthenticationState.ClearAsync(cancellationToken); return null; }
        var rotated = new WebUserSession { AccessToken = result.Value.AccessToken, AccessTokenExpiresAt = result.Value.AccessTokenExpiresAt,
            RefreshToken = result.Value.RefreshToken, RefreshTokenExpiresAt = result.Value.RefreshTokenExpiresAt };
        await store.SetAsync(rotated, cancellationToken); return rotated;
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string path, object? body, string token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, path); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task<WebApiResult<T>> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.NoContent && typeof(T) == typeof(object))
                return new(response.StatusCode, (T)(object)new object(), ApiProblem.Empty);
            T? value = await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
            return new(response.StatusCode, value, ApiProblem.Empty);
        }
        try
        {
            using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            JsonElement root = json.RootElement;
            string? code = root.TryGetProperty("code", out JsonElement c) ? c.GetString() : null;
            string? detail = root.TryGetProperty("detail", out JsonElement d) ? d.GetString() : null;
            string? trace = root.TryGetProperty("traceId", out JsonElement t) ? t.GetString() : null;
            var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("errors", out JsonElement e) && e.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty property in e.EnumerateObject()) errors[property.Name] = property.Value.EnumerateArray().Select(x => x.GetString() ?? "Geçersiz değer.").ToArray();
            return new(response.StatusCode, default, new(code, detail, trace, errors));
        }
        catch (JsonException) { return new(response.StatusCode, default, ApiProblem.Empty); }
    }
}
