using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;

namespace AutomatedTrading.Core.Tests.Signals;

public class IsSessionLowSignalTest
{
    private const string TEST_FILE = "MES.20260202.dat";
    private const int SESSION_LOW_TIME = 1800;

    [Fact]
    public void ReturnsTrue_WhenIsSessionLow()
    {
        var path = Path.Combine(AppContext.BaseDirectory, AppConstants.TEST_BASE_DIRECTORY);
        var bars = new LoadTestData(path)
            .LoadFile(TEST_FILE)
            .Take(18);

        foreach (var bar in bars)
            Console.WriteLine(bar.ToLog());
        var expected = bars.Select(x => x.Time == SESSION_LOW_TIME);
        var sessionLow = bars.Select(x => x.Close).Distinct().Min();
        var signalResults = bars.Select(x => new IsSessionLow(SESSION_LOW_TIME, sessionLow, bars.Count()).IsTriggered(new List<Bar> { x }));

        Assert.Equal(expected, signalResults);
    }
}