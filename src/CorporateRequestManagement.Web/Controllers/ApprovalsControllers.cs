using System.Net;
using CorporateRequestManagement.Web.Api;
using CorporateRequestManagement.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;

[Authorize(Roles = "Manager"), Route("Approvals/Manager")]
public sealed class ManagerApprovalsController(IApprovalsApiClient api) : Controller
{
    [HttpGet("")] public Task<IActionResult> Index([FromQuery] ApprovalListQuery query, CancellationToken ct) => ApprovalControllerFlow.Queue(this, api, false, query, ct);
    [HttpGet("{id:guid}")] public Task<IActionResult> Details(Guid id, CancellationToken ct) => ApprovalControllerFlow.Detail(this, api, false, id, ct);
    [HttpPost("{id:guid}/Approve"), ValidateAntiForgeryToken] public Task<IActionResult> Approve(Guid id, DecisionInputModel model, CancellationToken ct) => ApprovalControllerFlow.Decide(this, api, false, true, id, model, ct);
    [HttpPost("{id:guid}/Reject"), ValidateAntiForgeryToken] public Task<IActionResult> Reject(Guid id, DecisionInputModel model, CancellationToken ct) => ApprovalControllerFlow.Decide(this, api, false, false, id, model, ct);
}
[Authorize(Roles = "Admin"), Route("Approvals/Admin")]
public sealed class AdminApprovalsController(IApprovalsApiClient api) : Controller
{
    [HttpGet("")] public Task<IActionResult> Index([FromQuery] ApprovalListQuery query, CancellationToken ct) => ApprovalControllerFlow.Queue(this, api, true, query, ct);
    [HttpGet("{id:guid}")] public Task<IActionResult> Details(Guid id, CancellationToken ct) => ApprovalControllerFlow.Detail(this, api, true, id, ct);
    [HttpPost("{id:guid}/Approve"), ValidateAntiForgeryToken] public Task<IActionResult> Approve(Guid id, DecisionInputModel model, CancellationToken ct) => ApprovalControllerFlow.Decide(this, api, true, true, id, model, ct);
    [HttpPost("{id:guid}/Reject"), ValidateAntiForgeryToken] public Task<IActionResult> Reject(Guid id, DecisionInputModel model, CancellationToken ct) => ApprovalControllerFlow.Decide(this, api, true, false, id, model, ct);
}
internal static class ApprovalControllerFlow
{
    public static async Task<IActionResult> Queue(Controller controller, IApprovalsApiClient api, bool admin, ApprovalListQuery query, CancellationToken ct)
    { query = query with { PageNumber = WebPaging.Page(query.PageNumber), PageSize = WebPaging.PageSize(query.PageSize) }; WebApiResult<PagedContract<ApprovalQueueItemContract>> r = await api.GetQueueAsync(admin, query, ct); return r.IsSuccess ? controller.View("Index", new ApprovalQueuePage(r.Value!, admin, query)) : Failure(controller, r.StatusCode); }
    public static async Task<IActionResult> Detail(Controller controller, IApprovalsApiClient api, bool admin, Guid id, CancellationToken ct)
    { WebApiResult<ApprovalDetailContract> r = await api.GetDetailAsync(admin, id, ct); return r.IsSuccess ? controller.View("Details", new ApprovalDetailsPage(r.Value!, admin)) : Failure(controller, r.StatusCode); }
    public static async Task<IActionResult> Decide(Controller controller, IApprovalsApiClient api, bool admin, bool approve, Guid id, DecisionInputModel model, CancellationToken ct)
    {
        if (!approve && string.IsNullOrWhiteSpace(model.Reason)) { controller.TempData["Error"] = "Ret gerekçesi zorunludur."; return controller.RedirectToAction("Details", new { id }); }
        WebApiResult<ApprovalDetailContract> r = await api.DecideAsync(admin, id, approve, model.RowVersion, model.Reason, ct);
        controller.TempData[r.IsSuccess ? "Success" : "Error"] = r.IsSuccess ? (approve ? "Talep onaylandı." : "Talep reddedildi.") : Safe(r);
        return controller.RedirectToAction("Details", new { id });
    }
    private static IActionResult Failure(Controller c, HttpStatusCode status) => status switch { HttpStatusCode.Forbidden => c.RedirectToAction("AccessDenied", "Account"), HttpStatusCode.NotFound => c.View("NotFound"), HttpStatusCode.Unauthorized => c.RedirectToAction("Login", "Account"), _ => c.View("Error") };
    private static string Safe<T>(WebApiResult<T> r) => r.StatusCode switch { HttpStatusCode.Conflict when r.Problem.Code == "concurrency.conflict" => "Kayıt başka bir işlem tarafından güncellendi. Son veriyi yeniden yükleyin.", HttpStatusCode.Conflict when r.Problem.Code == "approval.self_action_forbidden" => "Kendi talebiniz üzerinde karar veremezsiniz.", HttpStatusCode.Conflict => "Bu talep daha önce işlenmiş veya artık karar verilebilir durumda değil.", HttpStatusCode.TooManyRequests => "Çok fazla işlem yaptınız, kısa süre sonra tekrar deneyin.", _ => "İşlem tamamlanamadı." };
}
