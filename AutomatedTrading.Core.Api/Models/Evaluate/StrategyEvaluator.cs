using AutomatedTrading.Core.Api.Models.Predict;
using AutomatedTrading.Core.Data.Queries;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Models.Evaluate;

/// <summary>
/// Unified strategy evaluator. Holds a set of ML predictors (one per active model from
/// the ModelConfig table) and lazily runs each predictor the first time a bar falls within that
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
    public bool IsReady { get; private set; }

    /// <summary>All predictions gathered so far this session, keyed by model name.</summary>
    public IReadOnlyDictionary<string, double> Predictions => _predictions;
    public IReadOnlyDictionary<string, double> ATR => _atr;

    // ─── Private state ────────────────────────────────────────────────────────
    private readonly Dictionary<string, (IMovementPredictor Predictor, ModelConfig Model)> _predictors;
    private readonly GetBarsBySessionDate? _getBars;
    private readonly Dictionary<string, double> _predictions = new();
    private readonly Dictionary<string, double> _predictionResults = new();
    private readonly Dictionary<string, double> _atr = new();
    private Dictionary<string, IList<BarData>> _data = new();

    // Serialises StartSessionAsync and Evaluate so bars cannot be processed while
    // session data is still loading.
    private readonly SemaphoreSlim _sessionLock = new(1, 1);

    private string? _sessionDate;
    private double? _proximity;
    private double? _stopLoss;
    private double? _targetProfit;
    private double? _trail;

    // ─── Constructors ─────────────────────────────────────────────────────────

    /// <summary>Production constructor — receives all active model predictors from DI.</summary>
    public StrategyEvaluator(
        Dictionary<string, (IMovementPredictor Predictor, ModelConfig Model)> predictors,
        GetBarsBySessionDate getBars)
    {
        _predictors = predictors;
        _getBars = getBars;
        resetState();
    }

    // ─── Session initialisation ───────────────────────────────────────────────

    /// <summary>
    /// Stores session context and loads the 20-day ATR. Does NOT run any predictors —
    /// predictions are triggered lazily from <see cref="Evaluate"/> as bars arrive.
    /// Call once per session at the relevant market open.
    /// </summary>
    public async Task StartSessionAsync(string sessionDate, double proximity, double stopLoss, double targetProfit, double trail)
    {
        Logger.Info($"{nameof(StrategyEvaluator)}.StartSessionAsync:" +
            $"SessionDate={sessionDate,-12}");

        await _sessionLock.WaitAsync();
        try
        {
            resetState();

            _sessionDate = sessionDate;
            _proximity = proximity;
            _stopLoss = stopLoss;
            _targetProfit = targetProfit;
            _trail = trail;

            if (_getBars is null)
            {
                IsReady = true;
                return;
            }

            var end = DateTime.ParseExact(_sessionDate, AppConstants.DATE_FORMAT, null);
            var start = end;

            var sessionDates = new List<DateTime> { start };
            for (var i = 0; i <= LOOKBACK; i++)
            {
                sessionDates.Add(TradingCalendar.GetPreviousSessionDate(start));
            }

            foreach (var date in sessionDates)
            {
                Logger.Info($"Loading session date for {date}...");

                var sessionBars = await _getBars.Execute(date);
                if (sessionBars.Count > 0)
                {
                    _data[date.ToString(AppConstants.DATE_FORMAT)] = sessionBars;
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
        Logger.Info($"{nameof(StrategyEvaluator)}.Evaluate:" +
            $"SessionDate/Time={_sessionDate ?? "null"} {sessionTime,-8}" +
            $"BarHigh={barHigh,9:f2}, BarLow={barLow,9:f2}, BarClose={barClose,9:f2}, Position={position,5}, EntryPrice={entryPrice,9:f2}");

        await _sessionLock.WaitAsync();
        try
        {
            if (IsReady && _sessionDate is not null)
            {
                await runPendingPredictions(sessionTime);
                return evaluate(barClose, sessionTime);
            }
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
        var dataSnapshot = _data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
        var sessionDate = _sessionDate!;
        var currentBar = dataSnapshot[_sessionDate!]?.FirstOrDefault(x => x.SessionTime == sessionTime);

        foreach (var (modelName, (predictor, model)) in _predictors)
        {
            if (!isWindowOpen(sessionTime, model.WindowStart, model.WindowEnd)) continue;

            if (_predictions.ContainsKey(modelName))
            {
                if (currentBar is not null) updatePredictionResults(currentBar, model);
                continue;
            }

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
                var hasWindow = model.WindowStart is not null && model.WindowEnd is not null;
                if (hasWindow)
                {
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
                            Logger.Info($"This shouldn't be happening anymore: Not enough bars to evaluate {model.ModelName} on {key} @ {sessionTime}, skipping...");
                            continue;
                        }
                        sequence.Add(BarData.Aggregate(bars));
                    }
                }
                else
                {
                    var sessionBarCount = dataSnapshot[sessionDate].Count;
                    if (sessionBarCount < AppConstants.MINIMUM_BARS_TO_EVALUATE)
                    {
                        Logger.Info($"Not enough bars to evaluate {model.ModelName} @ {sessionTime}, skipping...");
                        continue;
                    }

                    foreach (var key in dataSnapshot.Keys)
                    {
                        var bars = dataSnapshot[key] ?? [];
                        if (bars.Count < AppConstants.MINIMUM_BARS_TO_EVALUATE)
                        {
                            Logger.Info($"Not enough bars to evaluate {model.ModelName}, skipping...");
                        }
                        sequence.Add(BarData.Aggregate(bars));
                    }
                }
                sequence = sequence.OrderBy(b => b.Timestamp).ToList();

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
                    _atr.Add(modelName, Computations.ATR(sequence, sequence.Count - 1, LOOKBACK));
                    updatePredictionResults(currentBar!, model);
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

    private void updatePredictionResults(BarData bar, ModelConfig model)
    {
        switch (model.Target)
        {
            case "HIGH":
                if (_predictionResults.TryGetValue(model.ModelName, out double high))
                {
                    if (bar.High > high)
                    {
                        Logger.Info($"{model.ModelName} - new high detected, {bar.High} @ {bar.SessionTime}");
                        _predictionResults[model.ModelName] = bar.High;
                        logPredictionSummary();
                    }
                }
                else
                {
                    Logger.Info($"{model.ModelName} - new prediction added, setting high {bar.High} @ {bar.SessionTime}");
                    _predictionResults.Add(model.ModelName, bar.High);
                    logPredictionSummary();

                }

                break;
            case "LOW":
                if (_predictionResults.TryGetValue(model.ModelName, out double low))
                {
                    if (bar.Low < low)
                    {
                        Logger.Info($"{model.ModelName} - new low detected, {bar.Low} @ {bar.SessionTime}");
                        _predictionResults[model.ModelName] = bar.Low;
                        logPredictionSummary();

                    }
                }
                else
                {
                    Logger.Info($"{model.ModelName} - new prediction added, setting low {bar.Low} @ {bar.SessionTime}");
                    _predictionResults.Add(model.ModelName, bar.Low);
                    logPredictionSummary();
                }
                break;
        }
    }

    private void logPredictionSummary()
    {
        List<string> lines = new List<string>();
        foreach (var p in _predictions.OrderBy(x => x.Key))
        {
            var prediction = p.Value;
            var actual = _predictionResults.TryGetValue(p.Key, out var r) ? r : double.NaN;
            var difference = actual != double.NaN ? prediction - actual : actual;
            lines.Add($"{p.Key,-25}pred: {prediction,8:f2}, actual: {actual,8:f2}, diff: {difference,8:f2}, ATR: {_atr[p.Key],8:f2}, diff/ATR: {Operations.SafeDiv(difference, _atr[p.Key]),8:f4}");
        }
        var summary = string.Join("\r\n\t", lines);
        Logger.Info($"Predictions \r\n\t{summary}");
    }

    private void resetState()
    {
        IsReady = false;
        _sessionDate = null;
        _predictions.Clear();
        _predictionResults.Clear();
        _atr.Clear();
        _data = [];
    }

    private BarEvaluation evaluate(double barClose, string sessionTime)
    {
        var position = PositionSide.Flat;
        var stop = 0.0;

        double? upperTarget = null, lowerTarget = null;
        foreach (var (modelName, (predictor, model)) in _predictors)
        {
            if (!isWindowOpen(sessionTime, model.WindowStart, model.WindowEnd)) continue;
            if (!_predictions.ContainsKey(modelName) || !_atr.ContainsKey(modelName)) continue;

            var atr = _atr[modelName];
            var prox = atr * _proximity ?? 1;

            switch (model.Target)
            {
                case "HIGH":
                    upperTarget = _predictions[modelName] - prox;
                    Logger.Info($"Upper target updated: {upperTarget:f2}");
                    break;
                case "LOW":
                    lowerTarget = _predictions[modelName] + prox;
                    Logger.Info($"Lower target updated: {lowerTarget:f2}");
                    break;
            }

            if (upperTarget.HasValue && lowerTarget.HasValue &&
                upperTarget.Value < lowerTarget.Value + prox)
            {
                Logger.Info($"Model prediction overlap - high: {upperTarget.Value:f2}, low: {lowerTarget.Value:f2}");
                while (upperTarget.Value < lowerTarget.Value + prox)
                {
                    upperTarget += prox / 2;
                    lowerTarget -= prox / 2;
                }
                Logger.Info($"Targets updated: upper={upperTarget:f2}, lower={lowerTarget:f2}");
            }
        }

        switch (position)
        {
            case PositionSide.Flat:
                if (barClose > upperTarget)
                {
                    stop = barClose + _stopLoss ?? 25;
                    Logger.Info($"Entering SHORT: Position=Flat, BarClose={barClose},StopLoss={stop}");
                    return new BarEvaluation(StrategySignal.EnterShort, stop);
                }
                if (barClose < lowerTarget)
                {
                    stop = barClose - _stopLoss ?? 25;
                    Logger.Info($"Entering LONG: Position=Flat, BarClose={barClose},StopLoss={stop}");
                    return new BarEvaluation(StrategySignal.EnterLong, stop);
                }
                return new BarEvaluation(StrategySignal.None, stop);
            case PositionSide.Long:
                if (barClose > upperTarget)
                {
                    stop = barClose - _trail ?? 5.0;
                    Logger.Info($"Currently LONG, Nearing predicted high @ {upperTarget:F2}, tightening stop distance = {stop}");
                    return new BarEvaluation(StrategySignal.UpdateStop, stop);
                }
                return new BarEvaluation(StrategySignal.None, stop);
            case PositionSide.Short:
                if (barClose < lowerTarget)
                {
                    stop = barClose + _trail ?? 5.0;
                    Logger.Info($"Currently SHORT, Nearing predicted low @ {lowerTarget:F2}, tightening stop distance = {stop}");
                    return new BarEvaluation(StrategySignal.UpdateStop, stop);
                }
                return new BarEvaluation(StrategySignal.EnterShort, stop);
            default:
                return new BarEvaluation(StrategySignal.None, 0);
        }
    }
}
