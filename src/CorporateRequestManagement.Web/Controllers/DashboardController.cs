using CorporateRequestManagement.Web.Authentication;
using CorporateRequestManagement.Web.Models.Dashboard;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;
[Authorize]
public sealed class DashboardController(IAuthenticatedUserApiClient api, IUserTokenStore store) : Controller
{
    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        AuthenticatedUserContract? user = await api.GetCurrentUserAsync(cancellationToken);
        if (user is null) { await store.ClearAsync(cancellationToken); await HttpContext.SignOutAsync(WebAuthenticationDefaults.Scheme); return RedirectToAction("Login", "Account", new { returnUrl = "/" }); }
        return View(new DashboardViewModel($"{user.FirstName} {user.LastName}", user.Email, user.Role, user.DepartmentId));
    }
}
