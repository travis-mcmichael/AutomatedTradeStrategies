using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace AutomatedTrading.Core.Signals;

public class InTradeWindow : ISignal, IHasLog
{
    public int TradeWindowStart { get; private set; }
    public int TradeWindowEnd { get; private set; }
    public string Log { get; private set; } = "";
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
        var result = bar.Time > TradeWindowStart && bar.Time < TradeWindowEnd;
        Log = $"InTradeWindow.isTriggered: result={result}, barTime={bar.Time}, tradeWindowStart={TradeWindowStart}, tradeWindowEnd={TradeWindowEnd}";

        return result;
    }
}