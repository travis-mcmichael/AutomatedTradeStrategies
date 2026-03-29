using System.Data;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Console.InferenceTesting;

public class InferenceEvaluationTest
{
    private readonly IBarRepository _barRepo;
    private List<ModelConfig> _modelConfigs;
    private Dictionary<string, BaseMLEvaluator> _evaluators;
    public InferenceEvaluationTest(IBarRepository barRepo, List<ModelConfig> modelConfigs)
    {
        _barRepo = barRepo;
        _modelConfigs = modelConfigs;

        loadEvaluators();
    }

    public async Task RunAsync(string startDate, string endDate)
    {
        var start = DateTime.ParseExact(startDate, "yyyyMMdd", null);
        var end = DateTime.ParseExact(endDate, "yyyyMMdd", null);
        var sessionDates = getSessionDates(start, end);
        var skipped = 0;
        var results = _evaluators.Keys.ToDictionary(k => k, _ => new List<SessionResult>());
        var activeConfigs = _modelConfigs.Where(config => _evaluators.ContainsKey(config.Name)).ToList();

        logHeader();

        foreach (var date in sessionDates)
        {
            var sessionResults = await runSessionInference(date);
            if (sessionResults is null)
            {
                skipped++;
                continue;
            }
            var first = true;
            foreach (var config in activeConfigs)
            {
                if (!sessionResults.TryGetValue(config.Name, out var r)) continue;
                logSession(r, config, date, first);
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
            logMonthHeader(month);

            foreach (var config in activeConfigs)
            {
                var monthResults = results[config.Name]
                    .Where(r => r.SessionDate[..6] == month)
                    .ToList();
                if (monthResults.Count == 0) continue;
                logStats($"  {config.Name,-22}",monthResults);
            }
        }
        logSkipped(skipped);
        foreach(var config in activeConfigs)
        {
            if (results[config.Name].Count == 0) continue;
            logStats($"  {config.Name,-22}",results[config.Name]);
        }
        foreach (var ev in _evaluators.Values)
            ev.Dispose();
    }

    private void loadEvaluators()
    {
        _evaluators = new Dictionary<string, BaseMLEvaluator>();
        foreach (var config in _modelConfigs)
        {
            var dir = Path.Combine(AppConstants.MODEL_DIR, config.ModelDate, config.Name);
            var modelPath = Path.Combine(dir, AppConstants.XGB_MODEL_FILENAME);
            var scalerPath = Path.Combine(dir, AppConstants.SCALER_FILENAME);
            var evaluator = new BaseMLEvaluator(modelPath, scalerPath, config.FeatureCount);
            evaluator.LoadModel();
            if (!evaluator.SessionLoaded())
            {
                System.Console.WriteLine($"Model '{config.Name}' failed to load, skipping.");
                continue;
            }
            _evaluators[config.Name] = evaluator;
        }

        if (_evaluators.Count == 0)
            throw new DataException("No models loaded");
    }

    private List<string> getSessionDates(DateTime start, DateTime end)
    {
        var dates = new List<string>();
        for (var d = start.Date; d <= end.Date; d = TradingCalendar.GetNextSessionDate(d))
        {
            dates.Add(d.ToString("yyyyMMdd"));
        }
        return dates;
    }

    private async Task<Dictionary<string, SessionResult>?> runSessionInference(
        string sessionDate)
    {
        var prevSessionDate = TradingCalendar.GetPreviousSessionDate(DateTime.ParseExact(sessionDate, "yyyyMMdd", null)).ToString("yyyyMMdd");

        var todayMinuteBars = await _barRepo.GetBarsBySessionDateAsync(sessionDate);
        if (todayMinuteBars.Count == 0) return null;

        var prevMinuteBars = await _barRepo.GetBarsBySessionDateAsync(prevSessionDate);
        if (prevMinuteBars.Count == 0) return null;

        var sequence = await loadRecentDailyBars(_barRepo, prevSessionDate, 40);
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
            Operations.SafeDiv(current.High, sessionOpen),              // PrevHighRatio
            Operations.SafeDiv(current.Low,  sessionOpen),              // PrevLowRatio
            Computations.HighLowRange(current, atr20),       // PrevRange
            Computations.BodySize(current, atr20),           // PrevBodySize
            Computations.UpperWick(current, atr20),          // PrevUpperWick
            Computations.LowerWick(current, atr20),          // PrevLowerWick
            Computations.ClosePosition(current),             // PrevClosePosition
            Operations.SafeDiv(atr5,  sessionOpen),                     // ATR5
            Operations.SafeDiv(atr10, sessionOpen),                     // ATR10
            Operations.SafeDiv(atr20, sessionOpen),                     // ATR20
            avgRange5,                                        // AvgRange5
            avgRange10,                                       // AvgRange10
            Operations.SafeDiv(current.High - current.Low, avgRange10), // RangeRatio
            Computations.Return(sequence, j, 1),             // Return1D
            Computations.Return(sequence, j, 3),             // Return3D
            Computations.Return(sequence, j, 5),             // Return5D
            Operations.SafeDiv(sma5,  sma20),                           // SMA5SMA20Ratio
            Operations.SafeDiv(sma10, sma20),                           // SMA10SMA20Ratio
            Operations.SafeDiv(sessionOpen, sma5),                      // OpenSMA5Ratio
            Operations.SafeDiv(sessionOpen, sma10),                     // OpenSMA10Ratio
            Operations.SafeDiv(sessionOpen, sma20),                     // OpenSMA20Ratio
            Computations.SMA(sequence, j, 20, x => x.Volume), // VolumeMA20
            Computations.VolumeZScore(sequence, j, 20),        // VolumeZScore20
            Operations.SafeDiv(sessionOpen - current.High,  atr20),    // DistanceFromPrevHigh
            Operations.SafeDiv(sessionOpen - current.Low,   atr20),    // DistanceFromPrevLow
            Operations.SafeDiv(sessionOpen - current.Close, atr20),    // GapVsATR
            Operations.SafeDiv(atr10, atr20),                           // VolatilityRegime
            rsi14,                                            // RSI14
            (int)today.DayOfWeek,                            // DayOfWeek
            today.Day,                                        // DayOfMonth
        ];

        var output = new Dictionary<string, SessionResult>();

        if (_evaluators.TryGetValue("mes.session-day-bar.high", out var highEval))
        {
            double[] tail =
            [
                Computations.OvernightRange(prevMinuteBars, atr20),
                Computations.OvernightVolumeRatio(prevMinuteBars),
                Computations.RegularHoursRange(prevMinuteBars, atr20),
                Computations.FirstHourRange(prevMinuteBars, atr20),
                Computations.LastHourRange(prevMinuteBars, atr20),
                Computations.HighTimeRatio(prevMinuteBars),
                Computations.PrevOpenToHighRatio(prevMinuteBars, atr20),
                Computations.PrevOpenToLowRatio(prevMinuteBars, atr20),
            ];
            var scaled = highEval.Scale([.. shared, .. tail]);
            var predicted = sessionOpen + highEval.RunInference(scaled) * atr20;
            var actual = todayMinuteBars.Max(b => b.High);
            output["mes.session-day-bar.high"] = new SessionResult(
                sessionDate, 
                predicted,
                actual,
                predictedMove: predicted - sessionOpen,
                actualMove: actual - sessionOpen,
                atr20);
        }

        if (_evaluators.TryGetValue("mes.session-day-bar.low", out var lowEval))
        {
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
            var scaled = lowEval.Scale([.. shared, .. tail]);
            var predicted = sessionOpen - lowEval.RunInference(scaled) * atr20;
            var actual = todayMinuteBars.Min(b => b.Low);
            output["mes.session-day-bar.low"] = new SessionResult(
                sessionDate, 
                predicted, 
                actual,
                predictedMove: sessionOpen - predicted,
                actualMove:    sessionOpen - actual,
                atr20);
        }

        var rthBars = todayMinuteBars
            .OrderBy(b => b.Timestamp)
            .Where(b => b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
                        b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME)
            .ToList();

        if (_evaluators.TryGetValue("mes.trade-day-bar.high", out var tradeDayHighEval) && rthBars.Count > 0)
        {
            var tradeDayOpen = rthBars.First().Open;
            double[] tradeDayShared =
            [
                Operations.SafeDiv(current.High, tradeDayOpen),              // PrevHighRatio
                Operations.SafeDiv(current.Low,  tradeDayOpen),              // PrevLowRatio
                Computations.HighLowRange(current, atr20),                   // PrevRange
                Computations.BodySize(current, atr20),                       // PrevBodySize
                Computations.UpperWick(current, atr20),                      // PrevUpperWick
                Computations.LowerWick(current, atr20),                      // PrevLowerWick
                Computations.ClosePosition(current),                         // PrevClosePosition
                Operations.SafeDiv(atr5,  tradeDayOpen),                     // ATR5
                Operations.SafeDiv(atr10, tradeDayOpen),                     // ATR10
                Operations.SafeDiv(atr20, tradeDayOpen),                     // ATR20
                avgRange5,                                                    // AvgRange5
                avgRange10,                                                   // AvgRange10
                Operations.SafeDiv(current.High - current.Low, avgRange10),  // RangeRatio
                Computations.Return(sequence, j, 1),                         // Return1D
                Computations.Return(sequence, j, 3),                         // Return3D
                Computations.Return(sequence, j, 5),                         // Return5D
                Operations.SafeDiv(sma5,  sma20),                            // SMA5SMA20Ratio
                Operations.SafeDiv(sma10, sma20),                            // SMA10SMA20Ratio
                Operations.SafeDiv(tradeDayOpen, sma5),                      // OpenSMA5Ratio
                Operations.SafeDiv(tradeDayOpen, sma10),                     // OpenSMA10Ratio
                Operations.SafeDiv(tradeDayOpen, sma20),                     // OpenSMA20Ratio
                Computations.SMA(sequence, j, 20, x => x.Volume),            // VolumeMA20
                Computations.VolumeZScore(sequence, j, 20),                  // VolumeZScore20
                Operations.SafeDiv(tradeDayOpen - current.High,  atr20),     // DistanceFromPrevHigh
                Operations.SafeDiv(tradeDayOpen - current.Low,   atr20),     // DistanceFromPrevLow
                Operations.SafeDiv(tradeDayOpen - current.Close, atr20),     // GapVsATR
                Operations.SafeDiv(atr10, atr20),                            // VolatilityRegime
                rsi14,                                                        // RSI14
                (int)today.DayOfWeek,                                        // DayOfWeek
                today.Day,                                                    // DayOfMonth
            ];
            double[] tail =
            [
                Computations.PreMarketRange(todayMinuteBars, atr20),                      // PreMarketRange
                Computations.PreMarketHighVsOpen(todayMinuteBars, tradeDayOpen, atr20),   // PreMarketHighVsOpen
                Computations.PreMarketLowVsOpen(todayMinuteBars, tradeDayOpen, atr20),    // PreMarketLowVsOpen
                Computations.GapToRthOpen(tradeDayOpen, current.Close, atr20),            // GapToRthOpen
                Computations.PreMarketVolumeRatio(todayMinuteBars),                       // PreMarketVolumeRatio
                Computations.RthRange(prevMinuteBars, atr20),                             // PrevRthRange
                Computations.FirstHalfHourRange(prevMinuteBars, atr20),                   // PrevRthFirstHalfHourRange
                Computations.FirstHourRange(prevMinuteBars, atr20),                       // PrevRthFirstHourRange
                Computations.LastHourRange(prevMinuteBars, atr20),                        // PrevRthLastHourRange
                Computations.RthHighTimeRatio(prevMinuteBars),                            // PrevRthHighTimeRatio
                Computations.RthOpenToHighRatio(prevMinuteBars, atr20),                   // PrevRthOpenToHighRatio
                Computations.RthOpenToLowRatio(prevMinuteBars, atr20),                    // PrevRthOpenToLowRatio
            ];
            var scaled = tradeDayHighEval.Scale([.. tradeDayShared, .. tail]);
            var predicted = tradeDayOpen + tradeDayHighEval.RunInference(scaled) * atr20;
            var actual = rthBars.Max(b => b.High);
            output["mes.trade-day-bar.high"] = new SessionResult(
                sessionDate,
                predicted,
                actual,
                predictedMove: predicted - tradeDayOpen,
                actualMove: actual - tradeDayOpen,
                atr20);
        }

        if (_evaluators.TryGetValue("mes.trade-day-bar.low", out var tradeDayLowEval) && rthBars.Count > 0)
        {
            var tradeDayOpen = rthBars.First().Open;
            double[] tradeDayShared =
            [
                Operations.SafeDiv(current.High, tradeDayOpen),              // PrevHighRatio
                Operations.SafeDiv(current.Low,  tradeDayOpen),              // PrevLowRatio
                Computations.HighLowRange(current, atr20),                   // PrevRange
                Computations.BodySize(current, atr20),                       // PrevBodySize
                Computations.UpperWick(current, atr20),                      // PrevUpperWick
                Computations.LowerWick(current, atr20),                      // PrevLowerWick
                Computations.ClosePosition(current),                         // PrevClosePosition
                Operations.SafeDiv(atr5,  tradeDayOpen),                     // ATR5
                Operations.SafeDiv(atr10, tradeDayOpen),                     // ATR10
                Operations.SafeDiv(atr20, tradeDayOpen),                     // ATR20
                avgRange5,                                                    // AvgRange5
                avgRange10,                                                   // AvgRange10
                Operations.SafeDiv(current.High - current.Low, avgRange10),  // RangeRatio
                Computations.Return(sequence, j, 1),                         // Return1D
                Computations.Return(sequence, j, 3),                         // Return3D
                Computations.Return(sequence, j, 5),                         // Return5D
                Operations.SafeDiv(sma5,  sma20),                            // SMA5SMA20Ratio
                Operations.SafeDiv(sma10, sma20),                            // SMA10SMA20Ratio
                Operations.SafeDiv(tradeDayOpen, sma5),                      // OpenSMA5Ratio
                Operations.SafeDiv(tradeDayOpen, sma10),                     // OpenSMA10Ratio
                Operations.SafeDiv(tradeDayOpen, sma20),                     // OpenSMA20Ratio
                Computations.SMA(sequence, j, 20, x => x.Volume),            // VolumeMA20
                Computations.VolumeZScore(sequence, j, 20),                  // VolumeZScore20
                Operations.SafeDiv(tradeDayOpen - current.High,  atr20),     // DistanceFromPrevHigh
                Operations.SafeDiv(tradeDayOpen - current.Low,   atr20),     // DistanceFromPrevLow
                Operations.SafeDiv(tradeDayOpen - current.Close, atr20),     // GapVsATR
                Operations.SafeDiv(atr10, atr20),                            // VolatilityRegime
                rsi14,                                                        // RSI14
                (int)today.DayOfWeek,                                        // DayOfWeek
                today.Day,                                                    // DayOfMonth
            ];
            double[] tail =
            [
                Computations.PreMarketRange(todayMinuteBars, atr20),                      // PreMarketRange
                Computations.PreMarketHighVsOpen(todayMinuteBars, tradeDayOpen, atr20),   // PreMarketHighVsOpen
                Computations.PreMarketLowVsOpen(todayMinuteBars, tradeDayOpen, atr20),    // PreMarketLowVsOpen
                Computations.GapToRthOpen(tradeDayOpen, current.Close, atr20),            // GapToRthOpen
                Computations.PreMarketVolumeRatio(todayMinuteBars),                       // PreMarketVolumeRatio
                Computations.RthRange(prevMinuteBars, atr20),                             // PrevRthRange
                Computations.FirstHalfHourRange(prevMinuteBars, atr20),                   // PrevRthFirstHalfHourRange
                Computations.FirstHourRange(prevMinuteBars, atr20),                       // PrevRthFirstHourRange
                Computations.LastHourRange(prevMinuteBars, atr20),                        // PrevRthLastHourRange
                Computations.RthLowTimeRatio(prevMinuteBars),                             // PrevRthLowTimeRatio
                Computations.RthOpenToHighRatio(prevMinuteBars, atr20),                   // PrevRthOpenToHighRatio
                Computations.RthOpenToLowRatio(prevMinuteBars, atr20),                    // PrevRthOpenToLowRatio
                Computations.LastHourClosePosition(prevMinuteBars),                       // PrevRthLastHourClosePosition
            ];
            var scaled = tradeDayLowEval.Scale([.. tradeDayShared, .. tail]);
            var predicted = tradeDayOpen - tradeDayLowEval.RunInference(scaled) * atr20;
            var actual = rthBars.Min(b => b.Low);
            output["mes.trade-day-bar.low"] = new SessionResult(
                sessionDate,
                predicted,
                actual,
                predictedMove: tradeDayOpen - predicted,
                actualMove: tradeDayOpen - actual,
                atr20);
        }

        return output.Count > 0 ? output : null;
    }

