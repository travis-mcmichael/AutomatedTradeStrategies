using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Signals;

public class IsSessionLow : ISignal, IHasLog
{

    public double CurrentSessionLow { get; private set; }
    public string Log { get; private set; } = "";

    public IsSessionLow(double currentSessionLow)
    {
        CurrentSessionLow = currentSessionLow;
    }

    public bool IsTriggered(List<Bar> bars)
    {
        if (bars.Count < AppConstants.MINIMUM_BARS_TO_EVALUATE)
            return false;
        
        var result = CurrentSessionLow == bars.First().Low; 
        Log = $"IsSessionLow - isTriggered: {result}, current: {bars.First().Low}, currentSessionLow: {CurrentSessionLow} ";
        return result;
    }
}