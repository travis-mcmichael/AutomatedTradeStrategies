using AutomatedTrading.Core.Data;
using AutomatedTrading.Core.Data.Queries;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Export.Feature;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Feature Export starting...");

        var factory = new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION);

        var streamBars = new StreamBarsInRange(factory, null);
        var getChangeovers = new GetVersionChangeovers(factory);
        var modelRepo = new DefaultRepository<ModelConfig>(factory, null);

        var models = await modelRepo.GetDataAsync(new Dictionary<string, object>());

        foreach (var model in models.Where(m => m.Active && m.Export))
        {
            Console.WriteLine($"\nExporting: {model.ModelName}");
            var path = Operations.NormalizePath(model.FeatureLocation);
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            var outPath = Path.Combine(Operations.NormalizePath(model.FeatureLocation), "features.csv");

            var start = DateTime.ParseExact(model.TrainingStart, "yyyyMMdd", null);
            var end = DateTime.ParseExact(model.TrainingEnd, "yyyyMMdd", null);

            try
            {
                await new FeatureExporter(model, outPath, streamBars, getChangeovers).GenerateFeatures(start, end);
            }
            catch (Exception)
            {
                // Already logged by FeatureExporter; carry on with the remaining models.
            }
        }

        Console.WriteLine($"\nOutput complete for {models?.Count() ?? 0} predictive models.");
    }
}
