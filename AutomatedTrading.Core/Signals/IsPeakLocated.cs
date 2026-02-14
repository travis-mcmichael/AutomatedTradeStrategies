using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Framework;

namespace AutomatedTrading.Core.Signals;

public class IsPeakLocated : ISignal, IHasLog
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

    public string Log { get; set; } = "";

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
        var segmentLength = strength * 2 + 1;
        if (bars.Count < segmentLength)
            return false;
        
        var peaks = new List<Bar>();
        for (var i = strength; i < bars.Count - strength; i++)
        {
            var skip = false;
            var barToEvaluate = bars[i];
            System.Console.WriteLine($"Evaluating bar {barToEvaluate.ToLog()}");

            // evaluate each bar's value {strength} records before and after the current bar.
            var subset = bars.GetRange(i - strength, segmentLength);

            // if another bar within {strength} bars has a higher High than the current bar,
            // mark it for skipping. 
            for (var j = 0; j < subset.Count; j++)
            {
                if (subset[j].Close > barToEvaluate.Close) // - ignoreThreshold)
                {
                    skip = true;
                    break;
                }
            }

            if (skip)
                continue;

            // if the price movement in this set isn't above the threshold
            if (barToEvaluate.Close - subset.Min(x => x.Close) < pointMovementThreshold)
                continue;

            peaks.Add(barToEvaluate);
        }

        Peaks.AddRange(peaks);
        Log = "IsPeakLocated.IsTriggered: Peaks detected - " + string.Join(",", peaks.Select(x => $"{x.Close:#.##} @ {x.Time}" ));

        return peaks.Count > 0;
    }
}