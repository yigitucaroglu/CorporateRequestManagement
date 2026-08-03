using System.Net;
using CorporateRequestManagement.Web.Api;
using CorporateRequestManagement.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;

[Authorize(Policy = "Requests.SelfService"), Route("Requests")]
public sealed class RequestsController(IRequestsApiClient api) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] RequestListQuery query, CancellationToken ct)
    {
        query = query with
        {
            PageNumber = WebPaging.Page(query.PageNumber),
            PageSize = WebPaging.PageSize(query.PageSize),
            SearchTerm = string.IsNullOrWhiteSpace(query.SearchTerm) ? null : query.SearchTerm.Trim()
        };
        WebApiResult<PagedContract<RequestListItemContract>> result = await api.GetMineAsync(query, ct);
        return result.IsSuccess ? View(new RequestListPage(result.Value!, query)) : ErrorResult(result.StatusCode);
    }
    [HttpGet("Create")] public IActionResult Create() => View(new RequestInputModel());
    [HttpPost("Create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(RequestInputModel model, CancellationToken ct)
    {
        ValidateAmount(model); if (!ModelState.IsValid) return View(model);
        WebApiResult<RequestDetailContract> result = await api.CreateAsync(model.Title, model.Description, model.RequestType, model.Amount, ct);
        if (!result.IsSuccess) { MapProblem(result); return View(model); }
        TempData["Success"] = "Talebiniz oluşturuldu."; return RedirectToAction(nameof(Details), new { id = result.Value!.Id });
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        WebApiResult<RequestDetailContract> detail = await api.GetByIdAsync(id, ct); if (!detail.IsSuccess) return ErrorResult(detail.StatusCode);
        WebApiResult<IReadOnlyList<ApprovalHistoryContract>> history = await api.GetHistoryAsync(id, ct);
        return View(new RequestDetailsPage(detail.Value!, history.Value ?? []));
    }
    [HttpGet("{id:guid}/Edit")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        WebApiResult<RequestDetailContract> result = await api.GetByIdAsync(id, ct); if (!result.IsSuccess) return ErrorResult(result.StatusCode);
        RequestDetailContract x = result.Value!; if (x.Status != RequestStatusContract.Draft) return RedirectToAction(nameof(Details), new { id });
        return View(new RequestInputModel { Id = x.Id, Title = x.Title, Description = x.Description, RequestType = x.RequestType, Amount = x.Amount, RowVersion = x.RowVersion });
    }
    [HttpPost("{id:guid}/Edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, RequestInputModel model, CancellationToken ct)
    {
        model.Id = id; ValidateAmount(model); if (!ModelState.IsValid) return View(model);
        WebApiResult<RequestDetailContract> result = await api.UpdateAsync(id, model.Title, model.Description, model.RequestType, model.Amount, model.RowVersion, ct);
        if (!result.IsSuccess) { MapProblem(result); return View(model); }
        TempData["Success"] = "Talebiniz güncellendi."; return RedirectToAction(nameof(Details), new { id });
    }
    [HttpPost("{id:guid}/Submit"), ValidateAntiForgeryToken] public Task<IActionResult> Submit(Guid id, string rowVersion, CancellationToken ct) => Mutate(id, api.SubmitAsync(id, rowVersion, ct), "Talep onaya gönderildi.");
    [HttpPost("{id:guid}/Cancel"), ValidateAntiForgeryToken] public Task<IActionResult> Cancel(Guid id, string rowVersion, CancellationToken ct) => Mutate(id, api.CancelAsync(id, rowVersion, ct), "Talep iptal edildi.");
    private async Task<IActionResult> Mutate(Guid id, Task<WebApiResult<RequestDetailContract>> call, string message)
    { WebApiResult<RequestDetailContract> result = await call; TempData[result.IsSuccess ? "Success" : "Error"] = result.IsSuccess ? message : SafeMessage(result); return RedirectToAction(nameof(Details), new { id }); }
    private void MapProblem<T>(WebApiResult<T> result) { foreach (var error in result.Problem.Errors) foreach (string message in error.Value) ModelState.AddModelError(error.Key, message); if (result.Problem.Errors.Count == 0) ModelState.AddModelError(string.Empty, SafeMessage(result)); }
    private void ValidateAmount(RequestInputModel model) { if (model.RequestType is RequestTypeContract.Expense or RequestTypeContract.Equipment && !model.Amount.HasValue) ModelState.AddModelError(nameof(model.Amount), "Bu talep türü için tutar zorunludur."); }
    private IActionResult ErrorResult(HttpStatusCode status) => status switch { HttpStatusCode.Forbidden => RedirectToAction("AccessDenied", "Account"), HttpStatusCode.NotFound => View("NotFound"), HttpStatusCode.Unauthorized => RedirectToAction("Login", "Account"), _ => View("Error") };
    private static string SafeMessage<T>(WebApiResult<T> result) => result.StatusCode switch { HttpStatusCode.Conflict when result.Problem.Code == "concurrency.conflict" => "Kayıt başka bir işlem tarafından güncellendi. Son veriyi yeniden yükleyin.", HttpStatusCode.Conflict => "Bu işlem talebin güncel durumunda tamamlanamıyor.", HttpStatusCode.TooManyRequests => "Çok fazla işlem yaptınız, kısa süre sonra tekrar deneyin.", _ => "İşlem tamamlanamadı." };
}
