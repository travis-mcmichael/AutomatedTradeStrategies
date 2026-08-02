using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Export.Feature.Exporters;

namespace AutomatedTrading.Export.Feature;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Feature Export starting...");

        var configPath = Operations.NormalizePath(AppConstants.MODELS_CONFIG_PATH);
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Config not found: {configPath}");
            return;
        }

        var factory = new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION);

        var barRepo = new BarRepository(factory);
        var modelRepo = new ModelConfigRepository(factory);

        var models = await modelRepo.GetAllAsync();

        foreach (var model in models.Where(m => m.Active && m.Export))
        {
            Console.WriteLine($"\nExporting: {model.ModelName}");
            var path = Operations.NormalizePath(model.FeatureLocation);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            var outPath = Path.Combine(Operations.NormalizePath(model.FeatureLocation), "features.csv");

            var start = DateTime.ParseExact(model.TrainingStart, "yyyyMMdd", null);
            var end = DateTime.ParseExact(model.TrainingEnd,   "yyyyMMdd", null);

            await new FeatureExporter(model, outPath, barRepo).GenerateFeatures(start, end);
        }

        Console.WriteLine($"\nOutput complete for {models?.Count ?? 0} predictive models.");
    }
}
