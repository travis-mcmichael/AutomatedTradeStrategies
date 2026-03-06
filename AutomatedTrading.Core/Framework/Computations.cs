using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework;

public class Computations
{
    #region - Bar Computations -
    public static double Return(List<BarData> bars, int i, int lookback)
    {
        if (i < lookback) return 0;
        return bars[i].Close / bars[i - lookback].Close;
    }

    public static double TrueRange(List<BarData> bars, int i)
    {
        if (i == 0)
            return bars[i].High - bars[i].Low;

        var highLow = bars[i].High - bars[i].Low;
        var highClose = Math.Abs(bars[i].High - bars[i - 1].Close);
        var lowClose = Math.Abs(bars[i].Low - bars[i - 1].Close);
        return Math.Max(highLow, Math.Max(highClose, lowClose));
    }

    public static double ComputeATR(List<BarData> bars, int i, int period)
    {
        if (i < period) return 0;
        if (i == period)
        {
            double sum = 0;
            for (int j = 1; j <= period; j++)
                sum += bars[j].TrueRange ?? 0;

            return sum / period;
        }

        return ((bars[i - 1].ATR_14 * (period - 1)) + bars[i].TrueRange ?? 0) / period;
    }

    public static double ComputeEMA(List<BarData> bars, int i, int period, Func<BarData, double> selector)
    {
        var k = 2.0 / (period + 1);
        if (i == 0)
            return selector(bars[0]);

        return selector(bars[i]) * k * bars[i - 1].EMA_9 ?? 0 * (1 - k);
    }

    public static double ComputeRSI(List<BarData> bars, int i, int period)
    {
        if (i < period) return 0;
        double gain = 0;
        double loss = 0;
        for (int j = i - period + 1; j <= i; j++)
        {
            var change = bars[j].Close - bars[j - 1].Close;
            if (change > 0) gain += change;
            else loss -= change;
        }

        if (loss == 0) return 100;

        var rs = (gain / period) / (loss / period);
        return 100 - (100 / (1 + rs));
    }

    public static double SMA(List<BarData> bars, int i, int period)
    {
        if (i < period) return 0;
        return bars.Skip(i - period + 1)
        .Take(period)
        .Average(b => b.Close);
    }

    public static double StandardDeviation(List<BarData> bars, int i, int period)
    {
        var mean = SMA(bars, i, period);
        var slice = bars.Skip(i - period + 1).Take(period);
        var variance = slice.Sum(b => Math.Pow(b.Close - mean, 2)) / period;
        return Math.Sqrt(variance);
    }

    public static double HighLowRange(BarData bar)
    {
        return bar.High - bar.Low;
    }

    public static double BodySize(BarData bar)
    {
        return Math.Abs(bar.Close - bar.Open);
    }

    public static double UpperWick(BarData bar)
    {
        return bar.High - Math.Max(bar.Close, bar.Open);
    }

    public static double LowerWick(BarData bar)
    {
        return Math.Min(bar.Open, bar.Close) - bar.Low;
    }

    public static double ClosePosition(BarData bar)
    {
        return (bar.Close - bar.Low) / (bar.High - bar.Low == 0 ? 1 : bar.High - bar.Low);
    }

    public static double RealizedVolatility(List<BarData> bars, int i, int period)
    {
        if (i < period) return 0;
        var returns = new List<double>();
        for (var j = i - period + 1; j <= i; j++)
        {
            returns.Add(Math.Log(bars[j].Close / bars[j - 1].Close));
        }

        var mean = returns.Average();
        var variance = returns.Sum(r => Math.Pow(r - mean, 2)) / period;
        return Math.Sqrt(variance);
    }

    public static double Momentum(List<BarData> bars, int i, int window = 10)
    {
        if (i < window - 1 || bars.Count <= window) return 0;
        return bars[i].Close - bars[i - window].Close;
    }

