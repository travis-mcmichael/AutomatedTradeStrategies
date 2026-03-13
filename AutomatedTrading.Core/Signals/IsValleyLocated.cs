using AutomatedTrading.Core.Models;
using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Framework.Interfaces;

namespace AutomatedTrading.Core.Signals;

public class IsValleyLocated : ISignal, IStatusLog
{
    /// <summary>
    /// Number of bars to look ahead/behind when considering a bar as a peak
    /// </summary>
    private int strength { get; } = 0;

    /// <summary>
    /// Amount the total price change must exceed to trigger signal.
    /// </summary>
    private int pointMovementThreshold { get; } = 0;

    /// <summary>
    /// Bars that satisfy the valley-detection logic in the supplied range.
    /// </summary>
    public List<BarData> Valleys { get; } = new List<BarData>();
    public string Log { get; set; } = "";
    public IsValleyLocated(int strength, int pointMovementThreshold)
    {
        this.strength = strength;
        this.pointMovementThreshold = pointMovementThreshold;
    }

    public bool IsTriggered(List<BarData> bars)
    {
        var segmentLength = strength * 2 + 1;
        if (bars.Count < segmentLength)
            return false;

        var valleys = new List<BarData>();
        for (var i = strength; i < bars.Count - strength; i++)
        {
            var skip = false;
            var barToEvaluate = bars[i];
            System.Console.WriteLine($"Evaluating bar {barToEvaluate.ToLog()}");

            // evaluate each bar's value {strength} records before and after the current bar.
            var subset = bars.GetRange(i - strength, segmentLength);

            // if another bar within {strength} bars has a lower Close than the current bar,
            // mark it for skipping. 
            for (var j = 0; j < subset.Count; j++)
            {
                if (subset[j].Close < barToEvaluate.Close) // - ignoreThreshold)
                {
                    skip = true;
                    break;
                }
            }

            if (skip)
                continue;

            // if the price movement in this set isn't above the threshold
            if (subset.Max(x => x.Close) - barToEvaluate.Close < pointMovementThreshold)
                continue;

            valleys.Add(barToEvaluate);
        }

        Valleys.AddRange(valleys);

        Log = "IsValleyLocated.IsTriggered: Valleys detected - " + string.Join(",", valleys.Select(x => $"{x.Close:#.##} @ {x.Timestamp}"));
        return valleys.Count > 0;
    }

}