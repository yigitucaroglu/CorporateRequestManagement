using CorporateRequestManagement.Web.Api;
using CorporateRequestManagement.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;

[Authorize(Policy = "Manager.Exact"), Route("Manager/Requests")]
public sealed class ManagerRequestsController(IManagerRequestsApiClient api, IWebApiErrorMapper errors) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ManagementListQuery query, CancellationToken ct)
    {
        var result = await api.ListAsync(query, ct);
        return result.Value is null ? await errors.MapReadAsync(this, result.StatusCode, ct) : View(new ScopedRequestsPage(result.Value, query, [], false));
    }

    [HttpGet("{id:guid}")] public async Task<IActionResult> Details(Guid id, CancellationToken ct) { var r = await api.GetAsync(id, ct); return r.Value is null ? await errors.MapReadAsync(this, r.StatusCode, ct) : View(r.Value); }
}
