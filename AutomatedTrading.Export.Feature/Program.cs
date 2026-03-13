// See https://aka.ms/new-console-template for more information
using System.Globalization;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;
using Microsoft.Data.Sqlite;

namespace NinjaTickFeatureGenerator;

class Program
{
    static int sequenceLength = 100;
    static int predictionHorizon = 20;
    static async Task Main(string[] args)
    {
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
            string outputPath = $"{path}\\tickFeatures_{DateTime.Today:yyyyMMdd}.csv";
            await GenerateTickFeatureStreaming(outputPath);
        }
        else
        {
            string outputPath = $"{path}\\barFeatures_{DateTime.Today:yyyyMMdd}.csv";
            await GenerateBarFeatureStreaming(outputPath);
        }

        Console.WriteLine("Output complete.");
    }

    static async Task GenerateBarFeatureStreaming(string outputPath, DateTime startDate, DateTime endDate)
    {
        using var writer = new StreamWriter(outputPath);
        var buffer = new List<BarData>(sequenceLength + predictionHorizon + 1);
        var sampleCount = 0;
        var headerWritten = false;

        var currentStart = startDate;
        while (currentStart < endDate)
        {
            var currentEnd = currentStart.AddDays(7);
            if (currentEnd > endDate)
                currentEnd = endDate;

            Console.WriteLine($"Loading bars {currentStart} → {currentEnd}");
            using var reader = await SqliteDataLayer.GetBarDataReader(currentStart, currentEnd);
            while (await reader.ReadAsync())
            {
                var x = 0;
                var bar = new BarData
                {
                    Instrument = reader.GetString(x++),
                    Timeframe = reader.GetString(x++),
                    Timestamp = reader.GetDateTime(x++),
                    SessionDate = reader.GetString(x++),
                    Open = reader.GetDouble(x++),
                    High = reader.GetDouble(x++),
                    Low = reader.GetDouble(x++),
                    Close = reader.GetDouble(x++),
                    Volume = reader.GetDouble(x++)
                };
                buffer.Add(bar);
                if (buffer.Count < sequenceLength + predictionHorizon)
                    continue;
                if (!headerWritten)
                {
                    writer.WriteLine("label");
                    headerWritten = true;
                }
                sampleCount++;
                buffer.RemoveAt(0);
                if (sampleCount % 10000 == 0)
                {
                    Console.WriteLine($"Generated {sampleCount} samples...");
                }
            }
            currentStart = currentEnd;
        }
        Console.WriteLine($"Range exported: {startDate} → {endDate}");
        Console.WriteLine($"Generated {sampleCount} samples total.");
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

    static double? GetNullableDouble(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
        {
            index++;
            return null;
        }

        return reader.GetDouble(index);
    }

    static string? GetNullableString(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
        {
            index++;
            return null;
        }

        return reader.GetString(index);
    }

    static int? GetNullableInt(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
        {
            index++;
            return null;
        }

        return reader.GetInt32(index);
    }
}