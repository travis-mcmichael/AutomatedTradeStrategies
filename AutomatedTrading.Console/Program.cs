using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.DataLayer.Sqlite;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Console;

class Program
{
    // MES session times in CT (timestamps assumed to be in CT)
    // Session opens 16:00 (5 PM ET), RTH 08:30-15:15 (9:30 AM - 4:15 PM ET)
    static readonly TimeSpan SessionOpenTime = new(16,  0, 0);
    static readonly TimeSpan RthStartTime = new( 8, 30, 0);
    static readonly TimeSpan RthEndTime = new(15, 15, 0);
    static readonly List<ModelConfig> ModelConfigs =
    [
        new("mes.day-bar.high", "20260325", 38, isHigh: true),
        new("mes.day-bar.low",  "20260325", 39, isHigh: false),
    ];

    static async Task Main(string[] args)
    {
        var evaluators = loadEvaluators();
        if (evaluators.Count == 0)
        {
            System.Console.WriteLine("No models loaded.");
            return;
        }

        var runDate = new DateTime(2026, 3, 24);

        var barRepo = new BarRepository(new SqliteConnectionFactory(AppConstants.SQLITE_CONNECTION));
        var rangeStart = runDate; //new DateTime(2026, 3, 1);
        var rangeEnd = runDate; //DateTime.Today < new DateTime(2026, 12, 31) ? DateTime.Today : new DateTime(2026, 12, 31);
        var sessionDates = getSessionDates(rangeStart, rangeEnd);
        var results = evaluators.Keys.ToDictionary(k => k, _ => new List<SessionResult>());
        var skipped = 0;
        var activeConfigs = ModelConfigs.Where(c => evaluators.ContainsKey(c.Name)).ToList();

        System.Console.WriteLine($"\n{"Session",-10}" + 
        $" {"Model",-10}  {"PredMove",9}  {"ActMove",9} " + 
        $" {"Predicted",10}  {"Actual",10}  {"Error",8} " + 
        $" {"ATR",8}  {"Err/ATR",8}");
        System.Console.WriteLine(new string('-', 88));

        foreach (var sessionDate in sessionDates)
        {
            var sessionResults = await runSessionInference(barRepo, evaluators, sessionDate);
            if (sessionResults is null)
            {
                skipped++;
                continue;
            }

            var first = true;
            foreach (var config in activeConfigs)
            {
                if (!sessionResults.TryGetValue(config.Name, out var r)) continue;
                printSession(r,config, sessionDate, first);
                results[config.Name].Add(r);
                first = false;
            }
        }

        var allMonths = results.Values
            .SelectMany(r => r.Select(x => x.SessionDate[..6]))
            .Distinct()
            .OrderBy(m => m)
            .ToList();

        foreach (var month in allMonths)
        {
            var label = DateTime.ParseExact(month, "yyyyMM", null).ToString("MMMM yyyy");
            System.Console.WriteLine($"\n=== {label} ===");

            foreach (var config in activeConfigs)
            {
                var monthResults = results[config.Name]
                    .Where(r => r.SessionDate[..6] == month)
                    .ToList();
                if (monthResults.Count == 0) continue;
                printStats($"  {config.Name,-10}", monthResults);
            }
        }

        System.Console.WriteLine($"\n=== Totals  (skipped: {skipped}) ===");

        foreach (var config in activeConfigs)
        {
            if (results[config.Name].Count == 0) continue;
            printStats($"  {config.Name,-10}", results[config.Name]);
        }

        foreach (var ev in evaluators.Values)
            ev.Dispose();
    }

    static Dictionary<string, BaseMLEvaluator> loadEvaluators()
    {
        var evaluators = new Dictionary<string, BaseMLEvaluator>();
        foreach (var config in ModelConfigs)
        {
            var dir = Path.Combine("C:\\dev\\AutomatedTradeStrategies\\Models", config.ModelDate, config.Name);
            var modelPath = Path.Combine(dir, AppConstants.XGB_MODEL_FILENAME);
            var scalerPath = Path.Combine(dir, AppConstants.SCALER_FILENAME);
            var evaluator = new BaseMLEvaluator(modelPath, scalerPath, config.FeatureCount);
            evaluator.LoadModel();
            if (!evaluator.SessionLoaded())
            {
                System.Console.WriteLine($"Model '{config.Name}' failed to load, skipping.");
                continue;
            }
            evaluators[config.Name] = evaluator;
        }

        return evaluators;
    }

