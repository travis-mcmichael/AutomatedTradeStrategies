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
        new("mes.day-bar.high", "20260325", 38, isHigh: true),
        new("mes.day-bar.low",  "20260325", 39, isHigh: false),
    ];

    static async Task Main(string[] args)
    {
        var runDate = new DateTime(2026, 3, 24);
        var runType = "backtest";
        
        var barRepo = new BarRepository(new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION));
        var rangeStart = new DateTime(2025, 1, 1);
        var rangeEnd = new DateTime(2025, 1, 2); //DateTime.Today < new DateTime(2026, 12, 31) ? DateTime.Today : new DateTime(2026, 12, 31);
        
        if (runType == "inference")
        {
            var inferenceTest = new InferenceEvaluationTest(barRepo, ModelConfigs);
            await inferenceTest.RunAsync(rangeStart.ToString("yyyyMMdd"), rangeEnd.ToString("yyyyMMdd"));   
        }
        else
        {
            var highConfig = ModelConfigs.First(c => c.IsHigh);
            using var predictor = new ConsoleBarHighPredictor(barRepo, highConfig);

            var runs = new List<BacktestRun>();
            // {
            //     new("Prox0.3-SL5-TP20-T10",  new PredictedHighStrategyEvaluator(proximity: 0.3, stopLossPoints: 5.0, targetProfit: 20.0, trail: 10.0)),
            //     new("Prox0.5-SL5-TP20-T10",  new PredictedHighStrategyEvaluator(proximity: 0.5, stopLossPoints: 5.0, targetProfit: 20.0, trail: 10.0)),
            //     new("Prox0.7-SL5-TP20-T10",  new PredictedHighStrategyEvaluator(proximity: 0.7, stopLossPoints: 5.0, targetProfit: 20.0, trail: 10.0)),
            // };

            for (var p = 0.1;p < 1.0;p += 0.1)
            {
                for (var sl = 5.0; sl < 40; sl += 5.0)
                {
                    runs.Add(new($"Prox{p:F2}-{sl}-tp35-t15", new PredictedHighStrategyEvaluator(proximity: p, stopLossPoints: sl, targetProfit: 35.0, trail: 5.0)));
                    runs.Add(new($"Prox{p:F2}-{sl}-tp35-t15", new PredictedHighStrategyEvaluator(proximity: p, stopLossPoints: sl, targetProfit: 35.0, trail: 15.0)));
                    runs.Add(new($"Prox{p:F2}-{sl}-tp25-t15", new PredictedHighStrategyEvaluator(proximity: p, stopLossPoints: sl, targetProfit: 25.0, trail: 15.0)));
                    runs.Add(new($"Prox{p:F2}-{sl}-tp25-t10", new PredictedHighStrategyEvaluator(proximity: p, stopLossPoints: sl, targetProfit: 25.0, trail: 10.0)));
                    runs.Add(new($"Prox{p:F2}-{sl}-tp20-t15", new PredictedHighStrategyEvaluator(proximity: p, stopLossPoints: sl, targetProfit: 20.0, trail: 15.0)));
                }
            }

            var runner = new BacktestRunner(barRepo, predictor);
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
