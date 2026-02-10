using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Signals;

public class TwoGreenBars : ISignal
{
    public bool IsTriggered(IReadOnlyList<Bar> bars)
    {
        if (bars.Count < 2)
            return false;

        var last = bars[^1];
        var prev = bars[^2];

        return last.Close > last.Open && 
            prev.Close > prev.Open;
    }
}