using System;
using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework;

public class Computations
{
    #region - Bar Computations -

    /// <summary>
    /// Log return over <paramref name="lookback"/> bars: ln(close[i] / close[i - lookback]). Positive when price rose.
    /// Returns NaN when there aren't <paramref name="lookback"/> earlier bars.
    /// </summary>
    public static double Return(List<BarData> bars, int lookback)
    {
        var last = bars.Count - 1;
        if (bars.Count <= lookback) return double.NaN;
        return Math.Log(bars[last].Close / bars[last - lookback].Open);
    }

    public static int ConsecutiveUp(List<BarData> bars)
    {
        var count = 0;
        var i = bars.Count - 1;
        while (i >= 0 && bars[i].Close > bars[i].Open)
        {
            count++;
            i--;
        }
        return count;
    }

    public static int ConsecutiveDown(List<BarData> bars)
    {
        var count = 0;
        var i = bars.Count - 1;
        while (i >= 0 && bars[i].Close < bars[i].Open)
        {
            count++;
            i--;
        }
        return count;
    }

    /// <summary>
    /// True range of a bar: the largest of high - low, |high - previous close| and
    /// |low - previous close|, so overnight gaps count as range. The previous bar is required, so the first bar
    /// of a series has no true range of its own.
    /// </summary>
    public static double TrueRange(BarData current, BarData previous)
    {
        var highLow = current.High - current.Low;
        var highClose = Math.Abs(current.High - previous.Close);
        var lowClose = Math.Abs(current.Low - previous.Close);
        return Math.Max(highLow, Math.Max(highClose, lowClose));
    }

    public static double AverageBodyRatio(List<BarData> bars, int lookback, double atr)
    {
        if (bars.Count < lookback)
            return double.NaN;

        double sum = 0;
        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            sum += Operations.SafeDiv(
                BodySize(bars[i], atr),
                Range(bars[i], atr));
        }

