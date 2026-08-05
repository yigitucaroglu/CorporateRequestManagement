using System.Net;
using CorporateRequestManagement.Web.Api;
using CorporateRequestManagement.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;

[Authorize(Policy = "Admin.Exact"), Route("Admin/Departments")]
public sealed class AdminDepartmentsController(IAdminDepartmentsApiClient api, IWebApiErrorMapper errors) : Controller
{
    private static readonly NotFoundPage DepartmentNotFound = new(
        "Departman bulunamadı", "Aradığınız departman mevcut olmayabilir veya bu kaydı görüntüleme yetkiniz bulunmayabilir.",
        "Departmanlara Dön", "AdminDepartments", "Index");

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] ManagementListQuery query, CancellationToken ct)
    {
        var result = await api.ListAsync(query, ct);
        return result.Value is null ? await errors.MapReadAsync(this, result.StatusCode, ct) : View(new AdminDepartmentsPage(result.Value, query));
    }

    [HttpGet("Create")]
    public IActionResult Create() => View();

    [HttpPost("Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, CancellationToken ct)
    {
        var result = await api.CreateAsync(name, ct);
        if (result.Value is not null)
        {
            TempData["Success"] = "Departman oluşturuldu. Talep süreçlerinin başlayabilmesi için bir yönetici atayın.";
            return RedirectToAction(nameof(Details), new { id = result.Value.Id });
        }
        if (result.StatusCode == HttpStatusCode.BadRequest)
        {
            errors.AddValidationErrors(this, result.Problem, "Departman oluşturulamadı. Bilgileri kontrol edin.");
            return View();
        }
        return await errors.MapMutationAsync(this, result.StatusCode, nameof(Create), cancellationToken: ct);
    }

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Details(Guid id, CancellationToken ct) => DepartmentDetailsAsync(id, ct);

    [HttpGet("{id:guid}/Edit")]
    public Task<IActionResult> Edit(Guid id, CancellationToken ct) => DepartmentEditAsync(id, ct);

    [HttpPost("{id:guid}/Edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, string name, CancellationToken ct)
    {
        var result = await api.MutateAsync(HttpMethod.Put, $"api/admin/departments/{id}", new { name }, ct);
        if (result.IsSuccess) { TempData["Success"] = "Departman güncellendi."; return RedirectToAction(nameof(Details), new { id }); }
        if (result.StatusCode == HttpStatusCode.BadRequest)
        {
            errors.AddValidationErrors(this, result.Problem, "Departman bilgileri doğrulanamadı.");
            return await DepartmentEditAsync(id, ct);
        }
        return await errors.MapMutationAsync(this, result.StatusCode, nameof(Details), new { id }, ct);
    }

    [HttpPost("{id:guid}/Manager"), ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignManager(Guid id, Guid? managerId, CancellationToken ct)
    {
        var result = await api.MutateAsync(HttpMethod.Put, $"api/admin/departments/{id}/manager", new { managerId }, ct);
        if (result.IsSuccess) { TempData["Success"] = "Departman yöneticisi güncellendi."; return RedirectToAction(nameof(Details), new { id }); }
        if (IsSeparateFlow(result.StatusCode)) return await errors.MapMutationAsync(this, result.StatusCode, nameof(Details), new { id }, ct);
        errors.AddValidationErrors(this, result.Problem, MutationMessage(result.StatusCode));
        ViewData["SafeErrorMessage"] = MutationMessage(result.StatusCode);
        return await DepartmentDetailsAsync(id, ct);
    }

    [HttpGet("{id:guid}/CreateManager")]
    public Task<IActionResult> CreateManager(Guid id, CancellationToken ct) => CreateManagerPageAsync(id, ct);

    [HttpPost("{id:guid}/CreateManager"), ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateManager(Guid id, string firstName, string lastName, string email,
        string initialPassword, string confirmPassword, bool isActive, CancellationToken ct)
    {
        var result = await api.MutateAsync(HttpMethod.Post, $"api/admin/departments/{id}/manager/create-and-assign",
            new { firstName, lastName, email, initialPassword, confirmPassword, isActive }, ct);
        if (result.IsSuccess)
        {
            TempData["Success"] = "Yönetici oluşturuldu ve departmana atandı.";
            return RedirectToAction(nameof(Details), new { id });
        }
        if (IsSeparateFlow(result.StatusCode)) return await errors.MapMutationAsync(this, result.StatusCode, nameof(CreateManager), new { id }, ct);
        if (result.StatusCode == HttpStatusCode.Conflict && string.Equals(result.Problem.Code, "user.email_exists", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("email", "Bu e-posta adresiyle kayıtlı bir kullanıcı zaten bulunuyor.");
        }
        else
        {
            errors.AddValidationErrors(this, result.Problem, MutationMessage(result.StatusCode));
            if (result.StatusCode != HttpStatusCode.BadRequest) ViewData["SafeErrorMessage"] = MutationMessage(result.StatusCode);
        }
        ViewData["FirstName"] = firstName; ViewData["LastName"] = lastName; ViewData["Email"] = email;
        return await CreateManagerPageAsync(id, ct);
    }

    [HttpPost("{id:guid}/Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await api.MutateAsync(HttpMethod.Delete, $"api/admin/departments/{id}", new { }, ct);
        if (result.IsSuccess) { TempData["Success"] = "Departman silindi."; return RedirectToAction(nameof(Index)); }
        return await errors.MapMutationAsync(this, result.StatusCode, nameof(Details), new { id }, ct);
    }

    private async Task<IActionResult> DepartmentDetailsAsync(Guid id, CancellationToken ct)
    {
        var department = await api.GetAsync(id, ct);
        if (department.Value is null)
        {
            if (department.StatusCode == HttpStatusCode.NotFound) return errors.MapNotFound(this, DepartmentNotFound);
            if (ViewData["SafeErrorMessage"] is not null) return View("Error");
            return await errors.MapReadAsync(this, department.StatusCode, ct);
        }
        var managers = await api.GetEligibleManagersAsync(id, ct);
        if (managers.Value is null) return await errors.MapReadAsync(this,
            managers.StatusCode == HttpStatusCode.NotFound ? HttpStatusCode.BadGateway : managers.StatusCode, ct);
        return View("Details", new AdminDepartmentDetailsPage(department.Value, managers.Value));
    }

    private async Task<IActionResult> DepartmentEditAsync(Guid id, CancellationToken ct)
    {
        var department = await api.GetAsync(id, ct);
        if (department.Value is null) return department.StatusCode == HttpStatusCode.NotFound
            ? errors.MapNotFound(this, DepartmentNotFound) : await errors.MapReadAsync(this, department.StatusCode, ct);
        return View("Edit", new AdminDepartmentDetailsPage(department.Value, []));
    }

    private async Task<IActionResult> CreateManagerPageAsync(Guid id, CancellationToken ct)
    {
        var department = await api.GetAsync(id, ct);
        if (department.Value is null) return department.StatusCode == HttpStatusCode.NotFound
            ? errors.MapNotFound(this, DepartmentNotFound) : await errors.MapReadAsync(this, department.StatusCode, ct);
        return View("CreateManager", new CreateDepartmentManagerPage
        {
            Department = department.Value, FirstName = ViewData["FirstName"] as string ?? string.Empty,
            LastName = ViewData["LastName"] as string ?? string.Empty, Email = ViewData["Email"] as string ?? string.Empty
        });
    }

    private static bool IsSeparateFlow(HttpStatusCode statusCode) => statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound;
    private static string MutationMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => "Çok fazla işlem denendi. Lütfen kısa bir süre sonra tekrar deneyin.",
        HttpStatusCode.Conflict => "İşlem mevcut durumla çelişiyor; sayfayı yenileyip bilgileri kontrol ederek yeniden deneyin.",
        HttpStatusCode.BadRequest => "Bilgiler doğrulanamadı. Alanları kontrol edip yeniden deneyin.",
        _ => "İşlem şu anda tamamlanamadı. Lütfen yeniden deneyin."
    };
}
