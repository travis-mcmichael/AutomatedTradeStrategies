using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using AutomatedTradeStrategies.Management.Web.Models;
using AutomatedTrading.Core.Data;
using AutomatedTrading.Core.Models;

namespace AutomatedTradeStrategies.Management.Web.Controllers.Application;

public class HomeController(ILogger<HomeController> logger, IDefaultRepository<ModelConfig> models) : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public async Task<IActionResult> Models()
    {
        var result = await models.GetDataAsync(new Dictionary<string, object> { { "1", 1 } }, "ModelName");
        return Ok(result);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
