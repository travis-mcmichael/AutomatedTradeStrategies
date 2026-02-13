using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Tests.Signals;

public class TwoGreenBarsSignalTest
{
    [Fact]
    public void ReturnsTrue_WhenLastTwoBarsAreGreen()
    {
        var bars = new List<Bar>
        {
            new (80000, 100, 105, 99, 104, 1000),
            new (80001, 104, 110, 103, 109, 1200),
        };

        var signal = new TwoGreenBars();
        Assert.True(signal.IsTriggered(bars));
    }
}