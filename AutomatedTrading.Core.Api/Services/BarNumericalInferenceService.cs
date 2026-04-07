using AutomatedTrading.Core.Api.Models.API.Predict;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Config;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Services;

public abstract class BarNumericalInferenceService : IMovementPredictor
{
    private const string DATE_FORMAT = "yyyyMMdd";
    private const int LOOKBACK = 40;
    private const int ATR_SPAN = 20;
    private readonly IBarRepository barRepo;
    private BaseMLEvaluator? evaluator;
    public BarNumericalInferenceService(IBarRepository barRepo, ModelEntry model)
    {
        this.barRepo = barRepo;
        loadEvaluator(model);
    }

    public async Task<MovementPrediction?> PredictMovement(string sessionDate, double[] features)
    {
        if (!isLoaded()) return null;

        var (sequence, prevBars) = await loadSessionData(sessionDate);
        if (sequence is null || prevBars is null) return null;

        var atr = Computations.ATR(sequence, sequence.Count - 1, ATR_SPAN);
        var scaled = evaluator?.Scale(features) ?? [];
        var predictedMove = evaluator?.RunInference(scaled) ?? 0 * atr;
        
        return new MovementPrediction(predictedMove);
    }

    private bool isLoaded() => evaluator != null && evaluator.SessionLoaded();

    private void loadEvaluator(ModelEntry model)
    {
        if (!model.Active)
        return;
        var dir = Path.Combine(Operations.NormalizePath(model.OutputLocation), model.Version, model.ModelName);
        var eval = new BaseMLEvaluator(
            Path.Combine(dir, AppConstants.XGB_MODEL_FILENAME),
            Path.Combine(dir, AppConstants.SCALER_FILENAME),
            model.FeatureCount);
        eval.LoadModel();
        if (!eval.SessionLoaded())
        {
            Logger.Info($"Warning: model '{model.ModelName}' failed to load from {dir}");
            return;
        }
        evaluator = eval;
    }

    private async Task<(List<BarData>? sequence, List<BarData>? prevBars)> loadSessionData(string sessionDate)
    {
        var date = DateTime.ParseExact(sessionDate, DATE_FORMAT, null);
        var prevSessionDate = TradingCalendar.GetPreviousSessionDate(date).ToString(DATE_FORMAT);
        var prevBars = await barRepo.GetBarsBySessionDateAsync(prevSessionDate);
        if (!prevBars.Any()) return (null, null);
        var sequence = await loadDailyBars(prevSessionDate, LOOKBACK);
        if (sequence.Count <= 20) return (null, null);
        return (sequence, prevBars);
    }

    private async Task<List<BarData>> loadDailyBars(string sessionDate, int lookback)
    {
        var list = new List<BarData>();
        var date = DateTime.ParseExact(sessionDate, DATE_FORMAT, null);
        for (var i = lookback; i >= 0; i--)
        {
            var d = date.AddDays(-i).ToString(DATE_FORMAT);
            var bars = await barRepo.GetBarsBySessionDateAsync(d);
            if (bars.Count == 0) continue;
            list.Add(BarData.Aggregate(bars));
        }
        return list;
    }
}