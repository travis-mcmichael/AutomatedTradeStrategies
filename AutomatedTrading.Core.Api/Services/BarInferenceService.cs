using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Predictors;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Services;

public class BarInferenceService : IBarHighPredictor, IBarLowPredictor
{
    private const int HighFeatureCount = 38;
    private const int LowFeatureCount = 39;

    private readonly IBarRepository barRepo;
    private readonly BaseMLEvaluator highEvaluator;
    private readonly BaseMLEvaluator lowEvaluator;

    public bool HighLoaded => highEvaluator.SessionLoaded();
    public bool LowLoaded => lowEvaluator.SessionLoaded();

    public BarInferenceService(IBarRepository barRepo, IConfiguration config)
    {
        this.barRepo = barRepo;

        var modelDate = config["Models:Date"] ?? "20260323";

        highEvaluator = createEvaluator(AppConstants.MODEL_DIR, modelDate, "mes.day-bar.high", HighFeatureCount);
        lowEvaluator = createEvaluator(AppConstants.MODEL_DIR, modelDate, "mes.day-bar.low",  LowFeatureCount);
    }

    public async Task<BarHighResponse?> RunHighInference(string sessionDate, double sessionOpen)
    {
        if (!highEvaluator.SessionLoaded()) return null;

        var (sequence, prevMinuteBars) = await loadSessionData(sessionDate);
        if (sequence is null || prevMinuteBars is null) return null;

        var j = sequence.Count - 1;
        var current = sequence[j];
        var atr20 = Computations.ATR(sequence, j, 20);

        var shared   = buildSharedFeatures(sequence, j, current, sessionOpen, atr20, sessionDate);
        double[] tail =
        [
            Computations.OvernightRange(prevMinuteBars, atr20),
            Computations.OvernightVolumeRatio(prevMinuteBars),
            Computations.RegularHoursRange(prevMinuteBars, atr20),
            Computations.FirstHourRange(prevMinuteBars, atr20),
            Computations.LastHourRange(prevMinuteBars, atr20),
            Computations.HighTimeRatio(prevMinuteBars),
            Computations.PrevOpenToHighRatio(prevMinuteBars, atr20),
            Computations.PrevOpenToLowRatio(prevMinuteBars, atr20)
        ];

        var scaled = highEvaluator.Scale([.. shared, .. tail]);
        var predictedMove = highEvaluator.RunInference(scaled) * atr20;
        var predictedHigh = sessionOpen + predictedMove;

        return new BarHighResponse(predictedHigh, predictedMove);
    }

