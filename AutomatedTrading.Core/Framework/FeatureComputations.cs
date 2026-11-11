using System;
using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework;

/// <summary>
/// All data required to evaluate a single feature. Daily context is always required;
/// PrevMinuteBars and CurrentMinuteBars are needed only for features with those sources.
/// </summary>
public class FeatureContext
{
    /// <summary>
    /// Average True Range
    /// </summary>
    public double ATR { get; set; }
    public string SessionDate { get; set; }
    public Dictionary<string, List<BarData>> Data { get; set; }
    public FeatureContext(double atr, string sessionDate, Dictionary<string, List<BarData>> minuteBars,
        string modelWindowStart = null, string modelWindowEnd = null)
    {
        ATR = atr;
        SessionDate = sessionDate;
        // Shallow copy: only the current session's list is replaced, so the caller's dictionary (shared across rows
        // in the exporter) and the prior sessions are left untouched.
        Data = new Dictionary<string, List<BarData>>(minuteBars);

        // Only the current session is cut. Its bars from the window open onward have not happened yet when the
        // trigger fires, so they are dropped. The cut is chronological (by Timestamp), not by time of day, so a
        // window that wraps midnight (e.g. 17:00-08:30) is handled correctly.
        if (!string.IsNullOrEmpty(modelWindowStart) && !string.IsNullOrEmpty(modelWindowEnd)
            && minuteBars.TryGetValue(sessionDate, out var current))
        {
            var window = new TradeWindow { WindowStart = int.Parse(modelWindowStart), WindowEnd = int.Parse(modelWindowEnd) };
            var ordered = current.OrderBy(x => x.Timestamp).ToList();
            var windowOpen = ordered.FindIndex(x => window.InWindow(int.Parse(x.SessionTime)));
            if (windowOpen < 0)
                throw new InvalidOperationException($"No bars for {sessionDate} fall inside the model window {modelWindowStart}-{modelWindowEnd}.");
            Data[sessionDate] = ordered.Take(windowOpen).ToList();
        }
    }
}

public static class FeatureComputations
{
    public static double[] ParseFeatures(List<ModelFeature> features, FeatureContext ctx)
    {
        List<double> d = [];
        foreach (var feature in features)
        {
            d.Add(ParseFeatureComputation(feature, ctx));
        }
        return [.. d];
    }

    public static double ParseFeatureComputation(ModelFeature feature, FeatureContext ctx)
    {
        var sessionDate = DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null);
        var p = GetParams(feature);
        // get the correct context date to build context bars
        var contextDate = GetContextDate(sessionDate, p);
        var source = feature.Source ?? "sessionMinute";
        // bars to be used for context for evaluation.  
        // Context bars contains all the data for the model's date range, including bars from outside
        // the model's config window, in raw 1-minute format. 
        var contextBars = BuildContextSegment(contextDate, source, ctx.Data, p);

        var last = contextBars.Count - 1;
        if (last == -1) return double.NaN;
        var aggregatedContext = source.Contains("day", StringComparison.CurrentCultureIgnoreCase) ? contextBars.Last() : BarData.Aggregate(contextBars);
        var currentPrice = ctx.Data[ctx.SessionDate].Last().Close;

