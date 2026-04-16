using System;
using System.Collections.Generic;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.Config;

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

        var p = feature.Params;
        var source = feature.Source;
        var bars = new List<BarData>();
        var prevMinuteBars = ctx.MinuteBars[TradingCalendar
            .GetPreviousSessionDate(sessionDate)
            .ToString(AppConstants.DATE_FORMAT)];
        var currMinuteBars = ctx.MinuteBars[ctx.SessionDate];

        switch (source)
        {
            case "prevMinute":
                bars.AddRange(prevMinuteBars);
                break;
            case "currMinute":
                bars.AddRange(currMinuteBars);
                break;
            default:
                bars.AddRange(ctx.Sequence);
                break;
        }

        var current = ctx.Sequence[ctx.J];

        switch (feature.Name)
        {
            // ── Prev daily bar vs session open ────────────────────────────────
            case "PrevHighVsOpen":
                return Operations.SafeDiv(BarData.Aggregate(prevMinuteBars).High, BarData.Aggregate(currMinuteBars).Open);
            case "PrevLowVsOpen":
                return Operations.SafeDiv(BarData.Aggregate(prevMinuteBars).Low, BarData.Aggregate(currMinuteBars).Open);
            // ── Prev daily bar shape ──────────────────────────────────────────
            case "HighLowRange":
                return Computations.HighLowRange(BarData.Aggregate(bars), ctx.ATR);
            case "BodySize":
                return Computations.BodySize(BarData.Aggregate(bars), ctx.ATR);
            case "UpperWick":
                return Computations.UpperWick(BarData.Aggregate(bars), ctx.ATR);
            case "LowerWick":
                return Computations.LowerWick(BarData.Aggregate(bars), ctx.ATR);
            case "ClosePosition":
                return Computations.ClosePosition(BarData.Aggregate(bars));
            // ── ATR ratios ────────────────────────────────────────────────────
            case "AtrVsOpen":
                return Operations.SafeDiv(Computations.ATR(bars, ctx.J, p!["period"]), BarData.Aggregate(bars).Open);
            case "AtrRatio":
                return Operations.SafeDiv(
                    Computations.ATR(bars, ctx.J, p!["fast"]),
                    Computations.ATR(bars, ctx.J, p["slow"]));
            // ── Range features ────────────────────────────────────────────────
            case "AvgRange":
                return Computations.AverageRange(bars, ctx.J, p!["period"]);
            case "RangeVsAvgRange10":
                return Operations.SafeDiv(
                    current.High - current.Low,
                    Computations.AverageRange(bars, ctx.J, 10));
            // ── Returns ───────────────────────────────────────────────────────
            case "Return":
                return Computations.Return(bars, ctx.J, p!["lookback"]);
            // ── SMA ratios ────────────────────────────────────────────────────
            case "SmaRatio":
                return Operations.SafeDiv(
                    Computations.SMA(bars, ctx.J, p!["fast"], x => x.Close),
                    Computations.SMA(bars, ctx.J, p["slow"], x => x.Close));
            case "OpenVsSma":
                return Operations.SafeDiv(
                    BarData.Aggregate(bars).Open,
                    Computations.SMA(bars, ctx.J, p!["period"], x => x.Close));
            // ── Volume features ───────────────────────────────────────────────
            case "VolumeSma":
                return Computations.SMA(bars, ctx.J, p!["period"], x => x.Volume);
            case "VolumeZScore":
                return Computations.VolumeZScore(bars, ctx.J, p!["period"]);
            // ── Session open gap vs prev bar ──────────────────────────────────
            case "OpenVsPrevHigh":
                return Operations.SafeDiv(BarData.Aggregate(currMinuteBars).Open - BarData.Aggregate(prevMinuteBars).High, ctx.ATR);
            case "OpenVsPrevLow":
                return Operations.SafeDiv(BarData.Aggregate(currMinuteBars).Open - BarData.Aggregate(prevMinuteBars).Low, ctx.ATR);
            case "OpenVsPrevClose":
                return Operations.SafeDiv(BarData.Aggregate(currMinuteBars).Open - BarData.Aggregate(prevMinuteBars).Close, ctx.ATR);
            // ── Momentum / oscillators ────────────────────────────────────────
            case "Rsi":
                return Computations.RSI(bars, ctx.J, p!["period"]);
            // ── Calendar ──────────────────────────────────────────────────────
            case "DayOfWeek":
                return (int)DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null).DayOfWeek;
            case "DayOfMonth":
                return DateTime.ParseExact(ctx.SessionDate, AppConstants.DATE_FORMAT, null).Day;
            // ── Gap (daily context + session open) ────────────────────────────
            case "GapToRthOpen":
                return Computations.GapToRthOpen(bars, BarData.Aggregate(prevMinuteBars).Close, ctx.ATR);
            // ── Prev session minute bars ──────────────────────────────────────
            case "OvernightRange":
                return Computations.OvernightRange(bars, ctx.ATR);
            case "OvernightVolumeRatio":
                return Computations.OvernightVolumeRatio(bars);
            case "OvernightDropRatio":
                return Computations.OvernightDropRatio(bars, ctx.ATR);
            case "RegularHoursRange":
                return Computations.RegularHoursRange(bars, ctx.ATR);
            case "FirstHourRange":
                return Computations.FirstRthHourRange(bars, ctx.ATR);
            case "LastHourRange":
                return Computations.LastRthHourRange(bars, ctx.ATR);
            case "HighTimeRatio":
                return Computations.HighTimeRatio(bars);
            case "LowTimeRatio":
                return Computations.LowTimeRatio(bars); 
            case "OpenToHighRatio":
                return Computations.OpenToHighRatio(bars, ctx.ATR);
            case "OpenToLowRatio":
                return Computations.OpenToLowRatio(bars, ctx.ATR);
            case "LastHourClosePosition":
                return Computations.LastRthHourClosePosition(bars);
            case "RthRange":
                return Computations.RthRange(bars, ctx.ATR);
            case "FirstHalfHourRange":
                return Computations.FirstHalfRthHourRange(bars, ctx.ATR);
            case "RthHighTimeRatio":
                return Computations.RthHighTimeRatio(bars);
            case "RthLowTimeRatio":
                return Computations.RthLowTimeRatio(bars);
            case "RthOpenToHighRatio":
                return Computations.RthOpenToHighRatio(bars, ctx.ATR);
            case "RthOpenToLowRatio":
                return Computations.RthOpenToLowRatio(bars, ctx.ATR);
            // ── Current session pre-market minute bars ────────────────────────
            case "PreMarketRange":
                return Computations.PreMarketRange(bars, ctx.ATR);
            case "PreMarketHighVsOpen":
                return Computations.PreMarketHighVsOpen(bars, ctx.ATR);
            case "PreMarketLowVsOpen":
                return Computations.PreMarketLowVsOpen(bars, ctx.ATR);
            case "PreMarketVolumeRatio":
                return Computations.PreMarketVolumeRatio(bars);
            default:
                throw new ArgumentException($"Unknown feature name: '{feature.Name}'");
        }
    }
}