    static void printStats(string label, List<SessionResult> results)
    {
        var errors = results.Select(r => Math.Abs(r.Predicted - r.Actual)).ToList();
        var avgAtr = results.Average(r => r.Atr20);
        var mae = errors.Average();
        var avgPredMove = results.Average(r => r.PredictedMove);
        var avgActMove = results.Average(r => r.ActualMove);
        var w05 = errors.Count(e => e < avgAtr * 0.5);
        var w10 = errors.Count(e => e < avgAtr);
        var n = results.Count;
        System.Console.WriteLine(
            $"{label}  n={n,3} | AvgPredMove={avgPredMove,7:F2}  AvgActMove={avgActMove,7:F2} | " +
            $"MAE={mae,7:F2} pts | MAE/ATR={safeDiv(mae, avgAtr):F3} | " +
            $"W0.5={w05,3}/{n} ({(double)w05/n:P0}) | W1.0={w10,3}/{n} ({(double)w10/n:P0})");
    }

    static void printSession(SessionResult r, ModelConfig config, string sessionDate, bool first)
    {
        var error = r.Predicted - r.Actual;
        var errAtr = safeDiv(error, r.Atr20);
        var dateCol = first ? sessionDate : new string(' ', 8);
        System.Console.WriteLine($"{dateCol,-10} " +
        $"{config.Name,-10}  {r.PredictedMove,9:F2} {r.ActualMove,9:F2}" + 
        $"{r.Predicted,10:F2}  {r.Actual,10:F2}  {error,8:F2}" +
        $"{r.Atr20,8:F2} {errAtr,8:F3}");
    }

    static async Task<Dictionary<string, SessionResult>?> runSessionInference(
        IBarRepository barRepo,
        Dictionary<string, BaseMLEvaluator> evaluators,
        string sessionDate)
    {
        var prevSessionDate = getPreviousSessionDate(sessionDate);

        var todayMinuteBars = await barRepo.GetBarsBySessionDateAsync(sessionDate);
        if (todayMinuteBars.Count == 0) return null;

        var prevMinuteBars = await barRepo.GetBarsBySessionDateAsync(prevSessionDate);
        if (prevMinuteBars.Count == 0) return null;

        var sequence = await loadRecentDailyBars(barRepo, prevSessionDate, 40);
        if (sequence.Count < 21) return null;

        var j = sequence.Count - 1;
        var current = sequence[j];
        var sessionOpen = todayMinuteBars.OrderBy(b => b.Timestamp).First().Open;
        var atr5 = Computations.ATR(sequence, j,  5);
        var atr10 = Computations.ATR(sequence, j, 10);
        var atr20 = Computations.ATR(sequence, j, 20);
        var sma5 = Computations.SMA(sequence, j,  5, x => x.Close);
        var sma10 = Computations.SMA(sequence, j, 10, x => x.Close);
        var sma20 = Computations.SMA(sequence, j, 20, x => x.Close);
        var avgRange5 = Computations.AverageRange(sequence, j,  5);
        var avgRange10 = Computations.AverageRange(sequence, j, 10);
        var rsi14 = Computations.RSI(sequence, j, 14);
        var today = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        System.Console.WriteLine($"Session Open: {sessionOpen}");
        // Shared prefix — identical across all bar models (indices 0-29)
        double[] shared =
        [
            safeDiv(current.High, sessionOpen),              // PrevHighRatio
            safeDiv(current.Low,  sessionOpen),              // PrevLowRatio
            Computations.HighLowRange(current, atr20),       // PrevRange
            Computations.BodySize(current, atr20),           // PrevBodySize
            Computations.UpperWick(current, atr20),          // PrevUpperWick
            Computations.LowerWick(current, atr20),          // PrevLowerWick
            Computations.ClosePosition(current),             // PrevClosePosition
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
            Computations.SMA(sequence, j, 20, x => x.Volume), // VolumeMA20
            Computations.VolumeZScore(sequence, j, 20),        // VolumeZScore20
            safeDiv(sessionOpen - current.High,  atr20),    // DistanceFromPrevHigh
            safeDiv(sessionOpen - current.Low,   atr20),    // DistanceFromPrevLow
            safeDiv(sessionOpen - current.Close, atr20),    // GapVsATR
            safeDiv(atr10, atr20),                           // VolatilityRegime
            rsi14,                                            // RSI14
            (int)today.DayOfWeek,                            // DayOfWeek
            today.Day,                                        // DayOfMonth
        ];

        var output = new Dictionary<string, SessionResult>();

        if (evaluators.TryGetValue("mes.day-bar.high", out var highEval))
        {
            var intraday  = computeHighIntradayFeatures(prevMinuteBars, atr20);
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
            var scaled = highEval.Scale([.. shared, .. tail]);
            var predicted = sessionOpen + highEval.RunInference(scaled) * atr20;
            var actual = todayMinuteBars.Max(b => b.High);
            output["mes.day-bar.high"] = new SessionResult(
                sessionDate, 
                predicted,
                actual,
                predictedMove: predicted - sessionOpen,
                actualMove: actual - sessionOpen,
                atr20);
        }

        if (evaluators.TryGetValue("mes.day-bar.low", out var lowEval))
        {
            var intraday  = computeLowIntradayFeatures(prevMinuteBars, atr20);
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
            var scaled    = lowEval.Scale([.. shared, .. tail]);
            var predicted = sessionOpen - lowEval.RunInference(scaled) * atr20;
            var actual    = todayMinuteBars.Min(b => b.Low);
            output["mes.day-bar.low"] = new SessionResult(
                sessionDate, 
                predicted, 
                actual,
                predictedMove: sessionOpen - predicted,
                actualMove:    sessionOpen - actual,
                atr20);
        }

        return output.Count > 0 ? output : null;
    }

