using AutomatedTrading.Console.ApiClient;
using AutomatedTrading.Core.Api.Models;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Framework.Logging;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Console.ApiTesting;

public enum ApiStrategy { SessionHigh, SessionLow, TradeDayHigh, TradeDayLow }

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
/// Use <see cref="RunBatchAsync"/> to sweep multiple parameter combinations
/// sequentially against the same strategy endpoint.
///
/// Requires the API to be running at the configured base URL.
/// </summary>
public class ApiBacktestRunner
{
    private readonly IBarRepository _barRepo;
    private readonly StrategyApiClient _client;
    private readonly ApiStrategy _strategy;

    public ApiBacktestRunner(IBarRepository barRepo, StrategyApiClient client, ApiStrategy strategy)
    {
        _barRepo = barRepo;
        _client = client;
        _strategy = strategy;
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
        var end = DateTime.ParseExact(endDate,   "yyyyMMdd", null);

        for (var date = start; date <= end; date = TradingCalendar.GetNextSessionDate(date))
        {
            var sessionDate = date.ToString("yyyyMMdd");
            var bars = await _barRepo.GetBarsBySessionDateAsync(sessionDate);
            if (bars.Count == 0) continue;

            var ordered = bars.OrderBy(b => b.Timestamp).ToList();

            var sessionOpen = getSessionOpen(ordered);
            if (sessionOpen is null) continue;

            var (isReady, predicted) = await startSessionAsync(sessionDate, sessionOpen.Value, p);
            if (!isReady)
            {
                Logger.Info($"{sessionDate}: not ready — skipping");
                continue;
            }

            Logger.Info($"{sessionDate}: prediction={predicted:F2}");

            var barsToEvaluate = getBarsToEvaluate(ordered);
            foreach (var bar in barsToEvaluate)
            {
                if (isStopTriggered(bar, run.Position, run.StopPrice))
                    closePosition(run, bar.Timestamp, sessionDate, run.StopPrice, "Stop");

                var (signal, stopPrice) = await evaluateBarAsync(bar, run.Position, run.EntryPrice);

                switch (signal)
                {
                    case "EnterShort":
                        run.Position = PositionSide.Short;
                        run.EntryPrice = bar.Close;
                        run.EntryTime = bar.Timestamp;
                        run.StopPrice = stopPrice;
                        Logger.Info($"{bar.SessionDate, 9}{bar.SessionTime, 7} - {signal, 10} @{bar.Close, 10:f2}, stop @{stopPrice, 10:f2}, prediction @{predicted, 10:f2}");
                        break;
                    case "EnterLong":
                        run.Position = PositionSide.Long;
                        run.EntryPrice = bar.Close;
                        run.EntryTime = bar.Timestamp;
                        run.StopPrice = stopPrice;
                        Logger.Info($"{bar.SessionDate, 9}{bar.SessionTime, 7} - {signal, 10} @{bar.Close, 10:f2}, stop @{stopPrice, 10:f2}, prediction @{predicted, 10:f2}");
                        break;
                    case "ExitLong" when run.Position == PositionSide.Long:
                        closePosition(run, bar.Timestamp, sessionDate, bar.Close, "ExitLong");
                        Logger.Info($"{bar.SessionDate, 9}{bar.SessionTime, 7} - {signal, 10} @{bar.Close, 10:f2}, prediction @{predicted, 10:F2}");
                        break;
                    case "ExitShort" when run.Position == PositionSide.Short:
                        closePosition(run, bar.Timestamp, sessionDate, bar.Close, "ExitShort");
                        Logger.Info($"{bar.SessionDate, 9}{bar.SessionTime, 7} - {signal, 10} @{bar.Close, 10:f2}, prediction {predicted, 10:F2}");
                        break;
                    case "UpdateStop":
                        run.StopPrice = stopPrice;
                        Logger.Info($"{bar.SessionDate} {bar.SessionTime} - {signal, 10} @{bar.Close}, new stop {stopPrice:F2}");
                        break;
                }
            }

            if (run.Position != PositionSide.Flat)
            {
                var last = barsToEvaluate[^1];
                closePosition(run, last.Timestamp, sessionDate, last.Close, "SessionClose");
            }
        }

        return run;
    }

    // ── Strategy-specific helpers ─────────────────────────────────────────────

    private double? getSessionOpen(List<BarData> ordered) => _strategy switch
    {
        ApiStrategy.SessionHigh or ApiStrategy.SessionLow => ordered.First().Open,
        ApiStrategy.TradeDayHigh or ApiStrategy.TradeDayLow =>
            ordered.FirstOrDefault(b =>
                b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
                b.Timestamp.TimeOfDay < AppConstants.RTH_END_TIME)?.Open,
        _ => null,
    };

    private List<BarData> getBarsToEvaluate(List<BarData> ordered) => _strategy switch
    {
        ApiStrategy.TradeDayHigh or ApiStrategy.TradeDayLow => ordered
            .Where(b => b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
                        b.Timestamp.TimeOfDay < AppConstants.RTH_END_TIME)
            .ToList(),
        _ => ordered,
    };

