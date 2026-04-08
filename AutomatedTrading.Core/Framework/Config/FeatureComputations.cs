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
    public BarData Current { get; set; }
    public double Open { get; set; }
    public double ATR { get; set; }
    public string SessionDate { get; set; }
    public List<BarData> PrevBars { get; set; }
    public List<BarData>? CurrentBars { get; set; } = null;
    public FeatureContext(List<BarData> sequence, int j, BarData current, double open, double atr, string sessionDate, List<BarData> prevBars, List<BarData>? currentBars)
    {
        Sequence = sequence;
        J = j;
        Current = current;
        Open = open;
        ATR = atr;
        SessionDate = sessionDate;
        PrevBars = prevBars;
        CurrentBars = currentBars;
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
        var p = feature.Params;

        switch (feature.Name)
        {
            // ── Prev daily bar vs session open ────────────────────────────────
            case "PrevHighVsOpen":
                return Operations.SafeDiv(ctx.Current.High, ctx.Open);

            case "PrevLowVsOpen":
                return Operations.SafeDiv(ctx.Current.Low, ctx.Open);

            // ── Prev daily bar shape ──────────────────────────────────────────
            case "PrevHighLowRange":
                return Computations.HighLowRange(ctx.Current, ctx.ATR);

            case "PrevBodySize":
                return Computations.BodySize(ctx.Current, ctx.ATR);

            case "PrevUpperWick":
                return Computations.UpperWick(ctx.Current, ctx.ATR);

            case "PrevLowerWick":
                return Computations.LowerWick(ctx.Current, ctx.ATR);

            case "PrevClosePosition":
                return Computations.ClosePosition(ctx.Current);

            // ── ATR ratios ────────────────────────────────────────────────────
            case "AtrVsOpen":
                return Operations.SafeDiv(Computations.ATR(ctx.Sequence, ctx.J, p!["period"]), ctx.Open);

            case "AtrRatio":
                return Operations.SafeDiv(
                    Computations.ATR(ctx.Sequence, ctx.J, p!["fast"]),
                    Computations.ATR(ctx.Sequence, ctx.J, p["slow"]));

            // ── Range features ────────────────────────────────────────────────
            case "AvgRange":
                return Computations.AverageRange(ctx.Sequence, ctx.J, p!["period"]);

            case "RangeVsAvgRange10":
                return Operations.SafeDiv(
                    ctx.Current.High - ctx.Current.Low,
                    Computations.AverageRange(ctx.Sequence, ctx.J, 10));

            // ── Returns ───────────────────────────────────────────────────────
            case "Return":
                return Computations.Return(ctx.Sequence, ctx.J, p!["lookback"]);

            // ── SMA ratios ────────────────────────────────────────────────────
            case "SmaRatio":
                return Operations.SafeDiv(
                    Computations.SMA(ctx.Sequence, ctx.J, p!["fast"], x => x.Close),
                    Computations.SMA(ctx.Sequence, ctx.J, p["slow"], x => x.Close));

            case "OpenVsSma":
                return Operations.SafeDiv(
                    ctx.Open,
                    Computations.SMA(ctx.Sequence, ctx.J, p!["period"], x => x.Close));

            // ── Volume features ───────────────────────────────────────────────
            case "VolumeSma":
                return Computations.SMA(ctx.Sequence, ctx.J, p!["period"], x => x.Volume);

            case "VolumeZScore":
                return Computations.VolumeZScore(ctx.Sequence, ctx.J, p!["period"]);

            // ── Session open gap vs prev bar ──────────────────────────────────
            case "OpenVsPrevHigh":
                return Operations.SafeDiv(ctx.Open - ctx.Current.High, ctx.ATR);

            case "OpenVsPrevLow":
                return Operations.SafeDiv(ctx.Open - ctx.Current.Low, ctx.ATR);

            case "OpenVsPrevClose":
                return Operations.SafeDiv(ctx.Open - ctx.Current.Close, ctx.ATR);

            // ── Momentum / oscillators ────────────────────────────────────────
            case "Rsi":
                return Computations.RSI(ctx.Sequence, ctx.J, p!["period"]);

            // ── Calendar ──────────────────────────────────────────────────────
            case "DayOfWeek":
                return (int)DateTime.ParseExact(ctx.SessionDate, "yyyyMMdd", null).DayOfWeek;

            case "DayOfMonth":
                return DateTime.ParseExact(ctx.SessionDate, "yyyyMMdd", null).Day;

            // ── Gap (daily context + session open) ────────────────────────────
            case "GapToRthOpen":
                return Computations.GapToRthOpen(ctx.Open, ctx.Current.Close, ctx.ATR);

            // ── Prev session minute bars ──────────────────────────────────────
            case "OvernightRange":
                return Computations.OvernightRange(ctx.PrevBars, ctx.ATR);

            case "OvernightVolumeRatio":
                return Computations.OvernightVolumeRatio(ctx.PrevBars);

            case "OvernightDropRatio":
                return Computations.OvernightDropRatio(ctx.PrevBars, ctx.ATR);

            case "RegularHoursRange":
                return Computations.RegularHoursRange(ctx.PrevBars, ctx.ATR);

            case "FirstHourRange":
                return Computations.FirstHourRange(ctx.PrevBars, ctx.ATR);

            case "LastHourRange":
                return Computations.LastHourRange(ctx.PrevBars, ctx.ATR);

            case "HighTimeRatio":
                return Computations.HighTimeRatio(ctx.PrevBars);

            case "LowTimeRatio":
                return Computations.LowTimeRatio(ctx.PrevBars);

            case "PrevOpenToHighRatio":
                return Computations.PrevOpenToHighRatio(ctx.PrevBars, ctx.ATR);

            case "PrevOpenToLowRatio":
                return Computations.PrevOpenToLowRatio(ctx.PrevBars, ctx.ATR);

            case "LastHourClosePosition":
                return Computations.LastHourClosePosition(ctx.PrevBars);

            case "RthRange":
                return Computations.RthRange(ctx.PrevBars, ctx.ATR);

            case "FirstHalfHourRange":
                return Computations.FirstHalfHourRange(ctx.PrevBars, ctx.ATR);

            case "RthHighTimeRatio":
                return Computations.RthHighTimeRatio(ctx.PrevBars);

            case "RthLowTimeRatio":
                return Computations.RthLowTimeRatio(ctx.PrevBars);

            case "RthOpenToHighRatio":
                return Computations.RthOpenToHighRatio(ctx.PrevBars, ctx.ATR);

            case "RthOpenToLowRatio":
                return Computations.RthOpenToLowRatio(ctx.PrevBars, ctx.ATR);

            // ── Current session pre-market minute bars ────────────────────────
            case "PreMarketRange":
                return Computations.PreMarketRange(ctx.CurrentBars!, ctx.ATR);

            case "PreMarketHighVsOpen":
                return Computations.PreMarketHighVsOpen(ctx.CurrentBars!, ctx.Open, ctx.ATR);

            case "PreMarketLowVsOpen":
                return Computations.PreMarketLowVsOpen(ctx.CurrentBars!, ctx.Open, ctx.ATR);

            case "PreMarketVolumeRatio":
                return Computations.PreMarketVolumeRatio(ctx.CurrentBars!);

            default:
                throw new ArgumentException($"Unknown feature name: '{feature.Name}'");
        }
    }
}
