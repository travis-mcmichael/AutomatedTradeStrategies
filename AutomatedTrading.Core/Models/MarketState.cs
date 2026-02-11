using AutomatedTrading.Core.Models;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Models;

public class MarketState : IMarketState
{
    private List<Bar> peaks { get; } = new();
    private List<Bar> valleys { get; } = new();
    public Bar DayHigh { get; private set; }
    public Bar DayLow { get; private set; }

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
        peaks.Clear();
        valleys.Clear();
    }

    public void MarkSignalPeak(Bar bar)
    {
        peaks.Add(bar);
    }

    public void MarkSignalValley(Bar bar)
    {
        valleys.Add(bar);
    }
}