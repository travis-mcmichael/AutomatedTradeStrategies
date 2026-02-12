using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AutomatedTrading.Core.Tests;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Strategies;

namespace AutomatedTrading.Core.Tests.Strategies;

public class LowInTradeWindowTest
{
    private const string TEST_FILE = "MES.20260202.dat";
    private const string DATA_DATE = "20260206";

    [Fact]
    public void ReturnsTrue_WhenLowInTradeWindow()
    {
        var tradeWindowStart = 80000;
        var tradeWindowEnd = 160000;
        var initialStopLoss = 5;
        var trailTrigger = 6;
        var trailDistance = 3;

        var path = Path.Combine(AppContext.BaseDirectory, AppConstants.TEST_BASE_DIRECTORY);
        var bars = new LoadTestData().LoadFile(Path.Combine(path, TEST_FILE), DATA_DATE);

        Console.WriteLine($"Bar count { bars.Count }");
        var expected = bars.Select(x => x.Time == 83400); // I made the lowest Low value on the bar at 8:34, every other value should evaluate to false
        var actual = new List<bool>();
        
        var strategy = new LowInTradeWindow(tradeWindowStart, tradeWindowEnd, initialStopLoss, trailTrigger, trailDistance);
        strategy.Initialize(bars.First());
        foreach (var bar in bars)
        {
            actual.Add(strategy.Evaluate(bar));
        }

        Console.WriteLine($"Expected: [{string.Join(",", expected)}]");
        Console.WriteLine($"Actual:   [{string.Join(",", actual)}]");

        Assert.Equal(expected, actual);
    }
}
