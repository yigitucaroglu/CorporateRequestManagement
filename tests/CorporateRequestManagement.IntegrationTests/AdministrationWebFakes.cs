extern alias webapp;
using System.Net;
namespace CorporateRequestManagement.WebTests;
using W = webapp::CorporateRequestManagement.Web.Api;

public abstract class AdministrationWebFakeBase
{
    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.NoContent;
    public int MutationCalls { get; set; }
    public W.ApiProblem Problem { get; set; } = new("internal.secret", "raw-api-detail-must-not-render", "trace-secret", new Dictionary<string, string[]>());
    protected W.WebApiResult<T> Result<T>(T? value = default) => new(StatusCode, value, Problem);
}
public sealed class FakeAdminUsersApiClient : AdministrationWebFakeBase, W.IAdminUsersApiClient
{
    public W.AdminUserContract? Detail { get; set; }
    public W.PagedContract<W.AdminUserContract> ListValue { get; set; } = new([], 0, 1, 20, 0, false, false);
    public Task<W.WebApiResult<W.PagedContract<W.AdminUserContract>>> ListAsync(W.ManagementListQuery query, CancellationToken ct = default) => Task.FromResult(new W.WebApiResult<W.PagedContract<W.AdminUserContract>>(HttpStatusCode.OK, ListValue, W.ApiProblem.Empty));
    public Task<W.WebApiResult<W.AdminUserContract>> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(new W.WebApiResult<W.AdminUserContract>(Detail is null ? StatusCode : HttpStatusCode.OK, Detail, Problem));
    public Task<W.WebApiResult<object>> MutateAsync(HttpMethod method, string path, object body, CancellationToken ct = default) { MutationCalls++; return Task.FromResult(Result<object>(StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices ? new object() : null)); }
}
public sealed class FakeAdminDepartmentsApiClient : AdministrationWebFakeBase, W.IAdminDepartmentsApiClient
{
    public W.PagedContract<W.DepartmentContract> ListValue { get; set; } = new([], 0, 1, 20, 0, false, false);
    public W.DepartmentContract? Detail { get; set; }
    public IReadOnlyList<W.EligibleManagerContract> EligibleManagers { get; set; } = [];
    public HttpStatusCode EligibleStatusCode { get; set; } = HttpStatusCode.OK;
    public int EligibleManagerCalls { get; set; }
    public Task<W.WebApiResult<W.PagedContract<W.DepartmentContract>>> ListAsync(W.ManagementListQuery query, CancellationToken ct = default) => Task.FromResult(new W.WebApiResult<W.PagedContract<W.DepartmentContract>>(HttpStatusCode.OK, ListValue, W.ApiProblem.Empty));
    public Task<W.WebApiResult<W.DepartmentContract>> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(new W.WebApiResult<W.DepartmentContract>(Detail is null ? StatusCode : HttpStatusCode.OK, Detail, W.ApiProblem.Empty));
    public Task<W.WebApiResult<W.DepartmentContract>> CreateAsync(string name, CancellationToken ct = default) { MutationCalls++; return Task.FromResult(new W.WebApiResult<W.DepartmentContract>(Detail is null ? StatusCode : HttpStatusCode.Created, Detail, Problem)); }
    public Task<W.WebApiResult<IReadOnlyList<W.EligibleManagerContract>>> GetEligibleManagersAsync(Guid id, CancellationToken ct = default) { EligibleManagerCalls++; return Task.FromResult(new W.WebApiResult<IReadOnlyList<W.EligibleManagerContract>>(EligibleStatusCode, EligibleStatusCode == HttpStatusCode.OK ? EligibleManagers : null, W.ApiProblem.Empty)); }
    public Task<W.WebApiResult<object>> MutateAsync(HttpMethod method, string path, object body, CancellationToken ct = default) { MutationCalls++; return Task.FromResult(Result<object>(StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices ? new object() : null)); }
}
public sealed class FakeAdminRequestsApiClient : AdministrationWebFakeBase, W.IAdminRequestsApiClient
{
    public W.ScopedRequestContract? Detail { get; set; }
    public W.PagedContract<W.ScopedRequestContract> ListValue { get; set; } = new([], 0, 1, 20, 0, false, false);
    public Task<W.WebApiResult<W.PagedContract<W.ScopedRequestContract>>> ListAsync(W.ManagementListQuery query, CancellationToken ct = default) => Task.FromResult(new W.WebApiResult<W.PagedContract<W.ScopedRequestContract>>(HttpStatusCode.OK, ListValue, W.ApiProblem.Empty));
    public Task<W.WebApiResult<W.ScopedRequestContract>> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(new W.WebApiResult<W.ScopedRequestContract>(Detail is null ? StatusCode : HttpStatusCode.OK, Detail, W.ApiProblem.Empty));
    public Task<W.WebApiResult<object>> DeleteAsync(Guid id, string rowVersion, CancellationToken ct = default) { MutationCalls++; return Task.FromResult(Result<object>(StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices ? new object() : null)); }
}
public sealed class FakeManagerRequestsApiClient : AdministrationWebFakeBase, W.IManagerRequestsApiClient
{
    public W.ScopedRequestContract? Detail { get; set; }
    public W.PagedContract<W.ScopedRequestContract> ListValue { get; set; } = new([], 0, 1, 20, 0, false, false);
    public Task<W.WebApiResult<W.PagedContract<W.ScopedRequestContract>>> ListAsync(W.ManagementListQuery query, CancellationToken ct = default) => Task.FromResult(new W.WebApiResult<W.PagedContract<W.ScopedRequestContract>>(HttpStatusCode.OK, ListValue, W.ApiProblem.Empty));
    public Task<W.WebApiResult<W.ScopedRequestContract>> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(new W.WebApiResult<W.ScopedRequestContract>(Detail is null ? StatusCode : HttpStatusCode.OK, Detail, W.ApiProblem.Empty));
}
public sealed class FakeAuditLogsApiClient : AdministrationWebFakeBase, W.IAuditLogsApiClient
{
    public W.AuditLogContract? Detail { get; set; }
    public W.PagedContract<W.AuditLogContract> ListValue { get; set; } = new([], 0, 1, 20, 0, false, false);
    public IReadOnlyList<W.AuditActorContract>? Actors { get; set; } = [];
    public HttpStatusCode ActorsStatusCode { get; set; } = HttpStatusCode.OK;
    public Task<W.WebApiResult<W.PagedContract<W.AuditLogContract>>> ListAsync(W.ManagementListQuery query, CancellationToken ct = default) => Task.FromResult(new W.WebApiResult<W.PagedContract<W.AuditLogContract>>(HttpStatusCode.OK, ListValue, W.ApiProblem.Empty));
    public Task<W.WebApiResult<W.AuditLogContract>> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(new W.WebApiResult<W.AuditLogContract>(Detail is null ? StatusCode : HttpStatusCode.OK, Detail, W.ApiProblem.Empty));
    public Task<W.WebApiResult<IReadOnlyList<W.AuditActorContract>>> GetActorsAsync(CancellationToken ct = default)
    {
        HttpStatusCode status = ActorsStatusCode;
        ActorsStatusCode = HttpStatusCode.OK;
        return Task.FromResult(new W.WebApiResult<IReadOnlyList<W.AuditActorContract>>(status, status == HttpStatusCode.OK ? Actors : null, W.ApiProblem.Empty));
    }
}
public sealed class FakeProfileApiClient : AdministrationWebFakeBase, W.IProfileApiClient
{
    public Task<W.WebApiResult<W.AdminUserContract>> GetAsync(CancellationToken ct = default) => Task.FromResult(Result<W.AdminUserContract>());
    public Task<W.WebApiResult<object>> ChangePasswordAsync(object body, CancellationToken ct = default) { MutationCalls++; return Task.FromResult(Result<object>(StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices ? new object() : null)); }
}
