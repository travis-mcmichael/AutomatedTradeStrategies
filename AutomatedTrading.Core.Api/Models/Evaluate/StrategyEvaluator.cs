using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using AutomatedTrading.Core.Api.Models.API.Predict;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Config;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Framework.Predictors;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Models.Evaluate;

/// <summary>
/// Unified strategy evaluator. Holds a set of ML predictors (one per active model from
/// models.json) and lazily runs each predictor the first time a bar falls within that
/// model's configured window (WindowStart / WindowEnd in "HHmmss" format). Models with
/// no window configured predict on the first available bar.
///
/// Entry/exit signal logic has not yet been implemented. <see cref="Evaluate"/> always
/// returns <see cref="StrategySignal.None"/> until trading logic is added.
/// </summary>
public class StrategyEvaluator
{
    private const int LOOKBACK = 20;
    // ─── Observable session state ─────────────────────────────────────────────
    public bool IsReady    { get; private set; }
    public double DailyAtr { get; private set; }

    /// <summary>All predictions gathered so far this session, keyed by model name.</summary>
    public IReadOnlyDictionary<string, double> Predictions => _predictions;

    // ─── Private state ────────────────────────────────────────────────────────
    private readonly Dictionary<string, (IMovementPredictor Predictor, ModelEntry Model)> _predictors;
    private readonly IBarRepository? _barRepo;
    private readonly ConcurrentDictionary<string, double> _predictions = new();
    private readonly ConcurrentDictionary<string, bool> _predictedModels = new();
    private readonly Dictionary<string, List<BarData>> _data = new();

    private string? _sessionDate;

    // ─── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Production constructor — receives all active model predictors from DI.</summary>
    public StrategyEvaluator(
        Dictionary<string, (IMovementPredictor Predictor, ModelEntry Model)> predictors,
        IBarRepository barRepo)
    {
        _predictors = predictors;
        _barRepo    = barRepo;
        resetState();
    }

    // ─── Session initialisation ───────────────────────────────────────────────

    /// <summary>
    /// Stores session context and loads the 20-day ATR. Does NOT run any predictors —
    /// predictions are triggered lazily from <see cref="Evaluate"/> as bars arrive.
    /// Call once per session at the relevant market open.
    /// </summary>
    public async Task StartSessionAsync(string sessionDate)
    {
        Logger.Info($"{nameof(StrategyEvaluator)}.StartSessionAsync:" +
            $"SessionDate={sessionDate,-12}");

        resetState();

        _sessionDate = sessionDate;

        if (_barRepo is null) return;

        var dataTmp = new List<BarData>();
        var end = DateTime.ParseExact(_sessionDate, AppConstants.DATE_FORMAT, null);
        var start = end;
        for (var i = 0; i <= LOOKBACK; i++)
            start = TradingCalendar.GetPreviousSessionDate(start);
        await _barRepo.StreamBarsAsync(start, end, bar =>
        {
            if (bar.SessionDate == null) return Task.CompletedTask;
            if (!_data.TryGetValue(bar.SessionDate, out var list))
            {
                list = new List<BarData>();
                _data[bar.SessionDate] = list;
            }
            list.Add(bar);
            return Task.CompletedTask;
        });
        Logger.Info($"Loaded sessions starting @ {start:yyyy-MM-dd} through {end:yyyy-MM-dd}");

        IsReady = true;
    }

    // ─── Per-bar evaluation ───────────────────────────────────────────────────