    private bool isStopTriggered(BarData bar, PositionSide position, double stopPrice) =>
        _strategy switch
        {
            ApiStrategy.SessionHigh or ApiStrategy.TradeDayHigh => position == PositionSide.Short && bar.High >= stopPrice,
            ApiStrategy.SessionLow or ApiStrategy.TradeDayLow  =>position == PositionSide.Long  && bar.Low  <= stopPrice,
            _ => false,
        };

    private async Task<(bool IsReady, double Predicted)> startSessionAsync(
        string sessionDate, double sessionOpen, ApiBacktestParams p)
    {
        return _strategy switch
        {
            ApiStrategy.SessionHigh => await startSessionHigh(sessionDate, sessionOpen, p),
            ApiStrategy.SessionLow => await startSessionLow(sessionDate, sessionOpen, p),
            ApiStrategy.TradeDayHigh => await startTradeDayHigh(sessionDate, sessionOpen, p),
            ApiStrategy.TradeDayLow => await startTradeDayLow(sessionDate, sessionOpen, p),
            _ => (false, 0),
        };
    }

    private async Task<(bool, double)> startSessionHigh(string date, double open, ApiBacktestParams p)
    {
        var r = await _client.StartSessionHighAsync(
            new SessionHighSessionRequest(date, open, p.Proximity, p.StopLoss, p.TargetProfit, p.Trail));
        return r is null ? (false, 0) : (r.IsReady, r.PredictedHigh);
    }

    private async Task<(bool, double)> startSessionLow(string date, double open, ApiBacktestParams p)
    {
        var r = await _client.StartSessionLowAsync(
            new SessionLowSessionRequest(date, open, p.Proximity, p.StopLoss, p.TargetProfit, p.Trail));
        return r is null ? (false, 0) : (r.IsReady, r.PredictedLow);
    }

    private async Task<(bool, double)> startTradeDayHigh(string date, double open, ApiBacktestParams p)
    {
        var r = await _client.StartTradeDayHighAsync(
            new TradeDayHighSessionRequest(date, open, p.Proximity, p.StopLoss, p.TargetProfit, p.Trail));
        return r is null ? (false, 0) : (r.IsReady, r.PredictedHigh);
    }

    private async Task<(bool, double)> startTradeDayLow(string date, double open, ApiBacktestParams p)
    {
        var r = await _client.StartTradeDayLowAsync(
            new TradeDayLowSessionRequest(date, open, p.Proximity, p.StopLoss, p.TargetProfit, p.Trail));
        return r is null ? (false, 0) : (r.IsReady, r.PredictedLow);
    }

    private async Task<(string Signal, double StopPrice)> evaluateBarAsync(
        BarData bar, PositionSide position, double entryPrice)
    {
        var pos = position.ToString();
        return _strategy switch
        {
            ApiStrategy.SessionHigh => await evalSessionHigh(bar, pos, entryPrice),
            ApiStrategy.SessionLow => await evalSessionLow(bar, pos, entryPrice),
            ApiStrategy.TradeDayHigh => await evalTradeDayHigh(bar, pos, entryPrice),
            ApiStrategy.TradeDayLow => await evalTradeDayLow(bar, pos, entryPrice),
            _ => ("None", 0),
        };
    }

    private async Task<(string, double)> evalSessionHigh(BarData bar, string pos, double entry)
    {
        var r = await _client.EvaluateSessionHighBarAsync(new SessionHighBarRequest(bar.High, bar.Close, pos, entry));
        return r is null ? ("None", 0) : (r.Signal, r.StopPrice);
    }

    private async Task<(string, double)> evalSessionLow(BarData bar, string pos, double entry)
    {
        var r = await _client.EvaluateSessionLowBarAsync(new SessionLowBarRequest(bar.Low, bar.Close, pos, entry));
        return r is null ? ("None", 0) : (r.Signal, r.StopPrice);
    }

    private async Task<(string, double)> evalTradeDayHigh(BarData bar, string pos, double entry)
    {
        var r = await _client.EvaluateTradeDayHighBarAsync(new TradeDayHighBarRequest(bar.High, bar.Close, pos, entry));
        return r is null ? ("None", 0) : (r.Signal, r.StopPrice);
    }

    private async Task<(string, double)> evalTradeDayLow(BarData bar, string pos, double entry)
    {
        var r = await _client.EvaluateTradeDayLowBarAsync(new TradeDayLowBarRequest(bar.Low, bar.Close, pos, entry));
        return r is null ? ("None", 0) : (r.Signal, r.StopPrice);
    }

    // ── Trade recording ───────────────────────────────────────────────────────

    private static void closePosition(
        ApiBacktestRun run, DateTime exitTime, string sessionDate, double exitPrice, string reason)
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
    }
}
