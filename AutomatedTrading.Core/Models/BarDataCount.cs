using System.Collections.Generic;

namespace AutomatedTrading.Core.Models;

public class BarDataDateCount
{
    public string SessionDate { get; set; } = default!;
    public int BarCount { get; set; }
    public List<BarDataInstrumentCount> Details { get; set; } = [];
}

public class BarDataInstrumentCount
{
    public string Instrument { get; set; } = default!;
    public string InstrumentVersion { get; set; } = default!;
    public int BarCount { get; set; }
}
