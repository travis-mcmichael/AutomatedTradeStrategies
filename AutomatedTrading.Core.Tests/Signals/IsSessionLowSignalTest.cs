using System.Collections.Generic;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;

namespace AutomatedTrading.Core.Tests.Signals;

public class IsSessionLowSignalTest
{
    [Fact]
    public void ReturnsTrue_WhenIsSessionLow()
    {
        var low = 99.0;
        var bars = new List<Bar> { new(80000, 100, 105, low, 104, 1000) };

        var signal = new IsSessionLow(80000, low, bars.Count);
        Assert.True(signal.IsTriggered(bars));
    }
}