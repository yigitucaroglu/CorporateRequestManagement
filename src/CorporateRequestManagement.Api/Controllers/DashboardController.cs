using CorporateRequestManagement.Application.Features.Dashboard;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Api.Controllers;

[ApiController, Route("api/dashboard"), Authorize]
public sealed class DashboardController(ISender sender) : ControllerBase
{
    [HttpGet("employee"), Authorize(Policy = "Dashboard.Employee")]
    public Task<EmployeeDashboardDto> Employee(CancellationToken ct) => sender.Send(new GetEmployeeDashboardQuery(), ct);
    [HttpGet("manager"), Authorize(Policy = "Requests.ManagerApproval")]
    public Task<ManagerDashboardDto> Manager(CancellationToken ct) => sender.Send(new GetManagerDashboardQuery(), ct);
    [HttpGet("admin"), Authorize(Policy = "Requests.AdminApproval")]
    public Task<AdminDashboardDto> Admin(CancellationToken ct) => sender.Send(new GetAdminDashboardQuery(), ct);
}