    public static double VWAP(BarData bar, double cumulativePv, double cumulativeVol)
    {
        cumulativePv += bar.Close * bar.Volume;
        cumulativeVol += bar.Volume;

        return cumulativePv / cumulativeVol;
    }

    public static double MinutesFromOpen(BarData bar, DateTime sessionOpen)
    {
        return (bar.Timestamp - sessionOpen).TotalMinutes;
    }

    public static double FutureReturn(List<BarData> bars, int i, int lookahead)
    {
        if (i + lookahead >= bars.Count) return 0;
        return (bars[i + lookahead].Close / bars[i].Close) - 1.0;
    }

    #endregion

    #region - Tick Calculations - 
    public static double Return(List<TickData> ticks, int i, int lookback)
    {
        if (i - lookback < 0)
            return 0;
        return (ticks[i].LastPrice - ticks[i - lookback].LastPrice) / ticks[i - lookback].LastPrice;
    }

    public static double MidReturn(List<TickData> ticks, int i)
    {
        if (i == 0) return 0;
        var bid = ticks[i].Bid ?? 0;
        var ask = ticks[i].Ask ?? 0;
        var lastBid = ticks[i - 1].Bid ?? 0;
        var lastAsk = ticks[i - 1].Bid ?? 0;

        var mid = (bid + ask) / 2.0;
        var prevMid = (lastBid + lastAsk) / 2.0;
        return (mid - prevMid) / prevMid;
    }

    public static double Spread(TickData t)
    {
        return t.Ask ?? 0 - t.Bid ?? 0;
    }

    public static double TickInterval(List<TickData> ticks, int i)
    {
        if (i == 0) return 0;
        return (ticks[i].TickTime - ticks[i - 1].TickTime) / 1000.0;
    }

    public static double RollingVolatility(List<TickData> ticks, int i, int window)
    {
        if (i - window < 1) return 0;
        var sum = 0.0;
        var sumSq = 0.0;
        var count = 0;

        for (var j = i - window + 1; j <= i; j++)
        {
            var r = (ticks[j].LastPrice - ticks[j - 1].LastPrice) / ticks[j - 1].LastPrice;
            sum += r;
            sumSq += r * r;
            count++;
        }

        if (count < 2) return 0;
        var mean = sum / count;
        var variance = (sumSq / count) - (mean * mean);
        return Math.Sqrt(Math.Max(variance, 0));
    }

    public static double RollingVolume(List<TickData> ticks, int i, int window)
    {
        if (i - window < 0) return 0;
        var sum = 0.0;
        for (var j = i - window + 1; j <= i; j++)
        {
            sum += ticks[j].Volume;
        }
        return sum;
    }

    public static double VolumeZScore(List<TickData> ticks, int i, int window)
    {
        if (i - window < 0)
            return 0;

        var sum = 0.0;
        var sumSq = 0.0;
        var count = 0;
        for (var j = i - window + 1; j <= i; j++)
        {
            var v = ticks[j].Volume;
            sum += v;
            sumSq += v * v;
            count++;
        }
        if (count < 2)
            return 0;
        var mean = sum / count;
        var variance = (sumSq / count) - (mean * mean);
        var std = Math.Sqrt(Math.Max(variance, 0));
        if (std == 0)
            return 0;

        return (ticks[i].Volume - mean) / std;
    }

    public static double Delta(List<TickData> ticks, int i)
    {
        if (i == 0)
            return 0;
        var current = ticks[i].LastPrice;
        var previous = ticks[i - 1].LastPrice;

        if (current > previous)
            return ticks[i].Volume;
        if (current < previous)
            return -ticks[i].Volume;
        return 0;
    }

    public static double RollingDelta(List<TickData> ticks, int i, int window)
    {
        if (i - window < 0)
            return 0;
        double sum = 0;

        for (var j = i - window + 1; j <= i; j++)
        {
            sum += Delta(ticks, j);
        }
        return sum;
    }
    #endregion
}