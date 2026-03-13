using AutomatedTrading.Core.Framework.Extensions;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Models;

public class MarketState : IMarketState
{
    public IList<BarData> Peaks { get; } = new List<BarData>();
    public IList<BarData> Valleys { get; } = new List<BarData>();
    public BarData? DayStart { get; private set; }
    public BarData? DayHigh { get; private set; }
    public BarData? DayLow { get; private set; }
    public int BarCount { get; set; } = 0;

    public void OnNewBar(BarData bar)
    {
        if (bar.Timestamp.ToInt() < AppConstants.TRADE_DAY_START || bar.Timestamp.ToInt() > AppConstants.TRADE_DAY_END)
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

    public void OnNewSession(BarData bar)
    {
        BarCount = 0;
        Peaks.Clear();
        Valleys.Clear();
    }

    public void MarkSignalPeak(BarData bar)
    {
        Peaks.Add(bar);
    }

    public void MarkSignalValley(BarData bar)
    {
        Valleys.Add(bar);
    }
}