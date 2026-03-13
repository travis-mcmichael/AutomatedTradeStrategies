using System;
using System.Collections.Generic;
using System.Data.Common;
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
        DateTime? sessionDate = null;
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

        foreach (var bar in bars)
        {
            if (!sessionDate.HasValue || bar.Timestamp.Date != sessionDate.Value.Date)
            {
                sessionDate = bar.Timestamp;
            }

            bar.SessionDate = $"{sessionDate:yyyyMMdd}";
        }

        //computeBarFeatures(bars);
        //computeFutureLabels(bars);

        try
        {
            if (bars.Count > 0)
            {
                totalInserted = await SqliteDataLayer.InsertBarDataBatch(bars);
            }   
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
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
            try
            {
                var bar = bars[i];
				if (!currentSessionDate.HasValue || bar.Timestamp.Date != currentSessionDate.Value.Date)
				{
					cumulativePV = 0;
					cumulativeVolume = 0;
					currentSessionDate = bar.Timestamp;
				}
				cumulativePV += bar.Close * bar.Volume;
				cumulativeVolume += bar.Volume;
				
                bar.Instrument = "MESMAR26";
				bar.Timeframe = "1m";
				bar.SessionDate = $"{getSessionOpen(bar.Timestamp).AddDays(1).Date:yyyyMMdd}";
	
				bar.BidVolume = null;
				bar.AskVolume = null;
				bar.Delta = null;
				bar.CumulativeDelta = null;
	
				bar.HighLowRange = Computations.HighLowRange(bars[i]);
				bar.BodySize = Computations.BodySize(bars[i]);
				bar.UpperWick = Computations.UpperWick(bars[i]);
				bar.LowerWick = Computations.LowerWick(bars[i]);
				bar.ClosePosition = Computations.ClosePosition(bars[i]);
				
				bar.Return_1 = Computations.Return(bars, i, 1);
				bar.Return_3 = Computations.Return(bars, i, 3);
				bar.Return_5 = Computations.Return(bars, i, 5);
				bar.Return_10 = Computations.Return(bars, i, 10);
	
				bar.TrueRange = Computations.TrueRange(bars, i);
				
				bar.ATR_14 = Computations.ComputeATR(bars, i, 14);
				bar.EMA_9 = Computations.ComputeEMA(bars, i, 9, b => b.Close);
				bar.EMA_21 = Computations.ComputeEMA(bars, i, 21, b => b.Close);
				bar.EMASpread = bar.EMA_9 - bar.EMA_21;
				
				bar.ADX_14 = Computations.ComputeADX(bars, i, 14);
	
				bar.MACD = Computations.ComputeEMA(bars, i, 12, b => b.Close) - Computations.ComputeEMA(bars, i, 26, b => b.Close);
				bar.MACDSignal = Computations.ComputeEMA(bars, i, 9, b => b.MACD ?? 0);
				bar.MACDHist = bar.MACD - bar.MACDSignal;
	
				bar.RSI_14 = Computations.ComputeRSI(bars, i, 14);
				bar.RSI_7 = Computations.ComputeRSI(bars, i, 7);
				
				var sma20 = Computations.SMA(bars, i, 20);
				var standardDev = Computations.StandardDeviation(bars, i, 20);
				bar.BB_Upper = sma20 + 2 * standardDev;
				bar.BB_Lower = sma20 - 2 * standardDev;
				bar.BB_Width = bar.BB_Upper - bar.BB_Lower;
				bar.PercentB = (bar.Close - bar.BB_Lower) / (bar.BB_Upper - bar.BB_Lower);
	
				bar.RealizedVol_20 = Computations.RealizedVolatility(bars, i, 20);
				bar.Momentum_10 = Computations.Momentum(bars, i, 10);
				bar.VWAP = cumulativeVolume == 0 ? 0 : cumulativePV / cumulativeVolume;
                bar.VolumeZScore = Computations.VolumeZScore(bars, i, 50);
	
	
				bar.MinutesFromOpen = (bar.Timestamp - getSessionOpen(bar.Timestamp)).TotalMinutes;
				bar.IsOpening = bar.MinutesFromOpen < 30 ? 1 : 0;
				bar.IsMidDay = (bar.MinutesFromOpen >= 30 && bar.MinutesFromOpen < 300) ? 1 : 0;
				bar.IsClosing = bar.MinutesFromOpen >= 300 ? 1 : 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
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
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture);
        return new BarData
        {
            Instrument = "MES",
            Timeframe = "1m",
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
            var vol = parts[4].Split(['.'], StringSplitOptions.RemoveEmptyEntries)[0];
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

    private static DateTime getSessionOpen(DateTime timestamp)
    {
        var sessionStartToday = new DateTime(
            timestamp.Year,
            timestamp.Month,
            timestamp.Day,
            17, 0, 0,
            DateTimeKind.Unspecified);

        if (timestamp >= sessionStartToday)
            return sessionStartToday;

        return sessionStartToday.AddDays(-1);
    }
}