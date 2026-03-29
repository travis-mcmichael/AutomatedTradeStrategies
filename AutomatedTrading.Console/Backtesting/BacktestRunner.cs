using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Framework.Enums;

namespace AutomatedTrading.Console.Backtesting;

/// <summary>
/// Loops through a calendar range of sessions, feeds each bar to every
/// <see cref="BacktestRun"/> in the provided list, and records trades.
///
/// The <paramref name="predict"/> delegate is called once per session and its
/// result shared across all runs, so a parameter sweep does not multiply
/// inference calls. The delegate receives the ordered bar list so it can
/// extract whichever open price the model requires (session open vs RTH open).
///
/// Usage — session-high strategy, two parameter combinations:
/// <code>
///   var runs = new[]
///   {
///       new BacktestRun("Prox0.3", new HighBacktestEvaluator(
///           new SessionHighStrategyEvaluator(0.3, 5, 20, 10).StartSession,
///           new SessionHighStrategyEvaluator(0.3, 5, 20, 10).Evaluate)),
///       new BacktestRun("Prox0.5", new HighBacktestEvaluator(...)),
///   };
///
///   var runner = new BacktestRunner(barRepo, async (date, bars) =>
///   {
///       var p = await predictor.PredictHighAsync(date, bars[0].Open);
///       return p?.PredictedHigh;
///   });
///
///   await runner.RunAsync("20260101", "20260331", runs);
/// </code>
/// </summary>
public class BacktestRunner
{
    private readonly IBarRepository _barRepo;
    private readonly Func<string, IReadOnlyList<BarData>, Task<double?>> _predict;

    /// <param name="predict">
    /// Async delegate that accepts the session date and the full ordered bar list,
    /// and returns the predicted price level (e.g. predicted high or predicted low)
    /// or <c>null</c> if no prediction is available for that session.
    /// </param>
    public BacktestRunner(IBarRepository barRepo, Func<string, IReadOnlyList<BarData>, Task<double?>> predict)
    {
        _barRepo = barRepo;
        _predict = predict;
    }

