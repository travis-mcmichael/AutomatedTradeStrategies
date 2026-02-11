namespace AutomatedTrading.Core.Models;

public class Bar
{
    public int Time {get;set;}
    public double Open {get;set;}
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public long Volume { get; set; }

    public Bar(int time, double open, double high, double low, double close, long volume)
    {
        Time = time;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }
}