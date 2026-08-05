using CorporateRequestManagement.Web.Api;
using CorporateRequestManagement.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;

[Authorize(Policy = "Admin.Exact"), Route("Admin/Requests")]
public sealed class AdminRequestsController(IAdminRequestsApiClient api, IAdminDepartmentsApiClient departments, IWebApiErrorMapper errors) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ManagementListQuery query, CancellationToken ct)
    {
        var result = await api.ListAsync(query, ct);
        if (result.Value is null) return await errors.MapReadAsync(this, result.StatusCode, ct);
        var lookup = await departments.ListAsync(new ManagementListQuery(PageSize: 100, SortBy: "name", SortDirection: "Ascending"), ct);
        return View(new ScopedRequestsPage(result.Value, query, lookup.Value?.Items ?? [], true));
    }

    [HttpGet("{id:guid}")] public async Task<IActionResult> Details(Guid id, CancellationToken ct) { var r = await api.GetAsync(id, ct); return r.Value is null ? await errors.MapReadAsync(this, r.StatusCode, ct) : View(r.Value); }
    [HttpPost("{id:guid}/Delete"), ValidateAntiForgeryToken] public async Task<IActionResult> Delete(Guid id, string rowVersion, CancellationToken ct) { var r = await api.DeleteAsync(id, rowVersion, ct); if (r.IsSuccess) { TempData["Success"] = "Talep silindi."; return RedirectToAction(nameof(Index)); } return await errors.MapMutationAsync(this, r.StatusCode, nameof(Details), new { id }, ct); }
}
