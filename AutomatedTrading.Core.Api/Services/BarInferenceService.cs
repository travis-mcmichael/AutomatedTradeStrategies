using AutomatedTrading.Core.Api.Evaluators;
using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Api.Services;

public class BarInferenceService
{
    // MES session times in CT
    private static readonly TimeSpan SessionOpenTime = new(16, 0, 0);
    private static readonly TimeSpan RthStartTime    = new( 8, 30, 0);
    private static readonly TimeSpan RthEndTime      = new(15, 15, 0);

    private const int HighFeatureCount = 38;
    private const int LowFeatureCount  = 39;

    private readonly IBarRepository barRepo;
    private readonly BaseMLEvaluator highEvaluator;
    private readonly BaseMLEvaluator lowEvaluator;

    public bool HighLoaded => highEvaluator.SessionLoaded();
    public bool LowLoaded  => lowEvaluator.SessionLoaded();

    public BarInferenceService(IBarRepository barRepo, IConfiguration config)
    {
        this.barRepo = barRepo;

        var modelDate = config["Models:Date"] ?? "20260320";
        var baseDir   = AppDomain.CurrentDomain.BaseDirectory;

        highEvaluator = createEvaluator(baseDir, modelDate, "bar-high", HighFeatureCount);
        lowEvaluator  = createEvaluator(baseDir, modelDate, "bar-low",  LowFeatureCount);
    }

    public async Task<BarHighResponse?> RunHighInference(string sessionDate, double sessionOpen)
    {
        if (!highEvaluator.SessionLoaded()) return null;

        var (sequence, prevMinuteBars) = await loadSessionData(sessionDate);
        if (sequence is null || prevMinuteBars is null) return null;

        var j       = sequence.Count - 1;
        var current = sequence[j];
        var atr20   = Computations.ATR(sequence, j, 20);

        var shared   = buildSharedFeatures(sequence, j, current, sessionOpen, atr20, sessionDate);
        var intraday = computeHighIntradayFeatures(prevMinuteBars, atr20);
        double[] tail =
        [
            intraday.OvernightRange,
            intraday.OvernightVolumeRatio,
            intraday.RegularHoursRange,
            intraday.FirstHourRange,
            intraday.LastHourRange,
            intraday.HighTimeRatio,
            intraday.PrevOpenToHighRatio,
            intraday.PrevOpenToLowRatio,
        ];

        var scaled       = highEvaluator.Scale([.. shared, .. tail]);
        var predictedMove = highEvaluator.RunInference(scaled) * atr20;
        var predictedHigh = sessionOpen + predictedMove;

        return new BarHighResponse(predictedHigh, predictedMove);
    }

