using System.Collections.Generic;

namespace AutomatedTrading.Core.Models;

public interface IMarketState
{
    IList<BarData> Peaks { get; }
    IList<BarData> Valleys { get; }
    BarData? DayStart { get; }
    BarData? DayHigh { get; }
    BarData? DayLow { get; }
    int BarCount { get; set; }
    void OnNewBar(BarData bar);
    void OnNewSession(BarData bar);
    void MarkSignalPeak(BarData bar);
    void MarkSignalValley(BarData bar);
}