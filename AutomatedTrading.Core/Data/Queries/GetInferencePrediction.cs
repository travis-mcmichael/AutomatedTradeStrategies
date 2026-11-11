using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Utility;

namespace AutomatedTrading.Core.Data.Queries;

// Runs a model's inference for a session date the way the live API does when the model's trigger fires:
//   - the trigger is the first bar at which the model's window is open and enough bars exist (in the window for
//     windowed models, in the session otherwise). Bars after the trigger bar are not used.
//   - the sequence has one aggregated bar per session over the ATR lookback, as StrategyEvaluator builds it.
//   - features and the ONNX model are the ones InferenceService uses, loaded from the model's current Version.
// Runs only when GetInferenceReadiness says the model is ready for the date. Bars are loaded by SessionDate, as the
// live path does, with the same changeover adjustment as training.
public class GetInferencePrediction(
    GetInferenceReadiness readiness,
    GetAllModelConfigs allModelConfigs,
    GetVersionChangeovers versionChangeovers,
    GetBarsInSessionRange barsInRange)
{
    private const int ATR_SPAN = 20;            // InferenceService.ATR_SPAN
    private const string TIME_FORMAT = "HH:mm:ss";

    public async Task<InferencePrediction> Execute(long modelConfigId, DateTime sessionDate)
    {
        var label = sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var check = await readiness.Execute(modelConfigId, sessionDate);
        if (!check.IsReady)
            return InferencePrediction.Failed(label, "The model is not ready for this session. Check readiness for the session first.");

        var model = (await allModelConfigs.Execute()).FirstOrDefault(m => m.Id == modelConfigId);
        if (model is null)
            return InferencePrediction.Failed(label, $"ModelConfig {modelConfigId} does not exist.");

        var dir = Path.Combine(Operations.NormalizePath(model.OutputLocation), model.ModelName, model.Version);
        using var evaluator = new BaseMLEvaluator(
            Path.Combine(dir, AppConstants.XGB_MODEL_FILENAME),
            Path.Combine(dir, AppConstants.SCALER_FILENAME));
        evaluator.LoadModel();
        if (!evaluator.SessionLoaded())
            return InferencePrediction.Failed(label, $"The model did not load from {dir}.");

        try
        {
            var earliest = earliestNeeded(model, sessionDate);
            var sessionKey = Key(sessionDate);
            var bySession = (await barsInRange.Execute(model.Instrument, Key(earliest), sessionKey))
                .GroupBy(b => b.SessionDate!)
                .ToDictionary(g => g.Key, g => g.ToList());

            var changeovers = await versionChangeovers.Execute(model.Instrument, earliest, sessionDate);
            if (changeovers.Count > 0)
                FeatureExporter.HandleChangeoverAdjustment(changeovers, [.. bySession.Keys.OrderByDescending(k => k, StringComparer.Ordinal)], bySession);

            if (!bySession.TryGetValue(sessionKey, out var sessionBars) || sessionBars.Count == 0)
                return InferencePrediction.Failed(label, $"There are no bars for {label}.");
            sessionBars = [.. sessionBars.OrderBy(b => b.Timestamp)];

            var hasWindow = model.WindowStart is not null && model.WindowEnd is not null;
            var windowStart = hasWindow ? int.Parse(model.WindowStart!) : 0;
            var windowEnd = hasWindow ? int.Parse(model.WindowEnd!) : 0;

            var (trigger, barsAtTrigger) = findTrigger(sessionBars, hasWindow, windowStart, windowEnd);
            if (trigger is null)
                return InferencePrediction.Failed(label,
                    $"The model's trigger was not reached on {label}: {barsAtTrigger} bars, which is not enough" +
                    (hasWindow ? " inside its window." : "."));

            // Everything the live path would have had at the trigger: prior sessions in full, the session up to the trigger bar.
            var cut = timeOf(trigger);
            var snapshot = new Dictionary<string, List<BarData>>();
            foreach (var pair in bySession)
            {
                snapshot[pair.Key] = pair.Key == sessionKey
                    ? [.. sessionBars.Where(b => timeOf(b) <= cut)]
                    : pair.Value;
            }

            var sequence = new List<BarData>();
            foreach (var pair in snapshot)
            {
                if (hasWindow)
                {
                    var inWindow = pair.Value.Where(b => InWindow(timeOf(b), windowStart, windowEnd)).OrderBy(b => b.Timestamp).ToList();
                    if (inWindow.Count < AppConstants.MINIMUM_BARS_TO_EVALUATE) continue;
                    sequence.Add(BarData.Aggregate(inWindow));
                }
                else
                {
                    if (pair.Value.Count == 0) continue;
                    sequence.Add(BarData.Aggregate([.. pair.Value.OrderBy(b => b.Timestamp)]));
                }
            }
            sequence = [.. sequence.OrderBy(b => b.Timestamp)];
            if (sequence.Count == 0)
                return InferencePrediction.Failed(label, "No sequence of sessions could be built for the trigger.");

            var atr = Computations.ATR(sequence, ATR_SPAN);
            if (double.IsNaN(atr) || atr == 0)
                return InferencePrediction.Failed(label,
                    $"ATR({ATR_SPAN}) needs {ATR_SPAN + 1} sessions of bars; {sequence.Count} were available.");

            var open = sequence.Last().Open;
            var features = FeatureComputations.ParseFeatures(model.ModelFeatures,
                new FeatureContext(atr, sessionKey, snapshot, model.WindowStart ?? "", model.WindowEnd ?? ""));
            var raw = evaluator.RunInference(evaluator.Scale(features)) * atr;
            if (raw == 0)
                return InferencePrediction.Failed(label, "The model returned no output. The evaluator logs a feature count mismatch if that is the cause.");

            // Same convention as InferenceService: LOW models negate the move so that open + Move is the predicted low.
            var move = model.Target == "HIGH" ? raw : -raw;
            return new InferencePrediction
            {
                Succeeded = true,
                SessionDate = label,
                AsOfTime = FormatTime(cut),
                Target = model.Target,
                Open = open,
                Atr = atr,
                Move = move,
                Level = open + move
            };
        }
        catch (Exception ex)
        {
            return InferencePrediction.Failed(label, ex.Message);
        }
    }

    // Earliest session any feature's context or the ATR lookback reads, mirroring GetInferenceReadiness.
    private static DateTime earliestNeeded(ModelConfig model, DateTime sessionDate)
    {
        var earliest = sessionDate.Date;
        for (var i = 0; i < ATR_SPAN; i++)
            earliest = TradingCalendar.GetPreviousSessionDate(earliest);

        foreach (var feature in model.ModelFeatures)
        {
            var context = FeatureComputations.GetContextDate(sessionDate, FeatureComputations.GetParams(feature));
            if (context < earliest) earliest = context;
        }
        return earliest;
    }

    // The first bar (in time order) at which the live trigger fires. Also returns how many bars the count reached.
    private static (BarData? Trigger, int Count) findTrigger(List<BarData> bars, bool hasWindow, int windowStart, int windowEnd)
    {
        var total = 0;
        var inWindow = 0;
        foreach (var bar in bars)
        {
            total++;
            var open = !hasWindow || InWindow(timeOf(bar), windowStart, windowEnd);
            if (hasWindow && open) inWindow++;
            var ready = hasWindow ? open && inWindow >= AppConstants.MINIMUM_BARS_TO_EVALUATE
                                  : total >= AppConstants.MINIMUM_BARS_TO_EVALUATE;
            if (ready) return (bar, total);
        }
        return (null, total);
    }

    // Same rule as StrategyEvaluator.isWindowOpen: a window with start > end wraps past midnight.
    private static bool InWindow(int time, int start, int end) =>
        start < end ? time >= start && time < end : time >= start || time < end;

    private static int timeOf(BarData bar) => int.TryParse(bar.SessionTime, NumberStyles.None, CultureInfo.InvariantCulture, out var t) ? t : -1;

    private static string FormatTime(int hhmmss)
    {
        var s = hhmmss.ToString("000000", CultureInfo.InvariantCulture);
        return $"{s.Substring(0, 2)}:{s.Substring(2, 2)}:{s.Substring(4, 2)}";
    }

    private static string Key(DateTime date) => date.ToString(AppConstants.DATE_FORMAT, CultureInfo.InvariantCulture);
}
