using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Predictors;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Api.Models.API.Predict;

namespace AutomatedTrading.Core.Api.Models.Evaluate;

/// <summary>
/// Encapsulates all trading logic for the PredictedHigh strategy so it can be exercised
/// independently of NinjaTrader in automated tests or a backtesting runner.
///
/// Production use (via the REST API):
///   var evaluator = new PredictedHighStrategyEvaluator(predictor, barRepo);
///   await evaluator.StartSessionAsync(sessionDate, sessionOpen, proximity, ...);
///   var eval = evaluator.Evaluate(barHigh, barClose, position, entryPrice);
///
/// Test use (inject values directly, no DB or ML required):
///   var evaluator = new PredictedHighStrategyEvaluator(proximity: 0.5, stopLossPoints: 5.0, ...);
///   evaluator.StartSession(predictedHigh: 4500.0, dailyAtr: 20.0);
///   var eval = evaluator.Evaluate(4499.0, 4497.0, PositionSide.Flat);
/// </summary>
public class SessionHighStrategyEvaluator
{
    // ─── Configuration ────────────────────────────────────────────────────────
    public double Proximity { get; private set; }
    public double StopLossPoints { get; private set; }
    public double TargetProfit { get; private set; }
    public double Trail { get; private set; }

    // ─── Observable session state ─────────────────────────────────────────────
    public bool IsReady { get; private set; }
    public double PredictedHigh { get; private set; }
    public double DailyAtr { get; private set; }

    // ─── Internal state ───────────────────────────────────────────────────────
    private bool _trailingStopActive;
    private double _trailingStopPrice;
    private readonly IBarHighPredictor? _predictor;
    private readonly IBarRepository? _barRepo;

    // ─── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Production constructor — injected with the ML predictor and bar repository.</summary>
    public SessionHighStrategyEvaluator(IBarHighPredictor predictor, IBarRepository barRepo)
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
    public SessionHighStrategyEvaluator(
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
        Logger.Info($"{GetType().Name}.StartSessionAsync:" +
            $"\r\n\t{"SessionDate",20}:{sessionDate,20}" + 
            $"\r\n\t{"SessionOpen",20}:{sessionOpen,20:f2}" + 
            $"\r\n\t{"Proximity",20}:{proximity,20:f2}" + 
            $"\r\n\t{"StopLoss",20}:{stopLossPoints,20:f2}" + 
            $"\r\n\t{"TargetProfit",20}:{targetProfit,20:f2}" + 
            $"\r\n\t{"Trail",20}:{trail,20:f2}");

        Proximity = proximity;
        StopLossPoints = stopLossPoints;
        TargetProfit = targetProfit;
        Trail = trail;
        resetState();

        if (_predictor is null || _barRepo is null) return;

        var prediction = await _predictor.PredictSessionHighAsync(sessionDate, sessionOpen);
        if (prediction is null) return;

        PredictedHigh = prediction.PredictedHigh;
        DailyAtr = await loadDailyAtrAsync(sessionDate);
        IsReady = true;
    }

    /// <summary>
    /// Test path: inject a known prediction and ATR directly, bypassing the ML model and DB.
    /// </summary>
    public void StartSession(double predictedHigh, double dailyAtr)
    {
        resetState();
        PredictedHigh = predictedHigh;
        DailyAtr = dailyAtr;
        IsReady = true;
    }

    // ─── Per-bar evaluation ───────────────────────────────────────────────────

    /// <summary>
    /// Evaluates a single completed bar and returns the signal to act on.
    /// <para><paramref name="entryPrice"/> should be the actual fill price when <paramref name="position"/>
    /// is <see cref="PositionSide.Short"/>; if omitted the bar close is used as a proxy.</para>
    /// </summary>
    public BarEvaluation Evaluate(
        double barHigh,
        double barClose,
        PositionSide position,
        double entryPrice = 0)
    {
        Logger.Info($"{GetType().Name}.Evaluate:" +
            $"\r\n\t{"BarHigh",20}:{barHigh,20:f2}" +
            $"\r\n\t{"BarClose",20}:{barClose,20:f2}" +
            $"\r\n\t{"Position",20}:{position,20}" + 
            $"\r\n\t{"EntryPrice",20}:{entryPrice,20}");

        if (!IsReady) return new(StrategySignal.None, 0);

        // ── Near predicted HIGH: enter short or flatten long ─────────────────
        if (barHigh >= PredictedHigh - Proximity * DailyAtr)
        {
            Logger.Info($"Bar.High near predicted high: barHigh @ {barHigh:f2}, predicted @ {PredictedHigh:f2}, prox * atr @ {Proximity * DailyAtr:f2}");
            if (position == PositionSide.Long)
            {
                return new(StrategySignal.ExitLong, 0);
            }

            if (position == PositionSide.Flat)
            {
                PredictedHigh += Proximity * DailyAtr;
                return new(StrategySignal.EnterShort, PredictedHigh + StopLossPoints);
            }
        }

        // ── Trailing stop management (short position only) ───────────────────
        if (position == PositionSide.Short)
        {
            var ep = entryPrice > 0 ? entryPrice : barClose;
            var profit = ep - barClose;

            if (!_trailingStopActive && profit >= TargetProfit)
            {
                _trailingStopActive = true;
                _trailingStopPrice  = barClose + Trail;
                Logger.Info($"Trailing Stop activated: profit @ {profit:f2}, target @ {TargetProfit:f2}, initial trailing stop @ {_trailingStopPrice:f2}");
                return new(StrategySignal.UpdateStop, _trailingStopPrice);
            }

            if (_trailingStopActive)
            {
                var candidate = barClose + Trail;
                if (candidate < _trailingStopPrice)
                {
                    _trailingStopPrice = candidate;
                    Logger.Info($"Trailing stop update triggered: close @ {barClose:f2}, trail @ {Trail:f2}, new stop @ {_trailingStopPrice:f2}");
                    return new(StrategySignal.UpdateStop, _trailingStopPrice);
                }
            }
        }
        else if (_trailingStopActive)
        {
            // Position was closed (stop hit or manual exit) — reset trailing state.
            _trailingStopActive = false;
            _trailingStopPrice = double.MaxValue;
        }

        return new(StrategySignal.None, 0);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private void resetState()
    {
        IsReady = false;
        PredictedHigh = double.MaxValue;
        DailyAtr = 0;
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
