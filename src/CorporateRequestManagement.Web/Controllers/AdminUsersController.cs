using CorporateRequestManagement.Web.Api;
using CorporateRequestManagement.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;

[Authorize(Policy = "Admin.Exact"), Route("Admin/Users")]
public sealed class AdminUsersController(IAdminUsersApiClient api, IAdminDepartmentsApiClient departments, IWebApiErrorMapper errors) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ManagementListQuery query, CancellationToken ct)
    {
        var result = await api.ListAsync(query, ct);
        if (result.Value is null) return await errors.MapReadAsync(this, result.StatusCode, ct);
        return View(new AdminUsersPage(result.Value, query, await DepartmentLookupAsync(ct)));
    }

    [HttpGet("Create")]
    public async Task<IActionResult> Create(Guid? departmentId, Guid? returnToDepartment, CancellationToken ct)
    {
        IReadOnlyList<DepartmentContract> lookup = await DepartmentLookupAsync(ct);
        Guid? selected = departmentId.HasValue && lookup.Any(x => x.Id == departmentId.Value) ? departmentId : null;
        Guid? safeReturn = returnToDepartment.HasValue && lookup.Any(x => x.Id == returnToDepartment.Value) ? returnToDepartment : null;
        return View(new AdminUserFormPage(null, lookup, selected, safeReturn));
    }

    [HttpPost("Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string firstName, string lastName, string email, string initialPassword, string confirmInitialPassword, UserRoleContract role, Guid? departmentId, bool isActive, Guid? returnToDepartment, CancellationToken ct)
    {
        if (initialPassword != confirmInitialPassword) ModelState.AddModelError("confirmInitialPassword", "Parola tekrarı eşleşmiyor.");
        if (!ModelState.IsValid) return View(new AdminUserFormPage(null, await DepartmentLookupAsync(ct), departmentId, returnToDepartment));
        var result = await api.MutateAsync(HttpMethod.Post, "api/admin/users", new { firstName, lastName, email, initialPassword, confirmInitialPassword, role, departmentId, isActive }, ct);
        if (result.IsSuccess) { TempData["Success"] = "Kullanıcı oluşturuldu."; return returnToDepartment.HasValue ? RedirectToAction("Details", "AdminDepartments", new { id = returnToDepartment.Value }) : RedirectToAction(nameof(Index)); }
        if (result.StatusCode == System.Net.HttpStatusCode.BadRequest) { errors.AddValidationErrors(this, result.Problem, "Kullanıcı oluşturulamadı. Bilgileri kontrol edin."); return View(new AdminUserFormPage(null, await DepartmentLookupAsync(ct), departmentId, returnToDepartment)); }
        if (result.StatusCode is System.Net.HttpStatusCode.Conflict or System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.InternalServerError or System.Net.HttpStatusCode.ServiceUnavailable or System.Net.HttpStatusCode.GatewayTimeout)
        {
            ViewData["SafeErrorMessage"] = result.StatusCode == System.Net.HttpStatusCode.Conflict ? "Bu e-posta adresiyle kayıtlı başka bir kullanıcı bulunuyor." : result.StatusCode == System.Net.HttpStatusCode.TooManyRequests ? "Çok fazla işlem denendi. Lütfen kısa bir süre sonra tekrar deneyin." : "İşlem şu anda tamamlanamadı. Lütfen yeniden deneyin.";
            return View(new AdminUserFormPage(null, await DepartmentLookupAsync(ct), departmentId, returnToDepartment));
        }
        return await errors.MapMutationAsync(this, result.StatusCode, nameof(Create), cancellationToken: ct);
    }

    [HttpGet("{id:guid}")] public async Task<IActionResult> Details(Guid id, CancellationToken ct) { var r = await api.GetAsync(id, ct); return r.Value is null ? await errors.MapReadAsync(this, r.StatusCode, ct) : View(r.Value); }

    [HttpGet("{id:guid}/Edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var result = await api.GetAsync(id, ct);
        return result.Value is null ? await errors.MapReadAsync(this, result.StatusCode, ct) : View(new AdminUserFormPage(result.Value, await DepartmentLookupAsync(ct)));
    }

    [HttpPost("{id:guid}/Edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, string firstName, string lastName, string email, UserRoleContract role, Guid? departmentId, bool isActive, CancellationToken ct)
    {
        var result = await api.MutateAsync(HttpMethod.Put, $"api/admin/users/{id}", new { firstName, lastName, email, role, departmentId, isActive }, ct);
        if (result.IsSuccess) { TempData["Success"] = "Kullanıcı güncellendi."; return RedirectToAction(nameof(Details), new { id }); }
        if (result.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            errors.AddValidationErrors(this, result.Problem, "Kullanıcı bilgileri doğrulanamadı.");
            var current = await api.GetAsync(id, ct);
            return current.Value is null ? await errors.MapReadAsync(this, current.StatusCode, ct) : View(new AdminUserFormPage(current.Value, await DepartmentLookupAsync(ct)));
        }
        return await errors.MapMutationAsync(this, result.StatusCode, nameof(Details), new { id }, ct);
    }

    [HttpPost("{id:guid}/Delete"), ValidateAntiForgeryToken] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) { var r = await api.MutateAsync(HttpMethod.Delete, $"api/admin/users/{id}", new { }, ct); if (r.IsSuccess) { TempData["Success"] = "Kullanıcı silindi."; return RedirectToAction(nameof(Index)); } return await errors.MapMutationAsync(this, r.StatusCode, nameof(Details), new { id }, ct); }
    [HttpPost("{id:guid}/ResetPassword"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(Guid id, string newPassword, string confirmPassword, CancellationToken ct)
    {
        var result = await api.MutateAsync(HttpMethod.Post, $"api/admin/users/{id}/reset-password", new { newPassword, confirmPassword }, ct);
        if (result.IsSuccess) { TempData["Success"] = "Kullanıcının parolası başarıyla sıfırlandı."; return RedirectToAction(nameof(Details), new { id }); }
        if (result.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            AddPasswordValidation(result.Problem);
            var user = await api.GetAsync(id, ct);
            if (user.Value is null) return await errors.MapReadAsync(this, user.StatusCode, ct);
            ViewData["OpenPasswordReset"] = true;
            return View("Details", user.Value);
        }
        return await errors.MapMutationAsync(this, result.StatusCode, nameof(Details), new { id }, ct);
    }

    private void AddPasswordValidation(ApiProblem problem)
    {
        var passwordErrors = new List<string>();
        var confirmationErrors = new List<string>();
        var summaryErrors = new List<string>();
        foreach ((string key, string[] messages) in problem.Errors)
        {
            string field = key.Equals("ConfirmPassword", StringComparison.OrdinalIgnoreCase) ? "confirmPassword"
                : key.Equals("NewPassword", StringComparison.OrdinalIgnoreCase) || key.Equals("Password", StringComparison.OrdinalIgnoreCase) ? "newPassword"
                : string.Empty;
            foreach (string message in messages)
            {
                ModelState.AddModelError(field, message);
                (field == "newPassword" ? passwordErrors : field == "confirmPassword" ? confirmationErrors : summaryErrors).Add(message);
            }
        }
        if (problem.Errors.Count == 0) { const string fallback="Parola bilgileri doğrulanamadı. Alanları kontrol edin."; ModelState.AddModelError(string.Empty, fallback); summaryErrors.Add(fallback); }
        ViewData["PasswordErrors"] = passwordErrors;
        ViewData["ConfirmationErrors"] = confirmationErrors;
        ViewData["PasswordSummaryErrors"] = summaryErrors;
    }

    private async Task<IReadOnlyList<DepartmentContract>> DepartmentLookupAsync(CancellationToken ct)
    {
        var result = await departments.ListAsync(new ManagementListQuery(PageSize: 100, SortBy: "name", SortDirection: "Ascending"), ct);
        return result.Value?.Items ?? [];
    }
}
