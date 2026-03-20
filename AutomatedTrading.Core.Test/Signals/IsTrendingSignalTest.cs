using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;
using AutomatedTrading.Core.Framework;

namespace AutomatedTrading.Core.Test.Signals;

public class IsTrendingSignalTest
{
    [Fact]
    public void ReturnsTrue_WhenIncreasing()
    {
        var strength = 3;
        var bars = new List<BarData>
        {
            new (81100, 6902.75, 6905.25, 6902.5, 6903.25, 283),
            new (81200, 6904, 6906.5, 6903.25, 6904.25, 514),
            new (81300, 6904.25, 6905.25, 6900.25, 6905.25, 467),
        };

        var signal = new IsTrending(strength, PriceDirection.INCREASING);
        Assert.True(signal.IsTriggered(bars));
    }

    [Fact]
    public void ReturnsTrue_WhenDecreasing()
    {
        var strength = 3;
        var bars = new List<BarData>
        {
            new (81100, 6906.25, 6905.25, 6900.25, 6905.25, 467),
            new (81200, 6905, 6906.5, 6903.25, 6904.25, 514),
            new (81300, 6904.75, 6905.25, 6902.5, 6903.25, 283),
        };

        var signal = new IsTrending(strength, PriceDirection.DECREASING);
        Assert.True(signal.IsTriggered(bars));
    }

    [Fact]
    public void ReturnsFalse_WhenNotIncreasingOrDecreasing()
    {
        var strength = 3;

        var bars = new List<BarData>
        {
            new (081400, 6904.5, 6905.75, 6904.25, 6904.75, 311),
            new (081500, 6904.75, 6904.75, 6902.75, 6903, 337),
            new (081600, 6902.75, 6905, 6903.25, 6904, 332),
        };

        var signal = new IsTrending(strength, PriceDirection.INCREASING);
        var signal2 = new IsTrending(strength, PriceDirection.DECREASING);

        Assert.False(signal.IsTriggered(bars));
        Assert.False(signal2.IsTriggered(bars));
    }
}