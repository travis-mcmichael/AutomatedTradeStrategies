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
/// via <see cref="Predictions"/>.
///
/// Entry/exit signal logic has not yet been implemented. <see cref="Evaluate"/> always
/// returns <see cref="StrategySignal.None"/> until trading logic is added.
/// </summary>
public class StrategyEvaluator
{
    // ─── Observable session state ─────────────────────────────────────────────
    public bool IsReady    { get; private set; }
    public double DailyAtr { get; private set; }

    /// <summary>All predictions gathered at session start, keyed by model name.</summary>
    public IReadOnlyDictionary<string, double> Predictions => _predictions;

    // ─── Private state ────────────────────────────────────────────────────────
    private readonly Dictionary<string, IMovementPredictor> _predictors;
    private readonly IBarRepository? _barRepo;
    private readonly Dictionary<string, double> _predictions = new();

    // ─── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Production constructor — receives all active model predictors from DI.</summary>
    public StrategyEvaluator(Dictionary<string, IMovementPredictor> predictors, IBarRepository barRepo)
    {
        _predictors = predictors;
        _barRepo    = barRepo;
        resetState();
    }

    /// <summary>Test constructor — no external dependencies needed.</summary>
    public StrategyEvaluator()
    {
        _predictors = new();
        resetState();
    }

    // ─── Session initialisation ───────────────────────────────────────────────

    /// <summary>
    /// Runs every active ML predictor and stores results in <see cref="Predictions"/>.
    /// Also loads the 20-day ATR for the session.
    /// Call once per session at the relevant market open.
    /// </summary>
    public async Task StartSessionAsync(string sessionDate, double sessionOpen)
    {
        Logger.Info($"{nameof(StrategyEvaluator)}.{nameof(StartSessionAsync)}:" +
            $"\r\n\t{"SessionDate",20}:{sessionDate,20}" +
            $"\r\n\t{"SessionOpen",20}:{sessionOpen,20:f2}");

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

        IsReady = true;
    }

    /// <summary>
    /// Test path: inject known predictions and ATR directly without hitting the DB or models.
    /// </summary>
    public void StartSession(Dictionary<string, double> predictions, double dailyAtr)
    {
        resetState();
        foreach (var (k, v) in predictions)
            _predictions[k] = v;
        DailyAtr = dailyAtr;
        IsReady  = true;
    }

    // ─── Per-bar evaluation ───────────────────────────────────────────────────

    /// <summary>
    /// Called on every bar close. Returns <see cref="StrategySignal.None"/> until
    /// entry/exit logic is implemented.
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

        return new BarEvaluation(StrategySignal.None, 0);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private void resetState()
    {
        IsReady  = false;
        DailyAtr = 0;
        _predictions.Clear();
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
