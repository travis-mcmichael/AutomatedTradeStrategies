using AutomatedTrading.Core.Models;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Models;

public class MarketState : IMarketState
{
    public IList<Bar> Peaks { get; } = new List<Bar>();
    public IList<Bar> Valleys { get; } = new List<Bar>();
    public Bar? DayHigh { get; private set; }
    public Bar? DayLow { get; private set; }

    public void OnNewBar(Bar bar)
    {
        if (bar.Low < DayLow?.Low)
        {
            DayLow = bar;
        }
        if (bar.High > DayHigh?.High)
        {
            DayHigh = bar;
        }
    }

    public void OnNewSession(Bar bar)
    {
        DayHigh = bar;
        DayLow = bar;
        Peaks.Clear();
        Valleys.Clear();
    }

    public void MarkSignalPeak(Bar bar)
    {
        Peaks.Add(bar);
    }

    public void MarkSignalValley(Bar bar)
    {
        Valleys.Add(bar);
    }
}