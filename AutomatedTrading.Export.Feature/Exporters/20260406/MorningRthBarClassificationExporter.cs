using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Export.Feature.Exporters;

/// <summary>
/// Generates one row per 1-minute bar during the RTH morning window (08:30–11:00 CT).
/// Target: 1 if price is at least 1 point higher 5 bars ahead, else 0.
///
/// SessionOpenReturn is anchored to the RTH open (08:30).
/// By 08:30 there are already overnight bars available for lookback context.
/// </summary>
public class MorningRthBarClassificationExporter
{
    private static readonly TimeSpan WindowEnd = new(11, 0, 0);

    private const int MinDayLookback  = 20;
    private const int MinBarLookback  = 20;
    private const double PointThreshold = 1.0;
    private const int ForwardBars     = 5;

    private readonly string outputPath;
    private readonly IBarRepository barRepository;

    public MorningRthBarClassificationExporter(string outputPath, IBarRepository barRepository)
    {
        this.outputPath = outputPath;
        this.barRepository = barRepository;
    }

    public async Task GenerateFeatures(DateTime start, DateTime end)
    {
        Console.WriteLine("Loading 1-minute bars from database...");
        var minuteBarsBySession = new Dictionary<string, List<BarData>>();

        await barRepository.StreamBarsAsync(start, end, bar =>
        {
            if (bar.SessionDate == null) return Task.CompletedTask;
            if (!minuteBarsBySession.TryGetValue(bar.SessionDate, out var list))
            {
                list = new List<BarData>();
                minuteBarsBySession[bar.SessionDate] = list;
            }
            list.Add(bar);
            return Task.CompletedTask;
        });

        Console.WriteLine($"Loaded {minuteBarsBySession.Count} sessions. Building features...");

        var sessionDates = minuteBarsBySession.Keys.OrderBy(x => x).ToList();
        var dailyBars    = sessionDates
            .Select(d => BarData.Aggregate(minuteBarsBySession[d]))
            .ToList();

        var headerWritten = false;
        var rowsWritten   = 0;

        using var writer = new StreamWriter(outputPath);

        for (int i = MinDayLookback; i < sessionDates.Count; i++)
        {
            var j = i - 1;

            var currentMinuteBars = minuteBarsBySession[sessionDates[i]];
            if (!currentMinuteBars.Any()) continue;

            try
            {
                var atr10 = Computations.ATR(dailyBars, j, 10);
                var atr20 = Computations.ATR(dailyBars, j, 20);
                if (atr20 == 0) continue;

                var rsi14   = Computations.RSI(dailyBars, j, 14);
                var ret1D   = Computations.Return(dailyBars, j, 1);
                var sessionDate = DateTime.ParseExact(sessionDates[i], "yyyyMMdd", null);

                var orderedBars = currentMinuteBars.OrderBy(b => b.Timestamp).ToList();

                // RTH open anchor (08:30 bar)
                var rthOpenBar = orderedBars.FirstOrDefault(b =>
                    b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME);
                if (rthOpenBar == null) continue;
                var sessionOpen = rthOpenBar.Open;

                // Window: 08:30–11:00
                var windowIndices = Enumerable.Range(0, orderedBars.Count)
                    .Where(k =>
                        orderedBars[k].Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
                        orderedBars[k].Timestamp.TimeOfDay <  WindowEnd)
                    .ToList();

                if (windowIndices.Count == 0) continue;

                var runningHigh = buildRunningHigh(orderedBars);
                var runningLow  = buildRunningLow(orderedBars);

                for (int wi = 0; wi < windowIndices.Count; wi++)
                {
                    var barIdx = windowIndices[wi];
                    if (barIdx < MinBarLookback)                   continue;
                    if (barIdx + ForwardBars >= orderedBars.Count) continue;

                    var bar       = orderedBars[barIdx];
                    var futureBar = orderedBars[barIdx + ForwardBars];
                    var lookback  = orderedBars.GetRange(barIdx - MinBarLookback, MinBarLookback);

                    var closePrices = lookback.Select(b => b.Close).Append(bar.Close).ToList();
                    var returns     = closePrices.Zip(closePrices.Skip(1), (a, b) => b - a).ToList();

                    var vol10  = StdDev(returns.TakeLast(10));
                    var vol20  = StdDev(returns);
                    var avgVol = lookback.Average(b => b.Volume);
                    var stdVol = StdDev(lookback.Select(b => b.Volume));
                    var volZ20 = stdVol > 0 ? (bar.Volume - avgVol) / stdVol : 0.0;
                    var timePos = windowIndices.Count > 1 ? (double)wi / (windowIndices.Count - 1) : 0.5;

                    var target = futureBar.Close >= bar.Close + PointThreshold ? 1 : 0;

                    var features = new double[]
                    {
                        Operations.SafeDiv(returns[^1], atr20),                                     // Return1
                        Operations.SafeDiv(bar.Close - lookback[MinBarLookback - 5].Close,  atr20), // Return5
                        Operations.SafeDiv(bar.Close - lookback[MinBarLookback - 10].Close, atr20), // Return10
                        Operations.SafeDiv(vol10, atr20),                                           // RollingVol10
                        Operations.SafeDiv(vol20, atr20),                                           // RollingVol20
                        Operations.SafeDiv(bar.High - bar.Low, atr20),                              // BarRange
                        Operations.SafeDiv(Math.Abs(bar.Close - bar.Open), atr20),                  // BarBody
                        Operations.SafeDiv(bar.High - Math.Max(bar.Open, bar.Close), atr20),        // UpperWick
                        Operations.SafeDiv(Math.Min(bar.Open, bar.Close) - bar.Low, atr20),         // LowerWick
                        volZ20,                                                                      // VolumeZScore20
                        Operations.SafeDiv(bar.Close - sessionOpen, atr20),                         // SessionOpenReturn
                        Operations.SafeDiv(runningHigh[barIdx] - bar.Close, atr20),                 // DistFromHighSoFar
                        Operations.SafeDiv(bar.Close - runningLow[barIdx], atr20),                  // DistFromLowSoFar
                        timePos,                                                                     // TimePositionInWindow
                        Operations.SafeDiv(atr10, atr20),                                           // DailyVolatilityRegime
                        rsi14,                                                                       // DailyRSI14
                        ret1D,                                                                       // DailyReturn1D
                        (int)sessionDate.DayOfWeek,                                                  // DayOfWeek
                        sessionDate.Day,                                                             // DayOfMonth
                    };

                    if (!headerWritten)
                    {
                        await writer.WriteLineAsync(
                            "Return1,Return5,Return10,RollingVol10,RollingVol20," +
                            "BarRange,BarBody,UpperWick,LowerWick,VolumeZScore20," +
                            "SessionOpenReturn,DistFromHighSoFar,DistFromLowSoFar,TimePositionInWindow," +
                            "DailyVolatilityRegime,DailyRSI14,DailyReturn1D,DayOfWeek,DayOfMonth," +
                            "TargetUpOneIn5Bars"
                        );
                        headerWritten = true;
                    }

                    await writer.WriteLineAsync(
                        string.Join(",", features.Select(f => f.ToString("G6"))) +
                        "," + target
                    );

                    rowsWritten++;
                    if (rowsWritten % 10000 == 0)
                        Console.WriteLine($"  {rowsWritten} rows written...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error on session {sessionDates[i]}: {ex.Message}");
            }
        }

        Console.WriteLine($"Feature export complete. {rowsWritten} rows written to {outputPath}");
    }

    private static double[] buildRunningHigh(List<BarData> bars)
    {
        var result = new double[bars.Count];
        result[0] = bars[0].High;
        for (int k = 1; k < bars.Count; k++)
            result[k] = Math.Max(result[k - 1], bars[k].High);
        return result;
    }

    private static double[] buildRunningLow(List<BarData> bars)
    {
        var result = new double[bars.Count];
        result[0] = bars[0].Low;
        for (int k = 1; k < bars.Count; k++)
            result[k] = Math.Min(result[k - 1], bars[k].Low);
        return result;
    }

    private static double StdDev(IEnumerable<double> values)
    {
        var arr = values.ToArray();
        if (arr.Length < 2) return 0;
        var mean = arr.Average();
        return Math.Sqrt(arr.Average(v => (v - mean) * (v - mean)));
    }
}
