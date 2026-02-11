namespace AutomatedTrading.Core.Models;

public interface IMarketState
{
    Bar DayHigh { get; }
    Bar DayLow { get; }

    void OnNewBar(Bar bar);
    void OnNewSession(Bar bar);
    void MarkSignalPeak(Bar bar);
    void MarkSignalValley(Bar bar);
}