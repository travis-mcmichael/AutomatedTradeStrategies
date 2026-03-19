// See https://aka.ms/new-console-template for more information
using System.Globalization;
using AutomatedTrading.Core.Framework.DataLayer;
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
        var startDate = new DateTime(2025, 12, 1);
        var endDate = new DateTime(2025, 12, 7);

        Console.WriteLine("Feature Export starting...");
        if (args.Length < 2)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine($"Feature Export <db path> <output csv path>");
            return;
        }
        string path = args[0];
        string execution = args[1];

        if (execution.Trim().ToLower() == "tick")
        {
            string outputPath = $"{path}\\tickFeatures.csv";
            await GenerateTickFeatureStreaming(outputPath);
        }
        else
        {
            string outputPath = $"{path}\\barFeatures.csv";
            await new NextDayHighBarFeatureExporter(outputPath).GenerateFeatures(startDate, endDate);
        }

        Console.WriteLine("Output complete.");
    }

    static async Task GenerateTickFeatureStreaming(string outputPath)
    {
        using var reader = await SqliteDataLayer.GetTickDataReader();
        using var writer = new StreamWriter(outputPath);
        var tickBuffer = new List<TickData>(sequenceLength + predictionHorizon + 1);
        var featureCount = 10;
        var sampleCount = 0;
        var headerWritten = false;
        while (await reader.ReadAsync())
        {
            var tick = new TickData
            {
                TickTime = reader.GetInt64(0),
                LastPrice = reader.GetFloat(1),
                Bid = reader.GetFloat(2),
                Ask = reader.GetFloat(3),
                Volume = reader.GetInt32(4)
            };
            tickBuffer.Add(tick);
            if (tickBuffer.Count < sequenceLength + predictionHorizon)
                continue;
            if (!headerWritten)
            {
                for (var t = 0; t < sequenceLength; t++)
                {
                    for (var f = 0; f < featureCount; f++)
                    {
                        writer.Write($"f_{t}_{f},");
                    }
                }
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
            var futurePrice = tickBuffer[currentIndex + predictionHorizon].LastPrice;
            var label = futurePrice > currentPrice ? 1 : 0;
            for (var t = 0; t < sequenceLength; t++)
            {
                for (var f = 0; f < featureCount; f++)
                {
                    writer.Write(features[t, f].ToString(CultureInfo.InvariantCulture));
                    writer.Write(",");
                }
            }
            writer.WriteLine(label);
            sampleCount++;
            tickBuffer.RemoveAt(0);
            if (sampleCount % 10000 == 0)
            {
                Console.WriteLine($"Generated {sampleCount} samples...");
            }
        }

        Console.WriteLine($"Total samples written: {sampleCount}");
    }
}