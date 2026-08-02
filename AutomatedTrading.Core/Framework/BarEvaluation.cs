using AutomatedTrading.Core.Framework.Enums;
namespace AutomatedTrading.Core.Framework;
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