    /// <summary>
    /// Called on every bar close. For each model whose prediction window has been reached
    /// and has not yet been predicted this session, runs inference and stores the result.
    /// Returns <see cref="StrategySignal.None"/> until entry/exit logic is implemented.
    /// </summary>
    public async Task<BarEvaluation> Evaluate(
        double barHigh,
        double barLow,
        double barClose,
        PositionSide position,
        string sessionTime,
        double entryPrice = 0)
    {
        Logger.Info($"{nameof(StrategyEvaluator)}.Evaluate:\t" +
            $"BarHigh={barHigh,-10:f2}" +
            $"BarLow={barLow,-10:f2}" +
            $"BarClose={barClose,-10:f2}" +
            $"Position={position,-6}" +
            $"SessionTime={sessionTime,-9}" +
            $"EntryPrice={entryPrice,-10:f2}");

        if (IsReady && _sessionDate is not null)
            await runPendingPredictions(sessionTime);

        return new BarEvaluation(StrategySignal.None, 0);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Checks every model that has not yet predicted this session and runs inference
    /// for any whose window has opened by <paramref name="sessionTime"/>.
    /// </summary>
    private async Task runPendingPredictions(string sessionTime)
    {
        foreach (var (modelName, (predictor, model)) in _predictors)
        {
            if (_predictedModels.ContainsKey(modelName)) continue;
            if (!isWindowOpen(sessionTime, model.WindowStart, model.WindowEnd)) continue;

            Logger.Info($"Running prediction for {model.ModelName}");

            // Fire-and-forget is intentional: Evaluate is called synchronously from
            // the bar-close handler. Await in a background task and store when done.
            _ = Task.Run(async () =>
            {
                try
                {
                    var sequence = new List<BarData>();
                    switch (model.BarType)
                    {
                        case "window":
                            var start = int.Parse(model.WindowStart);
                            var end = int.Parse(model.WindowEnd);
                            foreach (var key in _data.Keys)
                            {
                                sequence.Add(
                                    BarData.Aggregate(_data[key].Where(b =>
                                    {
                                        var time = int.Parse(b.SessionTime);
                                        return time >= start && time < end;
                                    })
                                    .OrderBy(b => b.Timestamp)
                                    .ToList()));
                            }
                            break;
                        case "session":
                            foreach (var key in _data.Keys)
                                sequence.Add(BarData.Aggregate(_data[key]));
                        break;
                        case "minute":
                            if (_data[_sessionDate] != null)
                            {
                                sequence = _data[_sessionDate];
                                if (sequence.Count > LOOKBACK)
                                {
                                    sequence = sequence
                                        .Skip(sequence.Count - LOOKBACK)
                                        .OrderBy(b => b.Timestamp)
                                        .ToList();
                                }
                            }
                        break;
                    }
                    
                    var open = sequence.First().Open;
                    var prediction = await predictor.PredictMovement(sequence, _data);
                    if (prediction is not null)
                    {
                        var level = open + prediction.PredictedMove;
                        _predictions[modelName] = level;
                        Logger.Info($" {modelName}: {level:f2} (move {prediction.PredictedMove:+0.00;-0.00})");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Prediction failed for {modelName}: {ex.Message}");
                }
            });

            // Mark immediately so subsequent bars don't trigger a second prediction
            // while the first is still in-flight.
            _predictedModels.TryAdd(modelName, true);
        }
    }

    /// <summary>
    /// Returns true when <paramref name="sessionTime"/> has reached the model's window.
    /// <list type="bullet">
    ///   <item>No window (both null) — always open (predict on first bar).</item>
    ///   <item>WindowStart only — open once sessionTime &gt;= WindowStart.</item>
    ///   <item>WindowStart + WindowEnd — open when sessionTime is within the range.</item>
    /// </list>
    /// </summary>
    private static bool isWindowOpen(string sessionTime, string? windowStart, string? windowEnd)
    {
        if (windowStart is null) return true;                          // no window — always trigger
        if (string.Compare(sessionTime, windowStart, StringComparison.Ordinal) < 0) return false;
        if (windowEnd is not null &&
            string.Compare(sessionTime, windowEnd, StringComparison.Ordinal) > 0) return false;
        return true;
    }

    private void resetState()
    {
        IsReady      = false;
        DailyAtr     = 0;
        _sessionDate = null;
        _predictions.Clear();
        _predictedModels.Clear();
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
