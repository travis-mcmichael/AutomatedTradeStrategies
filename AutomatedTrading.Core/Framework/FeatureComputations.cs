using System;
using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework;

/// <summary>
/// All data required to evaluate a single feature. Daily context is always required;
/// PrevMinuteBars and CurrentMinuteBars are needed only for features with those sources.
/// </summary>
public class FeatureContext(double atr, string sessionDate, Dictionary<string, List<BarData>> minuteBars)
{
    /// <summary>
    /// Average True Range
    /// </summary>
    public double ATR { get; set; } = atr;
    public string SessionDate { get; set; } = sessionDate;
    public Dictionary<string, List<BarData>> MinuteBars { get; set; } = minuteBars;
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
        Dictionary<string, int> p = [];
        foreach (var param in feature.ModelFeatureParams)
        {
            if (param.FeatureTypeParam != null)
                p.Add(param.FeatureTypeParam.ParameterKey, param.ParamValue);
        }
        var source = feature.Source;
        var contextBars = new List<BarData>(); // bars to be used for context for evaluation.  
        // Context bars contains all the data for the model's date range, including bars from outside
        // the model's config window, in raw 1-minute format. 

        // get the correct context date to build context bars
        var contextDate = GetContextDate(sessionDate, p);

        // set the context bars to the correct range
        switch (source)
        {
            case "window":
                var start = p["start"];
                var end = p["end"];

                var windowBars = ctx.MinuteBars[contextDate.ToString(AppConstants.DATE_FORMAT)]
                    .Where(x =>
                        new TradeWindow() { WindowStart = start, WindowEnd = end }.InWindow(int.Parse(x.SessionTime)));
                contextBars.AddRange(windowBars);
                break;
            default:
                contextBars.AddRange(ctx.MinuteBars[contextDate.ToString(AppConstants.DATE_FORMAT)]);
                break;
        }

        var last = contextBars.Count - 1;
        var aggregatedContext = BarData.Aggregate(contextBars);

        //var current = ctx.Sequence[ctx.J]; // bar to be evaluated (if only evaluating one)
        //var evaluationBars = ctx.Sequence; // bar to be evaluated + previous bar evaluation history
        // these should never be used with BarData.Aggregate() - this step has already been performed

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
                    Computations.ATR(contextBars, p!["short"]),
                    Computations.ATR(contextBars, p["long"]));
            case "AvgBodyRatio":
                return Computations.AverageBodyRatio(contextBars, p["lookback"], ctx.ATR);
            case "AvgRange":
                return Computations.AverageRange(contextBars, p["lookback"]);
            case "BodyRatio":
                return Operations.SafeDiv(
                    Computations.BodySize(aggregatedContext, ctx.ATR),
                    Computations.Range(aggregatedContext, ctx.ATR));
            case "BodySize":
                return Computations.BodySize(aggregatedContext, ctx.ATR);
            case "CloseOpenDiff":
                return Operations.SafeDiv(
                    aggregatedContext.Close - aggregatedContext.Open,
                    ctx.ATR);
            case "ClosePosition":
                return Computations.ClosePosition(aggregatedContext);
            case "ClosePrevCloseDiff":
                if (contextBars.Count < 2) return double.NaN;
                return Operations.SafeDiv(
                    contextBars[contextBars.Count - 1].Close - contextBars[contextBars.Count - 2].Close,
                    ctx.ATR);
            case "ClosePrevHighDiff":
                if (contextBars.Count < 2) return double.NaN;
                return Operations.SafeDiv(
                    contextBars[contextBars.Count - 1].Close - contextBars[contextBars.Count - 2].High,
                    ctx.ATR);
            case "ClosePrevLowDiff":
                if (contextBars.Count < 2) return double.NaN;
                return Operations.SafeDiv(
                    contextBars[contextBars.Count - 1].Close - contextBars[contextBars.Count - 2].Low,
                    ctx.ATR);
            case "CloseSmaDiff":
                return Operations.SafeDiv(
                    aggregatedContext.Close - Computations.SMA(contextBars, p["lookback"], x => x.Close),
                    ctx.ATR);
            case "ConsecutiveDown":
                return Computations.ConsecutiveDown(contextBars);
            case "ConsecutiveUp":
                return Computations.ConsecutiveUp(contextBars);
            case "DayOfWeek":
                return (int)DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null).DayOfWeek;
            case "DayOfMonth":
                return DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null).Day;
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
                    contextBars[last].High - aggregatedContext.Open,
                    ctx.ATR);
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
                    Computations.Momentum(contextBars, p!["short"]),
                    Computations.Momentum(contextBars, p!["long"]));
            case "Month":
                return DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null).Month;
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
                    Computations.SMA(contextBars, p!["lookback"], x => x.Close));
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
                return Computations.Return(contextBars, p!["lookback"]);
            case "Rsi":
                return Computations.RSI(contextBars, p!["lookback"]);
            case "Sma":
                return Computations.SMA(contextBars, p!["lookback"], x => x.Close);
            case "SmaRatio":
                return Operations.SafeDiv(
                    Computations.SMA(contextBars, p!["short"], x => x.Close),
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
                return Computations.SMA(contextBars, p!["lookback"], x => x.Volume);
            case "VolumeTrend":
                return Computations.VolumeTrend(contextBars, p!["lookback"]);
            case "VolumeZScore":
                return Computations.VolumeZScore(contextBars, p!["lookback"]);
            default:
                throw new ArgumentException($"Unknown/null feature name: '{feature?.FeatureType?.Name}'");
        }
    }

    private static DateTime GetContextDate(DateTime sessionDate, Dictionary<string, int> p)
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
}
