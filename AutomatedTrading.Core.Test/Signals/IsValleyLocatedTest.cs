using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;

namespace AutomatedTrading.Core.Tests.Signals;

public class IsValleyLocatedTest
{
    [Fact]
    public void ReturnsTrue_WhenValleyLocated()
    {
        var strength = 3;
        var pointMovementThreshold = 5;

        var testFile = "MES.20260202.dat";
        var dataDate = "20260205";

        var tradeWindowStart = 90000;
        var tradeWindowEnd = 100000;

        var path = Path.Combine(AppContext.BaseDirectory, AppConstants.TEST_BASE_DIRECTORY);
        var bars = new LoadTestData(path)
            .LoadFile(testFile, dataDate)
            .Where(x => x.Time >= tradeWindowStart && x.Time < tradeWindowEnd)
            .ToList();

        var signal = new IsValleyLocated(strength, pointMovementThreshold);
        var expected = new Dictionary<int, double>
        {
            { 91500, 6812 },
            { 92800, 6807.50 },
            { 93500, 6804.50 },
            { 94900, 6806 },
        };
        var actual = new Dictionary<int, double>();

        if (signal.IsTriggered(bars))
        {
            foreach (var bar in signal.Valleys)
            {
                System.Console.WriteLine(bar.ToLog());
                actual.Add(bar.Time, bar.Close);
            }
        }

        Assert.Equal(expected, actual);
    }
}