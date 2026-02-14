using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Signals;

public class IsTrending : ISignal
{
    private int strength;
    private bool increasing;
    public IsTrending(int strength, bool increasing)
    {
        this.strength = strength;
        this.increasing = increasing;
    }

    public bool IsTriggered(List<Bar> bars)
    {
        if (bars.Count < strength)
            return false;

        var segment = bars
            .Skip(bars.Count - strength)
            .Take(strength)
            .ToList();

        if (increasing)
            return checkIncreasingTrend(segment);
        else 
            return checkDecreasingTrend(segment);        
    }

    private bool checkDecreasingTrend(List<Bar> bars)
    {
        for (var i = bars.Count - 1; i > 0; i--)
        {
            if (i == 0)
            {
                // if the first segment's Close was higher than it's Open
                // return false
                if (bars[i].Close > bars[i].Open)
                    return false;
                continue;
            }

            // If the segment's Close is greater than last segment's close OR if the segment's Close is higher than it's open.
            if (bars[i].Close > bars[i - 1].Close || bars[i].Close > bars[i].Open)
                return false;
        }

        return true;
    }

    private bool checkIncreasingTrend(List<Bar> bars)
    {
        for (var i = bars.Count - 1; i > 0; i--)
        {
            if (i == 0)
            {
                // if the first segment's Close was higher than it's Open
                // return false
                if (bars[i].Close < bars[i].Open)
                    return false;
                continue;
            }

            // If the segment's Close is greater than last segment's close OR if the segment's Close is higher than it's open.
            if (bars[i].Close < bars[i - 1].Close || bars[i].Close < bars[i].Open)
                return false;
        }

        return true;
    }
}