using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;
using System.Collections.Generic;

namespace AutomatedTrading.Core.Tests.Signals;

public class InTradeDaySignalTest
{
    [Fact]
    public void ReturnsTrue_WhenBetween8and4()
    {
        var bars = new List<Bar>
        {
            new (80000, 100, 105, 99, 104, 1000)
        };

        var signal = new InTradeDay();
        Assert.True(signal.IsTriggered(bars));
    }
}