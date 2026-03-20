using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Framework.Utility;

public class TradeSpanConvert
{
    public static BarData ConvertSpanToSingleBar(List<BarData> bars, string timeframe)
    {
        return new BarData
        {
            Open = bars.First().Open,
            Close = bars.Last().Close,
            High = bars.Max(x => x.High),
            Low = bars.Min(x => x.Low),
            Volume = bars.Sum(x => x.Volume),
            Instrument = bars.First().Instrument,
            SessionDate = bars.First().SessionDate,
            Timeframe = timeframe
        };
    }
}