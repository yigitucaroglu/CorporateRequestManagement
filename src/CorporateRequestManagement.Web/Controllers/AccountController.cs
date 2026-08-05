using System.Net;
using System.Security.Claims;
using CorporateRequestManagement.Web.Authentication;
using CorporateRequestManagement.Web.Models.Account;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;

[Route("account")]
public sealed class AccountController(IAuthenticationApiClient api, IUserTokenStore tokenStore, ILocalAuthenticationState localAuthenticationState) : Controller
{
    [HttpGet("login"), AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Login(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (await localAuthenticationState.HasValidSessionAsync(cancellationToken)) return RedirectToAction("Index", "Dashboard");
            await localAuthenticationState.ClearAsync(cancellationToken);
        }
        return View(new LoginViewModel { ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null });
    }

    [HttpPost("login"), AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid) { model.Password = string.Empty; return View(model); }
        ApiCallResult<AuthenticationContract> result;
        try { result = await api.LoginAsync(model.Email, model.Password, cancellationToken); }
        catch (HttpRequestException) { ModelState.AddModelError(string.Empty, "Kimlik doğrulama servisine ulaşılamıyor."); model.Password = string.Empty; return View(model); }
        if (result.Value is null)
        {
            string message = result.StatusCode switch { HttpStatusCode.TooManyRequests => "Çok fazla giriş denemesi yapıldı. Lütfen daha sonra tekrar deneyin.", HttpStatusCode.Unauthorized => "E-posta veya parola geçersiz.", _ => "Giriş işlemi tamamlanamadı." };
            ModelState.AddModelError(string.Empty, message); model.Password = string.Empty; return View(model);
        }
        AuthenticationContract auth = result.Value;
        await tokenStore.SetAsync(new WebUserSession { AccessToken = auth.AccessToken, AccessTokenExpiresAt = auth.AccessTokenExpiresAt, RefreshToken = auth.RefreshToken, RefreshTokenExpiresAt = auth.RefreshTokenExpiresAt }, cancellationToken);
        Claim[] claims = [new(ClaimTypes.NameIdentifier, auth.User.Id.ToString()), new(ClaimTypes.Name, $"{auth.User.FirstName} {auth.User.LastName}"), new(ClaimTypes.Email, auth.User.Email), new(ClaimTypes.Role, auth.User.Role)];
        var properties = new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, ExpiresUtc = auth.RefreshTokenExpiresAt };
        await HttpContext.SignInAsync(WebAuthenticationDefaults.Scheme, new ClaimsPrincipal(new ClaimsIdentity(claims, WebAuthenticationDefaults.Scheme)), properties);
        return LocalRedirect(Url.IsLocalUrl(model.ReturnUrl) ? model.ReturnUrl! : "/");
    }

    [HttpPost("logout"), Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        WebUserSession? session = await tokenStore.GetAsync(cancellationToken);
        try { if (session is not null) await api.RevokeAsync(session.RefreshToken, cancellationToken); } catch (HttpRequestException) { }
        finally { await localAuthenticationState.ClearAsync(cancellationToken); }
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("access-denied"), AllowAnonymous] public IActionResult AccessDenied() => View();

    [HttpGet("error"), AllowAnonymous, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Error()
    {
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        ViewData["SafeErrorMessage"] = "Beklenmeyen bir sorun oluştu. Lütfen yeniden deneyin.";
        return View("~/Views/Shared/Error.cshtml");
    }
}
