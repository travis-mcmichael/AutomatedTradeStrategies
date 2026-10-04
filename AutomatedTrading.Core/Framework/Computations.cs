using System;
using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework;

public class Computations
{
    #region - Bar Computations -

    /// <summary>
    /// Price range of the session so far: highest high minus lowest low from the start of bar <paramref name="i"/>'s
    /// session up to and including bar <paramref name="i"/>. Walks backwards until the SessionDate changes.
    /// Raw price points, not ATR-normalised.
    /// </summary>
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

    /// <summary>
    /// Volume-weighted average price of the session so far: each bar's typical price (high + low + close) / 3 is
    /// weighted by its volume, from the start of bar <paramref name="i"/>'s session up to bar <paramref name="i"/>.
    /// Falls back to bar <paramref name="i"/>'s close when the session has no volume.
    /// </summary>
    public static double SessionVWAP(List<BarData> bars, int i)
    {
        var sessionDate = bars[i].SessionDate;
        double pv = 0;
        double volume = 0;
        for (var j = i; j >= 0; j--)
        {
            if (bars[j].SessionDate != sessionDate)
                break;
            
            pv += (bars[j].High + bars[j].Low + bars[j].Close) / 3.0 * bars[j].Volume;
            volume += bars[j].Volume;
        }
        return volume == 0 ? bars[i].Close : pv / volume;
    }
    /// <summary>
    /// Log return over <paramref name="lookback"/> bars: ln(close[i] / close[i - lookback]). Positive when price rose.
    /// Returns 0 when there aren't <paramref name="lookback"/> earlier bars.
    /// </summary>
    public static double Return(List<BarData> bars, int i, int lookback) 
    {
        if (i < lookback) return 0;
        return Math.Log(bars[i].Close / bars[i - lookback].Close);
    }

    /// <summary>
    /// True range of bar <paramref name="i"/>: the largest of high - low, |high - previous close| and
    /// |low - previous close|, so overnight gaps count as range. For the first bar it's just high - low.
    /// </summary>
    public static double TrueRange(List<BarData> bars, int i) 
    {
        if (i == 0)
            return bars[i].High - bars[i].Low;

        var highLow = bars[i].High - bars[i].Low;
        var highClose = Math.Abs(bars[i].High - bars[i - 1].Close);
        var lowClose = Math.Abs(bars[i].Low - bars[i - 1].Close);
        return Math.Max(highLow, Math.Max(highClose, lowClose));
    }

    /// <summary>
    /// Average of (high - low) over the last <paramref name="lookback"/> bars ending at <paramref name="i"/>.
    /// Unlike <see cref="ATR"/> it ignores gaps from the previous close. Returns NaN when there aren't enough bars.
    /// </summary>
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

    /// <summary>
    /// Average True Range: a simple (unsmoothed) mean of <see cref="TrueRange"/> over the last
    /// <paramref name="lookback"/> bars ending at <paramref name="i"/>. A typical-move-size measure used to
    /// normalise most other features. Returns 0 when <paramref name="i"/> &lt; <paramref name="lookback"/>.
    /// </summary>
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

    /// <summary>
    /// Exponential moving average of <paramref name="selector"/> at bar <paramref name="i"/>, with smoothing factor
    /// k = 2 / (lookback + 1): value[i] * k + EMA[i - 1] * (1 - k), seeded with bar 0's value.
    /// Computed recursively all the way back to bar 0, so each call costs O(i) and recursion depth grows with i.
    /// </summary>
    public static double EMA(List<BarData> bars, int i, int lookback, Func<BarData, double> selector) 
    {
        var k = 2.0 / (lookback + 1);
        if (i == 0)
            return selector(bars[0]);
            
        return selector(bars[i]) * k + EMA(bars, i - 1, lookback, selector) * (1 - k);
    }

    /// <summary>
    /// Relative Strength Index (0–100) over the last <paramref name="lookback"/> close-to-close changes: compares total
    /// gains to total losses, 100 - 100 / (1 + gains / losses). Above 50 means up-moves dominated.
    /// Uses simple sums rather than Wilder's smoothing. Returns 100 when there were no losses, and 0 when there
    /// aren't enough bars.
    /// </summary>
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