    public async Task<BarLowResponse?> RunLowInference(string sessionDate, double sessionOpen)
    {
        if (!lowEvaluator.SessionLoaded()) return null;

        var (sequence, prevMinuteBars) = await loadSessionData(sessionDate);
        if (sequence is null || prevMinuteBars is null) return null;

        var j = sequence.Count - 1;
        var current = sequence[j];
        var atr20 = Computations.ATR(sequence, j, 20);

        var shared = buildSharedFeatures(sequence, j, current, sessionOpen, atr20, sessionDate);
        double[] tail =
        [
            Computations.OvernightRange(prevMinuteBars, atr20),
            Computations.OvernightVolumeRatio(prevMinuteBars),
            Computations.OvernightDropRatio(prevMinuteBars, atr20),
            Computations.RegularHoursRange(prevMinuteBars, atr20),
            Computations.FirstHourRange(prevMinuteBars, atr20),
            Computations.LastHourRange(prevMinuteBars, atr20),
            Computations.LowTimeRatio(prevMinuteBars),
            Computations.PrevOpenToLowRatio(prevMinuteBars, atr20),
            Computations.LastHourClosePosition(prevMinuteBars),
        ];

        var scaled = lowEvaluator.Scale([.. shared, .. tail]);
        var predictedMove = lowEvaluator.RunInference(scaled) * atr20;
        var predictedLow = sessionOpen - predictedMove;

        return new BarLowResponse(predictedLow, predictedMove);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IBarHighPredictor
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<BarHighPrediction?> PredictHighAsync(string sessionDate, double sessionOpen)
    {
        var result = await RunHighInference(sessionDate, sessionOpen);
        return result is null ? null : new BarHighPrediction(result.PredictedHigh, result.PredictedMove);
    }

    public async Task<BarLowPrediction?> PredictLowAsync(string sessionDate, double sessionOpen)
    {
        var result = await RunLowInference(sessionDate, sessionOpen);
        return result is null ? null : new BarLowPrediction(result.PredictedLow, result.PredictedMove);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Feature construction
    // ──────────────────────────────────────────────────────────────────────────

    private double[] buildSharedFeatures(List<BarData> sequence, int j, BarData current, double sessionOpen, double atr20, string sessionDate)
    {
        var atr5 = Computations.ATR(sequence, j,  5);
        var atr10 = Computations.ATR(sequence, j, 10);
        var sma5 = Computations.SMA(sequence, j,  5, x => x.Close);
        var sma10 = Computations.SMA(sequence, j, 10, x => x.Close);
        var sma20 = Computations.SMA(sequence, j, 20, x => x.Close);
        var avgRange5 = Computations.AverageRange(sequence, j,  5);
        var avgRange10 = Computations.AverageRange(sequence, j, 10);
        var rsi14 = Computations.RSI(sequence, j, 14);
        var today = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        return
        [
            Operations.SafeDiv(current.High, sessionOpen),
            Operations.SafeDiv(current.Low, sessionOpen),
            Computations.HighLowRange(current, atr20),
            Computations.BodySize(current, atr20),
            Computations.UpperWick(current, atr20),
            Computations.LowerWick(current, atr20),
            Computations.ClosePosition(current),
            Operations.SafeDiv(atr5, sessionOpen),
            Operations.SafeDiv(atr10, sessionOpen),
            Operations.SafeDiv(atr20, sessionOpen),
            avgRange5,
            avgRange10,
            Operations.SafeDiv(current.High - current.Low, avgRange10),
            Computations.Return(sequence, j, 1),
            Computations.Return(sequence, j, 3),
            Computations.Return(sequence, j, 5),
            Operations.SafeDiv(sma5, sma20),
            Operations.SafeDiv(sma10, sma20),
            Operations.SafeDiv(sessionOpen, sma5),
            Operations.SafeDiv(sessionOpen, sma10),
            Operations.SafeDiv(sessionOpen, sma20),
            Computations.SMA(sequence, j, 20, x => x.Volume),
            Computations.VolumeZScore(sequence, j, 20),
            Operations.SafeDiv(sessionOpen - current.High,  atr20),
            Operations.SafeDiv(sessionOpen - current.Low,   atr20),
            Operations.SafeDiv(sessionOpen - current.Close, atr20),
            Operations.SafeDiv(atr10, atr20),
            rsi14,
            (int)today.DayOfWeek,
            today.Day,
        ];
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Data loading
    // ──────────────────────────────────────────────────────────────────────────
    private async Task<(List<BarData>? sequence, List<BarData>? prevMinuteBars)> loadSessionData(string sessionDate)
    {
        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);
        var prevSessionDate = TradingCalendar.GetPreviousSessionDate(date).ToString("yyyyMMdd");
        var prevMinuteBars = await barRepo.GetBarsBySessionDateAsync(prevSessionDate);
        if (!prevMinuteBars.Any()) return (null, null);

        var sequence = await loadRecentDailyBars(prevSessionDate, 40);
        if (sequence.Count < 21) return (null, null);

        return (sequence, prevMinuteBars);
    }

    private async Task<List<BarData>> loadRecentDailyBars(string sessionDate, int lookbackDays)
    {
        var list = new List<BarData>();
        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        for (int i = lookbackDays; i >= 0; i--)
        {
            var d    = date.AddDays(-i).ToString("yyyyMMdd");
            var bars = await barRepo.GetBarsBySessionDateAsync(d);
            if (bars.Count == 0) continue;
            list.Add(BarData.Aggregate(bars));
        }

        return list;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static BaseMLEvaluator createEvaluator(string baseDir, string modelDate, string name, int featureCount)
    {
        var dir   = Path.Combine(baseDir, modelDate, name);
        var eval  = new BaseMLEvaluator(
            Path.Combine(dir, "xgb_model.onnx"),
            Path.Combine(dir, "scaler_params.json"),
            featureCount);
        eval.LoadModel();
        return eval;
    }
}
