using System.Linq;
using System.Collections.Generic;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Strategies;

namespace AutomatedTrading.Core.Tests.Strategies;

public class LowInTradeWindowTest
{
    [Fact]
    public void ReturnsTrue_WhenLowInTradeWindow()
    {
        var tradeWindowStart = 80000;
        var tradeWindowEnd = 160000;
        var initialStopLoss = 5;
        var trailTrigger = 6;
        var trailDistance = 3;

        var bars = new List<Bar>
        {
            // 8:00-8:09
            new (80000, 100, 105, 96, 102, 50),
            new (80100, 103, 110, 97, 106, 60),
            new (80200, 105, 112, 99, 107, 15),
            new (80300, 106, 112, 100, 108, 10),
            new (80400, 104, 112, 99, 102, 100),            
            new (80500, 102, 112, 95, 101, 25),
            new (80600, 101, 112, 94, 99, 35),
            new (80700, 98, 106, 93, 96, 20),
            new (80800, 95, 105, 90, 94, 10),
            new (80900, 92, 100, 86, 90, 5),
            
            // 8:10-8:19
            new (81000, 90, 99, 84, 92, 15),
            new (81100, 91, 100, 83, 93, 20),
            new (81200, 92, 82, 102, 9.2, 25),
            new (81300, 93, 83, 102, 94, 100),
            new (81400, 93, 85, 102, 94, 72),
            new (81500, 94, 103, 86, 96, 456),
            new (81600, 96, 104, 92, 97, 2),
            new (81700, 97, 102, 93, 97, 44),
            new (81800, 97, 100, 92, 97, 5676),
            new (81900, 98, 100, 90, 99, 765),

            // 8:20-8:29
            new (82000, 104, 110, 93, 106, 55),
            new (82100, 107, 113, 98, 109, 11),
            new (82200, 110, 117, 102, 110, 15),
            new (82300, 111, 114, 105, 108, 43),
            new (82400, 105, 111, 100, 103, 21),
            new (82500, 102, 108, 98, 101, 22),
            new (82600, 100, 104, 97, 98, 64),
            new (82700, 98, 103, 95, 99, 45),
            new (82800, 99, 105, 96, 98, 72),
            new (82900, 97, 103, 92, 94, 11),

            // 8:30-8:39
            new (83000, 92, 100, 88, 91, 78),
            new (83100, 91, 95, 86, 90, 77),
            new (83200, 90, 92, 85, 86, 23),
            new (83300, 85, 89, 80, 83, 1555),
            new (83400, 82, 88, 77, 83, 756),
            new (83500, 83, 85, 78, 87, 66),
            new (83600, 88, 89, 83, 91, 12),
            new (83700, 92, 94, 85, 95, 45),
            new (83800, 95, 99, 89, 96, 23),
            new (83900, 96, 100, 95, 100, 11),
        };

        var expected = bars.Select(x => x.Time == 83400); // I made the lowest Low value on the bar at 8:34, every other value should evaluate to false
        var actual = new List<bool>();
        
        var strategy = new LowInTradeWindow(tradeWindowStart, tradeWindowEnd, initialStopLoss, trailTrigger, trailDistance);
        strategy.Initialize(bars.First());
        foreach (var bar in bars)
        {
            actual.Add(strategy.Evaluate(bar));
        }

        System.Console.WriteLine($"Expected: [{string.Join(",", expected)}]");
        System.Console.WriteLine($"Actual:   [{string.Join(",", actual)}]");

        Assert.Equal(expected, actual);
    }
}
