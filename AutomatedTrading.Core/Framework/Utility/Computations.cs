using System;
using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.Utility;

public class Computations
{
    #region - Bar Computations -

    public static double SessionRange(List<BarData> bars, int i)
    {
        var session = bars[i].SessionDate;
        double high = double.MinValue;
        double low = double.MaxValue;

        for (var j = i; j >= 0; j--)
        {
            if (bars[j].SessionDate != session)
                break;
            
            if (bars[j].High > high)
                high = bars[j].High;
            if (bars[j].Low < low)
                low = bars[j].Low;
        }

        return high - low;
    }

    public static double SessionVWAP(List<BarData> bars, int i)
    {
        var sessionDate = bars[i].SessionDate;
        double pv = 0;
        double volume = 0;
        for (var j = i; j >= 0; j--)
        {
            if (bars[j].SessionDate != sessionDate)
                break;
            
            pv += bars[j].Close * bars[j].Volume;
            volume += bars[j].Volume;
        }
        return volume == 0 ? bars[i].Close : pv / volume;
    }
    public static double Return(List<BarData> bars, int i, int lookback) 
    {
        if (i < lookback) return 0;
        return Math.Log(bars[i].Close / bars[i - lookback].Close);
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

    public static double AverageRange(List<BarData> bars, int i, int lookback) 
    {
        if (i < lookback - 1)
            return double.NaN;
        
        double sum = 0;
        for (var j = i - lookback + 1; j <= i; j++)
        {
            sum += bars[j].High - bars[j].Low;
        }
        return sum / lookback;
    }

    public static double ATR(List<BarData> bars, int i, int lookback) 
    {
        if (i < lookback) return 0;

        double atr = 0;
        for (var j = i - lookback + 1; j <= i; j++)
        {
            atr += TrueRange(bars, j);
        }

        atr /= lookback;
        return atr;
    }

    public static double EMA(List<BarData> bars, int i, int lookback, Func<BarData, double> selector) 
    {
        var k = 2.0 / (lookback + 1);
        if (i == 0)
            return selector(bars[0]);
            
        return selector(bars[i]) * k * EMA(bars, i - 1, lookback, selector) * (1 - k);
    }

    public static double RSI(List<BarData> bars, int i, int lookback) 
    {
        if (i < lookback) return 0;
        double gain = 0;
        double loss = 0;
        for (int j = i - lookback + 1; j <= i; j++)
        {
            var change = bars[j].Close - bars[j - 1].Close;
            if (change > 0) gain += change;
            else loss -= change;
        }

        if (loss == 0) return 100;

        var rs = gain / lookback / (loss / lookback);
        return 100 - (100 / (1 + rs));
    }

    public static double ADX(List<BarData> bars, int i, int lookback) 
    {
        if (i < lookback * 2)
            return 0;

        double plusDM = 0;
        double minusDM = 0;
        double tr = 0;
        for (var j = i - lookback + 1; j <= i; j++)
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

    public static double SMA(List<BarData> bars, int i, int lookback, Func<BarData, double> selector) 
    {
        if (i < lookback || lookback == 0) return 0;

        double sum = 0.0;
        for (var j = i - lookback + 1; j <= i; j++)
        {
            sum += selector(bars[i]);
        }
        return sum / lookback;
    }

    public static double StandardDeviation(List<BarData> bars, int i, int lookback, Func<BarData, double> selector) 
    {
        var mean = SMA(bars, i, lookback, selector);
        var slice = bars.Skip(i - lookback + 1).Take(lookback);
        double sum = 0.0;
        foreach (var bar in slice)
        {
            sum += Math.Pow(selector(bar) - mean, 2);
        }
        var variance = sum / lookback;
        return Math.Sqrt(variance);
    }

    public static double HighLowRange(BarData bar, double atr) 
    {
        if (atr == 0) return 0;
        return (bar.High - bar.Low) / atr;
    }

    public static double BodySize(BarData bar, double atr) 
    {
        return Math.Abs(bar.Close - bar.Open) / (atr != 0 ? atr : 1);
    }

    public static double UpperWick(BarData bar, double atr) 
    {
        if (atr == 0) return 0;
        return (bar.High - Math.Max(bar.Close, bar.Open)) / atr;
    }

    public static double LowerWick(BarData bar, double atr) 
    {
        if (atr == 0) return 0;
        return (Math.Min(bar.Open, bar.Close) - bar.Low) / atr;
    }

    public static double ClosePosition(BarData bar) 
    {
        return bar.High - bar.Low == 0 ? 0 : (bar.Close - bar.Low) / (bar.High - bar.Low);
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

    public static double Momentum(List<BarData> bars, int i, int lookback) 
    {
        if (i < lookback || bars.Count < lookback) return 0;
        return bars[i].Close - bars[i - lookback].Close;
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

    public static double VolumeZScore(List<BarData> bars, int i, int lookback) 
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
    
    public static double NextSessionHigh(List<BarData> bars, int i, double atr)
    {
        //Console.WriteLine("Computing for NextSessionHigh");
        //Console.WriteLine($"CurrentBar - {bars[i].ToLog()}, atr - {atr}");

        var nextSessionDate = bars[i].SessionDate;
        //Console.WriteLine($"Next Session Date {nextSessionDate}");

        var j = i + 1;
        while (j < bars.Count && bars[j].SessionDate == nextSessionDate)
            j++;
        if (j >= bars.Count)
            return double.NaN;
        var nextOpen = bars[j].Open;
        var nextBars = new List<BarData>();
        
        //Console.WriteLine($"Next session open - {nextOpen}");
        while (j < bars.Count && bars[j].SessionDate != nextSessionDate)
        {
            nextBars.Add(bars[j]);
            j++;
        }

        if (!nextBars.Any() || atr == 0)
            return double.NaN;
        
        var nextHigh = nextBars.Max(x => x.High);
        //Console.WriteLine($"Result next high : {nextHigh}");
        return (nextHigh - nextOpen) / atr;
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