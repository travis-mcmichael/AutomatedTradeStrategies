using AutomatedTrading.Console.ApiTesting;
using AutomatedTrading.Core.Data;
using AutomatedTrading.Core.Data.Queries;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Strategies;

namespace AutomatedTrading.Console;

class Program
{
    static async Task Main(string[] args)
    {
        var runType = "backtest"; // inference | backtest
        var runDate = new DateTime(2026, 4, 13);
        var rangeStart = runDate;
        var rangeEnd = runDate;

        var getBars = new GetBarsBySessionDate(new DefaultRepository<BarData>(new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION), null));

        if (runType == "inference")
        {
            using var client = new AutomatedTradeClient("console-inference", 0.5, 5.0, 20.0, 10.0);
            var test = new ApiInferenceTest(getBars, client);
            await test.RunAsync(rangeStart.ToString("yyyyMMdd"), rangeEnd.ToString("yyyyMMdd"));
            return;
        }

        // ── Backtest: define parameter sets to sweep ──────────────────────────
        var paramSets = new[]
        {
            new ApiBacktestParams("Prox0.75-SL5-TP20-T10", Proximity: 0.25, StopLoss: 0.25, TargetProfit: 40.0, Trail: 20.0),
        };

        var runner = new ApiBacktestRunner(getBars);
        var results = await runner.RunBatchAsync(
            rangeStart.ToString("yyyyMMdd"),
            rangeEnd.ToString("yyyyMMdd"),
            paramSets);

        printResults(results);
    }

    static void printResults(List<ApiBacktestRun> results)
    {
        System.Console.WriteLine(
            $"\n{"Run",-35}{"Trades",6}{"Wins",5}{"Losses",6}{"GrossP",9}{"GrossL",9}{"Net",9}{"$",9}");
        System.Console.WriteLine(new string('-', 95));

        foreach (var run in results.OrderByDescending(r => r.NetProfit))
        {
            System.Console.WriteLine(
                $"{run.Name,-35}{run.TotalTrades,6}{run.WinningTrades,5}{run.LosingTrades,6}" +
                $"{run.GrossProfit,9:F2}{run.GrossLoss,9:F2}{run.NetProfit,9:F2}{run.NetProfit * AppConstants.DOLLARS_PER_POINT,9:F2}");
        }
    }
}
