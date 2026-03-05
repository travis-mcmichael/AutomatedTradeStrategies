// See https://aka.ms/new-console-template for more information
using System.Collections;
using System.Globalization;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;
using Microsoft.VisualBasic;

namespace AutomatedTrading.Import.Bar;
class Program
{
    static string INPUT_FILE = "";
    static string ConnectionString = "";

    static void Main(string[] args)
    {
        Console.WriteLine("Loading bars...");
        var bars = LoadBars(INPUT_FILE);
        Console.WriteLine($"Loaded {bars.Count} bars.");
        Console.WriteLine("Computing features...");
        ComputeFeatures(bars);
        Console.WriteLine("Computing future labels...");


    }

    static void ComputeFutureLabels(List<BarData> bars)
    {
        List<IBarModel> b = (List<IBarModel>)bars.Select(b => b);
        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            bar.FutureReturn_1 = Computations.FutureReturn(b, i, 1);
            bar.FutureReturn_3 = Computations.FutureReturn(b, i, 3);
            bar.FutureReturn_5 = Computations.FutureReturn(b, i, 5);
            bar.FutureReturn_10 = Computations.FutureReturn(b, i, 10);

            bar.Label_Up_1 = bar.FutureReturn_1 > 0 ? 1 : 0;
            bar.Label_Up_3 = bar.FutureReturn_3 > 0 ? 1 : 0;
            bar.Label_Up_5 = bar.FutureReturn_5 > 0 ? 1 : 0;
            bar.Label_Up_10 = bar.FutureReturn_10 > 0 ? 1 : 0;
        }
    }

    static void ComputeFeatures(List<BarData> bars)
    {
        double cumulativePV = 0;
        double cumulativeVolume = 0;
        DateTime? currentSessionDate = null;

        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            if (currentSessionDate.HasValue || bar.Timestamp.Value != currentSessionDate.Value)
            {
                cumulativePV = 0;
                cumulativeVolume = 0;
                currentSessionDate = bar.Timestamp.Value;
            }

            cumulativePV += bar.Close * bar.Volume;
            cumulativeVolume += bar.Volume;
            bar.VWAP = cumulativeVolume == 0 ? 0 : cumulativePV / cumulativeVolume;
            // todo : structure?
            bar.TrueRange = Computations.TrueRange(bars, i);
            bar.ATR_14 = Computations.ComputeATR(bars, i, 14);
            bar.RSI_14 = Computations.ComputeRSI(bars, i, 14);
            bar.RSI_7 = Computations.ComputeRSI(bars, i, 7);
            bar.EMA_9 = Computations.ComputeEMA(bars, i, 9, b => b.Close);
            bar.EMA_21 = Computations.ComputeEMA(bars, i, 21, b => b.Close);
            // todo: bar. EMA_Spread?
            // todo: bar.MACD
            // todo: bollinger?
            bar.Momentum_10 = Computations.Momentum(bars, i, 10);
            // todo: returns 1, 3, 5, 10
            // todo: compute session context?
        }
    }

    static List<IBarModel> LoadBars(string path)
    {
        var bars = new List<IBarModel>();
        var lines = File.ReadAllLines(path);
        
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(";");
            var dateTimePart = parts[0];
            var datePart = dateTimePart.Substring(0, 8);
            var timePart = dateTimePart.Substring(9, 6);
            var timestamp = DateTime.ParseExact(
                datePart + timePart,
                "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture);
            bars.Add(new Core.Models.Bar
            {
                Timestamp = timestamp,
                Open = double.Parse(parts[1], CultureInfo.InvariantCulture),
                High = double.Parse(parts[2], CultureInfo.InvariantCulture),
                Low = double.Parse(parts[3], CultureInfo.InvariantCulture),
                Close = double.Parse(parts[4], CultureInfo.InvariantCulture),
                Volume = double.Parse(parts[5], CultureInfo.InvariantCulture)
            });
        }

        return bars;
    }
}