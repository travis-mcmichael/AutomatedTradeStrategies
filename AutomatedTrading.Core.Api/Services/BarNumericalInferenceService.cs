using AutomatedTrading.Core.Api.Models.API.Predict;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Config;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Services;

public class BarNumericalInferenceService : IMovementPredictor
{
    private const string DATE_FORMAT = "yyyyMMdd";
    private const int LOOKBACK = 40;
    private const int ATR_SPAN = 20;

    private readonly IBarRepository _barRepo;
    private readonly List<ModelFeature> _features;
    private readonly bool _isTradeDay;
    private readonly bool _peak;
    private BaseMLEvaluator? _evaluator;

    public BarNumericalInferenceService(IBarRepository barRepo, ModelEntry model)
    {
        _barRepo = barRepo;
        _features = model.Features;
        _isTradeDay = model.Features.Any(f => f.Source == "currentMinute");
        _peak = model.ModelName.EndsWith(".high");
        loadEvaluator(model);
    }

    public async Task<MovementPrediction?> PredictMovement(string sessionDate, double sessionOpen)
    {
        if (!isLoaded()) return null;

        var (sequence, prevBars, currentBars) = await loadData(sessionDate);
        if (sequence is null || prevBars is null) return null;
        if (_isTradeDay && currentBars is null) return null;

        var j = sequence.Count - 1;
        var atr = Computations.ATR(sequence, j, ATR_SPAN);
        if (atr == 0) return null;

        var ctx = new FeatureContext(
            sequence,
            j,
            sequence[j],
            sessionOpen,
            atr,
            sessionDate,
            prevBars,
            currentBars);

        var features = FeatureComputations.ParseFeatures(_features, ctx);
        var scaled = _evaluator!.Scale(features);
        var rawMove = _evaluator.RunInference(scaled) * atr;

        // High models predict upside from open (positive); low models negate so that
        // open + PredictedMove gives the predicted low price.
        return new MovementPrediction(_peak ? rawMove : -rawMove);
    }

    private bool isLoaded() => _evaluator != null && _evaluator.SessionLoaded();

    private void loadEvaluator(ModelEntry model)
    {
        if (!model.Active) return;
        var dir = Path.Combine(Operations.NormalizePath(model.OutputLocation), model.ModelName, model.Version);
        var eval = new BaseMLEvaluator(
            Path.Combine(dir, AppConstants.XGB_MODEL_FILENAME),
            Path.Combine(dir, AppConstants.SCALER_FILENAME));
        eval.LoadModel();
        if (!eval.SessionLoaded())
        {
            Logger.Info($"Warning: model '{model.ModelName}' failed to load from {dir}");
            return;
        }
        _evaluator = eval;
    }

    // Loads the daily bar sequence (ending at the previous session) plus the previous
    // session's minute bars. For trade-day models, also loads the current session's
    // minute bars so pre-market features can be computed.
    private async Task<(List<BarData>? sequence, List<BarData>? prevBars, List<BarData>? currentBars)> loadData(string sessionDate)
    {
        var date = DateTime.ParseExact(sessionDate, DATE_FORMAT, null);
        var prevSessionDate = TradingCalendar.GetPreviousSessionDate(date).ToString(DATE_FORMAT);

        var prevBars = await _barRepo.GetBarsBySessionDateAsync(prevSessionDate);
        if (!prevBars.Any()) return (null, null, null);

        var sequence = await loadDailyBars(prevSessionDate, LOOKBACK);
        if (sequence.Count <= ATR_SPAN) return (null, null, null);

        List<BarData>? currentBars = null;
        if (_isTradeDay)
        {
            currentBars = await _barRepo.GetBarsBySessionDateAsync(sessionDate);
            if (!currentBars.Any()) return (null, null, null);
        }

        return (sequence, prevBars, currentBars);
    }

    private async Task<List<BarData>> loadDailyBars(string sessionDate, int lookback)
    {
        var list = new List<BarData>();
        var date = DateTime.ParseExact(sessionDate, DATE_FORMAT, null);
        for (var i = lookback; i >= 0; i--)
        {
            var d = date.AddDays(-i).ToString(DATE_FORMAT);
            var bars = await _barRepo.GetBarsBySessionDateAsync(d);
            if (bars.Count == 0) continue;
            list.Add(BarData.Aggregate(bars));
        }
        return list;
    }
}
