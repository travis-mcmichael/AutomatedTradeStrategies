using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Signals;

public class IsSessionLow : ISignal, IHasLog
{

    public double CurrentSessionLow { get; private set; }
    public int CurrentSessionLowTime { get; private set; }
    public int BarCount { get; private set; }
    public string Log { get; private set; } = "";

    public IsSessionLow(int sessionLowTime, double currentSessionLow, int barCount)
    {
        CurrentSessionLowTime = sessionLowTime;
        CurrentSessionLow = currentSessionLow;
        BarCount = barCount;
    }

    public bool IsTriggered(List<Bar> bars)
    {
        if (BarCount < AppConstants.MINIMUM_BARS_TO_EVALUATE)
        {
            Log = $"IsSessionLow.IsTriggered: Not enough data to evaluate (barCount={BarCount})";
            return false;
        }

        var result = CurrentSessionLow == bars.First().Close;
        Log = $"IsSessionLow.isTriggered: result={result}, current={bars.First().Close}, currentSessionLow={CurrentSessionLow}, currentSessionLowTime={CurrentSessionLowTime}";
        return result;
    }
}