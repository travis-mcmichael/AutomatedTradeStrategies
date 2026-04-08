using AutomatedTrading.Console.ApiClient;
using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Config;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Utility;

namespace AutomatedTrading.Console.ApiTesting;

/// <summary>
/// Calls the unified /strategy/session endpoint once per session date, reads all model
/// predictions from the response dictionary, then compares each predicted price to the
/// appropriate actual high or low and reports accuracy metrics.
///
/// Requires the API to be running at the configured base URL.
/// </summary>
public class ApiInferenceTest
{
    private readonly IBarRepository _barRepo;
    private readonly StrategyApiClient _client;

    private static string[] LoadActiveModelKeys() =>
        (ModelConfig.GetConfig()?.Models ?? [])
            .Where(m => m.Active && m.Features.Count > 0)
            .Select(m => m.ModelName)
            .ToArray();

    public ApiInferenceTest(IBarRepository barRepo, StrategyApiClient client)
    {
        _barRepo = barRepo;
        _client = client;
    }

    public async Task RunAsync(string startDate, string endDate)
    {
        var start = DateTime.ParseExact(startDate, "yyyyMMdd", null);
        var end   = DateTime.ParseExact(endDate,   "yyyyMMdd", null);
        var skipped = 0;
        var modelKeys = LoadActiveModelKeys();
        var results = modelKeys.ToDictionary(k => k, _ => new List<SessionResult>());

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
            var rthOpen = rthBars.Count > 0 ? rthBars.First().Open : sessionOpen;

            var resp = await _client.StartSessionAsync(new StrategySessionRequest(sessionDate, sessionOpen));
            if (resp?.IsReady != true) { skipped++; continue; }

            var sessionResults = new List<(string Key, SessionResult Result)>();

            foreach (var modelName in modelKeys)
            {
                if (!resp.Predictions.TryGetValue(modelName, out var predictedPrice)) continue;

                var isTradeDay = modelName.Contains("trade-day");
                var isHigh     = modelName.EndsWith(".high");
                var refOpen    = isTradeDay ? rthOpen : sessionOpen;
                var barsForActual = isTradeDay ? rthBars : ordered;
                if (barsForActual.Count == 0) continue;

                var actual = isHigh
                    ? barsForActual.Max(b => b.High)
                    : barsForActual.Min(b => b.Low);

                // Moves expressed as signed distance from reference open:
                //   high models: positive = above open
                //   low  models: positive = below open (negated so magnitude is comparable)
                var predictedMove = isHigh
                    ? predictedPrice - refOpen
                    : refOpen - predictedPrice;
                var actualMove = isHigh
                    ? actual - refOpen
                    : refOpen - actual;

                sessionResults.Add((modelName, new SessionResult(
                    sessionDate, predictedPrice, actual,
                    predictedMove, actualMove,
                    resp.DailyAtr)));
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
            foreach (var key in modelKeys)
            {
                var monthResults = results[key].Where(r => r.SessionDate[..6] == month).ToList();
                if (monthResults.Count == 0) continue;
                logStats($"  {key,-35}", monthResults);
            }
        }

        logSkipped(skipped);
        foreach (var key in modelKeys)
        {
            if (results[key].Count == 0) continue;
            logStats($"  {key,-35}", results[key]);
        }
    }

    // ── Logging ───────────────────────────────────────────────────────────────

    private static void logHeader()
    {
        System.Console.WriteLine(
            $"\n{"Session",-10}{"Model",-38}{"PredMove",9}{"ActMove",9}{"Predicted",10}{"Actual",10}{"Error",8}{"ATR",8}{"Err/ATR",8}");
        System.Console.WriteLine(new string('-', 120));
    }

    private static void logMonthHeader(string month) =>
        System.Console.WriteLine($"\n=== {DateTime.ParseExact(month, "yyyyMM", null):MMMM yyyy} ===");

    private static void logSkipped(int skipped) =>
        System.Console.WriteLine($"\n=== Totals  (skipped: {skipped}) ===");

    private static void logSession(SessionResult r, string modelName, string sessionDate, bool first)
    {
        var error  = r.Predicted - r.Actual;
        var errAtr = Operations.SafeDiv(error, r.Atr20);
        var dateCol = first ? sessionDate : new string(' ', 8);
        System.Console.WriteLine(
            $"{dateCol,-10}{modelName,-38}{r.PredictedMove,9:F2}{r.ActualMove,9:F2}{r.Predicted,10:F2}{r.Actual,10:F2}{error,8:F2}{r.Atr20,8:F2}{errAtr,8:F3}");
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
            $"{label.Trim(),-38}  n={n,3} | AvgPredMove={avgPredMove,7:F2}  AvgActMove={avgActMove,7:F2} | " +
            $"MAE={mae,7:F2} pts | MAE/ATR={Operations.SafeDiv(mae, avgAtr):F3} | " +
            $"W0.5={w05,3}/{n} ({(double)w05 / n,4:P0}) | W1.0={w10,3}/{n} ({(double)w10 / n,4:P0})");
    }
}
