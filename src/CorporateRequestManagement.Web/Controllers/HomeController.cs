using Microsoft.AspNetCore.Mvc;

namespace CorporateRequestManagement.Web.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
