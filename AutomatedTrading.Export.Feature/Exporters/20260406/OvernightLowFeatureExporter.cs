using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Export.Feature.Exporters;

/// <summary>
/// Generates one row per session.
/// Target: (sessionOpen - overnightLow) / ATR20 — how far price dropped below the 16:00 session open
/// during the overnight window (16:00 through pre-RTH, i.e., before 08:30).
///
/// All features are known at 16:00 when the overnight begins.
/// Previous session RTH intraday structure is downside-tuned to better predict overnight weakness.
/// </summary>
public class OvernightLowFeatureExporter
{
    private const int MinLookback = 20;
    private readonly string outputPath;
    private readonly IBarRepository barRepository;

    public OvernightLowFeatureExporter(string outputPath, IBarRepository barRepository)
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
        var dailyBars = sessionDates
            .Select(d => BarData.Aggregate(minuteBarsBySession[d]))
            .ToList();

        var headerWritten = false;
        var rowsWritten = 0;

        using var writer = new StreamWriter(outputPath);

        for (int i = MinLookback; i < sessionDates.Count; i++)
        {
            var j = i - 1; // index of most recent COMPLETE session in dailyBars

            var currentMinuteBars = minuteBarsBySession[sessionDates[i]];
            var prevMinuteBars    = minuteBarsBySession[sessionDates[j]];

            if (!currentMinuteBars.Any() || !prevMinuteBars.Any()) continue;

            try
            {
                var orderedCurrent = currentMinuteBars.OrderBy(b => b.Timestamp).ToList();

                var overnightBars = orderedCurrent.Where(b =>
                    b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME ||
                    b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();

                if (!overnightBars.Any()) continue;

                var sessionOpen   = overnightBars.OrderBy(b => b.Timestamp).First().Open;
                var overnightLow  = overnightBars.Min(b => b.Low);

                var atr5  = Computations.ATR(dailyBars, j, 5);
                var atr10 = Computations.ATR(dailyBars, j, 10);
                var atr20 = Computations.ATR(dailyBars, j, 20);
                if (atr20 == 0) continue;

                var sma5       = Computations.SMA(dailyBars, j, 5,  b => b.Close);
                var sma10      = Computations.SMA(dailyBars, j, 10, b => b.Close);
                var sma20      = Computations.SMA(dailyBars, j, 20, b => b.Close);
                var avgRange5  = Computations.AverageRange(dailyBars, j, 5);
                var avgRange10 = Computations.AverageRange(dailyBars, j, 10);
                var rsi14      = Computations.RSI(dailyBars, j, 14);
                var current    = dailyBars[j];

                var sessionDate = DateTime.ParseExact(sessionDates[i], "yyyyMMdd", null);
                var prevRth     = computePrevRthFeatures(prevMinuteBars, atr20);
                var target      = (sessionOpen - overnightLow) / atr20;

                var features = new double[]
                {
                    // --- Previous complete session shape ---
                    Operations.SafeDiv(current.High, sessionOpen),   // PrevHighRatio
                    Operations.SafeDiv(current.Low,  sessionOpen),   // PrevLowRatio
                    Computations.HighLowRange(current, atr20),       // PrevRange
                    Computations.BodySize(current, atr20),           // PrevBodySize
                    Computations.UpperWick(current, atr20),          // PrevUpperWick
                    Computations.LowerWick(current, atr20),          // PrevLowerWick
                    Computations.ClosePosition(current),             // PrevClosePosition (0=near low, 1=near high)

                    // --- Volatility / trend context ---
                    Operations.SafeDiv(atr5,  sessionOpen),          // ATR5
                    Operations.SafeDiv(atr10, sessionOpen),          // ATR10
                    Operations.SafeDiv(atr20, sessionOpen),          // ATR20
                    avgRange5,                                       // AvgRange5
                    avgRange10,                                      // AvgRange10
                    Operations.SafeDiv(current.High - current.Low, avgRange10), // RangeRatio
                    Computations.Return(dailyBars, j, 1),            // Return1D
                    Computations.Return(dailyBars, j, 3),            // Return3D
                    Computations.Return(dailyBars, j, 5),            // Return5D
                    Operations.SafeDiv(sma5,  sma20),                // SMA5SMA20Ratio
                    Operations.SafeDiv(sma10, sma20),                // SMA10SMA20Ratio
                    Operations.SafeDiv(sessionOpen, sma5),           // OpenSMA5Ratio
                    Operations.SafeDiv(sessionOpen, sma10),          // OpenSMA10Ratio
                    Operations.SafeDiv(sessionOpen, sma20),          // OpenSMA20Ratio

                    // --- Volume ---
                    Computations.SMA(dailyBars, j, 20, b => b.Volume),  // VolumeMA20
                    Computations.VolumeZScore(dailyBars, j, 20),        // VolumeZScore20

                    // --- Gap / distance from key levels ---
                    Operations.SafeDiv(sessionOpen - current.High,  atr20), // DistanceFromPrevHigh
                    Operations.SafeDiv(sessionOpen - current.Low,   atr20), // DistanceFromPrevLow
                    Operations.SafeDiv(sessionOpen - current.Close, atr20), // GapVsATR

                    // --- Regime ---
                    Operations.SafeDiv(atr10, atr20),                // VolatilityRegime
                    rsi14,                                           // RSI14

                    // --- Calendar ---
                    (int)sessionDate.DayOfWeek,                      // DayOfWeek
                    sessionDate.Day,                                  // DayOfMonth

                    // --- Previous session RTH structure (downside-tuned) ---
                    prevRth.RthRange,                                // PrevRthRange
                    prevRth.RthFirstHalfHourRange,                   // PrevRthFirstHalfHourRange
                    prevRth.RthFirstHourRange,                       // PrevRthFirstHourRange
                    prevRth.RthLastHourRange,                        // PrevRthLastHourRange
                    prevRth.RthLowTimeRatio,                         // PrevRthLowTimeRatio
                    prevRth.RthOpenToHighRatio,                      // PrevRthOpenToHighRatio
                    prevRth.RthOpenToLowRatio,                       // PrevRthOpenToLowRatio
                    prevRth.RthLastHourClosePosition,                // PrevRthLastHourClosePosition
                };

                if (!headerWritten)
                {
                    await writer.WriteLineAsync(
                        "PrevHighRatio,PrevLowRatio,PrevRange,PrevBodySize,PrevUpperWick,PrevLowerWick,PrevClosePosition," +
                        "ATR5,ATR10,ATR20,AvgRange5,AvgRange10,RangeRatio," +
                        "Return1D,Return3D,Return5D," +
                        "SMA5SMA20Ratio,SMA10SMA20Ratio," +
                        "OpenSMA5Ratio,OpenSMA10Ratio,OpenSMA20Ratio," +
                        "VolumeMA20,VolumeZScore20," +
                        "DistanceFromPrevHigh,DistanceFromPrevLow,GapVsATR," +
                        "VolatilityRegime,RSI14," +
                        "DayOfWeek,DayOfMonth," +
                        "PrevRthRange,PrevRthFirstHalfHourRange,PrevRthFirstHourRange,PrevRthLastHourRange," +
                        "PrevRthLowTimeRatio,PrevRthOpenToHighRatio,PrevRthOpenToLowRatio,PrevRthLastHourClosePosition," +
                        "TargetOvernightLow"
                    );
                    headerWritten = true;
                }

                await writer.WriteLineAsync(
                    string.Join(",", features.Select(f => f.ToString("G6"))) +
                    "," + target.ToString("G6")
                );

                rowsWritten++;
                if (rowsWritten % 100 == 0)
                    Console.WriteLine($"  {rowsWritten} rows written...");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error on session {sessionDates[i]}: {ex.Message}");
            }
        }

        Console.WriteLine($"Feature export complete. {rowsWritten} rows written to {outputPath}");
    }

