using System.Collections.Concurrent;
using AutomatedTrading.Core.Api.Models.Predict;
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
    private Dictionary<string, List<BarData>> _data = new();

    // Serialises StartSessionAsync and Evaluate so bars cannot be processed while
    // session data is still loading.
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

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

        await _sessionLock.WaitAsync();
        try
        {
            resetState();

            _sessionDate = sessionDate;

            if (_barRepo is null)
            {
                IsReady = true;
                return;
            }

            var end = DateTime.ParseExact(_sessionDate, AppConstants.DATE_FORMAT, null);
            var start = end;

            var sessionDates = new List<string>
            {
                start.ToString(AppConstants.DATE_FORMAT)
            };
            for (var i = 0; i <= LOOKBACK; i++)
            {
                start = TradingCalendar.GetPreviousSessionDate(start);
                sessionDates.Add(start.ToString(AppConstants.DATE_FORMAT));
            }
            
            foreach (var date in sessionDates)
            {
                Logger.Info($"Loading session date for {date}...");
                var sessionBars = await _barRepo.GetBarsBySessionDateAsync(date);
                if (sessionBars.Count > 0)
                {
                    _data[date] = sessionBars;
                }
            }
            
            Logger.Info($"Loaded sessions starting @ {start:yyyy-MM-dd} through {end:yyyy-MM-dd}");

            IsReady = true;
        }
        finally
        {
            _sessionLock.Release();
        }
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
        Logger.Info($"{nameof(StrategyEvaluator)}.Evaluate:\r\n\t" +
            $"SessionTime={sessionTime}\r\n\t" +
            $"SessionDate={_sessionDate ?? "null"}\r\n\t" +
            $"BarHigh={barHigh:f2}\r\n\t" +
            $"BarLow={barLow:f2}\r\n\t" +
            $"BarClose={barClose:f2}\r\n\t" +
            $"Position={position}\r\n\t" +
            $"EntryPrice={entryPrice:f2}\r\n\t" + 
            $"IsReady={IsReady,6}");

        await _sessionLock.WaitAsync();
        try
        {
            if (IsReady && _sessionDate is not null)
                await runPendingPredictions(sessionTime);
        }
        finally
        {
            _sessionLock.Release();
        }

        return new BarEvaluation(StrategySignal.None, 0);
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Checks every model that has not yet predicted this session and runs inference
    /// for any whose window has opened by <paramref name="sessionTime"/>.
    /// </summary>
    private async Task runPendingPredictions(string sessionTime)
    {
        // Snapshot mutable state so background tasks aren't affected by the next
        // StartSessionAsync call clearing _data or _sessionDate.
        var dataSnapshot   = _data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
        var sessionDate    = _sessionDate!;

        foreach (var (modelName, (predictor, model)) in _predictors)
        {
            //if (_predictedModels.ContainsKey(modelName)) continue;
            if (_predictions.ContainsKey(modelName)) continue;
            if (!isWindowOpen(sessionTime, model.WindowStart, model.WindowEnd)) continue;

            Logger.Info($"Running prediction for {model.ModelName}");

            // Mark immediately so subsequent bars don't trigger a second prediction
            // while the first is still in-flight.

            // Fire-and-forget is intentional: Evaluate is called synchronously from
            // the bar-close handler. Await in a background task and store when done.
            // _ = Task.Run(async () =>
            // {
                
            // });
            try
                {
                    var sequence = new List<BarData>();
                    switch (model.BarType)
                    {
                        case "window":

                            var windowBarCount = dataSnapshot[sessionDate].Count(b =>
                            {
                                return isWindowOpen(b.SessionTime!, model.WindowStart, model.WindowEnd);
                            });
                            if (windowBarCount < AppConstants.MINIMUM_BARS_TO_EVALUATE)
                            {
                                Logger.Info($"Not enough bars to evaluate {model.ModelName} @ {sessionTime}, skipping...");
                                continue;
                            }
                            
                            foreach (var key in dataSnapshot.Keys)
                            {
                                var bars = dataSnapshot[key] ?? new();
                                bars = bars.Where(b =>
                                    {
                                        return isWindowOpen(b.SessionTime!, model.WindowStart, model.WindowEnd);
                                    })
                                    .OrderBy(b => b.Timestamp)
                                    .ToList();
                                if (bars.Count < AppConstants.MINIMUM_BARS_TO_EVALUATE)
                                {
                                    //Logger.Info($"Not enough bars to evaluate {model.ModelName} on {key} @ {sessionTime}, skipping...");
                                    Logger.Info($"This shouldn't be happening anymore: Not enough bars to evaluate {model.ModelName} on {key} @ {sessionTime}, skipping...");
                                    continue;
                                }
                                sequence.Add(BarData.Aggregate(bars));
                            }
                            sequence = sequence.OrderBy(b => b.Timestamp).ToList();
                            break;
                        case "session":
                            var sessionBarCount = dataSnapshot[sessionDate].Count();
                            if (sessionBarCount < AppConstants.MINIMUM_BARS_TO_EVALUATE)
                            {
                                Logger.Info($"Not enough bars to evaluate {model.ModelName} @ {sessionTime}, skipping...");
                                continue;
                            }
                            
                            foreach (var key in dataSnapshot.Keys)
                            {
                                var bars = dataSnapshot[key] ?? new();
                                if (bars.Count < AppConstants.MINIMUM_BARS_TO_EVALUATE)
                                {
                                    Logger.Info($"Not enough bars to evaluate {model.ModelName}, skipping...");
                                }
                                sequence.Add(BarData.Aggregate(bars));
                            }
                            sequence = sequence.OrderBy(b => b.Timestamp).ToList();
                            break;
                        case "minute":
                            if (dataSnapshot.TryGetValue(sessionDate, out var sessionBars))
                            {
                                sequence = sessionBars;
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

                    if (sequence.Count == 0)
                    {
                        Logger.Warn($"No sequence data for {modelName}, skipping prediction.");
                        return;
                    }

                    var open = sequence.Last().Open;
                    var prediction = await predictor.PredictMovement(sequence, dataSnapshot);
                    if (prediction is not null)
                    {
                        var level = open + prediction.PredictedMove;
                        _predictions[modelName] = level;
                        var summary = string.Join("\r\n\t", _predictions.Select(p => $"{p.Key}: {p.Value:f2}"));
                        Logger.Info($"Predictions \r\n\t{summary}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"Prediction failed for {modelName}: {ex.Message}\r\n{ex.StackTrace}");
                }
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
        if (windowStart is null || windowEnd is null) return true;                      // no window — always trigger
        var start = int.Parse(windowStart);
        var end = int.Parse(windowEnd);
        var time = int.Parse(sessionTime);

        if (start < end)
            return time >= start && time < end;
        else 
            return time >= start || time < end;
    }

    private void resetState()
    {
        IsReady      = false;
        DailyAtr     = 0;
        _sessionDate = null;
        _predictions.Clear();
        _predictedModels.Clear();
        _data        = new Dictionary<string, List<BarData>>();
    }
}
