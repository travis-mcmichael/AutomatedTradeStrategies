using AutomatedTrading.Core.Framework.Enums;

namespace AutomatedTrading.Console.ApiTesting;

/// <summary>
/// Tracks position state and accumulated trade metrics for one API backtest run.
/// </summary>
public class ApiBacktestRun
{
    // ─── Identity ─────────────────────────────────────────────────────────────
    public string Name { get; }

    // ─── Position state (managed by ApiBacktestRunner) ────────────────────────
    internal PositionSide Position { get; set; } = PositionSide.Flat;
    internal double EntryPrice { get; set; }
    internal double StopPrice { get; set; } = double.MaxValue;
    internal DateTime EntryTime { get; set; }

    // ─── Results ──────────────────────────────────────────────────────────────
    private readonly List<TradeResult> _trades = [];
    public IReadOnlyList<TradeResult> Trades => _trades;
    public double GrossProfit { get; private set; }
    public double GrossLoss { get; private set; }
    public double NetProfit => GrossProfit - GrossLoss;
    public int TotalTrades => _trades.Count;
    public int WinningTrades { get; private set; }
    public int LosingTrades { get; private set; }
    public ApiBacktestRun(string name) => Name = name;
    internal void RecordTrade(TradeResult trade)
    {
        _trades.Add(trade);
        if (trade.PnL > 0)
        {
            GrossProfit += trade.PnL;
            WinningTrades++; 
        }
        else 
        {
            GrossLoss -= trade.PnL;
            LosingTrades++;
        }
    }
}
