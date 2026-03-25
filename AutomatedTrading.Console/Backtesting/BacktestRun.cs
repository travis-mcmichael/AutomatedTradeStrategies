using AutomatedTrading.Core.Strategies;

namespace AutomatedTrading.Console.Backtesting;

/// <summary>
/// One configured strategy instance to include in a backtest.
/// Create with a named <see cref="PredictedHighStrategyEvaluator"/> (with the desired
/// parameter values already set) and pass to <see cref="BacktestRunner.RunAsync"/>.
///
/// The runner writes to the internal position fields on each bar. Callers read the
/// computed metrics after the run completes.
/// </summary>
public class BacktestRun
{
    // ─── Identity ─────────────────────────────────────────────────────────────
    public string Name { get; }
    public PredictedHighStrategyEvaluator Evaluator { get; }

    // ─── Position state (managed exclusively by BacktestRunner) ───────────────
    internal PositionSide Position { get; set; } = PositionSide.Flat;
    internal double EntryPrice { get; set; }
    internal double StopPrice  { get; set; } = double.MaxValue;
    internal DateTime EntryTime  { get; set; }

    // ─── Results ──────────────────────────────────────────────────────────────
    private readonly List<TradeResult> _trades = new();
    public IReadOnlyList<TradeResult> Trades => _trades;
    public double GrossProfit  { get; private set; }
    public double GrossLoss { get; private set; }
    public double NetProfit => GrossProfit - GrossLoss;
    public int TotalTrades  => _trades.Count;
    public int WinningTrades { get; private set; }
    public int LosingTrades  { get; private set; }
    public BacktestRun(string name, PredictedHighStrategyEvaluator evaluator)
    {
        Name = name;
        Evaluator = evaluator;
    }

    internal void RecordTrade(TradeResult trade)
    {
        _trades.Add(trade);
        if (trade.PnL > 0) { GrossProfit += trade.PnL; WinningTrades++; }
        else { GrossLoss -= trade.PnL; LosingTrades++; }
    }
}
