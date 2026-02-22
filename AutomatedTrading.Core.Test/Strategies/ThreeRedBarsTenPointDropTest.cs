using System.Security.Cryptography;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Strategies;

namespace AutomatedTrading.Core.Test.Strategies;

public class ThreeRedBarsTenPointDropTest
{
    [Fact]
    public void ReturnsTrue_WhenThreeRedBarsTenPointDrop()
    {
        var testFile = "MES.20260209.dat";
        var dataDate = "20260213";
        var tradeWindowStart = 70000;
        var tradeWindowEnd = 71500;
        var initialStopLoss = 15;
        var trailTrigger = 5;
        var trailDistance = 3;

        var segmentLength = 3;

        var path = Path.Combine(AppContext.BaseDirectory, AppConstants.TEST_BASE_DIRECTORY);
        var bars = new LoadTestData(path)
            .LoadFile(testFile, dataDate)
            .Where(x => x.Time >= tradeWindowStart && x.Time < tradeWindowEnd)
            .ToList();
        
        var expected = bars.Take(12).Select(x => x.Time == 70700 || x.Time == 70800).ToList(); // time from starting element in segment
        var actual = new List<bool>();

        var strategy = new ThreeRedBarsTenPointDrop(initialStopLoss, trailTrigger, trailDistance);
        strategy.Initialize(bars.First());

        for (var i = 0; i < bars.Count() - segmentLength; i++)
        {
            var segment = bars.Skip(i).Take(segmentLength).ToList();
            Console.WriteLine(string.Join(",", $"{segment.Max(x => x.Close)}"));
            actual.Add(strategy.Evaluate(segment));
        }

        Assert.Equal(expected, actual);
    }
}