using AutomatedTrading.Core.Models;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Models;

public class MarketState : IMarketState
{
    public IList<Bar> Peaks { get; } = new List<Bar>();
    public IList<Bar> Valleys { get; } = new List<Bar>();
    public Bar? DayStart { get; private set; }
    public Bar? DayHigh { get; private set; }
    public Bar? DayLow { get; private set; }
    public int BarCount { get; set; } = 0;

    public void OnNewBar(Bar bar)
    {
        if (bar.Time < AppConstants.TRADE_DAY_START || bar.Time > AppConstants.TRADE_DAY_END)
            return;

        if (DayStart == null)
        {
            DayStart = bar;
        }

        if ((DayLow?.Close ?? 0) == 0 || bar.Close < DayLow?.Close)
        {
            DayLow = bar;
        }
        if ((DayHigh?.Close ?? 0) == 0 || bar.Close > DayHigh?.Close)
        {
            DayHigh = bar;
        }
        BarCount++;
    }

    public void OnNewSession(Bar bar)
    {
        BarCount = 0;
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