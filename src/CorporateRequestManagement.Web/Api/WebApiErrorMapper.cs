using System.Net;
using CorporateRequestManagement.Web.Authentication;
using CorporateRequestManagement.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Api;

public interface IWebApiErrorMapper
{
    Task<IActionResult> MapReadAsync(Controller controller, HttpStatusCode statusCode, CancellationToken cancellationToken = default);
    IActionResult MapNotFound(Controller controller, NotFoundPage notFound);
    Task<IActionResult> MapMutationAsync(Controller controller, HttpStatusCode statusCode, string fallbackAction, object? routeValues = null,
        CancellationToken cancellationToken = default);
    void AddValidationErrors(Controller controller, ApiProblem problem, string fallbackMessage);
}

internal sealed class WebApiErrorMapper(ILocalAuthenticationState authenticationState) : IWebApiErrorMapper
{
    private static readonly Dictionary<string, string> SafeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FirstName"] = "firstName", ["LastName"] = "lastName", ["Email"] = "email",
        ["InitialPassword"] = "initialPassword", ["Password"] = "initialPassword", ["ConfirmPassword"] = "confirmPassword",
        ["ConfirmInitialPassword"] = "confirmInitialPassword", ["NewPassword"] = "newPassword",
        ["DepartmentId"] = "departmentId", ["ManagerId"] = "managerId", ["Role"] = "role", ["Name"] = "name"
    };
    public async Task<IActionResult> MapReadAsync(Controller controller, HttpStatusCode statusCode, CancellationToken cancellationToken = default)
    {
        if (statusCode == HttpStatusCode.Unauthorized)
        {
            await authenticationState.ClearAsync(cancellationToken);
            string returnUrl = controller.Request.PathBase + controller.Request.Path + controller.Request.QueryString;
            return controller.RedirectToAction("Login", "Account", new { returnUrl });
        }

        return statusCode switch
        {
            HttpStatusCode.Forbidden => controller.RedirectToAction("AccessDenied", "Account"),
            HttpStatusCode.NotFound => controller.View("NotFound"),
            HttpStatusCode.TooManyRequests => ErrorView(controller, "Çok fazla istek gönderildi. Lütfen kısa bir süre sonra yeniden deneyin."),
            HttpStatusCode.ServiceUnavailable => ErrorView(controller, ApiProblem.ServiceUnavailable.Detail!),
            HttpStatusCode.GatewayTimeout => ErrorView(controller, ApiProblem.GatewayTimeout.Detail!),
            _ => ErrorView(controller, "İşlem şu anda tamamlanamıyor. Lütfen daha sonra yeniden deneyin.")
        };
    }

    public IActionResult MapNotFound(Controller controller, NotFoundPage notFound) => controller.View("NotFound", notFound);

    public async Task<IActionResult> MapMutationAsync(Controller controller, HttpStatusCode statusCode, string fallbackAction,
        object? routeValues = null, CancellationToken cancellationToken = default)
    {
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            return await MapReadAsync(controller, statusCode, cancellationToken);

        controller.TempData.Remove("Success");
        string message = statusCode switch
        {
            HttpStatusCode.Conflict => "Kayıt güncel değil veya işlem mevcut durumla çelişiyor. Sayfayı yenileyip yeniden deneyin.",
            HttpStatusCode.TooManyRequests => "Çok fazla istek gönderildi. Lütfen kısa bir süre sonra yeniden deneyin.",
            HttpStatusCode.ServiceUnavailable => ApiProblem.ServiceUnavailable.Detail!,
            HttpStatusCode.GatewayTimeout => ApiProblem.GatewayTimeout.Detail!,
            HttpStatusCode.BadRequest => "Bilgiler doğrulanamadı. Alanları kontrol edip yeniden deneyin.",
            _ => "İşlem şu anda tamamlanamıyor. Lütfen daha sonra yeniden deneyin."
        };
        controller.ViewData["SafeErrorMessage"] = message;
        return controller.View("Error");
    }

    public void AddValidationErrors(Controller controller, ApiProblem problem, string fallbackMessage)
    {
        bool added = false;
        foreach ((string field, string[] messages) in problem.Errors)
        {
            if (!SafeFields.TryGetValue(field, out string? safeField)) continue;
            foreach (string message in messages) { controller.ModelState.AddModelError(safeField, message); added = true; }
        }
        if (!added)
            controller.ModelState.AddModelError(string.Empty, fallbackMessage);
    }

    private static ViewResult ErrorView(Controller controller, string message)
    {
        controller.ViewData["SafeErrorMessage"] = message;
        return controller.View("Error");
    }
}
