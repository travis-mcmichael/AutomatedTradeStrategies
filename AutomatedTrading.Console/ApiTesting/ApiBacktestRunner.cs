using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Strategies;
using AutomatedTrading.Core.Data.Queries;

namespace AutomatedTrading.Console.ApiTesting;

public record ApiBacktestParams(
    string Name,
    double Proximity = 0.5,
    double StopLoss = 5.0,
    double TargetProfit = 20.0,
    double Trail = 10.0);

/// <summary>
/// Simulates the NinjaTrader flow against the live API:
/// calls the session endpoint once per session, then the bar endpoint for every bar,
/// and tracks positions and trade P&amp;L locally.
///
/// Each parameter set in <see cref="RunBatchAsync"/> gets its own
/// <see cref="AutomatedTradeClient"/> instance so proximity/stop values are
/// correctly scoped to that run.
///
/// Requires the API to be running at the configured base URL.
/// </summary>
public class ApiBacktestRunner
{
    private readonly GetBarsBySessionDate _getBars;

    public ApiBacktestRunner(GetBarsBySessionDate getBars)
    {
        _getBars = getBars;
    }

    // ── Batch entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// Runs each parameter set sequentially across the date range and returns
    /// one <see cref="ApiBacktestRun"/> per set.
    /// </summary>
    public async Task<List<ApiBacktestRun>> RunBatchAsync(
        string startDate,
        string endDate,
        IReadOnlyList<ApiBacktestParams> paramSets)
    {
        var results = new List<ApiBacktestRun>();
        foreach (var p in paramSets)
            results.Add(await RunAsync(startDate, endDate, p));
        return results;
    }

    // ── Single run ────────────────────────────────────────────────────────────

    public async Task<ApiBacktestRun> RunAsync(
        string startDate,
        string endDate,
        ApiBacktestParams p)
    {
        var run = new ApiBacktestRun(p.Name);
        var start = DateTime.ParseExact(startDate, "yyyyMMdd", null);
        var end = DateTime.ParseExact(endDate, "yyyyMMdd", null);

        using var client = new AutomatedTradeClient(p.Name, p.Proximity, p.StopLoss, p.TargetProfit, p.Trail);

        for (var date = start; date <= end; date = TradingCalendar.GetNextSessionDate(date))
        {
            var sessionDate = date.ToString("yyyyMMdd");
            var bars = await _getBars.Execute(date);
            if (bars.Count == 0) continue;

            var ordered = bars.OrderBy(b => b.Timestamp).ToList();

            var sessionOpen = ordered.FirstOrDefault()?.Open;
            if (sessionOpen is null) continue;

            var sessionResponse = await client.StartSession(date, sessionOpen.Value);
            if (sessionResponse is null)
            {
                Logger.Info($"{sessionDate}: session call returned null — is the API running?");
                continue;
            }
            if (!sessionResponse.IsReady)
            {
                Logger.Info($"{sessionDate}: evaluator not ready (ATR unavailable) — skipping");
                continue;
            }

            var barsToEvaluate = getBarsToEvaluate(ordered);
            foreach (var bar in barsToEvaluate)
            {
                if (isStopTriggered(bar, run.Position, run.StopPrice))
                    closePosition(run, client, bar.Timestamp, sessionDate, run.StopPrice, "Stop");

                var (signal, stopPrice) = await evaluateBarAsync(client, bar, run.Position, run.EntryPrice);

                switch (signal)
                {
                    case "EnterShort":
                        run.Position = PositionSide.Short;
                        run.EntryPrice = bar.Close;
                        run.EntryTime = bar.Timestamp;
                        run.StopPrice = stopPrice;
                        Logger.Info($"{bar.SessionDate,9}{bar.SessionTime,7} - {signal,10} @{bar.Close,10:f2}, stop @{stopPrice,10:f2}");
                        break;
                    case "EnterLong":
                        run.Position = PositionSide.Long;
                        run.EntryPrice = bar.Close;
                        run.EntryTime = bar.Timestamp;
                        run.StopPrice = stopPrice;
                        Logger.Info($"{bar.SessionDate,9}{bar.SessionTime,7} - {signal,10} @{bar.Close,10:f2}, stop @{stopPrice,10:f2}");
                        break;
                    case "ExitLong" when run.Position == PositionSide.Long:
                        closePosition(run, client, bar.Timestamp, sessionDate, bar.Close, "ExitLong");
                        Logger.Info($"{bar.SessionDate,9}{bar.SessionTime,7} - {signal,10} @{bar.Close,10:f2}");
                        break;
                    case "ExitShort" when run.Position == PositionSide.Short:
                        closePosition(run, client, bar.Timestamp, sessionDate, bar.Close, "ExitShort");
                        Logger.Info($"{bar.SessionDate,9}{bar.SessionTime,7} - {signal,10} @{bar.Close,10:f2}");
                        break;
                    case "UpdateStop":
                        run.StopPrice = stopPrice;
                        Logger.Info($"{bar.SessionDate} {bar.SessionTime} - {signal,10} @{bar.Close}, new stop {stopPrice:F2}");
                        break;
                }
            }

            if (run.Position != PositionSide.Flat)
            {
                var last = barsToEvaluate[^1];
                closePosition(run, client, last.Timestamp, sessionDate, last.Close, "SessionClose");
            }

            logSessionPredictions(sessionDate, sessionResponse);
        }

        return run;
    }

    // ── Strategy-specific helpers ─────────────────────────────────────────────

    private static List<BarData> getBarsToEvaluate(List<BarData> ordered) => ordered;

    private static bool isStopTriggered(BarData bar, PositionSide position, double stopPrice) =>
        position switch
        {
            PositionSide.Long => bar.Low <= stopPrice,
            PositionSide.Short => bar.High >= stopPrice,
            _ => false,
        };

    // ── API call helpers ──────────────────────────────────────────────────────

    private async Task<(string Signal, double StopPrice)> evaluateBarAsync(
        AutomatedTradeClient client, BarData bar, PositionSide position, double entryPrice)
    {
        client.Position = position;
        client.EntryPrice = entryPrice > 0 ? entryPrice : null;
        var r = await client.Evaluate(bar);
        return r is null ? ("None", 0) : (r.Signal ?? "None", r.StopPrice);
    }

    // ── Trade recording ───────────────────────────────────────────────────────

    private static void closePosition(
        ApiBacktestRun run, AutomatedTradeClient client,
        DateTime exitTime, string sessionDate, double exitPrice, string reason)
    {
        var pnl = run.Position == PositionSide.Short
            ? run.EntryPrice - exitPrice
            : exitPrice - run.EntryPrice;

        run.RecordTrade(new TradeResult(
            SessionDate: sessionDate,
            EntryTime: run.EntryTime,
            ExitTime: exitTime,
            Side: run.Position,
            EntryPrice: run.EntryPrice,
            ExitPrice: exitPrice,
            PnL: pnl,
            ExitReason: reason));

        run.Position = PositionSide.Flat;
        run.EntryPrice = 0;
        run.StopPrice = double.MaxValue;

        client.Position = PositionSide.Flat;
        client.EntryPrice = null;
    }

    // ── Logging helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Logs whatever predictions the API has populated for this session.
    /// Called after bar evaluation so lazy predictions have had time to arrive.
    /// </summary>
    private static void logSessionPredictions(string sessionDate, StrategySessionResponse response)
    {
        if (response.Predictions is null || response.Predictions.Count == 0) return;
        foreach (var (modelName, level) in response.Predictions)
            Logger.Info($"  {sessionDate} prediction [{modelName}]: {level:F2}");
    }
}
