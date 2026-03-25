namespace AutomatedTrading.Core.Strategies;

public enum StrategySignal { None, EnterShort, ExitLong, UpdateStop }

public enum PositionSide { Flat, Long, Short }

public record BarEvaluation
{
    public StrategySignal Signal { get; set; }
    public double StopPrice { get; set; }
    public BarEvaluation(StrategySignal signal, double stopPrice)
    {
        Signal = signal;
        StopPrice = stopPrice;
    }
}
