using AutomatedTrading.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace AutomatedTrading.Core.Signals;

public class InTradeWindow : ISignal
{
    public int TradeWindowStart { get; private set; }
    public int TradeWindowEnd { get; private set; }
    public InTradeWindow(int tradeWindowStart, int tradeWindowEnd)
    {
        TradeWindowStart = tradeWindowStart;
        TradeWindowEnd = tradeWindowEnd;
    }

    public bool IsTriggered(List<Bar> bars)
    {
        if (bars.Count == 0)
            return false;
        
        var bar = bars.First();

        return bar.Time > TradeWindowStart && bar.Time < TradeWindowEnd;
    }
}