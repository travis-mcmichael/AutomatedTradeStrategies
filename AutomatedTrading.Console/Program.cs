using AutomatedTrading.Core.Api.Evaluators;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Console;

class Program
{
    static string model  = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models\\20260319", "xgb_model.onnx");
    static string scaler = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models\\20260319", "scaler_params.json");
    const int featureCount = 38;

    // MES session times in CT (timestamps assumed to be in CT)
    // Session opens 16:00 (5 PM ET), RTH 08:30-15:15 (9:30 AM - 4:15 PM ET)
    static readonly TimeSpan SessionOpenTime = new(16,  0, 0);
    static readonly TimeSpan RthStartTime    = new( 8, 30, 0);
    static readonly TimeSpan RthEndTime      = new(15, 15, 0);

    static async Task Main(string[] args)
    {
        using var evaluator = new BaseMLEvaluator(model, scaler, featureCount);
        evaluator.LoadModel();
        if (!evaluator.SessionLoaded())
        {
            System.Console.WriteLine("Model failed to load.");
            return;
        }

        var barRepo = new BarRepository(new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION));

        // The session currently open has today's date as its session date
        // (e.g. the session that opened at 5 PM yesterday is session "20260319")
        var todaySessionDate = "20260108"; //DateTime.Today.ToString("yyyyMMdd");
        var prevSessionDate  = getPreviousSessionDate(todaySessionDate);

        // First bar of the current (open) session gives us the session open price
        var todayMinuteBars = await barRepo.GetBarsBySessionDateAsync(todaySessionDate);
        if (!todayMinuteBars.Any())
        {
            System.Console.WriteLine($"No bars found for today's session ({todaySessionDate}). " +
                                     "Has the session opened yet?");
            return;
        }

        // Previous session's 1-min bars — needed for intraday structure features
        var prevMinuteBars = await barRepo.GetBarsBySessionDateAsync(prevSessionDate);
        if (!prevMinuteBars.Any())
        {
            System.Console.WriteLine($"No bars found for previous session ({prevSessionDate}).");
            return;
        }

        var sessionOpen = todayMinuteBars.OrderBy(b => b.Timestamp).First().Open;

        // Daily sequence ending with the most recent COMPLETE session (prevSessionDate)
        // 40 calendar-day window gives ~28 trading days, ensuring 20+ sessions of history
        var sequence = await loadRecentDailyBars(barRepo, prevSessionDate, 40);
        var j = sequence.Count - 1;

        if (j < 20)
        {
            System.Console.WriteLine($"Insufficient history: {sequence.Count} sessions found, need at least 20.");
            return;
        }

        var current   = sequence[j]; // most recent complete session
        var atr5      = Computations.ATR(sequence, j, 5);
        var atr10     = Computations.ATR(sequence, j, 10);
        var atr20     = Computations.ATR(sequence, j, 20);
        var sma5      = Computations.SMA(sequence, j,  5, x => x.Close);
        var sma10     = Computations.SMA(sequence, j, 10, x => x.Close);
        var sma20     = Computations.SMA(sequence, j, 20, x => x.Close);
        var avgRange5 = Computations.AverageRange(sequence, j,  5);
        var avgRange10= Computations.AverageRange(sequence, j, 10);
        var rsi14     = Computations.RSI(sequence, j, 14);
        var today     = DateTime.ParseExact(todaySessionDate, "yyyyMMdd", null);
        var intraday  = computeIntradayFeatures(prevMinuteBars, atr20);

        var featureVector = new double[]
        {
            // --- Previous complete session shape ---
            safeDiv(current.High, sessionOpen),              // PrevHighRatio
            safeDiv(current.Low,  sessionOpen),              // PrevLowRatio
            Computations.HighLowRange(current, atr20),       // PrevRange
            Computations.BodySize(current, atr20),           // PrevBodySize
            Computations.UpperWick(current, atr20),          // PrevUpperWick
            Computations.LowerWick(current, atr20),          // PrevLowerWick
            Computations.ClosePosition(current),             // PrevClosePosition

            // --- Volatility / trend context ---
            safeDiv(atr5,  sessionOpen),                     // ATR5
            safeDiv(atr10, sessionOpen),                     // ATR10
            safeDiv(atr20, sessionOpen),                     // ATR20
            avgRange5,                                        // AvgRange5
            avgRange10,                                       // AvgRange10
            safeDiv(current.High - current.Low, avgRange10), // RangeRatio
            Computations.Return(sequence, j, 1),             // Return1D
            Computations.Return(sequence, j, 3),             // Return3D
            Computations.Return(sequence, j, 5),             // Return5D
            safeDiv(sma5,  sma20),                           // SMA5SMA20Ratio
            safeDiv(sma10, sma20),                           // SMA10SMA20Ratio
            safeDiv(sessionOpen, sma5),                      // OpenSMA5Ratio
            safeDiv(sessionOpen, sma10),                     // OpenSMA10Ratio
            safeDiv(sessionOpen, sma20),                     // OpenSMA20Ratio

            // --- Volume ---
            Computations.SMA(sequence, j, 20, x => x.Volume), // VolumeMA20
            Computations.VolumeZScore(sequence, j, 20),        // VolumeZScore20

            // --- Gap / distance from key levels ---
            safeDiv(sessionOpen - current.High,  atr20),    // DistanceFromPrevHigh
            safeDiv(sessionOpen - current.Low,   atr20),    // DistanceFromPrevLow
            safeDiv(sessionOpen - current.Close, atr20),    // GapVsATR

            // --- Regime ---
            safeDiv(atr10, atr20),                           // VolatilityRegime
            rsi14,                                            // RSI14

            // --- Calendar ---
            (int)today.DayOfWeek,                            // DayOfWeek
            today.Day,                                        // DayOfMonth

            // --- Previous session intraday structure ---
            intraday.OvernightRange,                         // OvernightRange
            intraday.OvernightVolumeRatio,                   // OvernightVolumeRatio
            intraday.RegularHoursRange,                      // RegularHoursRange
            intraday.FirstHourRange,                         // FirstHourRange
            intraday.LastHourRange,                          // LastHourRange
            intraday.HighTimeRatio,                          // HighTimeRatio
            intraday.PrevOpenToHighRatio,                    // PrevOpenToHighRatio
            intraday.PrevOpenToLowRatio,                     // PrevOpenToLowRatio
        };