    public async Task<BarLowResponse?> RunLowInference(string sessionDate, double sessionOpen)
    {
        if (!lowEvaluator.SessionLoaded()) return null;

        var (sequence, prevMinuteBars) = await loadSessionData(sessionDate);
        if (sequence is null || prevMinuteBars is null) return null;

        var j       = sequence.Count - 1;
        var current = sequence[j];
        var atr20   = Computations.ATR(sequence, j, 20);

        var shared   = buildSharedFeatures(sequence, j, current, sessionOpen, atr20, sessionDate);
        var intraday = computeLowIntradayFeatures(prevMinuteBars, atr20);
        double[] tail =
        [
            intraday.OvernightRange,
            intraday.OvernightVolumeRatio,
            intraday.OvernightDropRatio,
            intraday.RegularHoursRange,
            intraday.FirstHourRange,
            intraday.LastHourRange,
            intraday.LowTimeRatio,
            intraday.PrevOpenToLowRatio,
            intraday.LastHourClosePosition,
        ];

        var scaled       = lowEvaluator.Scale([.. shared, .. tail]);
        var predictedMove = lowEvaluator.RunInference(scaled) * atr20;
        var predictedLow  = sessionOpen - predictedMove;

        return new BarLowResponse(predictedLow, predictedMove);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Feature construction
    // ──────────────────────────────────────────────────────────────────────────

    private double[] buildSharedFeatures(List<BarData> sequence, int j, BarData current, double sessionOpen, double atr20, string sessionDate)
    {
        var atr5       = Computations.ATR(sequence, j,  5);
        var atr10      = Computations.ATR(sequence, j, 10);
        var sma5       = Computations.SMA(sequence, j,  5, x => x.Close);
        var sma10      = Computations.SMA(sequence, j, 10, x => x.Close);
        var sma20      = Computations.SMA(sequence, j, 20, x => x.Close);
        var avgRange5  = Computations.AverageRange(sequence, j,  5);
        var avgRange10 = Computations.AverageRange(sequence, j, 10);
        var rsi14      = Computations.RSI(sequence, j, 14);
        var today      = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        return
        [
            safeDiv(current.High, sessionOpen),
            safeDiv(current.Low,  sessionOpen),
            Computations.HighLowRange(current, atr20),
            Computations.BodySize(current, atr20),
            Computations.UpperWick(current, atr20),
            Computations.LowerWick(current, atr20),
            Computations.ClosePosition(current),
            safeDiv(atr5,  sessionOpen),
            safeDiv(atr10, sessionOpen),
            safeDiv(atr20, sessionOpen),
            avgRange5,
            avgRange10,
            safeDiv(current.High - current.Low, avgRange10),
            Computations.Return(sequence, j, 1),
            Computations.Return(sequence, j, 3),
            Computations.Return(sequence, j, 5),
            safeDiv(sma5,  sma20),
            safeDiv(sma10, sma20),
            safeDiv(sessionOpen, sma5),
            safeDiv(sessionOpen, sma10),
            safeDiv(sessionOpen, sma20),
            Computations.SMA(sequence, j, 20, x => x.Volume),
            Computations.VolumeZScore(sequence, j, 20),
            safeDiv(sessionOpen - current.High,  atr20),
            safeDiv(sessionOpen - current.Low,   atr20),
            safeDiv(sessionOpen - current.Close, atr20),
            safeDiv(atr10, atr20),
            rsi14,
            (int)today.DayOfWeek,
            today.Day,
        ];
    }

    private static (double OvernightRange, double OvernightVolumeRatio, double RegularHoursRange,
                    double FirstHourRange, double LastHourRange, double HighTimeRatio,
                    double PrevOpenToHighRatio, double PrevOpenToLowRatio)
        computeHighIntradayFeatures(List<BarData> bars, double atr20)
    {
        if (!bars.Any() || atr20 == 0)
            return (0, 0, 0, 0, 0, 0.5, 0, 0);

        var ordered      = bars.OrderBy(b => b.Timestamp).ToList();
        var sessionOpenP = ordered.First().Open;
        var totalVolume  = ordered.Sum(b => b.Volume);

        var overnight = ordered.Where(b => b.Timestamp.TimeOfDay >= SessionOpenTime ||
                                           b.Timestamp.TimeOfDay <  RthStartTime).ToList();
        var rth       = ordered.Where(b => b.Timestamp.TimeOfDay >= RthStartTime &&
                                           b.Timestamp.TimeOfDay <  RthEndTime).ToList();
        var firstHour = ordered.Where(b => b.Timestamp.TimeOfDay >= RthStartTime &&
                                           b.Timestamp.TimeOfDay <  RthStartTime + TimeSpan.FromHours(1)).ToList();
        var lastHour  = ordered.Where(b => b.Timestamp.TimeOfDay >= RthEndTime - TimeSpan.FromHours(1) &&
                                           b.Timestamp.TimeOfDay <  RthEndTime).ToList();

        var sessionHigh  = ordered.Max(b => b.High);
        var sessionLow   = ordered.Min(b => b.Low);
        var highBarIndex = ordered.IndexOf(ordered.MaxBy(b => b.High)!);
        var highTimeRatio = ordered.Count > 1 ? (double)highBarIndex / (ordered.Count - 1) : 0.5;

        return (
            OvernightRange:       overnight.Any() ? safeDiv(overnight.Max(b => b.High) - overnight.Min(b => b.Low), atr20) : 0,
            OvernightVolumeRatio: safeDiv(overnight.Sum(b => b.Volume), totalVolume),
            RegularHoursRange:    rth.Any()       ? safeDiv(rth.Max(b => b.High) - rth.Min(b => b.Low), atr20) : 0,
            FirstHourRange:       firstHour.Any() ? safeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr20) : 0,
            LastHourRange:        lastHour.Any()  ? safeDiv(lastHour.Max(b => b.High) - lastHour.Min(b => b.Low), atr20) : 0,
            HighTimeRatio:        highTimeRatio,
            PrevOpenToHighRatio:  safeDiv(sessionHigh  - sessionOpenP, atr20),
            PrevOpenToLowRatio:   safeDiv(sessionOpenP - sessionLow,   atr20)
        );
    }

