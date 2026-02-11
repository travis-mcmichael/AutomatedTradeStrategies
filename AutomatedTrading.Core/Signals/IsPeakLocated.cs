using System.Collections.Generic;
using System.Linq;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Core.Signals;

public class IsPeakLocated : ISignal
{
    /// <summary>
    /// Number of bars to look ahead/behind when considering a bar as a peak
    /// </summary>
    private int strength { get; } = 0;

    /// <summary>
    /// Amount the price must change between bars to recognize the change as significant.
    /// </summary>
    private int ignoreThreshold { get; } = 0;

    /// <summary>
    /// Amount the total price change must exceed to trigger signal.
    /// </summary>
    private int pointMovementThreshold { get; } = 0;

    /// <summary>
    /// Bars that satisfy the peak-detection logic in the supplied range.
    /// </summary>
    public List<Bar> Peaks { get; } = new List<Bar>();

    public IsPeakLocated(int strength, int ignoreThreshold, int pointMovementThreshold)
    {
        this.strength = strength;
        this.ignoreThreshold = ignoreThreshold;
        this.pointMovementThreshold = pointMovementThreshold;
    }

    /// <summary>
    /// Evaluates list of bars for Peak prices.
    /// </summary>
    /// <param name="bars">Set of bars to evaluate.</param>
    /// <returns>Boolean whether subset has any peaks (sets Peaks property if true).</returns>
    public bool IsTriggered(List<Bar> bars)
    {
        if (bars.Count < strength * 2 + 1)
            return false;

        for (var i = 0; i < bars.Count; i++)
        {
            var skip = false;

            // evaluate each High value {strength} records before and after the current bar.
            var subset = bars.GetRange(i - strength, i + strength);
            for (var j = 0; j < subset.Count; j++)
            {
                // if another bar within {strength} bars has a higher High than the current bar,
                // mark it for skipping. 
                if (bars[j].High > bars[i].High - ignoreThreshold)
                {
                    skip = true;
                    break;
                }
            }

            if (skip)
                continue;

            // if the price movement in this set isn't above the threshold
            if (bars[i].High - subset.Min(x => x.Low) > pointMovementThreshold)
                continue;

            Peaks.Add(bars[i]);
        }

        return Peaks.Count > 0;
    }
}