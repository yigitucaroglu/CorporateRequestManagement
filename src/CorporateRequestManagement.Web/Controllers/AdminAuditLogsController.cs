using CorporateRequestManagement.Web.Api;
using CorporateRequestManagement.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;

[Authorize(Policy = "Admin.Exact"), Route("Admin/AuditLogs")]
public sealed class AdminAuditLogsController(IAuditLogsApiClient api, IWebApiErrorMapper errors) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ManagementListQuery query, CancellationToken ct)
    {
        var result = await api.ListAsync(query, ct);
        if (result.Value is null) return await errors.MapReadAsync(this, result.StatusCode, ct);
        var lookup = await api.GetActorsAsync(ct);
        bool actorFilterAvailable = lookup.Value is not null;
        if (!actorFilterAvailable)
            ViewData["Warning"] = "İşlemi yapan kullanıcı filtresi şu anda yüklenemedi. Kayıtlar görüntülenmeye devam ediyor.";
        return View(new AuditLogsPage(result.Value, query, lookup.Value ?? [], actorFilterAvailable));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var result = await api.GetAsync(id, ct);
        return result.Value is null
            ? await errors.MapReadAsync(this, result.StatusCode, ct)
            : View(new AuditDetailsPage(result.Value, ManagementDisplay.Changes(result.Value.OldValues), ManagementDisplay.Changes(result.Value.NewValues)));
    }
}
