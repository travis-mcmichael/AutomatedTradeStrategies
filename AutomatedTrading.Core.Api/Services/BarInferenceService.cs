using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Predictors;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Services;

public class BarInferenceService : IBarHighPredictor, IBarLowPredictor, ITradeDayHighPredictor, ITradeDayLowPredictor
{
    private const int SessionHighFeatureCount = 38;
    private const int SessionLowFeatureCount = 39;
    private const int TradeDayHighFeatureCount = 42;
    private const int TradeDayLowFeatureCount = 43;

    private readonly IBarRepository barRepo;
    private readonly BaseMLEvaluator sessionHighEvaluator;
    private readonly BaseMLEvaluator sessionLowEvaluator;
    private readonly BaseMLEvaluator tradeDayHighEvaluator;
    private readonly BaseMLEvaluator tradeDayLowEvaluator;

    public bool SessionHighLoaded => sessionHighEvaluator.SessionLoaded();
    public bool SessionLowLoaded => sessionLowEvaluator.SessionLoaded();
    public bool TradeDayHighLoaded => tradeDayHighEvaluator.SessionLoaded();
    public bool TradeDayLowLoaded => tradeDayLowEvaluator.SessionLoaded();

    public BarInferenceService(IBarRepository barRepo, IConfiguration config)
    {
        this.barRepo = barRepo;
        var modelDate = config["Models:Date"] ?? "20260323";
        sessionHighEvaluator = createEvaluator(AppConstants.MODEL_DIR, modelDate, "mes.session-day-bar.high", SessionHighFeatureCount);
        sessionLowEvaluator = createEvaluator(AppConstants.MODEL_DIR, modelDate, "mes.session-day-bar.low", SessionLowFeatureCount);
        tradeDayHighEvaluator = createEvaluator(AppConstants.MODEL_DIR, modelDate, "mes.trade-day.high", TradeDayHighFeatureCount);
        tradeDayLowEvaluator  = createEvaluator(AppConstants.MODEL_DIR, modelDate, "mes.trade-day.low", TradeDayLowFeatureCount);
    }

