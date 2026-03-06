using System;
using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Framework;

namespace AutomatedTrading.Core.Signals;

public class BarChange : ISignal, IHasLog
{
    private readonly int pointsChangeThreshold;
    private readonly int barMax;
    private readonly PriceDirection priceDirection;
    public string Log { get; set; } = "";

    public BarChange(int pointsChangeThreshold, int barMax, PriceDirection priceDirection)
    {
        this.pointsChangeThreshold = pointsChangeThreshold;
        this.barMax = barMax;
        this.priceDirection = priceDirection;
    }

    public bool IsTriggered(List<BarData> bars)
    {
        var segment = new List<BarData>();
        if (barMax < 2 || pointsChangeThreshold < 1)
        {
            Log = $"BarChange.IsTriggered: result=false, bad setup values (barMax={barMax}, theshold={pointsChangeThreshold})";
            return false;
        }

        if (bars.Count < 2 || barMax < 2)
        {
            Log = $"BarChange.IsTriggered: result=false, not enough data (barCount={bars.Count})";
            return false;
        }

        if (bars.Count > barMax)
        {
            segment = bars.Skip(bars.Count - barMax).ToList(); ;
        }
        else
        {
            segment = bars;
        }

        var close = segment.Last().Close;
        if (priceDirection == PriceDirection.INCREASING)
        {
            var min = segment.First().Open;
            if (close - min < pointsChangeThreshold)
            {
                Log = $"BarChange.IsTriggered: result=false, close={close}, min={min}";
                return false;
            }
        }
        else
        {
            var max = segment.First().Open;
            if (max - close < pointsChangeThreshold)
            {
                Log = $"BarChange.IsTriggered: result=false, close={close}, max={max}";
                return false;
            }
        }

        Log = $"BarChange.IsTriggered: result=true, close={close}";
        return true;
    }
}