        switch (feature.FeatureType?.Name)
        {
            case "Atr":
                return Computations.ATR(contextBars, p["lookback"]);
            case "AtrOpenRatio":
                return Operations.SafeDiv(
                    Computations.ATR(contextBars, p["lookback"]),
                    BarData.Aggregate(contextBars).Open);
            case "AtrRatio":
                return Operations.SafeDiv(
                    Computations.ATR(contextBars, p["short"]),
                    Computations.ATR(contextBars, p["long"]));
            case "AvgBodyRatio":
                return Computations.AverageBodyRatio(contextBars, p["lookback"], ctx.ATR);
            case "AvgMove":
                return Computations.AverageMove(contextBars, p["lookback"]);
            case "AvgDownMove":
                return Computations.AverageMove([.. contextBars.Where(x => x.Close < x.Open)], p["lookback"]);
            case "AvgUpMove":
                return Computations.AverageMove([.. contextBars.Where(x => x.Close > x.Open)], p["lookback"]);
            case "AvgRange":
                return Computations.AverageRange(contextBars, p["lookback"]);
            case "BodyRatio":
                return Operations.SafeDiv(
                    Computations.BodySize(aggregatedContext, ctx.ATR),
                    Computations.Range(aggregatedContext, ctx.ATR));
            case "BodySize":
                return Computations.BodySize(aggregatedContext, ctx.ATR);
            case "CloseLowDiff":
                return Operations.SafeDiv(
                    aggregatedContext.Close - aggregatedContext.Low,
                    ctx.ATR);
            case "CloseOpenDiff":
                return Operations.SafeDiv(
                    aggregatedContext.Close - aggregatedContext.Open,
                    ctx.ATR);
            case "ClosePosition":
                return Computations.ClosePosition(aggregatedContext);
            case "ClosePrevCloseDiff":
                if (contextBars.Count < 2) return double.NaN;
                return Operations.SafeDiv(
                    contextBars[last].Close - contextBars[last - 1].Close,
                    ctx.ATR);
            case "ClosePrevHighDiff":
                if (contextBars.Count < 2) return double.NaN;
                return Operations.SafeDiv(
                    contextBars[last].Close - contextBars[last - 1].High,
                    ctx.ATR);
            case "ClosePrevLowDiff":
                if (contextBars.Count < 2) return double.NaN;
                return Operations.SafeDiv(
                    contextBars[last].Close - contextBars[last - 1].Low,
                    ctx.ATR);
            case "CloseSmaDiff":
                return Operations.SafeDiv(
                    aggregatedContext.Close - Computations.SMA(contextBars, p["lookback"], x => x.Close),
                    ctx.ATR);
            case "ConsecutiveDown":
                return Computations.ConsecutiveDown(contextBars);
            case "ConsecutiveUp":
                return Computations.ConsecutiveUp(contextBars);
            case "CurrentPrevHighDiff":
                return Operations.SafeDiv(
                    currentPrice - aggregatedContext.High,
                    ctx.ATR);
            case "CurrentPrevLowDiff":
                return Operations.SafeDiv(
                    currentPrice - aggregatedContext.Low,
                    ctx.ATR);
            case "CurrentPrevOpenDiff":
                return Operations.SafeDiv(
                    currentPrice - aggregatedContext.Open,
                    ctx.ATR);
            case "CurrentPrevCloseDiff":
                return Operations.SafeDiv(
                    currentPrice - aggregatedContext.Close,
                    ctx.ATR);
            case "CurrentPrevRangeRatio":
                return Operations.SafeDiv(
                    currentPrice - aggregatedContext.Low,
                    aggregatedContext.High - aggregatedContext.Low);
            case "DayOfWeek":
                return (int)sessionDate.DayOfWeek;
            case "DayOfMonth":
                return sessionDate.Day;
            case "DownCount":
                return contextBars.Count(x => x.Open > x.Close);
            case "DownVolume":
                return contextBars.Where(x => x.Open > x.Close).Sum(x => x.Volume);
            case "EmaDiff":
                return Operations.SafeDiv(
                    Computations.EMA(contextBars, p["short"], x => x.Close) - Computations.EMA(contextBars, p["long"], x => x.Close),
                    ctx.ATR);
            case "GreenRatio":
                return contextBars.Count(x => x.Open > x.Close) / contextBars.Count;
            case "HighOpenDiff":
                return Operations.SafeDiv(
                    aggregatedContext.High - aggregatedContext.Open,
                    ctx.ATR);
            case "HighCloseDiff":
                return Operations.SafeDiv(
                    aggregatedContext.High - aggregatedContext.Close,
                    ctx.ATR
                );
            case "HighTimeRatio":
                return Computations.HighTimeRatio(contextBars);
            case "HolidayAdjacent":
                return TradingCalendar.IsHolidayAdjacent(sessionDate) ? 1 : 0;
            case "LowTimeRatio":
                return Computations.LowTimeRatio(contextBars);
            case "LowerWick":
                return Computations.LowerWick(aggregatedContext, ctx.ATR);
            case "LowerWickRangeRatio":
                return Operations.SafeDiv(
                    Computations.LowerWick(aggregatedContext, ctx.ATR),
                    Computations.Range(aggregatedContext, ctx.ATR));
            case "MomentumRatio":
                return Operations.SafeDiv(
                    Computations.Momentum(contextBars, p["short"]),
                    Computations.Momentum(contextBars, p["long"]));
            case "Month":
                return sessionDate.Month;
            case "MovementTrendEfficiency":
                return Operations.SafeDiv(
                    Math.Abs(aggregatedContext.Open - aggregatedContext.Close),
                    Computations.AbsoluteMovement(contextBars));
            case "OpenLowDiff":
                return Operations.SafeDiv(
                    contextBars[last].Open - aggregatedContext.Low,
                    ctx.ATR);
            case "OpenCloseDiff":
                return Operations.SafeDiv(
                    aggregatedContext.Open - aggregatedContext.Close,
                    ctx.ATR);
            case "OpenHighDiff":
                return Operations.SafeDiv(
                    contextBars[last].Open - aggregatedContext.High,
                    ctx.ATR);
            case "OpenSmaRatio":
                return Operations.SafeDiv(
                    contextBars[last].Open,
                    Computations.SMA(contextBars, p["lookback"], x => x.Close));
            case "PrevCloseOpenDiff":
                if (contextBars.Count < 2) return double.NaN;
                return contextBars[last - 1].Close - contextBars[last].Open;
            case "Range":
                return Computations.Range(aggregatedContext, ctx.ATR);
            case "RangeAtrRatio":
                var recent = BarData.Aggregate([.. contextBars.Skip(contextBars.Count - p["short"])]);
                return Operations.SafeDiv(
                    Computations.Range(recent, ctx.ATR),
                    Computations.ATR(contextBars, p["long"]));
            case "RangeAvgRangeRatio":
                return Operations.SafeDiv(
                    contextBars[last].High - contextBars[last].Low,
                    Computations.AverageRange(contextBars, p["lookback"]));
            case "RangeRatio":
                var l = contextBars.Skip(contextBars.Count - p["long"]).ToList();
                return Operations.SafeDiv(
                    Computations.Range(BarData.Aggregate([.. l.Skip(l.Count - p["short"])]), ctx.ATR),
                    Computations.Range(BarData.Aggregate(l), ctx.ATR));
            case "RealizedVolatility":
                return Computations.RealizedVolatility(contextBars, p["lookback"]);
            case "Return":
                return Computations.Return(contextBars, p["lookback"]);
            case "ReturnPrev":
                return Math.Log(currentPrice / aggregatedContext.Open);
            case "Rsi":
                return Computations.RSI(contextBars, p["lookback"]);
            case "Sma":
                return Computations.SMA(contextBars, p["lookback"], x => x.Close);
            case "SmaRatio":
                return Operations.SafeDiv(
                    Computations.SMA(contextBars, p["short"], x => x.Close),
                    Computations.SMA(contextBars, p["long"], x => x.Close));
            case "UpCount":
                return contextBars.Count(x => x.Close > x.Open);
            case "UpDownVolumeRatio":
                return Operations.SafeDiv(
                    contextBars.Where(x => x.Close > x.Open).Sum(x => x.Volume),
                    contextBars.Where(x => x.Open > x.Close).Sum(x => x.Volume));
            case "UpperWick":
                return Computations.UpperWick(aggregatedContext, ctx.ATR);
            case "UpperWickRangeRatio":
                return Operations.SafeDiv(
                    Computations.UpperWick(aggregatedContext, ctx.ATR),
                    Computations.Range(aggregatedContext, ctx.ATR));
            case "UpVolume":
                return contextBars.Where(x => x.Close > x.Open).Sum(x => x.Volume);
            case "VolatilityRatio":
                return Operations.SafeDiv(
                    Computations.RealizedVolatility(contextBars, p["short"]),
                    Computations.RealizedVolatility(contextBars, p["long"]));
            case "Volume":
                return aggregatedContext.Volume;
            case "VolumePerPoint":
                return Operations.SafeDiv(
                    aggregatedContext.Volume,
                    Computations.Range(aggregatedContext, ctx.ATR));
            case "VolumeRatio":
                if (contextBars.Count < p["long"]) return double.NaN;
                return Operations.SafeDiv(
                    contextBars.Skip(contextBars.Count - p["short"]).ToList().Sum(x => x.Volume),
                    contextBars.Skip(contextBars.Count - p["long"]).ToList().Sum(x => x.Volume));
            case "VolumeSma":
                return Computations.SMA(contextBars, p["lookback"], x => x.Volume);
            case "VolumeTrend":
                return Computations.VolumeTrend(contextBars, p["lookback"]);
            case "VolumeZScore":
                return Computations.VolumeZScore(contextBars, p["lookback"]);
            default:
                throw new ArgumentException($"Unknown/null feature name: '{feature?.FeatureType?.Name}'");
        }
    }

    /// <summary>
    /// The feature's parameter values keyed by ParameterKey, exactly as the computation reads them.
    /// Params whose FeatureTypeParam was not loaded are skipped.
    /// </summary>
    public static Dictionary<string, int> GetParams(ModelFeature feature)
    {
        Dictionary<string, int> p = [];
        foreach (var param in feature.ModelFeatureParams)
        {
            if (param.FeatureTypeParam != null)
                p.Add(param.FeatureTypeParam.ParameterKey, param.ParamValue);
        }
        return p;
    }

    /// <summary>
    /// Gets context date -> either the date to retrieve 1m bars for OR the end date range of a series of 1d bars.
    /// (used with lookback parameter to get start date).
    /// </summary>
    /// <param name="sessionDate"></param>
    /// <param name="p"></param>
    /// <returns></returns>
    public static DateTime GetContextDate(DateTime sessionDate, Dictionary<string, int> p)
    {
        var prevDate = sessionDate;
        int weeks = 0, months = 0;
        var requiresAdjustment = p.TryGetValue("days", out int days) || p.TryGetValue("weeks", out weeks) || p.TryGetValue("months", out months);
        if (requiresAdjustment)
        {
            while (days >= 5) // workweek is 5 days
            {
                weeks++;
                days /= 5;
            }
            while (weeks >= 4) // treat months as 4 weeks
            {
                months++;
                weeks /= 4;
            }
            if (days > 0)
            {
                for (var i = 0; i < days; i++)
                {
                    prevDate = TradingCalendar.GetPreviousSessionDate(prevDate);
                }
            }
            if (weeks > 0)
            {
                prevDate = prevDate.AddDays(weeks * 7 * -1);
            }
            if (months > 0)
            {
                prevDate = prevDate.AddMonths(months * -1);
            }
            if (!TradingCalendar.IsTradingDay(prevDate))
            {
                prevDate = TradingCalendar.GetNextSessionDate(prevDate);
            }
        }
        return prevDate;
    }

    private static List<BarData> BuildContextSegment(DateTime contextDate, string source, Dictionary<string, List<BarData>> dataSegment, Dictionary<string, int> p)
    {
        // var source = feature.Source;
        var contextBars = new List<BarData>(); // bars to be used for context for evaluation.  
        // Context bars contains all the data for the model's date range, including bars from outside
        // the model's config window, in raw 1-minute format. 
        var hasWindow = p.TryGetValue("start", out var start);
        hasWindow = p.TryGetValue("end", out var end) && hasWindow;
        try
        {
            // set the context bars to the correct range
            switch (source)
            {
                case "windowMinute":
                    if (!hasWindow)
                        throw new InvalidOperationException("Missing window feature parameters");
                    var windowBars = dataSegment[contextDate.ToString(AppConstants.DATE_FORMAT)]
                        .Where(x =>
                            new TradeWindow() { WindowStart = start, WindowEnd = end }
                            .InWindow(int.Parse(x.SessionTime)))
                        .OrderBy(x => x.Timestamp)
                        .ToList();
                    contextBars.AddRange(windowBars);
                    break;
                case "windowDay":
                    if (!hasWindow)
                        throw new InvalidOperationException("Missing window feature parameters");
                    var windows = dataSegment.Keys
                        .Where(x => int.Parse(x) <= int.Parse(contextDate.ToString(AppConstants.DATE_FORMAT)))
                        .OrderBy(x => x)
                        .ToList();
                    foreach (var session in windows)
                    {
                        var sessionBars = dataSegment[session].Where(x => new TradeWindow() { WindowStart = start, WindowEnd = end }.InWindow(int.Parse(x.SessionTime))).ToList();
                        if (sessionBars.Count == 0) continue;
                        contextBars.Add(BarData.Aggregate(sessionBars));
                    }
                    break;
                case "sessionDay":
                    var sessions = dataSegment.Keys
                        .Where(x => int.Parse(x) <= int.Parse(contextDate.ToString(AppConstants.DATE_FORMAT)))
                        .OrderBy(x => x)
                        .ToList();
                    foreach (var session in sessions)
                    {
                        contextBars.Add(BarData.Aggregate([.. dataSegment[session]]));
                    }
                    break;
                default:
                    contextBars.AddRange([.. dataSegment[contextDate.ToString(AppConstants.DATE_FORMAT)].OrderBy(x => x.Timestamp)]);
                    break;
            }
        }
        catch (Exception ex)
        {
            throw;
        }

        return contextBars;
    }
}
