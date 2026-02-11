using AutomatedTrading.Core.Models;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Signals;

public class TwoGreenBars : ISignal
{
    public bool IsTriggered(List<Bar> bars)
    {
        if (bars.Count < 2)
            return false;

        var last = bars[0];
        var prev = bars[1];

        return last.Close > last.Open &&
            prev.Close > prev.Open;
    }
}
