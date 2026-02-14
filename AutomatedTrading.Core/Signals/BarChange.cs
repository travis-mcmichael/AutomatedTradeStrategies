using System;
using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Signals;

public class BarChange : ISignal
{
    private int pointsChangeThreshold;
    private int barMax;
    private bool increase;

    public BarChange(int pointsChangeThreshold, int barMax, bool increase)
    {
        this.pointsChangeThreshold = pointsChangeThreshold;
        this.barMax = barMax;
        this.increase = increase;
    }

    public bool IsTriggered(List<Bar> bars)
    {
        if (bars.Count < 2)
            return false;

        var outlier = increase ? bars.Min(x => x.Close) : bars.Max(x => x.Close);
        var close = bars.Last().Close;

        if (Math.Abs(close - outlier) < pointsChangeThreshold)
            return false;

        return true;
    }
}