    /// <summary>
    /// Trend-strength reading (0–100) from directional movement over the last <paramref name="lookback"/> bars:
    /// sums +DM (up-moves in the high) and -DM (down-moves in the low), turns each into a directional index
    /// relative to true range, and returns DX = 100 * |+DI - -DI| / (+DI + -DI). Higher means a stronger trend
    /// in either direction. This is a single-window DX rather than Wilder's smoothed ADX.
    /// Returns 0 when <paramref name="i"/> &lt; 2 × <paramref name="lookback"/> or there's no range.
    /// </summary>
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

    /// <summary>
    /// Simple moving average of <paramref name="selector"/> (e.g. close or volume) over the last
    /// <paramref name="lookback"/> bars ending at <paramref name="i"/>. Returns 0 when <paramref name="i"/> &lt;
    /// <paramref name="lookback"/> or <paramref name="lookback"/> is 0.
    /// </summary>
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

    /// <summary>
    /// Population standard deviation of <paramref name="selector"/> over the last <paramref name="lookback"/> bars
    /// ending at <paramref name="i"/>: how spread out the values are around their <see cref="SMA"/>.
    /// Has no guard for too-few bars; then <see cref="SMA"/> returns 0 and the result is measured around 0.
    /// </summary>
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

    /// <summary>
    /// The bar's full range (high - low) in ATR units, e.g. 1.5 means the bar spanned one and a half typical moves.
    /// Returns 0 when <paramref name="atr"/> is 0.
    /// </summary>
    public static double HighLowRange(BarData bar, double atr) 
    {
        if (atr == 0) return 0;
        return (bar.High - bar.Low) / atr;
    }

    /// <summary>
    /// Size of the candle body, |close - open|, in ATR units, ignoring direction. When <paramref name="atr"/> is 0
    /// it returns the raw body size in points instead.
    /// </summary>
    public static double BodySize(BarData bar, double atr) 
    {
        return Math.Abs(bar.Close - bar.Open) / (atr != 0 ? atr : 1);
    }

    /// <summary>
    /// Length of the upper wick (high minus the top of the body) in ATR units: how far price was pushed up and
    /// rejected. Returns 0 when <paramref name="atr"/> is 0.
    /// </summary>
    public static double UpperWick(BarData bar, double atr) 
    {
        if (atr == 0) return 0;
        return (bar.High - Math.Max(bar.Close, bar.Open)) / atr;
    }

    /// <summary>
    /// Length of the lower wick (bottom of the body minus low) in ATR units: how far price was pushed down and
    /// rejected. Returns 0 when <paramref name="atr"/> is 0.
    /// </summary>
    public static double LowerWick(BarData bar, double atr) 
    {
        if (atr == 0) return 0;
        return (Math.Min(bar.Open, bar.Close) - bar.Low) / atr;
    }

    /// <summary>
    /// Where the close sits within the bar's range, from 0 (closed at the low) to 1 (closed at the high).
    /// Returns 0 for a bar with no range.
    /// </summary>
    public static double ClosePosition(BarData bar) 
    {
        return bar.High - bar.Low == 0 ? 0 : (bar.Close - bar.Low) / (bar.High - bar.Low);
    }

    /// <summary>
    /// Realized volatility: the standard deviation of bar-to-bar log returns over the last
    /// <paramref name="period"/> bars ending at <paramref name="i"/>. Not annualised.
    /// Returns 0 when there aren't enough bars.
    /// </summary>
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

    /// <summary>
    /// Price change in points over <paramref name="lookback"/> bars: close[i] - close[i - lookback].
    /// Returns 0 when there aren't enough bars.
    /// </summary>
    public static double Momentum(List<BarData> bars, int i, int lookback) 
    {
        if (i < lookback || bars.Count < lookback) return 0;
        return bars[i].Close - bars[i - lookback].Close;
    }