    /// <summary>
    /// Runs all <paramref name="runs"/> across every trading session between
    /// <paramref name="startDate"/> and <paramref name="endDate"/> (inclusive, "yyyyMMdd").
    /// </summary>
    /// <param name="log">Optional progress sink — receives one line per session.</param>
    public async Task RunAsync(
        string startDate,
        string endDate,
        IReadOnlyList<BacktestRun> runs,
        Action<string>? log = null)
    {
        var start = DateTime.ParseExact(startDate, "yyyyMMdd", null);
        var end   = DateTime.ParseExact(endDate,   "yyyyMMdd", null);

        for (var date = start; date <= end; date = TradingCalendar.GetNextSessionDate(date))
        {
            var sessionDate = date.ToString("yyyyMMdd");
            var bars = await _barRepo.GetBarsBySessionDateAsync(sessionDate);
            if (bars.Count == 0) continue;

            var ordered = bars.OrderBy(b => b.Timestamp).ToList();

            // ── Prediction (computed once, shared across all runs) ────────────
            var prediction = await _predict(sessionDate, ordered);
            if (prediction is null)
            {
                log?.Invoke($"{sessionDate}: no prediction — skipping");
                continue;
            }

            var dailyAtr = await loadDailyAtrAsync(sessionDate);
            log?.Invoke($"{sessionDate}: prediction={prediction:F2}  dailyAtr={dailyAtr:F2}");

            // ── Arm every evaluator for this session ──────────────────────────
            foreach (var run in runs)
                run.Evaluator.StartSession(prediction.Value, dailyAtr);

            // ── Bar-by-bar simulation ─────────────────────────────────────────
            foreach (var bar in ordered)
            {
                foreach (var run in runs)
                {
                    // 1. Check stop before evaluating the bar.
                    if (run.Evaluator.IsStopTriggered(bar, run.Position, run.StopPrice))
                        closePosition(run, bar.Timestamp, sessionDate, run.StopPrice, "Stop");

                    // 2. Evaluate the bar and act on the returned signal.
                    var eval = run.Evaluator.Evaluate(bar, run.Position, run.EntryPrice);

                    switch (eval.Signal)
                    {
                        case StrategySignal.EnterShort:
                            run.Position = PositionSide.Short;
                            run.EntryPrice = bar.Close;
                            run.EntryTime = bar.Timestamp;
                            run.StopPrice = eval.StopPrice;
                            System.Console.WriteLine($"{bar.SessionDate} {bar.SessionTime} - EnterShort @{bar.Close}  stop {eval.StopPrice:F2}  prediction {prediction:F2}");
                            break;
                        case StrategySignal.EnterLong:
                            run.Position = PositionSide.Long;
                            run.EntryPrice = bar.Close;
                            run.EntryTime = bar.Timestamp;
                            run.StopPrice = eval.StopPrice;
                            System.Console.WriteLine($"{bar.SessionDate} {bar.SessionTime} - EnterLong  @{bar.Close}  stop {eval.StopPrice:F2}  prediction {prediction:F2}");
                            break;
                        case StrategySignal.ExitLong when run.Position == PositionSide.Long:
                            closePosition(run, bar.Timestamp, sessionDate, bar.Close, "ExitLong");
                            System.Console.WriteLine($"{bar.SessionDate} {bar.SessionTime} - ExitLong   @{bar.Close}  prediction {prediction:F2}");
                            break;
                        case StrategySignal.ExitShort when run.Position == PositionSide.Short:
                            closePosition(run, bar.Timestamp, sessionDate, bar.Close, "ExitShort");
                            System.Console.WriteLine($"{bar.SessionDate} {bar.SessionTime} - ExitShort  @{bar.Close}  prediction {prediction:F2}");
                            break;
                        case StrategySignal.UpdateStop:
                            run.StopPrice = eval.StopPrice;
                            System.Console.WriteLine($"{bar.SessionDate} {bar.SessionTime} - UpdateStop @{bar.Close}  new stop {eval.StopPrice:F2}");
                            break;
                    }
                }
            }

            // ── Force-close any open positions at session end ─────────────────
            var lastBar = ordered[^1];
            foreach (var run in runs)
            {
                if (run.Position != PositionSide.Flat)
                    closePosition(run, lastBar.Timestamp, sessionDate, lastBar.Close, "SessionClose");
            }
        }
    }

    private static void closePosition(
        BacktestRun run,
        DateTime exitTime,
        string sessionDate,
        double exitPrice,
        string exitReason)
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
            ExitReason: exitReason));

        run.Position = PositionSide.Flat;
        run.EntryPrice = 0;
        run.StopPrice = double.MaxValue;
    }

    /// <summary>
    /// Walks back through calendar days collecting up to 20 trading sessions
    /// and returns ATR(20) on the aggregated daily bars.
    /// </summary>
    private async Task<double> loadDailyAtrAsync(string sessionDate)
    {
        var dailyBars = new List<BarData>();
        var date = DateTime.ParseExact(sessionDate, "yyyyMMdd", null);

        for (var i = 1; dailyBars.Count < 20 && i <= 60; i++)
        {
            var d = date.AddDays(-i).ToString("yyyyMMdd");
            var bars = await _barRepo.GetBarsBySessionDateAsync(d);
            if (bars.Count == 0) continue;

            var ordered = bars.OrderBy(b => b.Timestamp).ToList();
            dailyBars.Insert(0, new BarData
            {
                Open = ordered[0].Open,
                High = ordered.Max(b => b.High),
                Low = ordered.Min(b => b.Low),
                Close = ordered[^1].Close,
            });
        }

        if (dailyBars.Count < 2) return 0;

        var lookback = Math.Min(20, dailyBars.Count - 1);
        return Computations.ATR(dailyBars, dailyBars.Count - 1, lookback);
    }
}
