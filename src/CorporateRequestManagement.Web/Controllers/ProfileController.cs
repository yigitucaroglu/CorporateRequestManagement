using CorporateRequestManagement.Web.Api;
using CorporateRequestManagement.Web.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;
[Authorize, Route("Profile")]
public sealed class ProfileController(IProfileApiClient api, ILocalAuthenticationState localState, IWebApiErrorMapper errors) : Controller
{
    [HttpGet("")] public async Task<IActionResult> Index(CancellationToken ct) { var r = await api.GetAsync(ct); return r.Value is null ? await errors.MapReadAsync(this, r.StatusCode, ct) : View(r.Value); }
    [HttpGet("ChangePassword")] public IActionResult ChangePassword() => View();
    [HttpPost("ChangePassword"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmNewPassword, CancellationToken ct)
    {
        if (newPassword != confirmNewPassword) { ModelState.AddModelError("confirmNewPassword", "Yeni parola tekrarı eşleşmiyor."); return View(); }
        var r = await api.ChangePasswordAsync(new { currentPassword, newPassword, confirmNewPassword }, ct);
        if (!r.IsSuccess)
        {
            if (r.StatusCode == System.Net.HttpStatusCode.BadRequest) { errors.AddValidationErrors(this, r.Problem, "Parola değiştirilemedi. Bilgileri kontrol edip yeniden deneyin."); return View(); }
            return await errors.MapMutationAsync(this, r.StatusCode, nameof(ChangePassword), cancellationToken: ct);
        }
        await localState.ClearAsync(ct); TempData["Success"] = "Parolanız değiştirildi. Yeni parolanızla giriş yapın."; return RedirectToAction("Login", "Account");
    }
}
