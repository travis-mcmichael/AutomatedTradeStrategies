using System;
using System.Collections.Generic;
using System.Configuration;
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
    public int J { get; set; }
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
        var contextBars = new List<BarData>(); // bars to be used for context for evaluation

        // get the correct previous date to build context bars
        var prevDate = sessionDate;
        if (p.TryGetValue("daysBack", out int daysBack) && daysBack > 0)
        {
            for (var i = 0; i < daysBack; i++)
            {
                prevDate = TradingCalendar.GetPreviousSessionDate(prevDate);
            }
        }

        // set the context bars to the correct range
        switch (source)
        {
            case "window":
                var start = p["start"];
                var end = p["end"];

                var windowBars = ctx.MinuteBars[prevDate.ToString(AppConstants.DATE_FORMAT)]
                    .Where(x =>
                        new TradeWindow() { WindowStart = start, WindowEnd = end }.InWindow(int.Parse(x.SessionTime)));
                contextBars.AddRange(windowBars);
                break;
            case "session":
                contextBars.AddRange(ctx.MinuteBars[prevDate.ToString(AppConstants.DATE_FORMAT)]);
                break;
        }

        var current = ctx.Sequence[ctx.J]; // bar to be evaluated (if only evaluating one)
        var evaluationBars = ctx.Sequence; // bars to be evaluated

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
                return Computations.BodySize(BarData.Aggregate(evaluationBars), ctx.ATR);
            case "BodySizePrev":
                return Computations.BodySize(BarData.Aggregate(contextBars), ctx.ATR);
            case "ClosePosition":
                return Computations.ClosePosition(BarData.Aggregate(evaluationBars));
            case "ClosePositionPrev":
                return Computations.ClosePosition(BarData.Aggregate(contextBars));
            case "DayOfWeek":
                return (int)DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null).DayOfWeek;
            case "DayOfMonth":
                return DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null).Day;
            case "HighOpenDiff":
                return Computations.HighOpenDiff(evaluationBars, ctx.ATR);
            case "HighOpenDiffPrev":
                return Computations.HighOpenDiff(contextBars, ctx.ATR);
            case "HighTimeRatio":
                return Computations.HighTimeRatio(evaluationBars);
            case "HighTimeRatioPrev":
                return Computations.HighTimeRatio(contextBars);
            case "LowTimeRatio":
                return Computations.LowTimeRatio(evaluationBars);
            case "LowTimeRatioPrev":
                return Computations.LowTimeRatio(contextBars);
            case "LowerWick":
                return Computations.LowerWick(BarData.Aggregate(evaluationBars), ctx.ATR);
            case "LowerWickPrev":
                return Computations.LowerWick(BarData.Aggregate(contextBars), ctx.ATR);
            case "Month":
                return DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null).Month;
            case "OpenLowDiff":
                return Computations.OpenLowDiff(evaluationBars, ctx.ATR);
            case "OpenLowDiffPrev":
                return Computations.OpenLowDiff(contextBars, ctx.ATR);
            case "OpenPrevCloseDiff":
                if (contextBars.Count == 0 || evaluationBars.Count == 0) return double.NaN;
                return Operations.SafeDiv(BarData.Aggregate(evaluationBars).Open - BarData.Aggregate(contextBars).Close, ctx.ATR);
            case "OpenPrevHighDiff":
                if (contextBars.Count == 0 || evaluationBars.Count == 0) return double.NaN;
                return Operations.SafeDiv(BarData.Aggregate(evaluationBars).Open - BarData.Aggregate(contextBars).High, ctx.ATR);
            case "OpenPrevLow":
                if (contextBars.Count == 0 || evaluationBars.Count == 0) return double.NaN;
                return Operations.SafeDiv(BarData.Aggregate(evaluationBars).Open - BarData.Aggregate(contextBars).Low, ctx.ATR);
            case "OpenVsSma":
                return Operations.SafeDiv(
                    BarData.Aggregate(evaluationBars).Open,
                    Computations.SMA(evaluationBars, ctx.J, p!["period"], x => x.Close));
            case "PrevHighVsOpen":
                if (contextBars.Count == 0 || evaluationBars.Count == 0) return double.NaN;
                return Operations.SafeDiv(BarData.Aggregate(contextBars).High, BarData.Aggregate(evaluationBars).Open);
            case "PrevLowVsOpen":
                if (contextBars.Count == 0 || evaluationBars.Count == 0) return double.NaN;
                return Operations.SafeDiv(BarData.Aggregate(contextBars).Low, BarData.Aggregate(evaluationBars).Open);
            case "Range":
                return Computations.Range(contextBars, ctx.ATR);
            case "RangePrev":
                return Computations.Range(contextBars, ctx.ATR);
            case "RangeVsAvgRange":
                return Operations.SafeDiv(
                    current.High - current.Low,
                    Computations.AverageRange(contextBars, ctx.J, p["range"]));
            case "Return":
                return Computations.Return(contextBars, ctx.J, p!["lookback"]);
            case "Rsi":
                return Computations.RSI(contextBars, ctx.J, p!["period"]);
            case "SmaRatio":
                return Operations.SafeDiv(
                    Computations.SMA(contextBars, ctx.J, p!["short"], x => x.Close),
                    Computations.SMA(contextBars, ctx.J, p["long"], x => x.Close));
            case "UpperWick":
                return Computations.UpperWick(BarData.Aggregate(contextBars), ctx.ATR);
            case "VolumeSma":
                return Computations.SMA(contextBars, ctx.J, p!["period"], x => x.Volume);
            case "VolumeZScore":
                return Computations.VolumeZScore(contextBars, ctx.J, p!["period"]);
            default:
                throw new ArgumentException($"Unknown/null feature name: '{feature?.FeatureType?.Name}'");
        }
    }
}
