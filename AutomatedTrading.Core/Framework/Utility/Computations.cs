using System;
using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Framework.Interfaces;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.Utility;

public class Computations
{
    #region - Bar Computations -
    public static double Return(List<ITradeSpan> bars, int i, int lookback)
    {
        if (i < lookback) return 0;
        return bars[i].Close / bars[i - lookback].Close;
    }

    public static double TrueRange(List<ITradeSpan> bars, int i)
    {
        if (i == 0)
            return bars[i].High - bars[i].Low;

        var highLow = bars[i].High - bars[i].Low;
        var highClose = Math.Abs(bars[i].High - bars[i - 1].Close);
        var lowClose = Math.Abs(bars[i].Low - bars[i - 1].Close);
        return Math.Max(highLow, Math.Max(highClose, lowClose));
    }

    public static double ComputeATR(List<ITradeSpan> bars, int i, int period)
    {
        if (i < period) return 0;
        if (i == period)
        {
            double sum = 0;
            for (int j = 1; j <= period; j++)
                sum += TrueRange(bars, j);

            return sum / period;
        }
        
        return ((ComputeATR(bars, i - 1, period) * (period - 1)) + TrueRange(bars, i)) / period;
    }

    public static double ComputeEMA(List<ITradeSpan> bars, int i, int period, Func<ITradeSpan, double> selector)
    {
        var k = 2.0 / (period + 1);
        if (i == 0)
            return selector(bars[0]);
            
        return selector(bars[i]) * k * ComputeEMA(bars, i - 1, period, selector) * (1 - k);
    }

    public static double ComputeRSI(List<ITradeSpan> bars, int i, int period)
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

    public static double ComputeADX(List<ITradeSpan> bars, int i, int period)
    {
        if (i < period * 2)
            return 0;

        double plusDM = 0;
        double minusDM = 0;
        double tr = 0;
        for (var j = i - period + 1; j <= i; j++)
        {
            var current = bars[j];
            var prev = bars[j - 1];
            var upMove = current.High - prev.High;
            var downMove = prev.Low - current.Low;
            double plus = 0;
            double minus = 0;
            if (upMove > downMove && upMove > 0)
                plus = upMove;
            if (downMove > upMove && downMove > 0)
                minus = downMove;
            plusDM += plus;
            minusDM += minus;
            var tr1 = current.High - current.Low;
            var tr2 = Math.Abs(current.High - prev.Close);
            var tr3 = Math.Abs(current.Low - prev.Close);
            tr += Math.Max(tr1, Math.Max(tr2, tr3));
        }

        if (tr == 0)
            return 0;
        double plusDI = 100 * (plusDM / tr);
        double minusDI = 100 * (minusDM / tr);
        double dx = 100 * Math.Abs(plusDI - minusDI) / (plusDI + minusDI);
        return dx;
    }

    public static double SMA(List<ITradeSpan> bars, int i, int period)
    {
        if (i < period) return 0;
        return bars.Skip(i - period + 1)
        .Take(period)
        .Average(b => b.Close);
    }

    public static double StandardDeviation(List<ITradeSpan> bars, int i, int period)
    {
        var mean = SMA(bars, i, period);
        var slice = bars.Skip(i - period + 1).Take(period);
        var variance = slice.Sum(b => Math.Pow(b.Close - mean, 2)) / period;
        return Math.Sqrt(variance);
    }

    public static double HighLowRange(ITradeSpan bar)
    {
        return bar.High - bar.Low;
    }

    public static double BodySize(ITradeSpan bar)
    {
        return Math.Abs(bar.Close - bar.Open);
    }

    public static double UpperWick(ITradeSpan bar)
    {
        return bar.High - Math.Max(bar.Close, bar.Open);
    }

    public static double LowerWick(ITradeSpan bar)
    {
        return Math.Min(bar.Open, bar.Close) - bar.Low;
    }

    public static double ClosePosition(ITradeSpan bar)
    {
        return (bar.Close - bar.Low) / (bar.High - bar.Low == 0 ? 1 : bar.High - bar.Low);
    }

    public static double RealizedVolatility(List<ITradeSpan> bars, int i, int period)
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

    public static double Momentum(List<ITradeSpan> bars, int i, int window = 10)
    {
        if (i < window || bars.Count < window) return 0;
        return bars[i].Close - bars[i - window].Close;
    }

    public static double VWAP(ITradeSpan bar, double cumulativePv, double cumulativeVol)
    {
        cumulativePv += bar.Close * bar.Volume;
        cumulativeVol += bar.Volume;

        return cumulativePv / cumulativeVol;
    }

    public static double MinutesFromOpen(BarData bar, DateTime sessionOpen)
    {
        return (bar.Timestamp - sessionOpen).TotalMinutes;
    }

    public static double FutureReturn(List<ITradeSpan> bars, int i, int lookahead)
    {
        if (i + lookahead >= bars.Count) return 0;
        return (bars[i + lookahead].Close / bars[i].Close) - 1.0;
    }

    public static double VolumeZScore(List<ITradeSpan> bars, int i, int lookback)
    {
        if (i < lookback - 1)
            return double.NaN;
        double sum = 0;
        double sumSq = 0;
        for (var ii = i - lookback + 1; ii <= i; ii++)
        {
            double v = bars[ii].Volume;
            sum += v;
            sumSq += v * v;
        }
        double mean = sum / lookback;
        double variance = (sumSq / lookback) - (mean * mean);
        if (variance <= 0)
            return 0;
        double std = variance > 0 ? Math.Sqrt(variance) : 0;
        return (bars[i].Volume - mean) / std;
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