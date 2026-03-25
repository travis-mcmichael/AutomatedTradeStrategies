using System.Globalization;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Export.Feature.Exporters;

namespace AutomatedTrading.Export.Feature;

class Program
{
    static int sequenceLength    = 100;
    static int predictionHorizon = 20;

    static async Task Main(string[] args)
    {
        Console.WriteLine("Feature Export starting...");
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: Feature Export <output directory> <tick|bar>");
            return;
        }

        string path      = args[0];
        string execution = args[1].Trim().ToLower();

        var factory    = new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION);
        var barRepo    = new BarRepository(factory);
        var tickRepo   = new TickRepository(factory);

        var startDate  = new DateTime(2021, 1, 1);
        var endDate    = new DateTime(2026, 2, 28);

        if (execution == "tick")
        {
            string dir        = Path.Combine(path, "tick");
            Directory.CreateDirectory(dir);
            string outputPath = Path.Combine(dir, "features.csv");
            await GenerateTickFeatureStreaming(outputPath, tickRepo);
        }
        else if (execution == "bar-high")
        {
            string dir        = Path.Combine(path, "bar-high");
            Directory.CreateDirectory(dir);
            string outputPath = Path.Combine(dir, "features.csv");
            await new SessionOpenHighFeatureExporter(outputPath, barRepo).GenerateFeatures(startDate, endDate);
        }
        else if (execution == "bar-low")
        {
            string dir        = Path.Combine(path, "bar-low");
            Directory.CreateDirectory(dir);
            string outputPath = Path.Combine(dir, "features.csv");
            await new SessionOpenLowFeatureExporter(outputPath, barRepo).GenerateFeatures(startDate, endDate);
        }
        else
        {
            Console.WriteLine("Unknown execution type. Expected: tick | bar-high | bar-low");
        }

        Console.WriteLine("Output complete.");
    }

    static async Task GenerateTickFeatureStreaming(string outputPath, ITickRepository tickRepo)
    {
        using var writer = new StreamWriter(outputPath);
        var tickBuffer   = new List<TickData>(sequenceLength + predictionHorizon + 1);
        var featureCount = 10;
        var sampleCount  = 0;
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
            var features     = new double[sequenceLength, featureCount];
            var start        = currentIndex - sequenceLength + 1;

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
            var label        = futurePrice > currentPrice ? 1 : 0;

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