    private RthIntradayFeatures computePrevRthFeatures(List<BarData> bars, double atr20)
    {
        if (!bars.Any()) return new RthIntradayFeatures();

        var rth = bars.OrderBy(b => b.Timestamp)
            .Where(b =>
                b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
                b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME).ToList();

        if (!rth.Any()) return new RthIntradayFeatures();

        var rthOpen = rth.First().Open;

        var firstHalfHour = rth.Where(b =>
            b.Timestamp.TimeOfDay < AppConstants.RTH_START_TIME + TimeSpan.FromMinutes(30)).ToList();
        var firstHour = rth.Where(b =>
            b.Timestamp.TimeOfDay < AppConstants.RTH_START_TIME + TimeSpan.FromHours(1)).ToList();
        var lastHour = rth.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_END_TIME - TimeSpan.FromHours(1)).ToList();

        var rthHigh = rth.Max(b => b.High);
        var rthLow  = rth.Min(b => b.Low);

        var lowBar      = rth.MinBy(b => b.Low)!;
        var lowBarIndex = rth.IndexOf(lowBar);
        var lowTimeRatio = rth.Count > 1 ? (double)lowBarIndex / (rth.Count - 1) : 0.5;

        double lastHourClosePosition = 0.5;
        if (lastHour.Any())
        {
            var lastHourHigh  = lastHour.Max(b => b.High);
            var lastHourLow   = lastHour.Min(b => b.Low);
            var lastHourClose = lastHour.OrderBy(b => b.Timestamp).Last().Close;
            lastHourClosePosition = lastHourHigh == lastHourLow
                ? 0.5
                : (lastHourClose - lastHourLow) / (lastHourHigh - lastHourLow);
        }

        return new RthIntradayFeatures
        {
            RthRange = Operations.SafeDiv(rthHigh - rthLow, atr20),
            RthFirstHalfHourRange = firstHalfHour.Any()
                ? Operations.SafeDiv(firstHalfHour.Max(b => b.High) - firstHalfHour.Min(b => b.Low), atr20) : 0,
            RthFirstHourRange = firstHour.Any()
                ? Operations.SafeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr20) : 0,
            RthLastHourRange = lastHour.Any()
                ? Operations.SafeDiv(lastHour.Max(b => b.High) - lastHour.Min(b => b.Low), atr20) : 0,
            RthLowTimeRatio      = lowTimeRatio,
            RthOpenToHighRatio   = Operations.SafeDiv(rthHigh - rthOpen, atr20),
            RthOpenToLowRatio    = Operations.SafeDiv(rthOpen  - rthLow,  atr20),
            RthLastHourClosePosition = lastHourClosePosition,
        };
    }

    private class RthIntradayFeatures
    {
        public double RthRange { get; set; }
        public double RthFirstHalfHourRange { get; set; }
        public double RthFirstHourRange { get; set; }
        public double RthLastHourRange { get; set; }
        public double RthLowTimeRatio { get; set; }
        public double RthOpenToHighRatio { get; set; }
        public double RthOpenToLowRatio { get; set; }
        public double RthLastHourClosePosition { get; set; }
    }
}
