using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Strategies;

namespace AutomatedTrading.Core.Tests.Strategies;

public class TrailingStopStrategyTest
{
    [Fact]
    public void UpdatesStopLoss_WhenStopUpdateTriggered()
    {
        var testFile = "MES.20260202.dat";
        var dataDate = "20260205";

        var tradeWindowStart = 100000;
        var tradeWindowEnd = 102500;
        var initialStopLoss = 5;
        var trailTrigger = 10;
        var trailDistance = 2;

        var path = Path.Combine(AppContext.BaseDirectory, AppConstants.TEST_BASE_DIRECTORY);
          var bars = new LoadTestData(path)
            .LoadFile(testFile, dataDate)
            .Where(x => x.Time >= tradeWindowStart && x.Time < tradeWindowEnd);
        var buyInPrice = bars.First().Close; // assume we buy at the end of the first bar

        var strategy = new TrailingStopStrategy(initialStopLoss, trailTrigger, trailDistance, buyInPrice);
        var results = new List<bool>(); // false on first bar
        var stopLosses = new List<double> { strategy.CurrentStopLoss };
        var stopHits = new List<int>(); // log the time the updates stops would get hit

        var expectedResults = bars.Select(x => new int[] { 101900, 102300 }.Contains(x.Time));
        var expectedStopLosses = new List<double> { 6812.25, 6821.5, 6831.75 };

        foreach (var bar in bars)
        {
            // These aren't enforced in this test, still good to track.
            if (bar.Close <= strategy.CurrentStopLoss)
                stopHits.Add(bar.Time);

            var result = strategy.EvaluateStopLoss(bar);
            if (result)
                stopLosses.Add(strategy.CurrentStopLoss);
            
            results.Add(result);
        }

        Assert.Equal(expectedResults, results);
        Assert.Equal(expectedStopLosses, stopLosses);
    }
}