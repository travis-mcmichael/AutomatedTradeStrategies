using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;

namespace AutomatedTrading.Core.Tests.Signals;

public class TwoGreenBarsSignalTest
{
    public void ReturnsTrue_WhenLastTwoBarsAreGreen()
    {
        var bars = new List<Bar>
        {
            new (DateTime.Now.AddMinutes(-2), 100, 105, 99, 104, 1000),
            new (DateTime.Now.AddMinutes(-1), 104, 110, 103, 109, 1200),
        };

        var signal = new TwoGreenBars();
        Assert.True(signal.IsTriggered(bars));
    }
}