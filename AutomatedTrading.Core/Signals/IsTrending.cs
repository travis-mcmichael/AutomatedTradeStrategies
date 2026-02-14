using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Framework;

namespace AutomatedTrading.Core.Signals;

public class IsTrending : ISignal, IHasLog
{
    private readonly int strength;
    private readonly PriceDirection priceDirection;
    public string Log { get; private set; } = "";
    public IsTrending(int strength, PriceDirection priceDirection)
    {
        this.strength = strength;
        this.priceDirection = priceDirection;
    }

    public bool IsTriggered(List<Bar> bars)
    {
        if (bars.Count < strength)
            return false;

        var segment = bars
            .Skip(bars.Count - strength)
            .Take(strength)
            .ToList();

        if (priceDirection == PriceDirection.INCREASING)
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
                {
                    Log = $"IsTrending(decreasing).Evaluate: results=false, current={bars[i].ToLog()}";
                    return false;

                }

                continue;
            }

            // If the segment's Close is greater than last segment's close OR if the segment's Close is higher than it's open.
            if (bars[i].Close > bars[i - 1].Close || bars[i].Close > bars[i].Open)
            {
                Log = $"IsTrending(decreasing).Evaluate: results=false, current={bars[i].ToLog()}, last={bars[i].ToLog}";
                return false;
            }
        }

        Log = $"IsTrending(decreasing).Evaluate: results=true";
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
                {
                    Log = $"IsTrending(increasing).Evaluate: results=false, current={bars[i].ToLog()}";
                    return false;   
                }
                continue;
            }

            // If the segment's Close is greater than last segment's close OR if the segment's Close is higher than it's open.
            if (bars[i].Close < bars[i - 1].Close || bars[i].Close < bars[i].Open)
            {
                Log = $"IsTrending(increasing).Evaluate: results=false, current={bars[i].ToLog()}, last={bars[i].ToLog}";
                return false;
            }
        }

        Log = $"IsTrending(increasing).Evaluate: results=true";
        return true;
    }
}