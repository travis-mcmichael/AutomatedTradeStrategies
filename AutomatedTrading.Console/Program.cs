using AutomatedTrading.Console.Backtesting;
using AutomatedTrading.Console.InferenceTesting;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Strategies;

namespace AutomatedTrading.Console;

class Program
{
    static readonly List<ModelConfig> ModelConfigs =
    [
        new("mes.day-bar.high", "20260326", 38, isHigh: true),
        new("mes.day-bar.low",  "20260326", 39, isHigh: false),
    ];

    static async Task Main(string[] args)
    {
        var runDate = new DateTime(2026, 3, 27);
        var runType = "inference";
        
        var barRepo = new BarRepository(new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION));
        var rangeStart = runDate;//new DateTime(2026, 3, 18);
        var rangeEnd = runDate;//new DateTime(2025, 1, 2); //DateTime.Today < new DateTime(2026, 12, 31) ? DateTime.Today : new DateTime(2026, 12, 31);
        
        if (runType == "inference")
        {
            var inferenceTest = new InferenceEvaluationTest(barRepo, ModelConfigs);
            await inferenceTest.RunAsync(rangeStart.ToString("yyyyMMdd"), rangeEnd.ToString("yyyyMMdd"));   
        }
        else
        {
            var highConfig = ModelConfigs.First(c => c.IsHigh);
            using var predictor = new ConsoleBarHighPredictor(barRepo, highConfig);

            var e03 = new SessionHighStrategyEvaluator(proximity: 0.3, stopLossPoints: 5.0, targetProfit: 20.0, trail: 10.0);
            var e05 = new SessionHighStrategyEvaluator(proximity: 0.5, stopLossPoints: 5.0, targetProfit: 20.0, trail: 10.0);

            var runs = new List<BacktestRun>
            {
                new("Prox0.3-SL5-TP20-T10", new HighBacktestEvaluator(e03.StartSession, e03.Evaluate)),
                new("Prox0.5-SL5-TP20-T10", new HighBacktestEvaluator(e05.StartSession, e05.Evaluate)),
            };

            // for (var prox = 0.0; prox < 1.25; prox += 0.25)
            // {
            //     var adjustedProximity = prox == 0.0 ? 0.01 : prox;
            //     for (var stopLoss = 5.0; stopLoss < 50; stopLoss += 2.5)
            //     {
            //         for (var prof = 10.0; prof <= 50.0; prof += 2.50)
            //         {
            //             for (var trail = 5; trail < 25 && trail < prof - 5; trail += 5)
            //             {
            //                 var ev = new SessionHighStrategyEvaluator(adjustedProximity, stopLoss, prof, trail);
            //                 runs.Add(new($"Prox{adjustedProximity:f2}-SL{stopLoss:f2}-TP{prof:f2}-T{trail}", new HighBacktestEvaluator(ev.StartSession, ev.Evaluate)));
            //             }
            //         }
            //     }
            // }

            var runner = new BacktestRunner(barRepo, async (date, bars) =>
            {
                var p = await predictor.PredictHighAsync(date, bars[0].Open);
                return p?.PredictedHigh;
            });
            await runner.RunAsync(
                rangeStart.ToString("yyyyMMdd"),
                rangeEnd.ToString("yyyyMMdd"),
                runs,
                log: line => System.Console.WriteLine(line));

            System.Console.WriteLine($"\n{"Run",-30} {"Trades",6} {"Wins",5} {"Losses",6} {"GrossP",9} {"GrossL",9} {"Net",9} {"$", 9}");
            System.Console.WriteLine(new string('-', 90));
            var ordered = runs.OrderByDescending(x => x.NetProfit);
            foreach (var run in ordered)
            {
                System.Console.WriteLine(
                    $"{run.Name,-30} {run.TotalTrades,6} {run.WinningTrades,5} {run.LosingTrades,6}" +
                    $" {run.GrossProfit,9:F2} {run.GrossLoss,9:F2} {run.NetProfit,9:F2} {run.NetProfit * AppConstants.DOLLARS_PER_POINT,9:F2}");
                // var counter = 0;
                // foreach (var trade in run.Trades)
                // {
                //     System.Console.WriteLine($"\tTrade # {++counter},Session:{trade.SessionDate},Pos:{trade.Side},profit (pts):{trade.PnL} - ${trade.PnL * AppConstants.DOLLARS_PER_POINT}");
                //     System.Console.WriteLine($"\t\tEntryTime:\t{trade.EntryTime:HHmmss}\t{"Entry", 8}:{trade.EntryPrice, 10}");
                //     System.Console.WriteLine($"\t\tExitTime:\t{trade.ExitTime:HHmmss}\t{"Exit", 8}:{trade.ExitPrice, 10}\tReason:{trade.ExitReason, 10}");
                // }
            }
        }
    }
}
