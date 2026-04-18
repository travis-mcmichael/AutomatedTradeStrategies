using AutomatedTrading.Core.Api.Models.Predict;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Config;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Services;

public class InferenceService : IMovementPredictor
{
    private const int ATR_SPAN = 20;
    private readonly List<ModelFeature> _features;
    private readonly bool _peak;
    private BaseMLEvaluator? _evaluator;
    private string _modelName;

    public InferenceService(ModelEntry model)
    {
        _features = model.Features;
        _peak = model.Target == "HIGH";// Framework.Enums.PredictionTarget.HIGH;
        _modelName = model.ModelName;
        loadEvaluator(model);
    }

    public async Task<MovementPrediction?> PredictMovement(List<BarData> sequence, Dictionary<string, List<BarData>> barDataBySession)
    {
        if (!isLoaded()) return null;

        var j = sequence.Count - 1;
        var atr = Computations.ATR(sequence, j, ATR_SPAN);
        if (atr == 0) return null;
        var current = sequence[j];
        var open = sequence.First().Open;
        
        Logger.Info($"Predict movement for {_modelName} on {current.SessionDate} with window open @ {open:F2}, ATR{ATR_SPAN} = {atr}.");

        var ctx = new FeatureContext(
            sequence,
            j,
            atr,
            current.SessionDate!,
            barDataBySession
            );

        var features = FeatureComputations.ParseFeatures(_features, ctx);
        var scaled = _evaluator!.Scale(features);
        var rawMove = _evaluator.RunInference(scaled) * atr;

        // High models predict upside from open (positive); low models negate so that
        // open + PredictedMove gives the predicted low price.
        var mv = _peak ? rawMove : -rawMove;
        Logger.Info($"Predict movement result: {mv}");
        return new MovementPrediction(mv);
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
}
