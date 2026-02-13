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

        var expected = bars.Select(x => new int[] { 81600, 81700, 81900, 82200, 83100, 83200 }.Contains(x.Time));
        var actual = new List<bool>();

        var strategy = new LowInTradeWindow(tradeWindowStart, tradeWindowEnd, initialStopLoss, trailTrigger, trailDistance);
        strategy.Initialize(bars.First());
        foreach (var bar in bars)
        {
            actual.Add(strategy.Evaluate(bar));
        }
        
        Assert.Equal(expected, actual);
    }
}
