using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AutomatedTrading.Core.Models;
using AutomatedTrading.Core.Signals;

namespace AutomatedTrading.Core.Tests.Signals;

public class IsSessionLowSignalTest
{
    [Fact]
    public void ReturnsTrue_WhenIsSessionLow()
    {
        var testFile = "MES.20260202.dat";

        var path = Path.Combine(AppContext.BaseDirectory, AppConstants.TEST_BASE_DIRECTORY);
        var bars = new LoadTestData(path)
            .LoadFile(testFile)
            .Take(18);

        var expected = bars.Select(x => x.Time == 180000);
        var sessionLow = bars.Min(x => x.Close);
        var signalResults = bars.Select(x => new IsSessionLow(180000, sessionLow, bars.Count()).IsTriggered(new List<Bar> { x }));
        Assert.Equal(expected, signalResults);
    }
}