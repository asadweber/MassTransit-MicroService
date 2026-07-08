using Microsoft.AspNetCore.Mvc;

namespace SagaDashboard.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();
}
