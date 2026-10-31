using System;
using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Data;

namespace AutomatedTrading.Core.Models;

public class BarData : IDatabaseEntity
{
    public string TableName { get; } = "BarData";
    public string SchemaName { get; } = "dbo";
    public long Id { get; set; }
    public double Open { get; set; }
    public double High { get; set; }
    public double Low { get; set; }
    public double Close { get; set; }
    public double Volume { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Timeframe { get; set; }
    public string? SessionDate { get; set; }
    public string? SessionTime { get; set; }
    public string? Instrument { get; set; }
    public string? InstrumentVersion { get; set; }
    public BarData() { }
    public BarData(BarData bar)
    {
        Open = bar.Open;
        High = bar.High;
        Low = bar.Low;
        Close = bar.Close;
        Volume = bar.Volume;
        Instrument = bar.Instrument;
        Timeframe = bar.Timeframe;
        Timestamp = bar.Timestamp;
        SessionDate = bar.SessionDate;
        InstrumentVersion = bar.InstrumentVersion;
    }
    public virtual string ToLog()
    {
        return $"timestamp={Timestamp:yyyy-MM-dd HH:mm:ss},sessiondate={SessionDate},open={Open},high={High},low={Low},close={Close},volume={Volume}";
    }

    public static BarData Aggregate(List<BarData> bars)
    {
        if (bars.Count == 0)
            throw new InvalidOperationException("No bars to aggregate");

        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var first = ordered.First();
        var last = ordered.Last();
        return new BarData
        {
            Timestamp = first.Timestamp,
            SessionDate = first.SessionDate,
            Open = first.Open,
            Close = last.Close,
            High = ordered.Max(x => x.High),
            Low = ordered.Min(x => x.Low),
            Volume = ordered.Sum(x => x.Volume),
            InstrumentVersion = last.InstrumentVersion,
            Instrument = last.Instrument,
        };
    }
}