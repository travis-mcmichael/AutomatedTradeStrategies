namespace AutomatedTrading.Core.Models;

public class Bar
{
    public int Time { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Volume { get; set; }

    public Bar(int time, double open, double high, double low, double close, double volume)
    {
        Time = time;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }

    public string ToLog()
    {
        return $"time={Time},open={Open},high={High},low={Low},close={Close},volume={Volume}";
    }
}