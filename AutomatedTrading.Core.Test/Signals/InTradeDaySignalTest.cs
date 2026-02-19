using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Test.Signals;

public class InTradeDaySignalTest
{
    [Fact]
    public void ReturnsTrue_WhenBetweenEightThirtyandFour()
    {
        var bars = new List<Bar>
        {
            new (83100, 100, 105, 99, 104, 1000)
        };

        var signal = new InTradeDay();
        Assert.True(signal.IsTriggered(bars));
    }
}