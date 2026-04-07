using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Api.Models.API.Predict;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Config;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Services;

public class BarInferenceService : IBarHighPredictor, IBarLowPredictor, ITradeDayHighPredictor, ITradeDayLowPredictor
{
    private readonly IBarRepository barRepo;
    private readonly Dictionary<string, BaseMLEvaluator> evaluators;

    public BarInferenceService(IBarRepository barRepo)
    {
        this.barRepo = barRepo;
        evaluators = loadEvaluators();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Session-day-bar inference (session open → session high/low)
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<BarHighResponse?> RunSessionHighInference(string sessionDate, double sessionOpen)
    {
        if (!isLoaded("mes.session-day-bar.high")) return null;
        var eval = evaluators["mes.session-day-bar.high"];

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
        var scaled = eval.Scale([.. shared, .. tail]);
        var predictedMove = eval.RunInference(scaled) * atr20;
        return new BarHighResponse(sessionOpen + predictedMove, predictedMove);
    }

    public async Task<BarLowResponse?> RunSessionLowInference(string sessionDate, double sessionOpen)
    {
        if (!isLoaded("mes.session-day-bar.low")) return null;
        var eval = evaluators["mes.session-day-bar.low"];

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
        var scaled = eval.Scale([.. shared, .. tail]);
        var predictedMove = eval.RunInference(scaled) * atr20;
        return new BarLowResponse(sessionOpen - predictedMove, predictedMove);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Trade-day inference (RTH open → RTH high/low)
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<TradeDayHighResponse?> RunTradeDayHighInference(string sessionDate, double rthOpen)
    {
        if (!isLoaded("mes.trade-day-bar.high")) return null;
        var eval = evaluators["mes.trade-day-bar.high"];

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
        var scaled = eval.Scale([.. shared, .. preMarket, .. rthTail]);
        var predictedMove = eval.RunInference(scaled) * atr20;
        return new TradeDayHighResponse(rthOpen + predictedMove, predictedMove);
    }

    public async Task<TradeDayLowResponse?> RunTradeDayLowInference(string sessionDate, double rthOpen)
    {
        if (!isLoaded("mes.trade-day-bar.low")) return null;
        var eval = evaluators["mes.trade-day-bar.low"];

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
        var scaled = eval.Scale([.. shared, .. preMarket, .. rthTail]);
        var predictedMove = eval.RunInference(scaled) * atr20;
        return new TradeDayLowResponse(rthOpen - predictedMove, predictedMove);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // IBarHighPredictor / IBarLowPredictor
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<BarHighPrediction?> PredictSessionHighAsync(string sessionDate, double sessionOpen)
    {
        var result = await RunSessionHighInference(sessionDate, sessionOpen);
        return result is null ? null : new BarHighPrediction(result.PredictedMove);
    }

    public async Task<BarLowPrediction?> PredictSessionLowAsync(string sessionDate, double sessionOpen)
    {
        var result = await RunSessionLowInference(sessionDate, sessionOpen);
        return result is null ? null : new BarLowPrediction(-result.PredictedMove);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ITradeDayHighPredictor / ITradeDayLowPredictor
    // ──────────────────────────────────────────────────────────────────────────

    public async Task<TradeDayHighPrediction?> PredictTradeDayHighAsync(string sessionDate, double rthOpen)
    {
        var result = await RunTradeDayHighInference(sessionDate, rthOpen);
        return result is null ? null : new TradeDayHighPrediction(result.PredictedMove);
    }

    public async Task<TradeDayLowPrediction?> PredictTradeDayLowAsync(string sessionDate, double rthOpen)
    {
        var result = await RunTradeDayLowInference(sessionDate, rthOpen);
        return result is null ? null : new TradeDayLowPrediction(-result.PredictedMove);
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

    private async Task<(List<BarData>? sequence, List<BarData>? prevMinuteBars, List<BarData>? currentMinuteBars)> loadTradeDayData(string sessionDate)
    {
        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);
        var prevSessionDate = TradingCalendar.GetPreviousSessionDate(date).ToString("yyyyMMdd");

        var prevMinuteBars    = await barRepo.GetBarsBySessionDateAsync(prevSessionDate);
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
    // Startup
    // ──────────────────────────────────────────────────────────────────────────

    private bool isLoaded(string name) =>
        evaluators.TryGetValue(name, out var e) && e.SessionLoaded();

    private static Dictionary<string, BaseMLEvaluator> loadEvaluators()
    {
        var result = new Dictionary<string, BaseMLEvaluator>();
        foreach (var m in ModelConfig.GetConfig()?.Models ?? [])
        {
            if (!m.Active) continue;
            var dir = Path.Combine(Operations.NormalizePath(m.OutputLocation), m.Version, m.ModelName);
            var eval = new BaseMLEvaluator(
                Path.Combine(dir, AppConstants.XGB_MODEL_FILENAME),
                Path.Combine(dir, AppConstants.SCALER_FILENAME),
                m.FeatureCount);
            eval.LoadModel();
            if (!eval.SessionLoaded())
            {
                Console.WriteLine($"Warning: model '{m.ModelName}' failed to load from {dir}");
                continue;
            }
            result[m.ModelName] = eval;
        }
        return result;
    }
}