        return Operations.SafeDiv(sum, lookback);
    }

    public static double AverageHighMove(List<BarData> bars, int lookback)
    {
        if (bars.Count < lookback) return double.NaN;
        double sum = 0;

        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            sum += bars[i].High - bars[i].Open;
        }

        return Operations.SafeDiv(sum, lookback);

    }

    public static double AverageLowMove(List<BarData> bars, int lookback)
    {
        if (bars.Count < lookback) return double.NaN;
        double sum = 0;

        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            sum += bars[i].Low - bars[i].Open;
        }

        return Operations.SafeDiv(sum, lookback);

    }

    public static double AverageMove(List<BarData> bars, int lookback)
    {
        if (bars.Count < lookback) return double.NaN;

        double sum = 0;
        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            sum += bars[i].Close - bars[i].Open;
        }

        return Operations.SafeDiv(sum, lookback);
    }

    /// <summary>
    /// Average of (high - low) over the last <paramref name="lookback"/> bars.
    /// Unlike <see cref="ATR"/> it ignores gaps from the previous close. Returns NaN when there aren't enough bars.
    /// </summary>
    public static double AverageRange(List<BarData> bars, int lookback)
    {
        if (bars.Count < lookback)
            return double.NaN;

        double sum = 0;
        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            sum += bars[i].High - bars[i].Low;
        }
        return sum / lookback;
    }

    /// <summary>
    /// Average True Range: a simple (unsmoothed) mean of <see cref="TrueRange"/> over the last
    /// <paramref name="lookback"/> bars. A typical-move-size measure used to normalise most other features.
    /// Returns NaN when there aren't more than <paramref name="lookback"/> bars, since each true range needs a previous close.
    /// </summary>
    public static double ATR(List<BarData> bars, int lookback)
    {
        if (bars.Count <= lookback) return double.NaN;

        double atr = 0;
        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            atr += TrueRange(bars[i], bars[i - 1]);
        }

        atr /= lookback;
        return atr;
    }

    /// <summary>
    /// Exponential moving average of <paramref name="selector"/> over all the bars, with smoothing factor
    /// k = 2 / (lookback + 1): value[j] * k + EMA[j - 1] * (1 - k), seeded with the first bar's value.
    /// Returns the value at the last bar. Walks every bar on each call, so it costs O(n).
    /// </summary>
    public static double EMA(List<BarData> bars, int lookback, Func<BarData, double> selector)
    {
        var k = 2.0 / (lookback + 1);
        var ema = selector(bars[0]);
        for (var j = 1; j < bars.Count; j++)
            ema = selector(bars[j]) * k + ema * (1 - k);
        return ema;
    }

    /// <summary>
    /// Relative Strength Index (0–100) over the last <paramref name="lookback"/> close-to-close changes: compares total
    /// gains to total losses, 100 - 100 / (1 + gains / losses). Above 50 means up-moves dominated.
    /// Uses simple sums rather than Wilder's smoothing. Returns 100 when there were gains but no losses, 50 when
    /// the window was flat, and NaN when there aren't enough bars.
    /// </summary>
    public static double RSI(List<BarData> bars, int lookback)
    {
        if (bars.Count <= lookback) return double.NaN;
        double gain = 0;
        double loss = 0;
        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            var change = bars[i].Close - bars[i - 1].Close;
            if (change > 0) gain += change;
            else loss -= change;
        }

        if (loss == 0) return gain == 0 ? 50 : 100;

        var rs = gain / lookback / (loss / lookback);
        return 100 - (100 / (1 + rs));
    }

    /// <summary>
    /// Simple moving average of <paramref name="selector"/> (e.g. close or volume) over the last
    /// <paramref name="lookback"/> bars. Returns NaN when there are fewer than <paramref name="lookback"/> bars,
    /// or <paramref name="lookback"/> is 0.
    /// </summary>
    public static double SMA(List<BarData> bars, int lookback, Func<BarData, double> selector)
    {
        if (lookback <= 0 || bars.Count < lookback) return double.NaN;

        double sum = 0.0;
        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            sum += selector(bars[i]);
        }
        return sum / lookback;
    }

    /// <summary>
    /// Population standard deviation of <paramref name="selector"/> over the last <paramref name="lookback"/> bars
    /// how spread out the values are around their <see cref="SMA"/>.
    /// Returns NaN when there are fewer than <paramref name="lookback"/> bars.
    /// </summary>
    public static double StandardDeviation(List<BarData> bars, int lookback, Func<BarData, double> selector)
    {
        if (lookback <= 0 || bars.Count < lookback) return double.NaN;
        var mean = SMA(bars, lookback, selector);
        double sum = 0.0;
        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            sum += Math.Pow(selector(bars[i]) - mean, 2);
        }
        var variance = sum / lookback;
        return Math.Sqrt(variance);
    }

    /// <summary>
    /// Size of the candle body, |close - open|, in ATR units, ignoring direction.
    /// Returns NaN when <paramref name="atr"/> is 0.
    /// </summary>
    public static double BodySize(BarData bar, double atr)
    {
        if (atr == 0) return double.NaN;
        return Math.Abs(bar.Close - bar.Open) / atr;
    }

    /// <summary>
    /// Length of the upper wick (high minus the top of the body) in ATR units: how far price was pushed up and
    /// rejected. Returns NaN when <paramref name="atr"/> is 0.
    /// </summary>
    public static double UpperWick(BarData bar, double atr)
    {
        if (atr == 0) return double.NaN;
        return (bar.High - Math.Max(bar.Close, bar.Open)) / atr;
    }

    /// <summary>
    /// Length of the lower wick (bottom of the body minus low) in ATR units: how far price was pushed down and
    /// rejected. Returns NaN when <paramref name="atr"/> is 0.
    /// </summary>
    public static double LowerWick(BarData bar, double atr)
    {
        if (atr == 0) return double.NaN;
        return (Math.Min(bar.Open, bar.Close) - bar.Low) / atr;
    }

    /// <summary>
    /// Where the close sits within the bar's range, from 0 (closed at the low) to 1 (closed at the high).
    /// Returns NaN for a bar with no range.
    /// </summary>
    public static double ClosePosition(BarData bar)
    {
        return bar.High - bar.Low == 0 ? double.NaN : (bar.Close - bar.Low) / (bar.High - bar.Low);
    }

    public static double RealizedVolatility(List<BarData> bars, int lookback)
    {
        if (lookback <= 0 || bars.Count <= lookback) return double.NaN;
        var returns = new List<double>();
        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            returns.Add(Math.Log(bars[i].Close / bars[i - 1].Close));
        }
        var mean = returns.Average();
        var variance = returns.Sum(r => Math.Pow(r - mean, 2)) / lookback;
        return Math.Sqrt(variance);
    }

    /// <summary>
    /// Price change in points over <paramref name="lookback"/> bars: close[i] - close[i - lookback].
    /// Returns NaN when there aren't enough bars.
    /// </summary>
    public static double Momentum(List<BarData> bars, int lookback)
    {
        var last = bars.Count - 1;
        if (last < lookback) return double.NaN;
        return bars[last].Close - bars[last - lookback].Close;
    }

    public static double AbsoluteMovement(List<BarData> bars)
    {
        var totalMovement = 0.0;
        foreach (var bar in bars)
        {
            totalMovement +=
                Math.Abs(bar.Open - bar.High) +
                Math.Abs(bar.High - bar.Low) +
                Math.Abs(bar.Low - bar.Close);
        }
        return totalMovement;
    }

    /// <summary>
    /// Minutes elapsed between <paramref name="sessionOpen"/> and the bar's timestamp.
    /// </summary>
    public static double MinutesFromOpen(BarData bar, DateTime sessionOpen)
    {
        return (bar.Timestamp - sessionOpen).TotalMinutes;
    }

    public static double VolumeTrend(List<BarData> bars, int lookback)
    {
        if (bars.Count < lookback + 1)
            return double.NaN;

        int up = 0, down = 0;
        var last = bars.Count - 1;
        for (var i = last - lookback + 1; i <= last; i++)
        {
            if (bars[i].Volume > bars[i - 1].Volume)
                up++;
            else if (bars[i].Volume < bars[i - 1].Volume)
                down++;
        }
        var bar = BarData.Aggregate(bars);
        if (up > down && bar.Open < bar.Close)
        {
            return 1;
        }
        else if (down > up && bar.Open > bar.Close)
        {
            return -1;
        }
        return 0;
    }

    /// <summary>
    /// How unusual the last bar's volume is versus the last <paramref name="lookback"/> bars (including itself),
    /// in standard deviations from their mean: 2 means much heavier than normal, negative means lighter.
    /// Returns NaN when there aren't enough bars or volume didn't vary.
    /// </summary>
    public static double VolumeZScore(List<BarData> bars, int lookback)
    {
        var last = bars.Count - 1;
        if (bars.Count < lookback)
            return double.NaN;
        double sum = 0;
        double sumSq = 0;
        for (var i = bars.Count - lookback; i < bars.Count; i++)
        {
            double v = bars[i].Volume;
            sum += v;
            sumSq += v * v;
        }
        double mean = sum / lookback;
        double variance = (sumSq / lookback) - (mean * mean);
        if (variance <= 0)
            return double.NaN;
        return (bars[last].Volume - mean) / Math.Sqrt(variance);
    }

    public static double Range(BarData bar, double atr)
    {
        if (atr == 0) return double.NaN;
        return Operations.SafeDiv(bar.High - bar.Low, atr);
    }

    /// <summary>
    /// When the session high was made, as a fraction of the way through the bars: 0 = the first bar, 1 = the last.
    /// Based on bar position rather than clock time; if the high is matched more than once, the earliest counts.
    /// Returns NaN for a single bar, where there is no timing to measure.
    /// </summary>
    public static double HighTimeRatio(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var high = ordered.Max(b => b.High);
        var highBarIndex = ordered.IndexOf(ordered.First(b => b.High == high)!);
        return ordered.Count > 1 ? (double)highBarIndex / (ordered.Count - 1) : double.NaN;
    }

    /// <summary>
    /// When the session low was made, as a fraction of the way through the bars: 0 = the first bar, 1 = the last.
    /// Based on bar position rather than clock time; if the low is matched more than once, the earliest counts.
    /// Returns NaN for a single bar, where there is no timing to measure.
    /// </summary>
    public static double LowTimeRatio(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var low = ordered.Min(b => b.Low);
        var lowBarIndex = ordered.IndexOf(ordered.First(b => b.Low == low)!);
        return ordered.Count > 1 ? (double)lowBarIndex / (ordered.Count - 1) : double.NaN;
    }

    #endregion
}