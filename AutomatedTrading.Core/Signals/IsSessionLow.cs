using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Signals;

public class IsSessionLow : ISignal
{
    public double CurrentSessionLow { get; private set; }

    public IsSessionLow(double currentSessionLow)
    {
        CurrentSessionLow = currentSessionLow;
    }

    public bool IsTriggered(List<Bar> bars)
    {
        if (bars.Count == 0)
            return false;
        
        return CurrentSessionLow == bars.First().Low;
    }
}