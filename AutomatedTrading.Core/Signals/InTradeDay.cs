using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace AutomatedTrading.Core.Signals;
public class InTradeDay : ISignal, IHasLog
{
    public string Log { get; private set; } = "";
    public bool IsTriggered(List<Bar> bars)
    {
        if (bars.Count == 0)
            return false;
        
        var bar = bars.First();

        var result = bar.Time > AppConstants.TRADE_DAY_START && bar.Time < AppConstants.TRADE_DAY_END;
        Log = $"InTradeDay - isTriggered: {result} barTime:{bar.Time}, tradeDayStart: {AppConstants.TRADE_DAY_START}, tradeDayEnd: {AppConstants.TRADE_DAY_END}";
        
        return result;
    }
}