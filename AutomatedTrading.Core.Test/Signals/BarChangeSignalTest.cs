using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;
using AutomatedTrading.Core.Framework;

namespace AutomatedTrading.Core.Test.Signals;

public class BarChangeSignalTest
{
    [Fact]
    public void ReturnsTrue_WhenValueIncreasesALot()
    {
        var pointChangeThreshold = 10;
        var barMax = 5;

        var bars = new List<BarData>
        {
            new (84900, 6977.25, 6983, 6977.5, 6978.25, 4119),
            new (85000, 6978.25, 6980, 6976.75, 6980, 2717),
            new (85100, 6980, 6986.25, 6979.50, 6986.25, 5994),
            new (85200, 6986, 6987.5, 6983.25, 6986.75, 4442),
            new (85300, 6986.75, 6989.25, 6983.75, 6988.75, 4158),
            new (85400, 6988.5, 6991, 6987.25, 6990.25, 4487),
            new (85500, 6990.5, 6994.5, 6998.75, 6997, 4505),
        };

        var signal = new BarChange(pointChangeThreshold, barMax, PriceDirection.INCREASING);
        Assert.True(signal.IsTriggered(bars));
    }

    [Fact]
    public void ReturnsTrue_WhenValueDecreasesALot()
    {
        var pointChangeThreshold = 10;
        var barMax = 5;

        var bars = new List<BarData>
        {
            new (205200, 6920.5, 6921, 6916.5, 6917.75, 1968),
            new (205300, 6917.75, 6918, 6915.25, 6917.25, 1192),
            new (205400, 6917.75, 6919, 6914.25, 6915, 1311),
            new (205500, 6915.25, 6916.25, 6909, 6910.5, 2441),
            new (205600, 6910.25, 6911, 6906.25, 6906.25, 1976),
            new (205700, 6906.25, 6906.5, 6901.75, 6904.75, 2383),
        };
        var signal = new BarChange(pointChangeThreshold, barMax, PriceDirection.DECREASING);
        Assert.True(signal.IsTriggered(bars));
    }

    [Fact]
    public void ReturnsFalse_WhenValueIncreasesALittle()
    {
        var pointChangeThreshold = 10;
        var barMax = 5;

        var bars = new List<BarData>
        {
            new (203100, 6920.75, 6922.5, 6919.75, 6920.5, 2179),
            new (203200, 6920.25, 6922.75, 6918.75, 6919, 1818),
            new (203300, 6919.25, 6920.5, 6918.25, 6920.25, 2039),
            new (203400, 6920.25, 6924.75, 6920.25, 6923.75, 1820),
            new (203500, 6924, 6926.25, 6923, 6924.25, 1461),
        };

        var signal = new BarChange(pointChangeThreshold, barMax, PriceDirection.INCREASING);
        Assert.False(signal.IsTriggered(bars));
    }
}