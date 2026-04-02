using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Api.Models.API.Predict;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Framework.Predictors;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Models.Evaluate;

/// <summary>
/// Trading logic for the TradeDayLow strategy.
/// Predicts the low within RTH (08:30–15:15) from the RTH open.
///
/// Production use (via the REST API):
///   var evaluator = new TradeDayLowStrategyEvaluator(predictor, barRepo);
///   await evaluator.StartSessionAsync(sessionDate, rthOpen, proximity, ...);
///   var eval = evaluator.Evaluate(barLow, barClose, position, entryPrice);
///
/// Test use (inject values directly, no DB or ML required):
///   var evaluator = new TradeDayLowStrategyEvaluator(proximity: 0.5, stopLossPoints: 5.0, ...);
///   evaluator.StartSession(predictedLow: 4400.0, dailyAtr: 20.0);
///   var eval = evaluator.Evaluate(4401.0, 4403.0, PositionSide.Flat);
/// </summary>
public class TradeDayLowStrategyEvaluator
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
    private bool _trailingStopActive;
    private double _trailingStopPrice;
    private readonly ITradeDayLowPredictor? _predictor;
    private readonly IBarRepository? _barRepo;
    private int _barCount;

    // ─── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Production constructor — injected with the ML predictor and bar repository.</summary>
    public TradeDayLowStrategyEvaluator(ITradeDayLowPredictor predictor, IBarRepository barRepo)
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
    public TradeDayLowStrategyEvaluator(
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
    /// Production path: fetches the RTH-low prediction from the ML model and computes
    /// daily ATR from historical bar data. Call once at the RTH open each session.
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

        var prediction = await _predictor.PredictTradeDayLowAsync(sessionDate, sessionOpen);
        if (prediction is null) return;

        PredictedLow = prediction.PredictedLow;
        DailyAtr = await loadDailyAtrAsync(sessionDate);
        IsReady = true;
    }

    /// <summary>Test path: inject a known prediction and ATR directly.</summary>
    public void StartSession(double predictedLow, double dailyAtr)
    {
        resetState();
        PredictedLow = predictedLow;
        DailyAtr = dailyAtr;
        IsReady = true;
    }

    // ─── Per-bar evaluation ───────────────────────────────────────────────────

    public BarEvaluation Evaluate(
        double barLow,
        double barClose,
        PositionSide position,
        double entryPrice = 0)
    {
        Logger.Info($"{GetType().Name}.Evaluate:" +
            $"\r\n\t{"BarLow",20}:{barLow,20:f2}" +
            $"\r\n\t{"BarClose",20}:{barClose,20:f2}" +
            $"\r\n\t{"Position",20}:{position,20}" +
            $"\r\n\t{"EntryPrice",20}:{entryPrice,20}");

        if (!IsReady || _barCount < 15) return new(StrategySignal.None, 0);

        // ── Near predicted LOW: enter long or flatten short ──────────────────
        if (barLow <= PredictedLow + Proximity * DailyAtr)
        {
            Logger.Info($"Bar.Low near predicted low: barLow @ {barLow:f2}, predicted @ {PredictedLow:f2}, prox * atr @ {Proximity * DailyAtr:f2}");
            
            if (position == PositionSide.Short)
            {
                return new(StrategySignal.ExitShort, 0);
            }

            if (position == PositionSide.Flat)
            {
                PredictedLow -= Proximity * DailyAtr / 2;
                return new(StrategySignal.EnterLong, PredictedLow - StopLossPoints);
            }
        }

        // ── Trailing stop management (long position only) ────────────────────
        if (position == PositionSide.Long)
        {
            var ep = entryPrice > 0 ? entryPrice : barClose;
            var profit = barClose - ep;

            if (!_trailingStopActive && profit >= TargetProfit)
            {
                _trailingStopActive = true;
                _trailingStopPrice = barClose - Trail;
                Logger.Info($"Trailing Stop activated: profit @ {profit:f2}, target @ {TargetProfit:f2}, initial trailing stop @ {_trailingStopPrice:f2}");
                return new(StrategySignal.UpdateStop, _trailingStopPrice);
            }

            if (_trailingStopActive)
            {
                var candidate = barClose - Trail;
                if (candidate > _trailingStopPrice + TargetProfit)
                {
                    _trailingStopPrice = candidate + Trail;
                    Logger.Info($"Trailing stop update triggered: close @ {barClose:f2}, trail @ {Trail:f2}, new stop @ {_trailingStopPrice:f2}");
                    return new(StrategySignal.UpdateStop, _trailingStopPrice);
                }
            }
        }
        else if (_trailingStopActive)
        {
            _trailingStopActive = false;
            _trailingStopPrice = double.MinValue;
        }

        return new(StrategySignal.None, 0);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private void resetState()
    {
        IsReady = false;
        PredictedLow = double.MinValue;
        DailyAtr = 0;
        _trailingStopActive = false;
        _trailingStopPrice = double.MaxValue;
        _barCount = 0;
    }

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