    /// <summary>
    /// Running volume-weighted average price. Adds this bar's typical price × volume and its volume to the
    /// caller's running totals (updated in place) and returns the VWAP so far.
    /// Divides by zero (NaN) if no volume has accumulated yet.
    /// </summary>
    public static double VWAP(BarData bar, ref double cumulativePv, ref double cumulativeVol)
    {
        cumulativePv += (bar.High + bar.Low + bar.Close) / 3.0 * bar.Volume;
        cumulativeVol += bar.Volume;

        return cumulativePv / cumulativeVol;
    }

    /// <summary>
    /// Minutes elapsed between <paramref name="sessionOpen"/> and the bar's timestamp.
    /// </summary>
    public static double MinutesFromOpen(BarData bar, DateTime sessionOpen)
    {
        return (bar.Timestamp - sessionOpen).TotalMinutes;
    }

    /// <summary>
    /// Forward-looking simple return over the next <paramref name="lookahead"/> bars: close[i + lookahead] / close[i] - 1.
    /// Uses future data, so it's for building training targets, not live features.
    /// Returns 0 near the end of the data.
    /// </summary>
    public static double FutureReturn(List<BarData> bars, int i, int lookahead) 
    {
        if (i + lookahead >= bars.Count) return 0;
        return (bars[i + lookahead].Close / bars[i].Close) - 1.0;
    }

    /// <summary>
    /// How unusual bar <paramref name="i"/>'s volume is versus the last <paramref name="lookback"/> bars (including itself),
    /// in standard deviations from their mean: 2 means much heavier than normal, negative means lighter.
    /// Returns NaN when there aren't enough bars and 0 when volume didn't vary.
    /// </summary>
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
    
    /// <summary>
    /// Training target: how far the next session's high rose above that session's open, in ATR units. Skips past
    /// the rest of bar <paramref name="i"/>'s session, then measures the following session.
    /// Uses future data. Returns NaN when there is no next session or <paramref name="atr"/> is 0.
    /// </summary>
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

