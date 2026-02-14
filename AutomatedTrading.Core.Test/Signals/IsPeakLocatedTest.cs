using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;

namespace AutomatedTrading.Core.Tests.Signals;

public class IsPeakLocatedTest
{
    [Fact]
    public void ReturnsTrue_WhenPeakLocated()
    {
        var strength = 3;
        var ignoreThreshold = 2; // ignore price movements below a threshold when considering a peak i.e. 
        var pointMovementThreshold = 5; // minimum total point movement from high to low that must be reached to be considered a peak 

        var testFile = "MES.20260202.dat";
        var dataDate = "20260205";

        var tradeWindowStart = 91000;
        var tradeWindowEnd = 102000;

        var path = Path.Combine(AppContext.BaseDirectory, AppConstants.TEST_BASE_DIRECTORY);
        var bars = new LoadTestData(path)
            .LoadFile(testFile, dataDate)
            .Where(x => x.Time >= tradeWindowStart && x.Time < tradeWindowEnd)
            .ToList();
        var signal = new IsPeakLocated(strength, ignoreThreshold, pointMovementThreshold);

        var expected = new Dictionary<int, double> 
        { 
            { 92000, 6825.25 },
            { 92600, 6816.5 },
            { 93100, 6814.5 },
            { 94300, 6825.75 },
            { 95200, 6816.5 },
            { 100100, 6818.75 },
        };
        var actual = new Dictionary<int, double>();

        if (signal.IsTriggered(bars))
        {
            foreach (var bar in signal.Peaks)
            {
                System.Console.WriteLine(bar.ToLog());
                actual.Add(bar.Time, bar.Close);
            }
        }
        
        Assert.Equal(expected, actual);
    }
}