    private static (double OvernightRange, double OvernightVolumeRatio, double OvernightDropRatio,
                    double RegularHoursRange, double FirstHourRange, double LastHourRange,
                    double LowTimeRatio, double PrevOpenToLowRatio, double LastHourClosePosition)
        computeLowIntradayFeatures(List<BarData> bars, double atr20)
    {
        if (!bars.Any() || atr20 == 0)
            return (0, 0, 0, 0, 0, 0, 0.5, 0, 0.5);

        var ordered      = bars.OrderBy(b => b.Timestamp).ToList();
        var sessionOpenP = ordered.First().Open;
        var totalVolume  = ordered.Sum(b => b.Volume);

        var overnight = ordered.Where(b => b.Timestamp.TimeOfDay >= SessionOpenTime ||
                                           b.Timestamp.TimeOfDay <  RthStartTime).ToList();
        var rth       = ordered.Where(b => b.Timestamp.TimeOfDay >= RthStartTime &&
                                           b.Timestamp.TimeOfDay <  RthEndTime).ToList();
        var firstHour = ordered.Where(b => b.Timestamp.TimeOfDay >= RthStartTime &&
                                           b.Timestamp.TimeOfDay <  RthStartTime + TimeSpan.FromHours(1)).ToList();
        var lastHour  = ordered.Where(b => b.Timestamp.TimeOfDay >= RthEndTime - TimeSpan.FromHours(1) &&
                                           b.Timestamp.TimeOfDay <  RthEndTime).ToList();

        var sessionLow   = ordered.Min(b => b.Low);
        var lowBarIndex  = ordered.IndexOf(ordered.MinBy(b => b.Low)!);
        var lowTimeRatio = ordered.Count > 1 ? (double)lowBarIndex / (ordered.Count - 1) : 0.5;

        var overnightLow       = overnight.Any() ? overnight.Min(b => b.Low) : sessionOpenP;
        var overnightDropRatio = safeDiv(sessionOpenP - overnightLow, atr20);

        double lastHourClosePos = 0.5;
        if (lastHour.Any())
        {
            var lhHigh  = lastHour.Max(b => b.High);
            var lhLow   = lastHour.Min(b => b.Low);
            var lhClose = lastHour.OrderBy(b => b.Timestamp).Last().Close;
            lastHourClosePos = lhHigh == lhLow ? 0.5 : (lhClose - lhLow) / (lhHigh - lhLow);
        }

        return (
            OvernightRange:        overnight.Any() ? safeDiv(overnight.Max(b => b.High) - overnight.Min(b => b.Low), atr20) : 0,
            OvernightVolumeRatio:  safeDiv(overnight.Sum(b => b.Volume), totalVolume),
            OvernightDropRatio:    overnightDropRatio,
            RegularHoursRange:     rth.Any()       ? safeDiv(rth.Max(b => b.High) - rth.Min(b => b.Low), atr20) : 0,
            FirstHourRange:        firstHour.Any() ? safeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr20) : 0,
            LastHourRange:         lastHour.Any()  ? safeDiv(lastHour.Max(b => b.High) - lastHour.Min(b => b.Low), atr20) : 0,
            LowTimeRatio:          lowTimeRatio,
            PrevOpenToLowRatio:    safeDiv(sessionOpenP - sessionLow, atr20),
            LastHourClosePosition: lastHourClosePos
        );
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Data loading
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<(List<BarData>? sequence, List<BarData>? prevMinuteBars)> loadSessionData(string sessionDate)
    {
        var prevSessionDate = getPreviousSessionDate(sessionDate);

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
            list.Add(aggregateSession(bars));
        }

        return list;
    }

    private static BarData aggregateSession(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        return new BarData
        {
            Timestamp   = ordered.First().Timestamp,
            SessionDate = ordered.First().SessionDate,
            Open        = ordered.First().Open,
            Close       = ordered.Last().Close,
            High        = ordered.Max(x => x.High),
            Low         = ordered.Min(x => x.Low),
            Volume      = ordered.Sum(x => x.Volume),
        };
    }

    private static string getPreviousSessionDate(string sessionDate)
    {
        var dt = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);
        return dt.DayOfWeek switch
        {
            DayOfWeek.Monday => dt.AddDays(-3).ToString("yyyyMMdd"),
            DayOfWeek.Sunday => dt.AddDays(-2).ToString("yyyyMMdd"),
            _                => dt.AddDays(-1).ToString("yyyyMMdd"),
        };
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static BaseMLEvaluator createEvaluator(string baseDir, string modelDate, string name, int featureCount)
    {
        var dir   = Path.Combine(baseDir, "Models", modelDate, name);
        var eval  = new BaseMLEvaluator(
            Path.Combine(dir, "xgb_model.onnx"),
            Path.Combine(dir, "scaler_params.json"),
            featureCount);
        eval.LoadModel();
        return eval;
    }

    private static double safeDiv(double a, double b) => b == 0 ? 0 : a / b;
}
