using AutomatedTrading.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace AutomatedTrading.Core.Signals;
public class InTradeDay : ISignal
{
    public bool IsTriggered(List<Bar> bars)
    {
        if (bars.Count == 0)
            return false;
        
        var bar = bars.First();

        return bar.Time > AppConstants.TRADE_DAY_START && bar.Time < AppConstants.TRADE_DAY_END;
    }
}