    private async Task<List<BarData>> loadRecentDailyBars(IBarRepository repo, string sessionDate, int lookbackDays)
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

    private void logHeader()
    {
        System.Console.WriteLine(
            $"\n{"Session",-10} {"Model",-22}  {"PredMove",9}  {"ActMove",9}  {"Predicted",10}  {"Actual",10}  {"Error",8}  {"ATR",8}  {"Err/ATR",8}");
        System.Console.WriteLine(new string('-', 109));
    }

    private void logMonthHeader(string month)
    {
        var label = DateTime.ParseExact(month, "yyyyMM", null).ToString("MMMM yyyy");
        System.Console.WriteLine($"\n=== {label} ===");
    }

    private void logSkipped(int skipped)
    {
        System.Console.WriteLine($"\n=== Totals  (skipped: {skipped}) ===");
    }

    private void logSession(SessionResult r, ModelConfig config, string sessionDate, bool first)
    {
        var error = r.Predicted - r.Actual;
        var errAtr = Operations.SafeDiv(error, r.Atr20);
        var dateCol = first ? sessionDate : new string(' ', 8);
        System.Console.WriteLine(
            $"{dateCol,-10} {config.Name,-22}  {r.PredictedMove,9:F2}  {r.ActualMove,9:F2}  {r.Predicted,10:F2}  {r.Actual,10:F2}  {error,8:F2}  {r.Atr20,8:F2}  {errAtr,8:F3}");
    }

    private void logStats(string label, List<SessionResult> results)
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
            $"MAE={mae,7:F2} pts | MAE/ATR={Operations.SafeDiv(mae, avgAtr):F3} | " +
            $"W0.5={w05,3}/{n} ({(double)w05/n:P0}) | W1.0={w10,3}/{n} ({(double)w10/n:P0})");
    }
}