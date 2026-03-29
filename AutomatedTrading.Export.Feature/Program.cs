using System.Globalization;
using System.Text.Json;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Export.Feature.Exporters;

namespace AutomatedTrading.Export.Feature;

class Program
{
    static int sequenceLength = 100;
    static int predictionHorizon = 20;

    static async Task Main(string[] args)
    {
        Console.WriteLine("Feature Export starting...");

        var configPath = NormalizePath(AppConstants.MODELS_CONFIG_PATH);
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Config not found: {configPath}");
            return;
        }

        var json = await File.ReadAllTextAsync(configPath);
        var config = JsonSerializer.Deserialize<ModelsConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (config?.Models == null || config.Models.Count == 0)
        {
            Console.WriteLine("No models found in config.");
            return;
        }

        var factory = new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION);
        var barRepo = new BarRepository(factory);
        var tickRepo = new TickRepository(factory);
        var today = DateTime.Today.ToString("yyyyMMdd");

        foreach (var model in config.Models.Where(m => m.Active && m.Export))
        {
            Console.WriteLine($"\nExporting: {model.ModelName}");

            var outPath = Path.Combine(NormalizePath(model.FeatureLocation), "features.csv");

            var start = DateTime.ParseExact(model.TrainingStart, "yyyyMMdd", null);
            var end = DateTime.ParseExact(model.TrainingEnd,   "yyyyMMdd", null);

            switch (model.ModelName)
            {
                case "tick":
                    await GenerateTickFeatureStreaming(outPath, tickRepo);
                    break;
                case "mes.day-bar.high":
                    await new SessionOpenHighFeatureExporter(outPath, barRepo).GenerateFeatures(start, end);
                    break;
                case "mes.day-bar.low":
                    await new SessionOpenLowFeatureExporter(outPath, barRepo).GenerateFeatures(start, end);
                    break;
                case "mes.trade-day-bar.high":
                    await new TradeDayOpenHighFeatureExporter(outPath, barRepo).GenerateFeatures(start, end);
                    break;
                case "mes.trade-day-bar.low":
                    await new TradeDayOpenLowFeatureExporter(outPath, barRepo).GenerateFeatures(start, end);
                    break;
                default:
                    Console.WriteLine($"Unknown model name '{model.ModelName}'. Skipping.");
                    break;
            }
        }

        Console.WriteLine("\nOutput complete.");
    }

    // Converts paths between WSL (/mnt/c/...) and Windows (C:\...) formats
    // based on the current OS, so the same config works in both environments.
    static string NormalizePath(string path)
    {
        if (OperatingSystem.IsWindows() && path.StartsWith("/mnt/") && path.Length > 6)
        {
            var drive = char.ToUpper(path[5]);
            var rest  = path[6..].Replace('/', '\\');
            return $"{drive}:{rest}";
        }
        if (!OperatingSystem.IsWindows() && path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':')
        {
            var drive = char.ToLower(path[0]);
            var rest  = path[2..].Replace('\\', '/');
            return $"/mnt/{drive}{rest}";
        }
        return path;
    }

    static async Task GenerateTickFeatureStreaming(string outputPath, ITickRepository tickRepo)
    {
        using var writer = new StreamWriter(outputPath);
        var tickBuffer = new List<TickData>(sequenceLength + predictionHorizon + 1);
        var featureCount = 10;
        var sampleCount = 0;
        var headerWritten= false;

        await tickRepo.StreamTicksAsync(async tick =>
        {
            tickBuffer.Add(tick);
            if (tickBuffer.Count < sequenceLength + predictionHorizon)
                return;

            if (!headerWritten)
            {
                for (var t = 0; t < sequenceLength; t++)
                    for (var f = 0; f < featureCount; f++)
                        writer.Write($"f_{t}_{f},");
                writer.WriteLine("label");
                headerWritten = true;
            }

            var currentIndex = tickBuffer.Count - predictionHorizon - 1;
            var features = new double[sequenceLength, featureCount];
            var start = currentIndex - sequenceLength + 1;

            for (var j = 0; j < sequenceLength; j++)
            {
                var idx = start + j;
                features[j, 0] = Computations.Return(tickBuffer, idx, 1);
                features[j, 1] = Computations.Return(tickBuffer, idx, 5);
                features[j, 2] = Computations.RollingVolatility(tickBuffer, idx, 20);
                features[j, 3] = Computations.Spread(tickBuffer[idx]);
                features[j, 4] = Computations.MidReturn(tickBuffer, idx);
                features[j, 5] = Computations.RollingVolume(tickBuffer, idx, 20);
                features[j, 6] = Computations.VolumeZScore(tickBuffer, idx, 50);
                features[j, 7] = Computations.Delta(tickBuffer, idx);
                features[j, 8] = Computations.RollingDelta(tickBuffer, idx, 20);
                features[j, 9] = Computations.TickInterval(tickBuffer, idx);
            }

            var currentPrice = tickBuffer[currentIndex].LastPrice;
            var futurePrice  = tickBuffer[currentIndex + predictionHorizon].LastPrice;
            var label = futurePrice > currentPrice ? 1 : 0;

            for (var t = 0; t < sequenceLength; t++)
                for (var f = 0; f < featureCount; f++)
                {
                    await writer.WriteAsync(features[t, f].ToString(CultureInfo.InvariantCulture));
                    await writer.WriteAsync(",");
                }

            await writer.WriteLineAsync(label.ToString());
            sampleCount++;
            tickBuffer.RemoveAt(0);

            if (sampleCount % 10000 == 0)
                Console.WriteLine($"Generated {sampleCount} samples...");
        });

        Console.WriteLine($"Total samples written: {sampleCount}");
    }
}

class ModelsConfig
{
    public List<ModelEntry> Models { get; set; } = [];
}

class ModelEntry
{
    public string ModelName      { get; set; } = "";
    public string Version        { get; set; } = "";
    public string Instrument     { get; set; } = "";
    public string TrainingStart  { get; set; } = "";
    public string TrainingEnd    { get; set; } = "";
    public string FeatureLocation { get; set; } = "";
    public string OutputLocation { get; set; } = "";
    public bool   Export         { get; set; }
    public bool   Active         { get; set; }
}
