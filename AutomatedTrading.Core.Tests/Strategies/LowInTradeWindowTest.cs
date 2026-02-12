using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Strategies;

namespace AutomatedTrading.Core.Tests.Strategies;

public class LowInTradeWindowTest
{
    private const string TEST_FILE = "MES.20260202.dat";
    private const string DATA_DATE = "20260205";

    [Fact]
    public void ReturnsTrue_WhenLowInTradeWindow()
    {
        var tradeWindowStart = 80000;
        var tradeWindowEnd = 90000;
        var initialStopLoss = 5;
        var trailTrigger = 6;
        var trailDistance = 3;

        var path = Path.Combine(AppContext.BaseDirectory, AppConstants.TEST_BASE_DIRECTORY);
        var bars = new LoadTestData(path)
            .LoadFile(TEST_FILE, DATA_DATE)
            .Where(x => x.Time >= tradeWindowStart && x.Time < tradeWindowEnd);

        // Bar data for this day hit a low for the time period from 8:00 AM (beginning of trade day)
        var expected = bars.Select(x => new int[] { 82900, 85400, 85500, 85600 }.Contains(x.Time));
        var actual = new List<bool>();

        var strategy = new LowInTradeWindow(tradeWindowStart, tradeWindowEnd, initialStopLoss, trailTrigger, trailDistance);
        strategy.Initialize(bars.First());
        foreach (var bar in bars)
        {
            actual.Add(strategy.Evaluate(bar));
            Console.WriteLine(strategy.Log);
        }

        Console.WriteLine($"Expected: [{string.Join(",", expected)}]");
        Console.WriteLine($"Actual:   [{string.Join(",", actual)}]");

        Assert.Equal(expected, actual);
    }
}
