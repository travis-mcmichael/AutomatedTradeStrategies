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
    const int featureCount = 40;

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

        // Generate all weekday dates in 2026 up to today as potential session dates
        var sessionDates = get2026SessionDates();

        System.Console.WriteLine($"{"Session",-12} {"Open",8} {"PredHigh",10} {"ActualHigh",11} {"Error",8} {"ErrorxATR",10}");
        System.Console.WriteLine(new string('-', 65));

        var errors    = new List<double>();
        var atrs      = new List<double>();
        var skipped   = 0;

        foreach (var sessionDate in sessionDates)
        {
            var result = await runSessionInference(barRepo, evaluator, sessionDate);
            if (result is null)
            {
                skipped++;
                continue;
            }

            var (sessionOpen, predictedHigh, actualHigh, atr20) = result.Value;
            var error = predictedHigh - actualHigh;
            errors.Add(Math.Abs(error));
            atrs.Add(atr20);

            System.Console.WriteLine(
                $"{sessionDate,-12} {sessionOpen,8:F2} {predictedHigh,10:F2} {actualHigh,11:F2} {error,8:F2} {Operations.SafeDiv(error, atr20),10:F3}");
        }

        if (errors.Count == 0)
        {
            System.Console.WriteLine("No sessions evaluated.");
            return;
        }

        var mae          = errors.Average();
        var avgAtr       = atrs.Average();
        var withinHalfAtr= errors.Count(e => e < avgAtr * 0.5);
        var withinOneAtr = errors.Count(e => e < avgAtr);

        System.Console.WriteLine(new string('-', 65));
        System.Console.WriteLine($"Sessions evaluated: {errors.Count}  |  Skipped: {skipped}");
        System.Console.WriteLine($"MAE:               {mae:F2} pts");
        System.Console.WriteLine($"Avg ATR20:         {avgAtr:F2} pts");
        System.Console.WriteLine($"MAE / ATR20:       {Operations.SafeDiv(mae, avgAtr):F3}");
        System.Console.WriteLine($"Within 0.5x ATR:  {withinHalfAtr}/{errors.Count} ({(double)withinHalfAtr/errors.Count:P0})");
        System.Console.WriteLine($"Within 1.0x ATR:  {withinOneAtr}/{errors.Count} ({(double)withinOneAtr/errors.Count:P0})");
    }

    static async Task<(double sessionOpen, double predictedHigh, double actualHigh, double atr20)?> runSessionInference(
        IBarRepository barRepo, BaseMLEvaluator evaluator, string sessionDate)
    {
        var prevSessionDate = getPreviousSessionDate(sessionDate);

        var todayMinuteBars = await barRepo.GetBarsBySessionDateAsync(sessionDate);
        if (!todayMinuteBars.Any()) return null;

        var prevMinuteBars = await barRepo.GetBarsBySessionDateAsync(prevSessionDate);
        if (!prevMinuteBars.Any()) return null;

        var sessionOpen  = todayMinuteBars.OrderBy(b => b.Timestamp).First().Open;
        var actualHigh   = todayMinuteBars.Max(b => b.High);

        var sequence = await loadRecentDailyBars(barRepo, prevSessionDate, 40);
        if (sequence.Count < 21) return null;

        var j = sequence.Count - 1;

        var current    = sequence[j];
        var atr5       = Computations.ATR(sequence, j, 5);
        var atr10      = Computations.ATR(sequence, j, 10);
        var atr20      = Computations.ATR(sequence, j, 20);
        var sma5       = Computations.SMA(sequence, j,  5, x => x.Close);
        var sma10      = Computations.SMA(sequence, j, 10, x => x.Close);
        var sma20      = Computations.SMA(sequence, j, 20, x => x.Close);
        var avgRange5  = Computations.AverageRange(sequence, j,  5);
        var avgRange10 = Computations.AverageRange(sequence, j, 10);
        var rsi14      = Computations.RSI(sequence, j, 14);
        var today      = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);
        var intraday   = computeIntradayFeatures(prevMinuteBars, atr20);

        var featureVector = new double[]
        {
            // --- Previous complete session shape ---
            Operations.SafeDiv(current.High, sessionOpen),              // PrevHighRatio
            Operations.SafeDiv(current.Low,  sessionOpen),              // PrevLowRatio
            Computations.HighLowRange(current, atr20),       // PrevRange
            Computations.BodySize(current, atr20),           // PrevBodySize
            Computations.UpperWick(current, atr20),          // PrevUpperWick
            Computations.LowerWick(current, atr20),          // PrevLowerWick
            Computations.ClosePosition(current),             // PrevClosePosition

            // --- Volatility / trend context ---
            Operations.SafeDiv(atr5,  sessionOpen),          // ATR5
            Operations.SafeDiv(atr10, sessionOpen),          // ATR10
            Operations.SafeDiv(atr20, sessionOpen),          // ATR20
            avgRange5,                                       // AvgRange5
            avgRange10,                                      // AvgRange10
            Operations.SafeDiv(current.High - current.Low, avgRange10), // RangeRatio
            Computations.Return(sequence, j, 1),             // Return1D
            Computations.Return(sequence, j, 3),             // Return3D
            Computations.Return(sequence, j, 5),             // Return5D
            Operations.SafeDiv(sma5,  sma20),                // SMA5SMA20Ratio
            Operations.SafeDiv(sma10, sma20),                // SMA10SMA20Ratio
            Operations.SafeDiv(sessionOpen, sma5),           // OpenSMA5Ratio
            Operations.SafeDiv(sessionOpen, sma10),          // OpenSMA10Ratio
            Operations.SafeDiv(sessionOpen, sma20),          // OpenSMA20Ratio

            // --- Volume ---
            Computations.SMA(sequence, j, 20, x => x.Volume), // VolumeMA20
            Computations.VolumeZScore(sequence, j, 20),       // VolumeZScore20

            // --- Gap / distance from key levels ---
            Operations.SafeDiv(sessionOpen - current.High,  atr20),    // DistanceFromPrevHigh
            Operations.SafeDiv(sessionOpen - current.Low,   atr20),    // DistanceFromPrevLow
            Operations.SafeDiv(sessionOpen - current.Close, atr20),    // GapVsATR

            // --- Regime ---
            Operations.SafeDiv(atr10, atr20),                 // VolatilityRegime
            rsi14,                                            // RSI14

            // --- Calendar ---
            (int)today.DayOfWeek,                             // DayOfWeek
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

        var scaled       = applyScaler(featureVector, evaluator);
        var prediction   = evaluator.RunInference(scaled);
        var predictedHigh = sessionOpen + prediction * atr20;

        return (sessionOpen, predictedHigh, actualHigh, atr20);
    }

    // Returns weekday dates from Jan 2 2026 through today (or Dec 31 2026, whichever is earlier)
    static List<string> get2026SessionDates()
    {
        var dates  = new List<string>();
        var start  = new DateTime(2026, 1, 2);
        var end    = new DateTime(DateTime.Today.Year == 2026 ? DateTime.Today.Year : 2026,
                                  DateTime.Today.Year == 2026 ? DateTime.Today.Month : 12,
                                  DateTime.Today.Year == 2026 ? DateTime.Today.Day   : 31);
        end = end < new DateTime(2026, 12, 31) ? end : new DateTime(2026, 12, 31);

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
                continue;
            dates.Add(d.ToString("yyyyMMdd"));
        }

        return dates;
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
            OvernightRange:       overnight.Any() ? Operations.SafeDiv(overnight.Max(b => b.High) - overnight.Min(b => b.Low), atr20) : 0,
            OvernightVolumeRatio: Operations.SafeDiv(overnight.Sum(b => b.Volume), totalVolume),
            RegularHoursRange:    rth.Any()       ? Operations.SafeDiv(rth.Max(b => b.High)       - rth.Min(b => b.Low),       atr20) : 0,
            FirstHourRange:       firstHour.Any() ? Operations.SafeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr20) : 0,
            LastHourRange:        lastHour.Any()  ? Operations.SafeDiv(lastHour.Max(b => b.High)  - lastHour.Min(b => b.Low),  atr20) : 0,
            HighTimeRatio:        highTimeRatio,
            PrevOpenToHighRatio:  Operations.SafeDiv(sessionHigh - sessionOpenP, atr20),
            PrevOpenToLowRatio:   Operations.SafeDiv(sessionOpenP - sessionLow,  atr20)
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
