using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using AutomatedTradeStrategies.Management.Web.Models;
using AutomatedTrading.Core.Data;
using AutomatedTrading.Core.Data.Queries;
using AutomatedTrading.Core.Import;
using AutomatedTrading.Core.Models;

namespace AutomatedTradeStrategies.Management.Web.Controllers.Application;

public class HomeController(
    ILogger<HomeController> logger,
    GetAllModelConfigs allModelConfigs,
    GetBarDataCountsByDate barDataCountsByDate,
    GetDistinctBarDataInstruments distinctBarDataInstruments,
    IDefaultInsertCommand<BarData> barInsertCommand) : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public async Task<IActionResult> Models()
    {
        var result = (await allModelConfigs.Execute()).OrderBy(m => m.ModelName);
        return Ok(result);
    }

    public async Task<IActionResult> BarDataCounts()
    {
        var result = await barDataCountsByDate.Execute();
        return Ok(result);
    }

    public async Task<IActionResult> Instruments()
    {
        var result = await distinctBarDataInstruments.Execute();
        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> ImportBarData(List<IFormFile> files)
    {
        if (files == null || files.Count == 0)
            return BadRequest("No files were uploaded.");

        var results = new List<object>();
        foreach (var file in files)
        {
            var tempPath = Path.GetTempFileName();
            try
            {
                await using (var stream = System.IO.File.Create(tempPath))
                    await file.CopyToAsync(stream);

                var imported = await BarImporter.ImportBarData(tempPath, file.FileName, barInsertCommand);
                results.Add(new { fileName = file.FileName, imported });
            }
            catch (FormatException ex)
            {
                results.Add(new { fileName = file.FileName, imported = 0, error = ex.Message });
            }
            finally
            {
                System.IO.File.Delete(tempPath);
            }
        }

        return Ok(results);
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
