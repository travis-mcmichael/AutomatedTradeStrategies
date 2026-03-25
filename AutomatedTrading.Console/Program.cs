using AutomatedTrading.Console.InferenceTesting;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

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

        var barRepo = new BarRepository(new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION));
        var rangeStart = runDate; //new DateTime(2026, 3, 1);
        var rangeEnd = runDate; //DateTime.Today < new DateTime(2026, 12, 31) ? DateTime.Today : new DateTime(2026, 12, 31);
        
        var inferenceTest = new InferenceEvaluationTest(barRepo, ModelConfigs);
        await inferenceTest.RunAsync(rangeStart.ToString("yyyyMMdd"), rangeEnd.ToString("yyyyMMdd"));
    }
}
