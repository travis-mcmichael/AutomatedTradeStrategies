using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework;

public class Importer
{
    public static async Task<int> ImportBarData(string file)
    {
        if (!File.Exists(file))
            return 0;

        var bars = new List<BarData>();
        var totalInserted = 0;

        using var reader = new StreamReader(file);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var bar = parseBarData(line);
            if (bar == null) continue;

            bars.Add(bar);
        }

        computeBarFeatures(bars);
        computeFutureLabels(bars);

        if (bars.Count > 0)
        {
            totalInserted = await SqliteDataLayer.InsertBarDataBatch(bars);
        }

        return totalInserted;
    }
    public static async Task<int> ImportTickData(string file)
    {
        if (!File.Exists(file))
            return 0;

        var tickBatch = new List<TickData>();
        var totalInserted = 0;

        using var reader = new StreamReader(file);
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            var tick = parseTickData(line);
            if (tick == null) continue;

            tickBatch.Add(tick);
        }

        if (tickBatch.Count > 0)
        {
            totalInserted = await SqliteDataLayer.InsertTickBatch(tickBatch);
        }

        return totalInserted;
    }
    private static void computeFutureLabels(List<BarData> bars)
    {
        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            bar.FutureReturn_1 = Computations.FutureReturn(bars, i, 1);
            bar.FutureReturn_3 = Computations.FutureReturn(bars, i, 3);
            bar.FutureReturn_5 = Computations.FutureReturn(bars, i, 5);
            bar.FutureReturn_10 = Computations.FutureReturn(bars, i, 10);

            bar.Label_Up_1 = bar.FutureReturn_1 > 0 ? 1 : 0;
            bar.Label_Up_3 = bar.FutureReturn_3 > 0 ? 1 : 0;
            bar.Label_Up_5 = bar.FutureReturn_5 > 0 ? 1 : 0;
            bar.Label_Up_10 = bar.FutureReturn_10 > 0 ? 1 : 0;
        }
    }
    private static void computeBarFeatures(List<BarData> bars)
    {
        double cumulativePV = 0;
        double cumulativeVolume = 0;
        DateTime? currentSessionDate = null;
        for (var i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            if (currentSessionDate.HasValue && bar.Timestamp.Date.Equals(currentSessionDate.Value.Date))
            {
                cumulativePV = 0;
                cumulativeVolume = 0;
                currentSessionDate = bar.Timestamp;
            }
            cumulativePV += bar.Close * bar.Volume;
            cumulativeVolume += bar.Volume;
            bar.VWAP = cumulativeVolume == 0 ? 0 : cumulativePV / cumulativeVolume;

            bar.TrueRange = Computations.TrueRange(bars, i);
            bar.ATR_14 = Computations.ComputeATR(bars, i, 14);
            bar.RSI_14 = Computations.ComputeRSI(bars, i, 14);
            bar.RSI_7 = Computations.ComputeRSI(bars, i, 7);
            bar.EMA_9 = Computations.ComputeEMA(bars, i, 9, b => b.Close);
            bar.EMA_21 = Computations.ComputeEMA(bars, i, 21, b => b.Close);

            bar.Momentum_10 = Computations.Momentum(bars, i, 10);
        }
    }
    private static BarData? parseBarData(string line)
    {
        var parts = line.Split(';');
        var dateTimePart = parts[0];
        var datePart = dateTimePart.Substring(0, 8);
        var timePart = dateTimePart.Substring(9, 6);
        var timestamp = DateTime.ParseExact(
            datePart + timePart,
            "yyyyMMdd HHmmss",
            CultureInfo.InvariantCulture);
        return new BarData
        {
            Timestamp = timestamp,
            Open = double.Parse(parts[1], CultureInfo.InvariantCulture),
            High = double.Parse(parts[2], CultureInfo.InvariantCulture),
            Low = double.Parse(parts[3], CultureInfo.InvariantCulture),
            Close = double.Parse(parts[4], CultureInfo.InvariantCulture),
            Volume = double.Parse(parts[5], CultureInfo.InvariantCulture)
        };
    }
    private static TickData? parseTickData(string line)
    {
        var parts = line.Split(';');
        if (parts.Length < 5) return null;
        try
        {
            // First field: "20251222 060000 3400000"
            string dateTimeRaw = parts[0].Trim();
            // Split into date + time + fractional
            string[] dtParts = dateTimeRaw.Split(' ');
            if (dtParts.Length > 3 || dtParts.Length < 2) return null;

            string datePart = dtParts[0]; // 20251222
            string timePart = dtParts[1]; // 060000
            string fracPart = dtParts.Length == 3 ? dtParts[2] : "0"; // 3400000 microseconds

            // Parse date and time
            DateTime dt = DateTime.ParseExact(datePart + " " + timePart,
                                              "yyyyMMdd HHmmss",
                                              CultureInfo.InvariantCulture);

            // Add fractional seconds
            if (long.TryParse(fracPart, out long microseconds))
            {
                dt = dt.AddMilliseconds(microseconds / 1000.0);
            }

            // Other fields
            float lastPrice = float.Parse(parts[1], CultureInfo.InvariantCulture);
            float bid = float.Parse(parts[2], CultureInfo.InvariantCulture);
            float ask = float.Parse(parts[3], CultureInfo.InvariantCulture);
            var vol = parts[4].Split('.', StringSplitOptions.RemoveEmptyEntries)[0];
            int volume = int.Parse(vol);
            long tickTime = new DateTimeOffset(dt).ToUnixTimeMilliseconds();

            return new TickData
            {
                Instrument = "ESMAR26", // hardcoded or set dynamically
                TickTime = tickTime,
                LastPrice = lastPrice,
                Volume = volume,
                Bid = bid,
                Ask = ask
            };
        }
        catch
        {
            return null;
        }
    }
}