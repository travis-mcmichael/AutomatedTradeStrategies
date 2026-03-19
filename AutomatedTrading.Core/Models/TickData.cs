namespace AutomatedTrading.Core.Models;

public class TickData
{
    public string Instrument { get; set; } = default!;
    public long TickTime { get; set; }
    public double LastPrice { get; set; }
    public long Volume { get; set; }
    public double? Bid { get; set; }
    public double? Ask { get; set; }
}