        var scaled     = applyScaler(featureVector, evaluator);
        var prediction = evaluator.RunInference(scaled);
        var expectedHigh = sessionOpen + prediction * atr20;

        System.Console.WriteLine($"Session:       {todaySessionDate}");
        System.Console.WriteLine($"Session open:  {sessionOpen:F2}");
        System.Console.WriteLine($"ATR20 (daily): {atr20:F2}");
        System.Console.WriteLine($"Predicted move: +{prediction * atr20:F2} pts ({prediction:F4} x ATR20)");
        System.Console.WriteLine($"Expected high:  {expectedHigh:F2}");
    }

    // Mirrors the intraday feature logic in SessionOpenHighFeatureExporter exactly
    static (double OvernightRange, double OvernightVolumeRatio, double RegularHoursRange,
            double FirstHourRange, double LastHourRange, double HighTimeRatio,
            double PrevOpenToHighRatio, double PrevOpenToLowRatio)
        computeIntradayFeatures(List<BarData> bars, double atr20)
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
        var highTimeRatio= ordered.Count > 1 ? (double)highBarIndex / (ordered.Count - 1) : 0.5;

        return (
            OvernightRange:       overnight.Any() ? safeDiv(overnight.Max(b => b.High) - overnight.Min(b => b.Low), atr20) : 0,
            OvernightVolumeRatio: safeDiv(overnight.Sum(b => b.Volume), totalVolume),
            RegularHoursRange:    rth.Any()       ? safeDiv(rth.Max(b => b.High)       - rth.Min(b => b.Low),       atr20) : 0,
            FirstHourRange:       firstHour.Any() ? safeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr20) : 0,
            LastHourRange:        lastHour.Any()  ? safeDiv(lastHour.Max(b => b.High)  - lastHour.Min(b => b.Low),  atr20) : 0,
            HighTimeRatio:        highTimeRatio,
            PrevOpenToHighRatio:  safeDiv(sessionHigh - sessionOpenP, atr20),
            PrevOpenToLowRatio:   safeDiv(sessionOpenP - sessionLow,  atr20)
        );
    }

    static BarData aggregateSession(List<BarData> bars)
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
            Volume      = ordered.Sum(x => x.Volume)
        };
    }

    static async Task<List<BarData>> loadRecentDailyBars(IBarRepository repo, string sessionDate, int lookbackDays)
    {
        var list = new List<BarData>();
        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        for (int i = lookbackDays; i >= 0; i--)
        {
            var d    = date.AddDays(-i).ToString("yyyyMMdd");
            var bars = await repo.GetBarsBySessionDateAsync(d);
            if (bars.Count == 0) continue;
            list.Add(aggregateSession(bars));
        }

        return list;
    }

    static double[] applyScaler(double[] features, BaseMLEvaluator evaluator)
    {
        if (!evaluator.ScalerLoaded)
            return features;

        var scaled     = new double[features.Length];
        var meanField  = typeof(BaseMLEvaluator).GetField("scalerMean",  System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var scaleField = typeof(BaseMLEvaluator).GetField("scalerScale", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var mean       = (double[])meanField!.GetValue(evaluator)!;
        var scale      = (double[])scaleField!.GetValue(evaluator)!;

        for (int i = 0; i < features.Length; i++)
            scaled[i] = scale[i] == 0 ? 0 : (features[i] - mean[i]) / scale[i];

        return scaled;
    }

    static double safeDiv(double a, double b) => b == 0 ? 0 : a / b;

    static string getPreviousSessionDate(string sessionDate)
    {
        var dt = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);
        return dt.DayOfWeek switch
        {
            DayOfWeek.Monday => dt.AddDays(-3).ToString("yyyyMMdd"),
            DayOfWeek.Sunday => dt.AddDays(-2).ToString("yyyyMMdd"),
            _                => dt.AddDays(-1).ToString("yyyyMMdd"),
        };
    }
}