    /// <summary>
    /// High-low range of the overnight bars (from the 16:00 session open until the 08:30 RTH start), in ATR units.
    /// Returns NaN when there are no overnight bars.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double OvernightRange(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList(); 
        var overnight = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME || 
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        return overnight.Count != 0 ? Operations.SafeDiv(overnight.Max(b => b.High) - overnight.Min(b => b.Low), atr) : double.NaN;
    }

    /// <summary>
    /// High-low range of the regular trading hours bars (08:30 to 15:15), in ATR units.
    /// Returns NaN when there are no RTH bars.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double RegularHoursRange(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var rth = ordered.Where(b => 
        b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
        b.Timestamp.TimeOfDay < AppConstants.RTH_END_TIME).ToList();
        return rth.Count != 0 ? Operations.SafeDiv(rth.Max(b => b.High) - rth.Min(b => b.Low), atr) : double.NaN;
    }

    /// <summary>
    /// Share of the session's total volume that traded overnight (16:00 to 08:30), from 0 to 1.
    /// Returns NaN when there are no overnight bars.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double OvernightVolumeRatio(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var totalVolume = ordered.Sum(b => b.Volume);
        var overnight = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME || 
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        return overnight.Count != 0 ? Operations.SafeDiv(overnight.Sum(b => b.Volume), totalVolume) : double.NaN;
    }

    /// <summary>
    /// How far the overnight low fell below the session's opening price, in ATR units (0 when it never went below).
    /// Uses the first bar's open as the session open.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
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

    /// <summary>
    /// High-low range of the first hour of regular trading (08:30 to 09:30), in ATR units.
    /// Returns 0 when there are no bars in that hour.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double FirstRthHourRange(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var firstHour = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME && 
            b.Timestamp.TimeOfDay < AppConstants.RTH_START_TIME + TimeSpan.FromHours(1)).ToList();
        return firstHour.Count != 0 ? Operations.SafeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr) : 0;
    }

    /// <summary>
    /// High-low range of the last hour of regular trading (14:15 to 15:15), in ATR units.
    /// Returns 0 when there are no bars in that hour.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double LastRthHourRange(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var lastHour  = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= AppConstants.RTH_END_TIME - TimeSpan.FromHours(1) &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME).ToList();
        return lastHour.Count != 0 ? Operations.SafeDiv(lastHour.Max(b => b.High) - lastHour.Min(b => b.Low), atr) : 0;
    }

    /// <summary>
    /// When the session high was made, as a fraction of the way through the bars: 0 = the first bar, 1 = the last.
    /// Based on bar position rather than clock time; if the high is matched more than once, the earliest counts.
    /// Returns 0.5 for a single bar.
    /// </summary>
    public static double HighTimeRatio(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var high = ordered.Max(b => b.High);
        var highBarIndex = ordered.IndexOf(ordered.First(b => b.High == high)!);
        return ordered.Count > 1 ? (double)highBarIndex / (ordered.Count - 1) : 0.5;
    }

    /// <summary>
    /// When the session low was made, as a fraction of the way through the bars: 0 = the first bar, 1 = the last.
    /// Based on bar position rather than clock time; if the low is matched more than once, the earliest counts.
    /// Returns 0.5 for a single bar.
    /// </summary>
    public static double LowTimeRatio(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var low = ordered.Min(b => b.Low);
        var lowBarIndex = ordered.IndexOf(ordered.First(b => b.Low == low)!);
        return ordered.Count > 1 ? (double)lowBarIndex / (ordered.Count - 1) : 0.5;
    }

    /// <summary>
    /// How far the session's high rose above its opening price (the first bar's open), in ATR units.
    /// </summary>
    public static double OpenToHighRatio(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var sessionOpen = ordered.First().Open;
        var sessionHigh  = ordered.Max(b => b.High);
        return Operations.SafeDiv(sessionHigh - sessionOpen, atr);
    }

    /// <summary>
    /// How far the session's low fell below its opening price (the first bar's open), in ATR units, as a positive number.
    /// </summary>
    public static double OpenToLowRatio(List<BarData> bars, double atr)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var sessionOpen = ordered.First().Open;
        var sessionLow   = ordered.Min(b => b.Low);
        return Operations.SafeDiv(sessionOpen - sessionLow, atr);
    }

    /// <summary>
    /// Where the RTH close sits within the last hour's range (14:15 to 15:15), from 0 (at that hour's low) to 1
    /// (at its high). Shows whether the day finished strong or weak. Returns 0.5 when the hour had no range and NaN
    /// when there are no bars in it.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double LastRthHourClosePosition(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var lastHour = ordered.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_END_TIME - TimeSpan.FromHours(1) &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME).ToList();
        double lastHourClosePos = double.NaN;
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

    /// <summary>
    /// High-low range of the regular trading hours bars only (08:30 to 15:15), in ATR units. Unlike
    /// <see cref="RegularHoursRange"/> it doesn't sort first, which doesn't matter for a high-low range.
    /// Returns NaN when there are no RTH bars.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double RthRange(List<BarData> bars, double atr)
    {
        var rth = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME).ToList();
        return rth.Count != 0 ? Operations.SafeDiv(rth.Max(b => b.High) - rth.Min(b => b.Low), atr) : double.NaN;
    }

    /// <summary>
    /// High-low range of the first 30 minutes of regular trading (08:30 to 09:00), in ATR units.
    /// Returns NaN when there are no bars in that window.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double FirstHalfRthHourRange(List<BarData> bars, double atr)
    {
        var firstHalfHour = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME + TimeSpan.FromMinutes(30)).ToList();
        return firstHalfHour.Count != 0
            ? Operations.SafeDiv(firstHalfHour.Max(b => b.High) - firstHalfHour.Min(b => b.Low), atr) : double.NaN;
    }

    /// <summary>
    /// When the RTH high was made, as a fraction of the way through the RTH bars: 0 = the first RTH bar, 1 = the last.
    /// Like <see cref="HighTimeRatio"/> but limited to 08:30–15:15. Returns NaN when there are no RTH bars and
    /// 0.5 for a single bar.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double RthHighTimeRatio(List<BarData> bars)
    {
        var rth = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME)
            .OrderBy(b => b.Timestamp).ToList();
        if (rth.Count == 0) return double.NaN;
        var max = rth.Max(x => x.High);
        var highBar = rth.FirstOrDefault(b => b.High == max)!;
        var idx = rth.IndexOf(highBar);
        return rth.Count > 1 ? (double)idx / (rth.Count - 1) : 0.5;
    }

    /// <summary>
    /// When the RTH low was made, as a fraction of the way through the RTH bars: 0 = the first RTH bar, 1 = the last.
    /// Like <see cref="LowTimeRatio"/> but limited to 08:30–15:15. Returns NaN when there are no RTH bars and
    /// 0.5 for a single bar.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double RthLowTimeRatio(List<BarData> bars)
    {
        var rth = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME)
            .OrderBy(b => b.Timestamp).ToList();
        if (rth.Count == 0) return double.NaN;
        var min = rth.Min(x => x.Low);
        var lowBar = rth.FirstOrDefault(x => x.Low == min)!;
        var idx = rth.IndexOf(lowBar);
        return rth.Count > 1 ? (double)idx / (rth.Count - 1) : 0.5;
    }

    /// <summary>
    /// How far the RTH high rose above the RTH open (the first bar at or after 08:30), in ATR units.
    /// Returns NaN when there are no RTH bars.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double RthOpenToHighRatio(List<BarData> bars, double atr)
    {
        var rth = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME)
            .OrderBy(b => b.Timestamp).ToList();
        if (rth.Count == 0) return double.NaN;
        var rthOpen = rth.First().Open;
        var rthHigh = rth.Max(b => b.High);
        return Operations.SafeDiv(rthHigh - rthOpen, atr);
    }

    /// <summary>
    /// How far the RTH low fell below the RTH open (the first bar at or after 08:30), in ATR units, as a positive
    /// number. Returns NaN when there are no RTH bars.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double RthOpenToLowRatio(List<BarData> bars, double atr)
    {
        var rth = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
            b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME)
            .OrderBy(b => b.Timestamp).ToList();
        if (rth.Count == 0) return double.NaN;
        var rthOpen = rth.First().Open;
        var rthLow  = rth.Min(b => b.Low);
        return Operations.SafeDiv(rthOpen - rthLow, atr);
    }

    // ── Pre-market features (current session, known before RTH opens) ─────────

    /// <summary>
    /// High-low range of the pre-market bars (16:00 session open until the 08:30 RTH start), in ATR units.
    /// Known before the RTH open, so it's usable for trade-day predictions. Returns NaN when there are no
    /// pre-market bars.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double PreMarketRange(List<BarData> bars, double atr)
    {
        var pm = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME ||
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        return pm.Count != 0 ? Operations.SafeDiv(pm.Max(b => b.High) - pm.Min(b => b.Low), atr) : double.NaN;
    }

    /// <summary>
    /// Pre-market high minus the RTH open, in ATR units: positive when pre-market traded above where RTH opened.
    /// Takes the RTH open from the first RTH bar in <paramref name="bars"/>, so those bars must be included.
    /// Returns NaN when there are no RTH or no pre-market bars.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double PreMarketHighVsOpen(List<BarData> bars, double atr)
    {
         var rthBars = bars.Where(b =>
        b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
        b.Timestamp.TimeOfDay < AppConstants.RTH_END_TIME)
        .ToList();
        var pm = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME ||
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        if (rthBars.Count == 0 || pm.Count == 0) return double.NaN;
        var rth = BarData.Aggregate(rthBars);
        return Operations.SafeDiv(pm.Max(b => b.High) - rth.Open, atr);
    }

    /// <summary>
    /// Pre-market low minus the RTH open, in ATR units. This is usually negative (the pre-market low is normally
    /// below the open), unlike <see cref="OpenToLowRatio"/> and <see cref="RthOpenToLowRatio"/>, which return the
    /// drop as a positive number. Takes the RTH open from the first RTH bar in <paramref name="bars"/>.
    /// Returns NaN when there are no RTH or no pre-market bars.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double PreMarketLowVsOpen(List<BarData> bars, double atr)
    {
        var rthBars = bars.Where(b =>
        b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
        b.Timestamp.TimeOfDay < AppConstants.RTH_END_TIME)
        .ToList();
        var pm = bars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME ||
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();
        if (rthBars.Count == 0 || pm.Count == 0) return double.NaN;
        var rth = BarData.Aggregate(rthBars);
        return Operations.SafeDiv(pm.Min(b => b.Low) - rth.Open, atr);
    }

    /// <summary>
    /// Opening gap: the RTH open minus <paramref name="prevClose"/> (the previous session's close), in ATR units.
    /// Positive for a gap up, negative for a gap down. Returns NaN when there are no RTH bars.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double GapToRthOpen(List<BarData> bars, double prevClose, double atr)
    {
        var rthBars = bars.Where(b =>
        b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
        b.Timestamp.TimeOfDay < AppConstants.RTH_END_TIME)
        .ToList();
        if (rthBars.Count == 0) return double.NaN;
        var rth = BarData.Aggregate(rthBars);
        return Operations.SafeDiv(rth.Open - prevClose, atr);
    } 

    /// <summary>
    /// Share of the supplied bars' total volume that traded pre-market (16:00 to 08:30), from 0 to 1.
    /// Returns NaN when there are no pre-market bars.
    /// Window membership is decided by <c>Timestamp.TimeOfDay</c> against the Central-time constants in
    /// <see cref="AppConstants"/>, so it assumes the bars' timestamps are in Central time.
    /// </summary>
    public static double PreMarketVolumeRatio(List<BarData> bars)
    {
        var pm = bars.Where(b =>
        b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME ||
        b.Timestamp.TimeOfDay < AppConstants.RTH_START_TIME)
        .ToList();
        if (pm.Count == 0) return double.NaN;
        return Operations.SafeDiv(pm.Sum(b => b.Volume), bars.Sum(b => b.Volume));
    }

    #endregion

    #region - Tick Calculations - 
    /// <summary>
    /// Simple return between two trades <paramref name="lookback"/> ticks apart:
    /// (price[i] - price[i - lookback]) / price[i - lookback]. Returns 0 when there aren't enough ticks.
    /// </summary>
    public static double Return(List<TickData> ticks, int i, int lookback)
    {
        if (i - lookback < 0)
            return 0;
        return (ticks[i].LastPrice - ticks[i - lookback].LastPrice) / ticks[i - lookback].LastPrice;
    }

    /// <summary>
    /// Simple return of the bid/ask midpoint from the previous tick to this one. A missing bid or ask counts as 0,
    /// so a missing quote on the previous tick divides by zero.
    /// </summary>
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

    /// <summary>
    /// Bid/ask spread in price points (ask - bid). A missing bid or ask counts as 0.
    /// </summary>
    public static double Spread(TickData t)
    {
        return (t.Ask ?? 0) - (t.Bid ?? 0);
    }

    /// <summary>
    /// Seconds since the previous tick (TickTime is in milliseconds). 0 for the first tick.
    /// </summary>
    public static double TickInterval(List<TickData> ticks, int i)
    {
        if (i == 0) return 0;
        return (ticks[i].TickTime - ticks[i - 1].TickTime) / 1000.0;
    }

    /// <summary>
    /// Standard deviation of tick-to-tick simple returns over the last <paramref name="window"/> ticks: how jumpy
    /// trade prices have been. Returns 0 when there aren't enough ticks.
    /// </summary>
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

    /// <summary>
    /// Total volume traded over the last <paramref name="window"/> ticks ending at <paramref name="i"/>.
    /// Returns 0 when there aren't enough ticks.
    /// </summary>
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

    /// <summary>
    /// How unusual tick <paramref name="i"/>'s volume is versus the last <paramref name="window"/> ticks (including
    /// itself), in standard deviations from their mean. Returns 0 when there aren't enough ticks or volume didn't vary.
    /// </summary>
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

    /// <summary>
    /// Signed volume for one trade (tick rule): +volume when the price ticked up from the previous trade (buyers
    /// lifting the offer), -volume when it ticked down, 0 when unchanged or for the first tick.
    /// </summary>
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

    /// <summary>
    /// Net buying vs selling pressure: the sum of <see cref="Delta"/> over the last <paramref name="window"/> ticks.
    /// Positive when more volume traded on up-ticks. Returns 0 when there aren't enough ticks.
    /// </summary>
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