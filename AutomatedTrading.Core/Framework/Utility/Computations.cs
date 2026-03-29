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
            
            pv += ((bars[j].High + bars[j].Low + bars[j].Close) / 3.0) * bars[j].Volume;
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
            
        return selector(bars[i]) * k + EMA(bars, i - 1, lookback, selector) * (1 - k);
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
        if (plusDI + minusDI == 0)
            return 0;
        double dx = 100 * Math.Abs(plusDI - minusDI) / (plusDI + minusDI);
        return dx;
    }

    public static double SMA(List<BarData> bars, int i, int lookback, Func<BarData, double> selector) 
    {
        if (i < lookback || lookback == 0) return 0;

        double sum = 0.0;
        for (var j = i - lookback + 1; j <= i; j++)
        {
            sum += selector(bars[j]);
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

    public static double VWAP(BarData bar, ref double cumulativePv, ref double cumulativeVol)
    {
        cumulativePv += ((bar.High + bar.Low + bar.Close) / 3.0) * bar.Volume;
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
        return (bars[i].Volume - mean) / Math.Sqrt(variance);
    }
    
    public static double NextSessionHigh(List<BarData> bars, int i, double atr)
    {
        var currentSessionDate = bars[i].SessionDate;

        var j = i + 1;
        while (j < bars.Count && bars[j].SessionDate == currentSessionDate)
            j++;
        if (j >= bars.Count)
            return double.NaN;
        var nextSessionDate = bars[j].SessionDate;
        var nextOpen = bars[j].Open;
        var nextBars = new List<BarData>();

        while (j < bars.Count && bars[j].SessionDate == nextSessionDate)
        {
            nextBars.Add(bars[j]);
            j++;
        }

        if (!nextBars.Any() || atr == 0)
            return double.NaN;

        var nextHigh = nextBars.Max(x => x.High);
        return (nextHigh - nextOpen) / atr;
    }

    public static double OvernightRange(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList(); 
        var overnight = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME || 
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        return overnight.Count != 0 ? Operations.SafeDiv(overnight.Max(b => b.High) - overnight.Min(b => b.Low), atr) : 0;
    }

    public static double OvernightVolumeRatio(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var totalVolume = ordered.Sum(b => b.Volume);
        var overnight = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME || 
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        return Operations.SafeDiv(overnight.Sum(b => b.Volume), totalVolume);
    }

    public static double OvernightDropRatio(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var sessionOpen = ordered.First().Open;
        var overnight = ordered.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME || 
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        var overnightLow = overnight.Count != 0 ? overnight.Min(b => b.Low) : sessionOpen;

        return Operations.SafeDiv(sessionOpen - overnightLow, atr);
    }

    public static double RegularHoursRange(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var rth = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME || 
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        return rth.Count != 0 ? Operations.SafeDiv(rth.Max(b => b.High) - rth.Min(b => b.Low), atr) : 0;
    }

    public static double FirstHourRange(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var firstHour = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME && 
            b.Timestamp.TimeOfDay < AppConstants.RTH_START_TIME + TimeSpan.FromHours(1)).ToList();
        return firstHour.Count != 0 ? Operations.SafeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr) : 0;
    }

    public static double LastHourRange(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var lastHour  = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= AppConstants.RTH_END_TIME - TimeSpan.FromHours(1) &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME).ToList();
        return lastHour.Count != 0 ? Operations.SafeDiv(lastHour.Max(b => b.High) - lastHour.Min(b => b.Low), atr) : 0;
    }

    public static double HighTimeRatio(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var high = ordered.Max(b => b.High);
        var highBarIndex = ordered.IndexOf(ordered.First(b => b.High == high)!);
        return ordered.Count > 1 ? (double)highBarIndex / (ordered.Count - 1) : 0.5;
    }

    public static double LowTimeRatio(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var low = ordered.Min(b => b.Low);
        var lowBarIndex = ordered.IndexOf(ordered.First(b => b.Low == low)!);
        return ordered.Count > 1 ? (double)lowBarIndex / (ordered.Count - 1) : 0.5;
    }

    public static double PrevOpenToHighRatio(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var sessionOpen = ordered.First().Open;
        var sessionHigh  = ordered.Max(b => b.High);
        return Operations.SafeDiv(sessionHigh - sessionOpen, atr);
    }

    public static double PrevOpenToLowRatio(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var sessionOpen = ordered.First().Open;
        var sessionLow   = ordered.Min(b => b.Low);
        return Operations.SafeDiv(sessionOpen - sessionLow, atr);
    }

    public static double LastHourClosePosition(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var lastHour = ordered.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_END_TIME - TimeSpan.FromHours(1) &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME).ToList();
        double lastHourClosePos = 0.5;
        if (lastHour.Count != 0)
        {
            var lhHigh = lastHour.Max(b => b.High);
            var lhLow = lastHour.Min(b => b.Low);
            var lhClose = lastHour.OrderBy(b => b.Timestamp).Last().Close;
            lastHourClosePos = lhHigh == lhLow ? 0.5 : (lhClose - lhLow) / (lhHigh - lhLow);
        }
        return lastHourClosePos;
    }

    // ── RTH-scoped intraday features (prev session) ───────────────────────────

    /// <summary>High-low range of the RTH window only, normalised by ATR.</summary>
    public static double RthRange(List<BarData> bars, double atr)
    {
        var rth = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME).ToList();
        return rth.Count != 0 ? Operations.SafeDiv(rth.Max(b => b.High) - rth.Min(b => b.Low), atr) : 0;
    }

    /// <summary>Range of the first 30 minutes of RTH, normalised by ATR.</summary>
    public static double FirstHalfHourRange(List<BarData> bars, double atr)
    {
        var firstHalfHour = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME + TimeSpan.FromMinutes(30)).ToList();
        return firstHalfHour.Count != 0
            ? Operations.SafeDiv(firstHalfHour.Max(b => b.High) - firstHalfHour.Min(b => b.Low), atr) : 0;
    }

    /// <summary>
    /// Fractional position (0 = start of RTH, 1 = end of RTH) at which the session high
    /// occurred, computed over RTH bars only.
    /// </summary>
    public static double RthHighTimeRatio(List<BarData> bars)
    {
        var rth = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME)
            .OrderBy(b => b.Timestamp).ToList();
        if (rth.Count == 0) return 0.5;
        var max = rth.Max(x => x.High);
        var highBar = rth.FirstOrDefault(b => b.High == max)!;
        var idx = rth.IndexOf(highBar);
        return rth.Count > 1 ? (double)idx / (rth.Count - 1) : 0.5;
    }

    /// <summary>
    /// Fractional position (0 = start of RTH, 1 = end of RTH) at which the session low
    /// occurred, computed over RTH bars only.
    /// </summary>
    public static double RthLowTimeRatio(List<BarData> bars)
    {
        var rth = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME)
            .OrderBy(b => b.Timestamp).ToList();
        if (rth.Count == 0) return 0.5;
        var min = rth.Min(x => x.Low);
        var lowBar = rth.FirstOrDefault(x => x.Low == min)!;
        var idx = rth.IndexOf(lowBar);
        return rth.Count > 1 ? (double)idx / (rth.Count - 1) : 0.5;
    }

    /// <summary>How far above the RTH open the session high reached, normalised by ATR.</summary>
    public static double RthOpenToHighRatio(List<BarData> bars, double atr)
    {
        var rth = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME)
            .OrderBy(b => b.Timestamp).ToList();
        if (rth.Count == 0) return 0;
        var rthOpen = rth.First().Open;
        var rthHigh = rth.Max(b => b.High);
        return Operations.SafeDiv(rthHigh - rthOpen, atr);
    }

    /// <summary>How far below the RTH open the session low reached, normalised by ATR.</summary>
    public static double RthOpenToLowRatio(List<BarData> bars, double atr)
    {
        var rth = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME)
            .OrderBy(b => b.Timestamp).ToList();
        if (rth.Count == 0) return 0;
        var rthOpen = rth.First().Open;
        var rthLow  = rth.Min(b => b.Low);
        return Operations.SafeDiv(rthOpen - rthLow, atr);
    }

    // ── Pre-market features (current session, known before RTH opens) ─────────

    /// <summary>High-low range of the pre-market window, normalised by ATR.</summary>
    public static double PreMarketRange(List<BarData> bars, double atr)
    {
        var pm = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME ||
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        return pm.Count != 0 ? Operations.SafeDiv(pm.Max(b => b.High) - pm.Min(b => b.Low), atr) : 0;
    }

    /// <summary>How far above the RTH open the pre-market high reached, normalised by ATR.</summary>
    public static double PreMarketHighVsOpen(List<BarData> bars, double rthOpen, double atr)
    {
        var pm = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME ||
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        return pm.Count != 0 ? Operations.SafeDiv(pm.Max(b => b.High) - rthOpen, atr) : 0;
    }

    /// <summary>How far below the RTH open the pre-market low reached, normalised by ATR.</summary>
    public static double PreMarketLowVsOpen(List<BarData> bars, double rthOpen, double atr)
    {
        var pm = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME ||
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        return pm.Count != 0 ? Operations.SafeDiv(pm.Min(b => b.Low) - rthOpen, atr) : 0;
    }

    /// <summary>Overnight gap: RTH open minus previous day's close, normalised by ATR.</summary>
    public static double GapToRthOpen(double rthOpen, double prevClose, double atr)
        => Operations.SafeDiv(rthOpen - prevClose, atr);

    /// <summary>Fraction of the full session's volume that traded during the pre-market window.</summary>
    public static double PreMarketVolumeRatio(List<BarData> bars)
    {
        var total = bars.Sum(b => b.Volume);
        var pm = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME ||
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        return Operations.SafeDiv(pm.Sum(b => b.Volume), total);
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
        var lastAsk = ticks[i - 1].Ask ?? 0;

        var mid = (bid + ask) / 2.0;
        var prevMid = (lastBid + lastAsk) / 2.0;
        return (mid - prevMid) / prevMid;
    }

    public static double Spread(TickData t)
    {
        return (t.Ask ?? 0) - (t.Bid ?? 0);
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