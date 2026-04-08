using AutomatedTrading.Core.Api.Models.API.Predict;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Framework.Predictors;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Models.Evaluate;

/// <summary>
/// Unified strategy evaluator. Holds a set of ML predictors (one per active model from
/// models.json) and runs all of them at session start, exposing the full prediction set
/// for use in combined entry/exit logic.
///
/// The active high and low price levels used for signal generation are resolved from
/// <see cref="Predictions"/> automatically: trade-day predictions take precedence over
/// session-day ones when both are present. All other predictions remain accessible via
/// <see cref="Predictions"/> for implementing more complex multi-model logic.
/// </summary>
public class StrategyEvaluator
{
    // ─── Configuration ────────────────────────────────────────────────────────
    public double Proximity { get; private set; }
    public double StopLossPoints { get; private set; }
    public double TargetProfit { get; private set; }
    public double Trail { get; private set; }

    // ─── Observable session state ─────────────────────────────────────────────
    public bool IsReady { get; private set; }
    public double DailyAtr { get; private set; }

    /// <summary>All predictions gathered at session start, keyed by model name.</summary>
    public IReadOnlyDictionary<string, double> Predictions => _predictions;

    // ─── Private state ────────────────────────────────────────────────────────
    private readonly Dictionary<string, IMovementPredictor> _predictors;
    private readonly IBarRepository? _barRepo;
    private readonly Dictionary<string, double> _predictions = new();

    // Resolved active levels (derived from Predictions at StartSession time)
    private double _predictedHigh;
    private double _predictedLow;

    // Per-session signal state
    private bool _highLevelHit;
    private bool _lowLevelHit;
    private bool _trailingStopActive;
    private double _trailingStopPrice;
    private int _barCount;

    // ─── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Production constructor — receives all active model predictors from DI.</summary>
    public StrategyEvaluator(Dictionary<string, IMovementPredictor> predictors, IBarRepository barRepo)
    {
        _predictors = predictors;
        _barRepo = barRepo;
        Proximity = 0.5;
        StopLossPoints = 5.0;
        TargetProfit = 20.0;
        Trail = 10.0;
        resetState();
    }

    /// <summary>Test constructor — no external dependencies needed.</summary>
    public StrategyEvaluator(double proximity, double stopLossPoints, double targetProfit, double trail)
    {
        _predictors = new();
        Proximity = proximity;
        StopLossPoints = stopLossPoints;
        TargetProfit = targetProfit;
        Trail = trail;
        resetState();
    }

    // ─── Session initialisation ───────────────────────────────────────────────

    /// <summary>
    /// Runs every active ML predictor and stores results in <see cref="Predictions"/>.
    /// Resolves the working high/low levels used by <see cref="Evaluate"/> from those results.
    /// Call once per session at the relevant market open.
    /// </summary>
    public async Task StartSessionAsync(
        string sessionDate,
        double sessionOpen,
        double proximity,
        double stopLossPoints,
        double targetProfit,
        double trail)
    {
        Logger.Info($"{nameof(StrategyEvaluator)}.StartSessionAsync:" +
            $"\r\n\t{"SessionDate",20}:{sessionDate,20}" +
            $"\r\n\t{"SessionOpen",20}:{sessionOpen,20:f2}" +
            $"\r\n\t{"Proximity",20}:{proximity,20:f2}" +
            $"\r\n\t{"StopLoss",20}:{stopLossPoints,20:f2}" +
            $"\r\n\t{"TargetProfit",20}:{targetProfit,20:f2}" +
            $"\r\n\t{"Trail",20}:{trail,20:f2}");

        Proximity      = proximity;
        StopLossPoints = stopLossPoints;
        TargetProfit   = targetProfit;
        Trail          = trail;
        resetState();

        if (_barRepo is null) return;

        foreach (var (modelName, predictor) in _predictors)
        {
            var prediction = await predictor.PredictMovement(sessionDate, sessionOpen);
            if (prediction is null) continue;
            _predictions[modelName] = sessionOpen + prediction.PredictedMove;
            Logger.Info($"  {modelName}: {_predictions[modelName]:f2} (move {prediction.PredictedMove:+0.00;-0.00})");
        }

        if (_predictions.Count == 0) return;

        DailyAtr = await loadDailyAtrAsync(sessionDate);
        if (DailyAtr == 0) return;

        resolveActiveLevels(sessionOpen);
        IsReady = true;
    }

    /// <summary>
    /// Test path: inject known predictions and ATR directly without hitting the DB or models.
    /// </summary>
    public void StartSession(Dictionary<string, double> predictions, double dailyAtr, double sessionOpen)
    {
        resetState();
        foreach (var (k, v) in predictions)
            _predictions[k] = v;
        DailyAtr = dailyAtr;
        resolveActiveLevels(sessionOpen);
        IsReady = true;
    }

    // ─── Per-bar evaluation ───────────────────────────────────────────────────

