using AutomatedTrading.Console.ApiClient;
using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Utility;

namespace AutomatedTrading.Console.ApiTesting;

/// <summary>
/// Runs inference for all four strategies against the live API across a date range,
/// then compares predictions to actuals and reports the same metrics as
/// <see cref="InferenceTesting.InferenceEvaluationTest"/>.
///
/// Requires the API to be running at the configured base URL.
/// </summary>
public class ApiInferenceTest
{
    private readonly IBarRepository _barRepo;
    private readonly StrategyApiClient _client;

    private static readonly string[] ModelKeys =
    [
        "mes.session-day-bar.high",
        "mes.session-day-bar.low",
        "mes.trade-day-bar.high",
        "mes.trade-day-bar.low",
    ];

    public ApiInferenceTest(IBarRepository barRepo, StrategyApiClient client)
    {
        _barRepo = barRepo;
        _client = client;
    }

    public async Task RunAsync(string startDate, string endDate)
    {
        var start = DateTime.ParseExact(startDate, "yyyyMMdd", null);
        var end = DateTime.ParseExact(endDate, "yyyyMMdd", null);
        var skipped = 0;
        var results = ModelKeys.ToDictionary(k => k, _ => new List<SessionResult>());

        logHeader();

        for (var date = start; date <= end; date = TradingCalendar.GetNextSessionDate(date))
        {
            var sessionDate = date.ToString("yyyyMMdd");
            var bars = await _barRepo.GetBarsBySessionDateAsync(sessionDate);
            if (bars.Count == 0) { skipped++; continue; }

            var ordered = bars.OrderBy(b => b.Timestamp).ToList();
            var sessionOpen = ordered.First().Open;
            var rthBars = ordered
                .Where(b => b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
                    b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME)
                .ToList();

            var sessionResults = new List<(string Key, SessionResult Result)>();

            // ── Session-day high ──────────────────────────────────────────────
            var shResp = await _client.StartSessionHighAsync(
                new SessionHighSessionRequest(sessionDate, sessionOpen));
            if (shResp?.IsReady == true)
            {
                var actual = ordered.Max(b => b.High);
                sessionResults.Add(("mes.session-day-bar.high", new SessionResult(
                    sessionDate, shResp.PredictedHigh, actual,
                    predictedMove: shResp.PredictedHigh - sessionOpen,
                    actualMove: actual - sessionOpen,
                    atr20: shResp.DailyAtr)));
            }

            // ── Session-day low ───────────────────────────────────────────────
            var slResp = await _client.StartSessionLowAsync(
                new SessionLowSessionRequest(sessionDate, sessionOpen));
            if (slResp?.IsReady == true)
            {
                var actual = ordered.Min(b => b.Low);
                sessionResults.Add(("mes.session-day-bar.low", new SessionResult(
                    sessionDate, slResp.PredictedLow, actual,
                    predictedMove: sessionOpen - slResp.PredictedLow,
                    actualMove: sessionOpen - actual,
                    atr20: slResp.DailyAtr)));
            }

            // ── Trade-day high & low ──────────────────────────────────────────
            if (rthBars.Count > 0)
            {
                var rthOpen = rthBars.First().Open;

                var tdHighResp = await _client.StartTradeDayHighAsync(
                    new TradeDayHighSessionRequest(sessionDate, rthOpen));
                if (tdHighResp?.IsReady == true)
                {
                    var actual = rthBars.Max(b => b.High);
                    sessionResults.Add(("mes.trade-day-bar.high", new SessionResult(
                        sessionDate, tdHighResp.PredictedHigh, actual,
                        predictedMove: tdHighResp.PredictedHigh - rthOpen,
                        actualMove: actual - rthOpen,
                        atr20: tdHighResp.DailyAtr)));
                }

                var tdLowResp = await _client.StartTradeDayLowAsync(
                    new TradeDayLowSessionRequest(sessionDate, rthOpen));
                if (tdLowResp?.IsReady == true)
                {
                    var actual = rthBars.Min(b => b.Low);
                    sessionResults.Add(("mes.trade-day-bar.low", new SessionResult(
                        sessionDate, tdLowResp.PredictedLow, actual,
                        predictedMove: rthOpen - tdLowResp.PredictedLow,
                        actualMove: rthOpen - actual,
                        atr20: tdLowResp.DailyAtr)));
                }
            }

            if (sessionResults.Count == 0) { skipped++; continue; }

            var first = true;
            foreach (var (key, r) in sessionResults)
            {
                logSession(r, key, sessionDate, first);
                results[key].Add(r);
                first = false;
            }
        }

        // ── Monthly summaries ─────────────────────────────────────────────────
        var allMonths = results.Values
            .SelectMany(r => r.Select(x => x.SessionDate[..6]))
            .Distinct().OrderBy(m => m).ToList();

        foreach (var month in allMonths)
        {
            logMonthHeader(month);
            foreach (var key in ModelKeys)
            {
                var monthResults = results[key].Where(r => r.SessionDate[..6] == month).ToList();
                if (monthResults.Count == 0) continue;
                logStats($"  {key,-22}", monthResults);
            }
        }

        logSkipped(skipped);
        foreach (var key in ModelKeys)
        {
            if (results[key].Count == 0) continue;
            logStats($"  {key,-22}", results[key]);
        }
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    private static void logHeader()
    {
        System.Console.WriteLine(
            $"\n{"Session", -10}{"Model", -30}{"PredMove", 9}{"ActMove", 9}{"Predicted", 10}{"Actual", 10}{"Error", 8}{"ATR", 8}{"Err/ATR", 8}");
        System.Console.WriteLine(new string('-', 117));
    }

    private static void logMonthHeader(string month) =>
        System.Console.WriteLine($"\n=== {DateTime.ParseExact(month, "yyyyMM", null):MMMM yyyy} ===");

    private static void logSkipped(int skipped) =>
        System.Console.WriteLine($"\n=== Totals  (skipped: {skipped}) ===");

    private static void logSession(SessionResult r, string modelName, string sessionDate, bool first)
    {
        var error = r.Predicted - r.Actual;
        var errAtr = Operations.SafeDiv(error, r.Atr20);
        var dateCol = first ? sessionDate : new string(' ', 8);
        System.Console.WriteLine(
            $"{dateCol, -10}{modelName, -30}{r.PredictedMove, 9:F2}{r.ActualMove, 9:F2}{r.Predicted, 10:F2}{r.Actual, 10:F2}{error, 8:F2}{r.Atr20, 8:F2}{errAtr, 8:F3}");
    }

    private static void logStats(string label, List<SessionResult> results)
    {
        var errors = results.Select(r => Math.Abs(r.Predicted - r.Actual)).ToList();
        var avgAtr = results.Average(r => r.Atr20);
        var mae = errors.Average();
        var avgPredMove = results.Average(r => r.PredictedMove);
        var avgActMove  = results.Average(r => r.ActualMove);
        var w05 = errors.Count(e => e < avgAtr * 0.5);
        var w10 = errors.Count(e => e < avgAtr);
        var n = results.Count;
        System.Console.WriteLine(
            $"{label.Trim(),-30}  n={n,3} | AvgPredMove={avgPredMove,7:F2}  AvgActMove={avgActMove,7:F2} | " +
            $"MAE={mae,7:F2} pts | MAE/ATR={Operations.SafeDiv(mae, avgAtr):F3} | " +
            $"W0.5={w05,3}/{n} ({(double)w05 / n,4:P0}) | W1.0={w10,3}/{n} ({(double)w10 / n,4:P0})");
    }
}
