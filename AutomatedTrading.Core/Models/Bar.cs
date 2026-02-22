using System;

namespace AutomatedTrading.Core.Models;

public class Bar
{
    public int Time { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Volume { get; set; }
    public DateTime? Date {get;set;} = null;

    public Bar()
    {
        Time = 0;
        Open = High = Low = Close = Volume = 0;
    }

    public Bar(int time, double open, double high, double low, double close, double volume)
    {
        Time = time;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
    }
    public Bar(int time, double open, double high, double low, double close, double volume, DateTime date)
    {
        Time = time;
        Open = open;
        High = high;
        Low = low;
        Close = close;
        Volume = volume;
        Date = date;
    }
    public string ToLog()
    {
        return $"time={Time},open={Open},high={High},low={Low},close={Close},volume={Volume}" + (Date.HasValue ? $",Date={Date:yyyyMMdd}" : "");
    }
}