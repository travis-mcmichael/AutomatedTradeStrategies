using AutomatedTrading.Core.Framework.Config;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Framework.ML;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Strategies;

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
    private readonly AutomatedTradeClient _client;

    private static ModelEntry[] LoadActiveModels() =>
        (ModelConfig.GetConfig()?.Models ?? [])
            .Where(m => m.Active && m.Features.Count > 0 && m.Target != "CLASS")
            .ToArray();

    public ApiInferenceTest(IBarRepository barRepo, AutomatedTradeClient client)
    {
        _barRepo = barRepo;
        _client  = client;
    }

    public async Task RunAsync(string startDate, string endDate)
    {
        var start   = DateTime.ParseExact(startDate, "yyyyMMdd", null);
        var end     = DateTime.ParseExact(endDate,   "yyyyMMdd", null);
        var skipped = 0;
        var models  = LoadActiveModels();
        var results = models.ToDictionary(m => m.ModelName, _ => new List<SessionResult>());

        logHeader();

        for (var date = start; date <= end; date = TradingCalendar.GetNextSessionDate(date))
        {
            var sessionDate = date.ToString("yyyyMMdd");
            var bars = await _barRepo.GetBarsBySessionDateAsync(sessionDate);
            if (bars.Count == 0) { skipped++; continue; }

            var ordered     = bars.OrderBy(b => b.Timestamp).ToList();
            var sessionOpen = ordered.First().Open;

            var resp = await _client.StartSession(date, sessionOpen);
            if (resp?.IsReady != true) { skipped++; continue; }

            var sessionResults = new List<(string Key, SessionResult Result)>();

            foreach (var model in models)
            {
                if (!resp.Predictions.TryGetValue(model.ModelName, out var predictedPrice)) continue;

                var isHigh = model.Target == "HIGH";

                List<BarData> barsForActual;
                double refOpen;

                if (model.BarType == "window" && model.WindowStart != null && model.WindowEnd != null)
                {
                    var winStart  = parseWindowTime(model.WindowStart);
                    var winEnd    = parseWindowTime(model.WindowEnd);
                    barsForActual = ordered
                        .Where(b => inWindow(b.Timestamp.TimeOfDay, winStart, winEnd))
                        .ToList();
                    refOpen = barsForActual.Count > 0 ? barsForActual.First().Open : sessionOpen;
                }
                else
                {
                    barsForActual = ordered;
                    refOpen       = sessionOpen;
                }

                if (barsForActual.Count == 0) continue;

                var actual = isHigh
                    ? barsForActual.Max(b => b.High)
                    : barsForActual.Min(b => b.Low);

                // Moves expressed as signed distance from the window open:
                //   high models: positive = above open
                //   low  models: positive = below open (negated so magnitude is comparable)
                var predictedMove = isHigh ? predictedPrice - refOpen : refOpen - predictedPrice;
                var actualMove    = isHigh ? actual - refOpen         : refOpen - actual;

                sessionResults.Add((model.ModelName, new SessionResult(
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
            foreach (var model in models)
            {
                var monthResults = results[model.ModelName].Where(r => r.SessionDate[..6] == month).ToList();
                if (monthResults.Count == 0) continue;
                logStats($"  {model.ModelName,-35}", monthResults);
            }
        }

        logSkipped(skipped);
        foreach (var model in models)
        {
            if (results[model.ModelName].Count == 0) continue;
            logStats($"  {model.ModelName,-35}", results[model.ModelName]);
        }
    }

    // Parses a window boundary string ("83000", "160000") into a TimeSpan.
    private static TimeSpan parseWindowTime(string t)
    {
        var s = t.PadLeft(6, '0');
        return new TimeSpan(int.Parse(s[..2]), int.Parse(s[2..4]), int.Parse(s[4..6]));
    }

    // Handles straight windows (08:30–16:00) and cross-midnight windows (17:00–08:30).
    private static bool inWindow(TimeSpan t, TimeSpan start, TimeSpan end) =>
        start <= end ? t >= start && t < end
                     : t >= start || t < end;

    // ── Logging ───────────────────────────────────────────────────────────────

    private static void logHeader()
    {
        Logger.Info(
            $"\n{"Session",-10}{"Model",-38}{"PredMove",9}{"ActMove",9}{"Predicted",10}{"Actual",10}{"Error",8}{"ATR",8}{"Err/ATR",8}");
        Logger.Info(new string('-', 120));
    }

    private static void logMonthHeader(string month) =>
        Logger.Info($"\n=== {DateTime.ParseExact(month, "yyyyMM", null):MMMM yyyy} ===");

    private static void logSkipped(int skipped) =>
        Logger.Info($"\n=== Totals  (skipped: {skipped}) ===");

    private static void logSession(SessionResult r, string modelName, string sessionDate, bool first)
    {
        var error  = r.Predicted - r.Actual;
        var errAtr = Operations.SafeDiv(error, r.Atr20);
        var dateCol = first ? sessionDate : new string(' ', 8);
        Logger.Info(
            $"{dateCol,-10}{modelName,-38}{r.PredictedMove,9:F2}{r.ActualMove,9:F2}{r.Predicted,10:F2}{r.Actual,10:F2}{error,8:F2}{r.Atr20,8:F2}{errAtr,8:F3}");
    }

    private static void logStats(string label, List<SessionResult> results)
    {
        var errors      = results.Select(r => Math.Abs(r.Predicted - r.Actual)).ToList();
        var avgAtr      = results.Average(r => r.Atr20);
        var mae         = errors.Average();
        var avgPredMove = results.Average(r => r.PredictedMove);
        var avgActMove  = results.Average(r => r.ActualMove);
        var w05 = errors.Count(e => e < avgAtr * 0.5);
        var w10 = errors.Count(e => e < avgAtr);
        var n   = results.Count;
        Logger.Info(
            $"{label.Trim(),-38}  n={n,3} | AvgPredMove={avgPredMove,7:F2}  AvgActMove={avgActMove,7:F2} | " +
            $"MAE={mae,7:F2} pts | MAE/ATR={Operations.SafeDiv(mae, avgAtr):F3} | " +
            $"W0.5={w05,3}/{n} ({(double)w05 / n,4:P0}) | W1.0={w10,3}/{n} ({(double)w10 / n,4:P0})");
    }
}
