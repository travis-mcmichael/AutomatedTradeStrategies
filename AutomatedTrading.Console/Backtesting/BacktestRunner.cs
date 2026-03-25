using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Framework.Enums;
using AutomatedTrading.Core.Strategies;

namespace AutomatedTrading.Console.Backtesting;

/// <summary>
/// Loops through a calendar range of sessions, feeds each bar to every
/// <see cref="BacktestRun"/> in the provided list, and records trades.
///
/// Usage — single strategy, three parameter combinations:
/// <code>
///   var runs = new[]
///   {
///       new BacktestRun("Prox0.3", new PredictedHighStrategyEvaluator(proximity: 0.3, ...)),
///       new BacktestRun("Prox0.5", new PredictedHighStrategyEvaluator(proximity: 0.5, ...)),
///       new BacktestRun("Prox0.7", new PredictedHighStrategyEvaluator(proximity: 0.7, ...)),
///   };
///   await runner.RunAsync("20260101", "20260331", runs);
/// </code>
///
/// The prediction (predicted high + daily ATR) is fetched once per session and
/// injected into all runs, so a parameter sweep does not multiply inference calls.
/// </summary>
public class BacktestRunner
{
    private readonly IBarRepository _barRepo;
    private readonly IBarHighPredictor _predictor;

    public BacktestRunner(IBarRepository barRepo, IBarHighPredictor predictor)
    {
        _barRepo = barRepo;
        _predictor = predictor;
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
        var end = DateTime.ParseExact(endDate, "yyyyMMdd", null);

        for (var date = start; date <= end; date = TradingCalendar.GetNextSessionDate(date))
        {

            var sessionDate = date.ToString("yyyyMMdd");
            var bars = await _barRepo.GetBarsBySessionDateAsync(sessionDate);
            if (bars.Count == 0) continue;

            var ordered = bars.OrderBy(b => b.Timestamp).ToList();
            var sessionOpen = ordered[0].Open;

            // ── Prediction (computed once, shared across all runs) ────────────
            var prediction = await _predictor.PredictAsync(sessionDate, sessionOpen);
            if (prediction is null)
            {
                log?.Invoke($"{sessionDate}: no prediction — skipping");
                continue;
            }

            var dailyAtr = await loadDailyAtrAsync(sessionDate);
            log?.Invoke($"{sessionDate}: predictedHigh={prediction.PredictedHigh:F2}  dailyAtr={dailyAtr:F2}  open={sessionOpen:F2}");

            // ── Arm every evaluator for this session ──────────────────────────
            foreach (var run in runs)
                run.Evaluator.StartSession(prediction.PredictedHigh, dailyAtr);

            // ── Bar-by-bar simulation ─────────────────────────────────────────
            foreach (var bar in ordered)
            {
                foreach (var run in runs)
                {
                    // 1. Check stop loss before evaluating the bar.
                    //    For a short, the stop is hit when the bar's high reaches
                    //    the stop price (fill at stop price — no gap assumption).
                    if (run.Position == PositionSide.Short && bar.High >= run.StopPrice)
                        closePosition(run, bar.Timestamp, sessionDate, run.StopPrice, "Stop");

                    // 2. Evaluate the bar and execute the returned signal.
                    var eval = run.Evaluator.Evaluate(bar.High, bar.Close, run.Position, run.EntryPrice);

                    switch (eval.Signal)
                    {
                        case StrategySignal.EnterShort:
                            run.Position   = PositionSide.Short;
                            run.EntryPrice = bar.Close;
                            run.EntryTime  = bar.Timestamp;
                            run.StopPrice  = eval.StopPrice;
                            break;

                        case StrategySignal.ExitLong when run.Position == PositionSide.Long:
                            closePosition(run, bar.Timestamp, sessionDate, bar.Close, "ExitLong");
                            break;

                        case StrategySignal.UpdateStop:
                            run.StopPrice = eval.StopPrice;
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
