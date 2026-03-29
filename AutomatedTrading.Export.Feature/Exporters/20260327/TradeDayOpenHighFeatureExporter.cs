using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Export.Feature.Exporters;

/// <summary>
/// Generates one row per trade day (RTH session).
/// Target: (rthHigh - rthOpen) / ATR20  — how far price ran above the RTH open.
///
/// Key differences vs SessionOpenHighFeatureExporter:
///   - Anchor price is the RTH open (08:30) instead of the session open (16:00)
///   - High/Low are measured within RTH only (08:30–15:15)
///   - Pre-market features are added as current-day context (known before RTH opens)
///   - Previous session intraday structure uses RTH-only windows
/// </summary>
public class TradeDayOpenHighFeatureExporter
{
    private const int MinLookback = 20;
    private readonly string outputPath;
    private readonly IBarRepository barRepository;

    public TradeDayOpenHighFeatureExporter(string outputPath, IBarRepository barRepository)
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
            var prevMinuteBars = minuteBarsBySession[sessionDates[j]];

            if (!currentMinuteBars.Any() || !prevMinuteBars.Any()) continue;

            try
            {
                var orderedCurrent = currentMinuteBars.OrderBy(b => b.Timestamp).ToList();

                var rthBars = orderedCurrent.Where(b =>
                    b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
                    b.Timestamp.TimeOfDay <  AppConstants.RTH_END_TIME).ToList();

                if (!rthBars.Any()) continue;

                var tradeDayOpen = rthBars.OrderBy(b => b.Timestamp).First().Open;
                var tradeDayHigh = rthBars.Max(b => b.High);

                var atr5 = Computations.ATR(dailyBars, j, 5);
                var atr10 = Computations.ATR(dailyBars, j, 10);
                var atr20 = Computations.ATR(dailyBars, j, 20);
                if (atr20 == 0) continue;

                var sma5 = Computations.SMA(dailyBars, j, 5,  b => b.Close);
                var sma10 = Computations.SMA(dailyBars, j, 10, b => b.Close);
                var sma20 = Computations.SMA(dailyBars, j, 20, b => b.Close);
                var avgRange5 = Computations.AverageRange(dailyBars, j, 5);
                var avgRange10 = Computations.AverageRange(dailyBars, j, 10);
                var rsi14 = Computations.RSI(dailyBars, j, 14);
                var current = dailyBars[j];

                var sessionDate = DateTime.ParseExact(sessionDates[i], "yyyyMMdd", null);
                var preMarket = computePreMarketFeatures(orderedCurrent, current.Close, tradeDayOpen, atr20);
                var intraday = computePrevRthFeatures(prevMinuteBars, atr20);
                var target = (tradeDayHigh - tradeDayOpen) / atr20;

                var features = new double[]
                {
                    // --- Previous complete session shape ---
                    Operations.SafeDiv(current.High, tradeDayOpen),  // PrevHighRatio
                    Operations.SafeDiv(current.Low, tradeDayOpen),  // PrevLowRatio
                    Computations.HighLowRange(current, atr20),        // PrevRange
                    Computations.BodySize(current, atr20),            // PrevBodySize
                    Computations.UpperWick(current, atr20),           // PrevUpperWick
                    Computations.LowerWick(current, atr20),           // PrevLowerWick
                    Computations.ClosePosition(current),              // PrevClosePosition

                    // --- Volatility / trend context ---
                    Operations.SafeDiv(atr5, tradeDayOpen),          // ATR5
                    Operations.SafeDiv(atr10, tradeDayOpen),          // ATR10
                    Operations.SafeDiv(atr20, tradeDayOpen),          // ATR20
                    avgRange5,                                        // AvgRange5
                    avgRange10,                                       // AvgRange10
                    Operations.SafeDiv(current.High - current.Low, avgRange10), // RangeRatio
                    Computations.Return(dailyBars, j, 1),             // Return1D
                    Computations.Return(dailyBars, j, 3),             // Return3D
                    Computations.Return(dailyBars, j, 5),             // Return5D
                    Operations.SafeDiv(sma5, sma20),                 // SMA5SMA20Ratio
                    Operations.SafeDiv(sma10, sma20),                 // SMA10SMA20Ratio
                    Operations.SafeDiv(tradeDayOpen, sma5),           // OpenSMA5Ratio
                    Operations.SafeDiv(tradeDayOpen, sma10),          // OpenSMA10Ratio
                    Operations.SafeDiv(tradeDayOpen, sma20),          // OpenSMA20Ratio

                    // --- Volume ---
                    Computations.SMA(dailyBars, j, 20, b => b.Volume), // VolumeMA20
                    Computations.VolumeZScore(dailyBars, j, 20),       // VolumeZScore20

                    // --- Gap / distance from key levels (vs RTH open) ---
                    Operations.SafeDiv(tradeDayOpen - current.High, atr20), // DistanceFromPrevHigh
                    Operations.SafeDiv(tradeDayOpen - current.Low, atr20), // DistanceFromPrevLow
                    Operations.SafeDiv(tradeDayOpen - current.Close, atr20), // GapVsATR

                    // --- Regime ---
                    Operations.SafeDiv(atr10, atr20),                 // VolatilityRegime
                    rsi14,                                            // RSI14

                    // --- Calendar ---
                    (int)sessionDate.DayOfWeek,                       // DayOfWeek
                    sessionDate.Day,                                  // DayOfMonth

                    // --- Current day pre-market context (known before RTH) ---
                    preMarket.PreMarketRange,                         // PreMarketRange
                    preMarket.PreMarketHighVsOpen,                    // PreMarketHighVsOpen
                    preMarket.PreMarketLowVsOpen,                     // PreMarketLowVsOpen
                    preMarket.GapToRthOpen,                           // GapToRthOpen
                    preMarket.PreMarketVolumeRatio,                   // PreMarketVolumeRatio

                    // --- Previous session RTH structure (upside-tuned) ---
                    intraday.RthRange,                                // PrevRthRange
                    intraday.RthFirstHalfHourRange,                   // PrevRthFirstHalfHourRange
                    intraday.RthFirstHourRange,                       // PrevRthFirstHourRange
                    intraday.RthLastHourRange,                        // PrevRthLastHourRange
                    intraday.RthHighTimeRatio,                        // PrevRthHighTimeRatio
                    intraday.RthOpenToHighRatio,                      // PrevRthOpenToHighRatio
                    intraday.RthOpenToLowRatio,                       // PrevRthOpenToLowRatio
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
                        "PreMarketRange,PreMarketHighVsOpen,PreMarketLowVsOpen,GapToRthOpen,PreMarketVolumeRatio," +
                        "PrevRthRange,PrevRthFirstHalfHourRange,PrevRthFirstHourRange,PrevRthLastHourRange," +
                        "PrevRthHighTimeRatio,PrevRthOpenToHighRatio,PrevRthOpenToLowRatio," +
                        "TargetTradeDayHigh"
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

    /// <summary>
    /// Features derived from current-day pre-market bars (session open through 08:30).
    /// All values are known before RTH opens and can be used as leading indicators.
    /// </summary>
    private PreMarketFeatures computePreMarketFeatures(
        List<BarData> orderedBars, double prevClose, double tradeDayOpen, double atr20)
    {
        var preMarket = orderedBars.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.SESSION_OPEN_TIME ||
            b.Timestamp.TimeOfDay <  AppConstants.RTH_START_TIME).ToList();

        if (!preMarket.Any())
            return new PreMarketFeatures();

        var pmHigh = preMarket.Max(b => b.High);
        var pmLow = preMarket.Min(b => b.Low);
        var pmVolume = preMarket.Sum(b => b.Volume);
        var totalVolume = orderedBars.Sum(b => b.Volume);

        return new PreMarketFeatures
        {
            // Pre-market high-low range normalised by ATR
            PreMarketRange = Operations.SafeDiv(pmHigh - pmLow, atr20),
            // How far above the RTH open did price trade pre-market (upside strength)
            PreMarketHighVsOpen = Operations.SafeDiv(pmHigh - tradeDayOpen, atr20),
            // How far below the RTH open did price trade pre-market (downside weakness)
            PreMarketLowVsOpen = Operations.SafeDiv(pmLow  - tradeDayOpen, atr20),
            // RTH open vs previous day's close (overnight gap)
            GapToRthOpen = Operations.SafeDiv(tradeDayOpen - prevClose, atr20),
            // Pre-market share of total session volume
            PreMarketVolumeRatio = Operations.SafeDiv(pmVolume, totalVolume),
        };
    }

    /// <summary>
    /// RTH-only intraday structure from the previous session (upside-tuned).
    /// </summary>
    private RthIntradayFeatures computePrevRthFeatures(List<BarData> bars, double atr20)
    {
        if (!bars.Any()) return new RthIntradayFeatures();

        var rth = bars.OrderBy(b => b.Timestamp)
            .Where(b =>
                b.Timestamp.TimeOfDay >= AppConstants.RTH_START_TIME &&
                b.Timestamp.TimeOfDay < AppConstants.RTH_END_TIME).ToList();

        if (!rth.Any()) return new RthIntradayFeatures();

        var rthOpen = rth.First().Open;

        var firstHalfHour = rth.Where(b =>
            b.Timestamp.TimeOfDay < AppConstants.RTH_START_TIME + TimeSpan.FromMinutes(30)).ToList();
        var firstHour = rth.Where(b =>
            b.Timestamp.TimeOfDay < AppConstants.RTH_START_TIME + TimeSpan.FromHours(1)).ToList();
        var lastHour = rth.Where(b =>
            b.Timestamp.TimeOfDay >= AppConstants.RTH_END_TIME - TimeSpan.FromHours(1)).ToList();

        var rthHigh = rth.Max(b => b.High);
        var rthLow = rth.Min(b => b.Low);

        var highBar = rth.MaxBy(b => b.High)!;
        var highBarIndex = rth.IndexOf(highBar);
        var highTimeRatio = rth.Count > 1 ? (double)highBarIndex / (rth.Count - 1) : 0.5;

        return new RthIntradayFeatures
        {
            RthRange = Operations.SafeDiv(rthHigh - rthLow, atr20),
            RthFirstHalfHourRange = firstHalfHour.Any()
                ? Operations.SafeDiv(firstHalfHour.Max(b => b.High) - firstHalfHour.Min(b => b.Low), atr20) : 0,
            RthFirstHourRange = firstHour.Any()
                ? Operations.SafeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr20) : 0,
            RthLastHourRange = lastHour.Any()
                ? Operations.SafeDiv(lastHour.Max(b => b.High) - lastHour.Min(b => b.Low), atr20) : 0,
            RthHighTimeRatio = highTimeRatio,
            RthOpenToHighRatio = Operations.SafeDiv(rthHigh - rthOpen, atr20),
            RthOpenToLowRatio = Operations.SafeDiv(rthOpen  - rthLow,  atr20),
        };
    }

    private class PreMarketFeatures
    {
        public double PreMarketRange { get; set; }
        public double PreMarketHighVsOpen { get; set; }
        public double PreMarketLowVsOpen { get; set; }
        public double GapToRthOpen { get; set; }
        public double PreMarketVolumeRatio { get; set; }
    }

    private class RthIntradayFeatures
    {
        public double RthRange { get; set; }
        public double RthFirstHalfHourRange { get; set; }
        public double RthFirstHourRange { get; set; }
        public double RthLastHourRange { get; set; }
        public double RthHighTimeRatio { get; set; }
        public double RthOpenToHighRatio { get; set; }
        public double RthOpenToLowRatio { get; set; }
    }
}