    /// <summary>
    /// Evaluates a single completed bar against all available predictions and returns
    /// the signal the strategy should act on.
    /// </summary>
    public BarEvaluation Evaluate(
        double barHigh,
        double barLow,
        double barClose,
        PositionSide position,
        double entryPrice = 0)
    {
        Logger.Info($"{nameof(StrategyEvaluator)}.Evaluate:" +
            $"\r\n\t{"BarHigh",20}:{barHigh,20:f2}" +
            $"\r\n\t{"BarLow",20}:{barLow,20:f2}" +
            $"\r\n\t{"BarClose",20}:{barClose,20:f2}" +
            $"\r\n\t{"Position",20}:{position,20}" +
            $"\r\n\t{"EntryPrice",20}:{entryPrice,20:f2}");

        if (!IsReady) return new(StrategySignal.None, 0);

        _barCount++;
        if (_barCount < 15) return new(StrategySignal.None, 0);

        var band = Proximity * DailyAtr;

        // ── Near predicted HIGH: enter short or exit long ─────────────────────
        if (_predictedHigh != double.MinValue && barHigh >= _predictedHigh - band)
        {
            Logger.Info($"Bar.High near predicted high: {barHigh:f2} >= {_predictedHigh - band:f2}");

            if (position == PositionSide.Long)
                return new(StrategySignal.ExitLong, 0);

            if (position == PositionSide.Flat && !_highLevelHit)
            {
                _highLevelHit   = true;
                _predictedHigh += band; // shift level away to prevent immediate re-entry
                return new(StrategySignal.EnterShort, _predictedHigh + StopLossPoints);
            }

            _highLevelHit = true;
        }

        // ── Near predicted LOW: enter long or exit short ──────────────────────
        if (_predictedLow != double.MaxValue && barLow <= _predictedLow + band)
        {
            Logger.Info($"Bar.Low near predicted low: {barLow:f2} <= {_predictedLow + band:f2}");

            if (position == PositionSide.Short)
                return new(StrategySignal.ExitShort, 0);

            if (position == PositionSide.Flat && !_lowLevelHit)
            {
                _lowLevelHit  = true;
                _predictedLow -= band / 2; // tighten level slightly after touch
                return new(StrategySignal.EnterLong, _predictedLow - StopLossPoints);
            }

            _lowLevelHit = true;
        }

        // ── Trailing stop: short ──────────────────────────────────────────────
        if (position == PositionSide.Short)
        {
            var ep     = entryPrice > 0 ? entryPrice : barClose;
            var profit = ep - barClose;

            if (!_trailingStopActive && profit >= TargetProfit)
            {
                _trailingStopActive = true;
                _trailingStopPrice  = barClose + Trail;
                Logger.Info($"Short trailing stop activated: stop @ {_trailingStopPrice:f2}");
                return new(StrategySignal.UpdateStop, _trailingStopPrice);
            }

            if (_trailingStopActive)
            {
                var candidate = barClose + Trail;
                if (candidate < _trailingStopPrice)
                {
                    _trailingStopPrice = candidate;
                    Logger.Info($"Short trailing stop updated: stop @ {_trailingStopPrice:f2}");
                    return new(StrategySignal.UpdateStop, _trailingStopPrice);
                }
            }
        }
        // ── Trailing stop: long ───────────────────────────────────────────────
        else if (position == PositionSide.Long)
        {
            var ep     = entryPrice > 0 ? entryPrice : barClose;
            var profit = barClose - ep;

            if (!_trailingStopActive && profit >= TargetProfit)
            {
                _trailingStopActive = true;
                _trailingStopPrice  = barClose - Trail;
                Logger.Info($"Long trailing stop activated: stop @ {_trailingStopPrice:f2}");
                return new(StrategySignal.UpdateStop, _trailingStopPrice);
            }

            if (_trailingStopActive)
            {
                var candidate = barClose - Trail;
                if (candidate > _trailingStopPrice)
                {
                    _trailingStopPrice = candidate;
                    Logger.Info($"Long trailing stop updated: stop @ {_trailingStopPrice:f2}");
                    return new(StrategySignal.UpdateStop, _trailingStopPrice);
                }
            }
        }
        else if (_trailingStopActive)
        {
            _trailingStopActive = false;
            _trailingStopPrice  = 0;
        }

        return new(StrategySignal.None, 0);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Selects the working high/low levels from the predictions dictionary.
    /// Trade-day variants take precedence over session-day ones when both are available.
    /// </summary>
    private void resolveActiveLevels(double sessionOpen)
    {
        _predictedHigh = _predictions.TryGetValue("mes.trade-day-bar.high", out var tdh)   ? tdh
                       : _predictions.TryGetValue("mes.session-day-bar.high", out var sdh) ? sdh
                       : double.MinValue;

        _predictedLow  = _predictions.TryGetValue("mes.trade-day-bar.low", out var tdl)    ? tdl
                       : _predictions.TryGetValue("mes.session-day-bar.low", out var sdl)  ? sdl
                       : double.MaxValue;

        Logger.Info($"Active levels — High: {_predictedHigh:f2}  Low: {_predictedLow:f2}  Open: {sessionOpen:f2}");
    }

    private void resetState()
    {
        IsReady             = false;
        DailyAtr            = 0;
        _predictions.Clear();
        _predictedHigh      = double.MinValue;
        _predictedLow       = double.MaxValue;
        _highLevelHit       = false;
        _lowLevelHit        = false;
        _trailingStopActive = false;
        _trailingStopPrice  = 0;
        _barCount           = 0;
    }

    private async Task<double> loadDailyAtrAsync(string sessionDate)
    {
        var dailyBars = new List<BarData>();
        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        for (var i = 1; dailyBars.Count < 20 && i <= 60; i++)
        {
            var d    = date.AddDays(-i).ToString("yyyyMMdd");
            var bars = await _barRepo!.GetBarsBySessionDateAsync(d);
            if (bars.Count == 0) continue;

            var ordered = bars.OrderBy(b => b.Timestamp).ToList();
            dailyBars.Insert(0, new BarData
            {
                Open  = ordered.First().Open,
                High  = ordered.Max(b => b.High),
                Low   = ordered.Min(b => b.Low),
                Close = ordered.Last().Close,
            });
        }

        if (dailyBars.Count < 2) return 0;
        var lookback = Math.Min(20, dailyBars.Count - 1);
        return Computations.ATR(dailyBars, dailyBars.Count - 1, lookback);
    }
}
