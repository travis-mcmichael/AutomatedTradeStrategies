using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Predictors;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Strategies;

public class SessionLowStrategyEvaluator
{
    // ─── Configuration ────────────────────────────────────────────────────────
    public double Proximity { get; private set; }
    public double StopLossPoints { get; private set; }
    public double TargetProfit { get; private set; }
    public double Trail { get; private set; }

    // ─── Observable session state ─────────────────────────────────────────────
    public bool IsReady { get; private set; }
    public double PredictedLow { get; private set; }
    public double DailyAtr { get; private set; }
    
    // ─── Internal state ───────────────────────────────────────────────────────
    private bool _hasLowHit;
    private bool _trailingStopActive;
    private double _trailingStopPrice;
    private readonly IBarLowPredictor? _predictor;
    private readonly IBarRepository? _barRepo;
    
    // ─── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Production constructor — injected with the ML predictor and bar repository.</summary>
    public SessionLowStrategyEvaluator(IBarLowPredictor predictor, IBarRepository barRepo)
    {
        _predictor = predictor;
        _barRepo = barRepo;
        Proximity = 0.5;
        StopLossPoints = 5.0;
        TargetProfit = 20.0;
        Trail = 10.0;
        resetState();
    }

    /// <summary>Test constructor — no external dependencies needed.</summary>
    public SessionLowStrategyEvaluator(
        double proximity, double stopLossPoints, double targetProfit, double trail)
    {
        Proximity = proximity;
        StopLossPoints = stopLossPoints;
        TargetProfit = targetProfit;
        Trail = trail;
        resetState();
    }
    
    // ─── Session initialisation ───────────────────────────────────────────────

    /// <summary>
    /// Production path: fetches the prediction from the ML model and computes daily ATR
    /// from historical bar data. Parameters are forwarded from the NinjaTrader UI so the
    /// evaluator stays in sync with the strategy properties panel.
    /// </summary>
    public async Task StartSessionAsync(
        string sessionDate,
        double sessionOpen,
        double proximity,
        double stopLossPoints,
        double targetProfit,
        double trail)
    {
        Proximity = proximity;
        StopLossPoints = stopLossPoints;
        TargetProfit = targetProfit;
        Trail = trail;
        resetState();

        if (_predictor is null || _barRepo is null) return;

        var prediction = await _predictor.PredictSessionLowAsync(sessionDate, sessionOpen);
        if (prediction is null) return;

        PredictedLow = prediction.PredictedLow;
        DailyAtr = await loadDailyAtrAsync(sessionDate);
        IsReady = true;
    }

    public void StartSession(double predictedLow, double dailyAtr)
    {
        resetState();
        PredictedLow = predictedLow;
        DailyAtr = dailyAtr;
        IsReady = true;
    }

    /// <summary>
    /// Evaluates a single completed bar and returns the signal to act on.
    /// <para><paramref name="entryPrice"/> should be the actual fill price when <paramref name="position"/>
    /// is <see cref="PositionSide.Short"/>; if omitted the bar close is used as a proxy.</para>
    /// </summary>
    public BarEvaluation Evaluate(
        double barLow,
        double barClose,
        PositionSide position,
        double entryPrice = 0)
    {
        if (!IsReady) return new (StrategySignal.None, 0);
        if (barLow <= PredictedLow + Proximity * DailyAtr)
        {
            if (position == PositionSide.Short)
            {
                _hasLowHit = true;
                return new (StrategySignal.ExitShort, 0);
            }
            if (position == PositionSide.Flat && !_hasLowHit)
            {
                _hasLowHit = true;
                return new (StrategySignal.EnterLong, PredictedLow - StopLossPoints);
            }
            _hasLowHit = true;
        }
        if (position == PositionSide.Long)
        {
            var ep = entryPrice > 0 ? entryPrice : barClose;
            var profit = barClose - ep;
            if (!_trailingStopActive && profit >= TargetProfit)
            {
                _trailingStopActive = true;
                _trailingStopPrice = barClose - Trail;
                return new (StrategySignal.UpdateStop, _trailingStopPrice);
            }
            if (_trailingStopActive)
            {
                var candidate = barClose - Trail;
                if (candidate > _trailingStopPrice)
                {
                    _trailingStopPrice = candidate;
                    return new (StrategySignal.UpdateStop, _trailingStopPrice);
                }
            }
        }
        else if (_trailingStopActive)
        {
            _trailingStopActive = false;
            _trailingStopPrice = double.MinValue;
        }
        return new (StrategySignal.None, 0);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private void resetState()
    {
        IsReady = false;
        PredictedLow = double.MinValue;
        DailyAtr = 0;
        _hasLowHit = false;
        _trailingStopActive = false;
        _trailingStopPrice = double.MaxValue;
    }

    /// <summary>
    /// Walks back through calendar days, collecting up to 20 trading sessions and
    /// computing ATR(20) from the aggregated daily bars.
    /// </summary>
    private async Task<double> loadDailyAtrAsync(string sessionDate)
    {
        var dailyBars = new List<BarData>();
        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        for (var i = 1; dailyBars.Count < 20 && i <= 60; i++)
        {
            var d = date.AddDays(-i).ToString("yyyyMMdd");
            var bars = await _barRepo!.GetBarsBySessionDateAsync(d);
            if (bars.Count == 0) continue;

            var ordered = bars.OrderBy(b => b.Timestamp).ToList();
            dailyBars.Insert(0, new BarData
            {
                Open = ordered[0].Open,
                High = ordered.Max(b => b.High),
                Low = ordered.Min(b => b.Low),
                Close = ordered.Last().Close,
            });
        }

        if (dailyBars.Count < 2) return 0;

        var lookback = Math.Min(20, dailyBars.Count - 1);
        return Computations.ATR(dailyBars, dailyBars.Count - 1, lookback);
    }
}