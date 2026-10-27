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
    public List<BarData> Sequence { get; set; }

    /// <summary>
    /// Index of the previous BarData, can be used to retrieve the previous bar by using 
    /// </summary>
    public int J { get; set; }

    /// <summary>
    /// Average True Range
    /// </summary>
    public double ATR { get; set; }
    public string SessionDate { get; set; }
    public Dictionary<string, List<BarData>> MinuteBars { get; set; }
    public FeatureContext(List<BarData> sequence, int j, double atr, string sessionDate, Dictionary<string, List<BarData>> minuteBars)
    {
        Sequence = sequence;
        J = j;
        ATR = atr;
        MinuteBars = minuteBars;
        SessionDate = sessionDate;
    }
}

public static class FeatureComputations
{
    public static double[] ParseFeatures(List<ModelFeature> features, FeatureContext ctx)
    {
        List<double> d = new List<double>();
        foreach (var feature in features)
        {
            d.Add(ParseFeatureComputation(feature, ctx));
        }
        return d.ToArray();
    }

    public static double ParseFeatureComputation(ModelFeature feature, FeatureContext ctx)
    {
        var sessionDate = DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null);
        Dictionary<string, int> p = new Dictionary<string, int>();
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

        var current = ctx.Sequence[ctx.J]; // bar to be evaluated (if only evaluating one)
        var evaluationBars = ctx.Sequence; // bar to be evaluated + previous bar evaluation history
                                           // these should never be used with BarData.Aggregate() - this step has already been performed

        switch (feature.FeatureType?.Name)
        {
            case "AtrRatio":
                return Operations.SafeDiv(
                    Computations.ATR(evaluationBars, ctx.J, p!["short"]),
                    Computations.ATR(evaluationBars, ctx.J, p["long"]));
            case "AtrOpenRatio":
                return Operations.SafeDiv(
                    Computations.ATR(evaluationBars, ctx.J, p["lookback"]),
                    BarData.Aggregate(contextBars).Open);
            case "AvgRange":
                return Computations.AverageRange(evaluationBars, ctx.J, p["lookback"]);
            case "BodySize":
                return Computations.BodySize(current, ctx.ATR);
            case "ClosePosition":
                return Computations.ClosePosition(BarData.Aggregate(contextBars));
            case "DayOfWeek":
                return (int)DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null).DayOfWeek;
            case "DayOfMonth":
                return DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null).Day;
            case "HighOpenDiff":
                return Operations.SafeDiv(current.High - BarData.Aggregate(contextBars).Open, ctx.ATR);
            case "HighTimeRatio":
                return Computations.HighTimeRatio(contextBars);
            case "LowTimeRatio":
                return Computations.LowTimeRatio(contextBars);
            case "LowerWick":
                return Computations.LowerWick(BarData.Aggregate(contextBars), ctx.ATR);
            case "Month":
                return DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null).Month;
            case "OpenLowDiff":
                return Operations.SafeDiv(current.Open - BarData.Aggregate(contextBars).Low, ctx.ATR);
            case "OpenCloseDiff":
                return Operations.SafeDiv(current.Open - BarData.Aggregate(contextBars).Close, ctx.ATR);
            case "OpenHighDiff":
                return Operations.SafeDiv(current.Open - BarData.Aggregate(contextBars).High, ctx.ATR);
            case "OpenSmaRatio":
                return Operations.SafeDiv(
                    current.Open,
                    Computations.SMA(evaluationBars, ctx.J, p!["lookback"], x => x.Close));
            case "Range":
                return Computations.Range(BarData.Aggregate(contextBars), ctx.ATR);
            case "RangeAvgRangeRatio":
                return Operations.SafeDiv(
                    current.High - current.Low,
                    Computations.AverageRange(evaluationBars, ctx.J, p["lookback"]));
            case "Return":
                return Computations.Return(evaluationBars, ctx.J, p!["lookback"]);
            case "Rsi":
                return Computations.RSI(evaluationBars, ctx.J, p!["lookback"]);
            case "SmaRatio":
                return Operations.SafeDiv(
                    Computations.SMA(evaluationBars, ctx.J, p!["short"], x => x.Close),
                    Computations.SMA(evaluationBars, ctx.J, p["long"], x => x.Close));
            case "UpperWick":
                return Computations.UpperWick(BarData.Aggregate(contextBars), ctx.ATR);
            case "VolumeSma":
                return Computations.SMA(evaluationBars, ctx.J, p!["lookback"], x => x.Volume);
            case "VolumeZScore":
                return Computations.VolumeZScore(evaluationBars, ctx.J, p!["lookback"]);
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
