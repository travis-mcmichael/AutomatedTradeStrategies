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
            p.Add(param.ParamKey, param.ParamValue);
        }
        var source = feature.Source;
        var bars = new List<BarData>();
        var prevDate = TradingCalendar.GetPreviousSessionDate(sessionDate);
        var minBarDate = ctx.MinuteBars.Keys
            .Select(k => DateTime.ParseExact(k, AppConstants.DATE_FORMAT, null))
            .DefaultIfEmpty(DateTime.MaxValue)
            .Min();
        while (!ctx.MinuteBars.ContainsKey(prevDate.ToString(AppConstants.DATE_FORMAT)))
        {
            if (prevDate < minBarDate)
                throw new InvalidOperationException($"No prior minute-bar data found before session {ctx.SessionDate}");
            prevDate = TradingCalendar.GetPreviousSessionDate(prevDate);
        }
        var prevMinuteBars = ctx.MinuteBars[prevDate.ToString(AppConstants.DATE_FORMAT)];
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
                if (currMinuteBars.Count == 0 || prevMinuteBars.Count == 0) return double.NaN;
                return Operations.SafeDiv(BarData.Aggregate(prevMinuteBars).High, BarData.Aggregate(currMinuteBars).Open);
            case "PrevLowVsOpen":
                if (currMinuteBars.Count == 0 || prevMinuteBars.Count == 0) return double.NaN;
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
                if (currMinuteBars.Count == 0 || prevMinuteBars.Count == 0) return double.NaN;
                return Operations.SafeDiv(BarData.Aggregate(currMinuteBars).Open - BarData.Aggregate(prevMinuteBars).High, ctx.ATR);
            case "OpenVsPrevLow":
                if (currMinuteBars.Count == 0 || prevMinuteBars.Count == 0) return double.NaN;
                return Operations.SafeDiv(BarData.Aggregate(currMinuteBars).Open - BarData.Aggregate(prevMinuteBars).Low, ctx.ATR);
            case "OpenVsPrevClose":
                if (currMinuteBars.Count == 0 || prevMinuteBars.Count == 0) return double.NaN;
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
                if (currMinuteBars.Count == 0 || prevMinuteBars.Count == 0) return double.NaN;
                return Computations.GapToRthOpen(currMinuteBars, BarData.Aggregate(prevMinuteBars).Close, ctx.ATR);
            // ── Prev session minute bars ──────────────────────────────────────
            case "OvernightRange":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.OvernightRange(currMinuteBars, ctx.ATR);
            case "OvernightVolumeRatio":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.OvernightVolumeRatio(currMinuteBars);
            case "OvernightDropRatio":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.OvernightDropRatio(currMinuteBars, ctx.ATR);
            case "RegularHoursRange":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.RegularHoursRange(currMinuteBars, ctx.ATR);
            case "FirstHourRange":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.FirstRthHourRange(currMinuteBars, ctx.ATR);
            case "LastHourRange":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.LastRthHourRange(currMinuteBars, ctx.ATR);
            case "HighTimeRatio":
                return Computations.HighTimeRatio(bars);
            case "LowTimeRatio":
                return Computations.LowTimeRatio(bars); 
            case "OpenToHighRatio":
                return Computations.OpenToHighRatio(bars, ctx.ATR);
            case "OpenToLowRatio":
                return Computations.OpenToLowRatio(bars, ctx.ATR);
            case "LastHourClosePosition":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.LastRthHourClosePosition(currMinuteBars);
            case "RthRange":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.RthRange(currMinuteBars, ctx.ATR);
            case "FirstHalfHourRange":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.FirstHalfRthHourRange(currMinuteBars, ctx.ATR);
            case "RthHighTimeRatio":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.RthHighTimeRatio(currMinuteBars);
            case "RthLowTimeRatio":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.RthLowTimeRatio(currMinuteBars);
            case "RthOpenToHighRatio":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.RthOpenToHighRatio(currMinuteBars, ctx.ATR);
            case "RthOpenToLowRatio":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.RthOpenToLowRatio(currMinuteBars, ctx.ATR);
            // ── Current session pre-market minute bars ────────────────────────
            case "PreMarketRange":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.PreMarketRange(currMinuteBars, ctx.ATR);
            case "PreMarketHighVsOpen":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.PreMarketHighVsOpen(currMinuteBars, ctx.ATR);
            case "PreMarketLowVsOpen":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.PreMarketLowVsOpen(currMinuteBars, ctx.ATR);
            case "PreMarketVolumeRatio":
                if (currMinuteBars.Count == 0) return double.NaN;
                return Computations.PreMarketVolumeRatio(currMinuteBars);
            default:
                throw new ArgumentException($"Unknown feature name: '{feature.Name}'");
        }
    }
}
