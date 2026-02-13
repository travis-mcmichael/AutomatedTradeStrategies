using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Tests.Signals;

public class InTradeWindowSignalTest
{
    [Fact]
    public void ReturnsTrue_WhenInTradeWindow()
    {
        var bars = new List<Bar>
        {
            new (80000, 100, 105, 99, 104, 1000)
        };

        var signal = new InTradeWindow(70000, 120000);
        Assert.True(signal.IsTriggered(bars));
    }
}