using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.Interfaces;
using AutomatedTrading.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace AutomatedTrading.Core.Signals;

public class InTradeDay : ISignal, IStatusLog
{
    public string Log { get; private set; } = "";
    public bool IsTriggered(List<BarData> bars)
    {
        if (bars.Count == 0)
            return false;

        var bar = bars.First();

        var result = bar.Timestamp.ToInt() > AppConstants.TRADE_DAY_START && bar.Timestamp.ToInt() < AppConstants.TRADE_DAY_END;
        Log = $"InTradeDay.isTriggered: result={result}, barTime={bar.Timestamp}, tradeDayStart={AppConstants.TRADE_DAY_START}, tradeDayEnd={AppConstants.TRADE_DAY_END}";

        return false;
    }
}