    public async Task<BarHighResponse?> RunHighInference(string sessionDate, double sessionOpen)
    {
        if (!sessionHighEvaluator.SessionLoaded()) return null;
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
            Computations.RegularHoursRange(prevMinuteBars, atr20),
            Computations.FirstHourRange(prevMinuteBars, atr20),
            Computations.LastHourRange(prevMinuteBars, atr20),
            Computations.HighTimeRatio(prevMinuteBars),
            Computations.PrevOpenToHighRatio(prevMinuteBars, atr20),
            Computations.PrevOpenToLowRatio(prevMinuteBars, atr20)
        ];
        var scaled = sessionHighEvaluator.Scale([.. shared, .. tail]);
        var predictedMove = sessionHighEvaluator.RunInference(scaled) * atr20;
        var predictedHigh = sessionOpen + predictedMove;
        return new BarHighResponse(predictedHigh, predictedMove);
    }

    public async Task<BarLowResponse?> RunLowInference(string sessionDate, double sessionOpen)
    {
        if (!sessionLowEvaluator.SessionLoaded()) return null;
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
        var scaled = sessionLowEvaluator.Scale([.. shared, .. tail]);
        var predictedMove = sessionLowEvaluator.RunInference(scaled) * atr20;
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
    // Trade-day inference (RTH open → RTH high/low)
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<TradeDayHighResponse?> RunTradeDayHighInference(string sessionDate, double rthOpen)
    {
        if (!tradeDayHighEvaluator.SessionLoaded()) return null;

        var (sequence, prevMinuteBars, currentMinuteBars) = await loadTradeDayData(sessionDate);
        if (sequence is null || prevMinuteBars is null || currentMinuteBars is null) return null;

        var j = sequence.Count - 1;
        var current = sequence[j];
        var atr20 = Computations.ATR(sequence, j, 20);

        var shared = buildSharedFeatures(sequence, j, current, rthOpen, atr20, sessionDate);
        double[] preMarket =
        [
            Computations.PreMarketRange(currentMinuteBars, atr20),
            Computations.PreMarketHighVsOpen(currentMinuteBars, rthOpen, atr20),
            Computations.PreMarketLowVsOpen(currentMinuteBars, rthOpen, atr20),
            Computations.GapToRthOpen(rthOpen, current.Close, atr20),
            Computations.PreMarketVolumeRatio(currentMinuteBars),
        ];
        double[] rthTail =
        [
            Computations.RthRange(prevMinuteBars, atr20),
            Computations.FirstHalfHourRange(prevMinuteBars, atr20),
            Computations.FirstHourRange(prevMinuteBars, atr20),
            Computations.LastHourRange(prevMinuteBars, atr20),
            Computations.RthHighTimeRatio(prevMinuteBars),
            Computations.RthOpenToHighRatio(prevMinuteBars, atr20),
            Computations.RthOpenToLowRatio(prevMinuteBars, atr20),
        ];

        var scaled = tradeDayHighEvaluator.Scale([.. shared, .. preMarket, .. rthTail]);
        var predictedMove = tradeDayHighEvaluator.RunInference(scaled) * atr20;
        var predictedHigh = rthOpen + predictedMove;

        return new TradeDayHighResponse(predictedHigh, predictedMove);
    }

    public async Task<TradeDayLowResponse?> RunTradeDayLowInference(string sessionDate, double rthOpen)
    {
        if (!tradeDayLowEvaluator.SessionLoaded()) return null;

        var (sequence, prevMinuteBars, currentMinuteBars) = await loadTradeDayData(sessionDate);
        if (sequence is null || prevMinuteBars is null || currentMinuteBars is null) return null;

        var j = sequence.Count - 1;
        var current = sequence[j];
        var atr20 = Computations.ATR(sequence, j, 20);

        var shared = buildSharedFeatures(sequence, j, current, rthOpen, atr20, sessionDate);
        double[] preMarket =
        [
            Computations.PreMarketRange(currentMinuteBars, atr20),
            Computations.PreMarketHighVsOpen(currentMinuteBars, rthOpen, atr20),
            Computations.PreMarketLowVsOpen(currentMinuteBars, rthOpen, atr20),
            Computations.GapToRthOpen(rthOpen, current.Close, atr20),
            Computations.PreMarketVolumeRatio(currentMinuteBars),
        ];
        double[] rthTail =
        [
            Computations.RthRange(prevMinuteBars, atr20),
            Computations.FirstHalfHourRange(prevMinuteBars, atr20),
            Computations.FirstHourRange(prevMinuteBars, atr20),
            Computations.LastHourRange(prevMinuteBars, atr20),
            Computations.RthLowTimeRatio(prevMinuteBars),
            Computations.RthOpenToHighRatio(prevMinuteBars, atr20),
            Computations.RthOpenToLowRatio(prevMinuteBars, atr20),
            Computations.LastHourClosePosition(prevMinuteBars),
        ];

        var scaled = tradeDayLowEvaluator.Scale([.. shared, .. preMarket, .. rthTail]);
        var predictedMove = tradeDayLowEvaluator.RunInference(scaled) * atr20;
        var predictedLow  = rthOpen - predictedMove;

        return new TradeDayLowResponse(predictedLow, predictedMove);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ITradeDayHighPredictor / ITradeDayLowPredictor
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<TradeDayHighPrediction?> PredictTradeDayHighAsync(string sessionDate, double rthOpen)
    {
        var result = await RunTradeDayHighInference(sessionDate, rthOpen);
        return result is null ? null : new TradeDayHighPrediction(result.PredictedHigh, result.PredictedMove);
    }

    public async Task<TradeDayLowPrediction?> PredictTradeDayLowAsync(string sessionDate, double rthOpen)
    {
        var result = await RunTradeDayLowInference(sessionDate, rthOpen);
        return result is null ? null : new TradeDayLowPrediction(result.PredictedLow, result.PredictedMove);
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

    /// <summary>
    /// Loads data required for trade-day (RTH) inference:
    ///   sequence        — daily bars up through the previous session (for ATR/SMA/etc.)
    ///   prevMinuteBars  — previous session's 1-min bars (for prev-RTH intraday features)
    ///   currentMinuteBars — current session's 1-min bars (for pre-market features)
    /// </summary>
    private async Task<(List<BarData>? sequence, List<BarData>? prevMinuteBars, List<BarData>? currentMinuteBars)> loadTradeDayData(string sessionDate)
    {
        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);
        var prevSessionDate = TradingCalendar.GetPreviousSessionDate(date).ToString("yyyyMMdd");

        var prevMinuteBars = await barRepo.GetBarsBySessionDateAsync(prevSessionDate);
        var currentMinuteBars = await barRepo.GetBarsBySessionDateAsync(sessionDate);

        if (!prevMinuteBars.Any() || !currentMinuteBars.Any()) return (null, null, null);

        var sequence = await loadRecentDailyBars(prevSessionDate, 40);
        if (sequence.Count < 21) return (null, null, null);

        return (sequence, prevMinuteBars, currentMinuteBars);
    }

    private async Task<List<BarData>> loadRecentDailyBars(string sessionDate, int lookbackDays)
    {
        var list = new List<BarData>();
        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        for (int i = lookbackDays; i >= 0; i--)
        {
            var d = date.AddDays(-i).ToString("yyyyMMdd");
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
        var dir = Path.Combine(baseDir, modelDate, name);
        var eval  = new BaseMLEvaluator(
            Path.Combine(dir, "xgb_model.onnx"),
            Path.Combine(dir, "scaler_params.json"),
            featureCount);
        eval.LoadModel();
        return eval;
    }
}
