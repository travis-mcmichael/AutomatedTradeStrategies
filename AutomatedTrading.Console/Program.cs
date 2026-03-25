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
        var rangeStart = runDate; //new DateTime(2026, 3, 1);
        var rangeEnd = runDate; //DateTime.Today < new DateTime(2026, 12, 31) ? DateTime.Today : new DateTime(2026, 12, 31);
        
        if (runType == "inference")
        {
            var inferenceTest = new InferenceEvaluationTest(barRepo, ModelConfigs);
            await inferenceTest.RunAsync(rangeStart.ToString("yyyyMMdd"), rangeEnd.ToString("yyyyMMdd"));   
        }
        else
        {
            var highConfig = ModelConfigs.First(c => c.IsHigh);
            using var predictor = new ConsoleBarHighPredictor(barRepo, highConfig);

            var runs = new List<BacktestRun>
            {
                new("Prox0.3-SL5-TP20-T10",  new PredictedHighStrategyEvaluator(proximity: 0.3, stopLossPoints: 5.0, targetProfit: 20.0, trail: 10.0)),
                new("Prox0.5-SL5-TP20-T10",  new PredictedHighStrategyEvaluator(proximity: 0.5, stopLossPoints: 5.0, targetProfit: 20.0, trail: 10.0)),
                new("Prox0.7-SL5-TP20-T10",  new PredictedHighStrategyEvaluator(proximity: 0.7, stopLossPoints: 5.0, targetProfit: 20.0, trail: 10.0)),
            };

            var runner = new BacktestRunner(barRepo, predictor);
            await runner.RunAsync(
                rangeStart.ToString("yyyyMMdd"),
                rangeEnd.ToString("yyyyMMdd"),
                runs,
                log: line => System.Console.WriteLine(line));

            System.Console.WriteLine($"\n{"Run",-30} {"Trades",6} {"Wins",5} {"Losses",6} {"GrossP",9} {"GrossL",9} {"Net",9}");
            System.Console.WriteLine(new string('-', 80));
            foreach (var run in runs)
            {
                System.Console.WriteLine(
                    $"{run.Name,-30} {run.TotalTrades,6} {run.WinningTrades,5} {run.LosingTrades,6}" +
                    $" {run.GrossProfit,9:F2} {run.GrossLoss,9:F2} {run.NetProfit,9:F2}");
            }
        }
    }
}
