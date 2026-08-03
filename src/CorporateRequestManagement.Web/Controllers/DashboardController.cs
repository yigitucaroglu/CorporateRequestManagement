using CorporateRequestManagement.Web.Api;
using CorporateRequestManagement.Web.Authentication;
using CorporateRequestManagement.Web.Models.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;

[Authorize]
public sealed class DashboardController(IAuthenticatedUserApiClient users, IDashboardApiClient dashboards, ILocalAuthenticationState localAuthenticationState) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        AuthenticatedUserContract? current = await users.GetCurrentUserAsync(ct);
        if (current is null) { await localAuthenticationState.ClearAsync(ct); return RedirectToAction("Login", "Account"); }
        string name = $"{current.FirstName} {current.LastName}";
        object? analytics = current.Role switch
        {
            "Employee" => (await dashboards.GetEmployeeAsync(ct)).Value,
            "Manager" => (await dashboards.GetManagerAsync(ct)).Value,
            "Admin" => (await dashboards.GetAdminAsync(ct)).Value,
            _ => null
        };
        if (analytics is null) return View("Error");
        return View(new DashboardViewModel(name, current.Role, analytics));
    }
}