    static (double OvernightRange, double OvernightVolumeRatio, double RegularHoursRange,
        double FirstHourRange, double LastHourRange, double HighTimeRatio,
        double PrevOpenToHighRatio, double PrevOpenToLowRatio)
    computeHighIntradayFeatures(List<BarData> bars, double atr20)
    {
        if (bars.Count == 0 || atr20 == 0)
            return (0, 0, 0, 0, 0, 0.5, 0, 0);

        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var sessionOpenP = ordered.First().Open;
        var totalVolume = ordered.Sum(b => b.Volume);

        var overnight = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= SessionOpenTime || 
            b.Timestamp.TimeOfDay <  RthStartTime).ToList();
        var rth = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= RthStartTime && 
            b.Timestamp.TimeOfDay <  RthEndTime).ToList();
        var firstHour = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= RthStartTime && 
            b.Timestamp.TimeOfDay < RthStartTime + TimeSpan.FromHours(1)).ToList();
        var lastHour  = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= RthEndTime - TimeSpan.FromHours(1) &&
            b.Timestamp.TimeOfDay <  RthEndTime).ToList();

        var sessionHigh  = ordered.Max(b => b.High);
        var sessionLow   = ordered.Min(b => b.Low);
        var highBarIndex = ordered.IndexOf(ordered.MaxBy(b => b.High)!);
        var highTimeRatio = ordered.Count > 1 ? (double)highBarIndex / (ordered.Count - 1) : 0.5;

        return (
            OvernightRange: overnight.Count != 0 ? safeDiv(overnight.Max(b => b.High) - overnight.Min(b => b.Low), atr20) : 0,
            OvernightVolumeRatio:safeDiv(overnight.Sum(b => b.Volume), totalVolume),
            RegularHoursRange:   rth.Count != 0 ? safeDiv(rth.Max(b => b.High) - rth.Min(b => b.Low), atr20) : 0,
            FirstHourRange: firstHour.Count != 0 ? safeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr20) : 0,
            LastHourRange: lastHour.Count != 0 ? safeDiv(lastHour.Max(b => b.High) - lastHour.Min(b => b.Low), atr20) : 0,
            HighTimeRatio: highTimeRatio,
            PrevOpenToHighRatio: safeDiv(sessionHigh - sessionOpenP, atr20),
            PrevOpenToLowRatio: safeDiv(sessionOpenP - sessionLow,   atr20)
        );
    }

    static (double OvernightRange, double OvernightVolumeRatio, double OvernightDropRatio,
        double RegularHoursRange, double FirstHourRange, double LastHourRange,
        double LowTimeRatio, double PrevOpenToLowRatio, double LastHourClosePosition)
    computeLowIntradayFeatures(List<BarData> bars, double atr20)
    {
        if (bars.Count == 0 || atr20 == 0)
            return (0, 0, 0, 0, 0, 0, 0.5, 0, 0.5);

        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var sessionOpenP = ordered.First().Open;
        var totalVolume = ordered.Sum(b => b.Volume);

        var overnight = ordered.Where(b =>
            b.Timestamp.TimeOfDay >= SessionOpenTime || 
            b.Timestamp.TimeOfDay <  RthStartTime).ToList();
        var rth = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= RthStartTime &&
            b.Timestamp.TimeOfDay <  RthEndTime).ToList();
        var firstHour = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= RthStartTime &&
            b.Timestamp.TimeOfDay <  RthStartTime + TimeSpan.FromHours(1)).ToList();
        var lastHour = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= RthEndTime - TimeSpan.FromHours(1) &&
            b.Timestamp.TimeOfDay <  RthEndTime).ToList();

        var sessionLow = ordered.Min(b => b.Low);
        var lowBarIndex = ordered.IndexOf(ordered.MinBy(b => b.Low)!);
        var lowTimeRatio = ordered.Count > 1 ? (double)lowBarIndex / (ordered.Count - 1) : 0.5;

        var overnightLow = overnight.Count != 0 ? overnight.Min(b => b.Low) : sessionOpenP;
        var overnightDropRatio = safeDiv(sessionOpenP - overnightLow, atr20);

        double lastHourClosePos = 0.5;
        if (lastHour.Count != 0)
        {
            var lhHigh = lastHour.Max(b => b.High);
            var lhLow = lastHour.Min(b => b.Low);
            var lhClose = lastHour.OrderBy(b => b.Timestamp).Last().Close;
            lastHourClosePos = lhHigh == lhLow ? 0.5 : (lhClose - lhLow) / (lhHigh - lhLow);
        }

        return (
            OvernightRange: overnight.Count != 0 ? safeDiv(overnight.Max(b => b.High) - overnight.Min(b => b.Low), atr20) : 0,
            OvernightVolumeRatio: safeDiv(overnight.Sum(b => b.Volume), totalVolume),
            OvernightDropRatio: overnightDropRatio,
            RegularHoursRange: rth.Count != 0 ? safeDiv(rth.Max(b => b.High) - rth.Min(b => b.Low), atr20) : 0,
            FirstHourRange: firstHour.Count != 0 ? safeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr20) : 0,
            LastHourRange: lastHour.Count != 0 ? safeDiv(lastHour.Max(b => b.High) - lastHour.Min(b => b.Low), atr20) : 0,
            LowTimeRatio: lowTimeRatio,
            PrevOpenToLowRatio: safeDiv(sessionOpenP - sessionLow, atr20),
            LastHourClosePosition: lastHourClosePos
        );
    }

    // Returns all weekday (trading) dates between start and end, inclusive.
    // When start == end, returns that single date if it falls on a weekday.
    static List<string> getSessionDates(DateTime start, DateTime end)
    {
        var dates = new List<string>();

        for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Saturday || d.DayOfWeek == DayOfWeek.Sunday)
                continue;
            dates.Add(d.ToString("yyyyMMdd"));
        }

        return dates;
    }

    static async Task<List<BarData>> loadRecentDailyBars(IBarRepository repo, string sessionDate, int lookbackDays)
    {
        var list = new List<BarData>();
        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        for (int i = lookbackDays; i >= 0; i--)
        {
            var d = date.AddDays(-i).ToString("yyyyMMdd");
            var bars = await repo.GetBarsBySessionDateAsync(d);
            if (bars.Count == 0) continue;
            list.Add(BarData.Aggregate(bars));
        }

        return list;
    }

    static double safeDiv(double a, double b) => b == 0 ? 0 : a / b;

    static string getPreviousSessionDate(string sessionDate)
    {
        var dt = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);
        return dt.DayOfWeek switch
        {
            DayOfWeek.Monday => dt.AddDays(-3).ToString("yyyyMMdd"),
            DayOfWeek.Sunday => dt.AddDays(-2).ToString("yyyyMMdd"),
            _ => dt.AddDays(-1).ToString("yyyyMMdd"),
        };
    }
}
