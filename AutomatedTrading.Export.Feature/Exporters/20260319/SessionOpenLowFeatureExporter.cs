using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Export.Feature.Exporters;

/// <summary>
/// Generates one row per session.
/// Target: (sessionOpen - sessionLow) / ATR20  — how far the session dropped from the open.
///
/// Feature differences vs SessionOpenHighFeatureExporter:
///   - LowTimeRatio replaces HighTimeRatio  (when was the low hit in the prior session)
///   - OvernightDropRatio added              (overnight weakness predicts intraday lows)
///   - LastHourClosePosition added           (bearish close = sellers controlled into the close)
///   - PrevOpenToHighRatio dropped           (less relevant to downside)
/// </summary>
public class SessionOpenLowFeatureExporter
{
    private const int MinLookback = 20;

    // MES session times in CT
    private static readonly TimeSpan SessionOpenTime = new(16,  0, 0);
    private static readonly TimeSpan RthStartTime = new( 8, 30, 0);
    private static readonly TimeSpan RthEndTime = new(15, 15, 0);

    private readonly string outputPath;
    private readonly IBarRepository barRepository;

    public SessionOpenLowFeatureExporter(string outputPath, IBarRepository barRepository)
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
            .Select(d => aggregateSession(minuteBarsBySession[d]))
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
                var sessionOpen = currentMinuteBars.OrderBy(b => b.Timestamp).First().Open;
                var sessionLow  = currentMinuteBars.Min(b => b.Low);

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
                var intraday = computeIntradayFeatures(prevMinuteBars, atr20);
                var target = (sessionOpen - sessionLow) / atr20;

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
                    Operations.SafeDiv(sessionOpen - current.High, atr20),      // DistanceFromPrevHigh
                    Operations.SafeDiv(sessionOpen - current.Low,  atr20),      // DistanceFromPrevLow
                    Operations.SafeDiv(sessionOpen - current.Close, atr20),     // GapVsATR

                    // --- Regime ---
                    Operations.SafeDiv(atr10, atr20),                // VolatilityRegime
                    rsi14,                                           // RSI14

                    // --- Calendar ---
                    (int)sessionDate.DayOfWeek,                      // DayOfWeek
                    sessionDate.Day,                                 // DayOfMonth

                    // --- Previous session intraday structure (downside-tuned) ---
                    intraday.OvernightRange,                         // OvernightRange
                    intraday.OvernightVolumeRatio,                   // OvernightVolumeRatio
                    intraday.OvernightDropRatio,                     // OvernightDropRatio (overnight low drop / ATR)
                    intraday.RegularHoursRange,                      // RegularHoursRange
                    intraday.FirstHourRange,                         // FirstHourRange
                    intraday.LastHourRange,                          // LastHourRange
                    intraday.LowTimeRatio,                           // LowTimeRatio (0=early, 1=late)
                    intraday.PrevOpenToLowRatio,                     // PrevOpenToLowRatio
                    intraday.LastHourClosePosition,                  // LastHourClosePosition (0=near low, 1=near high)
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
                        "OvernightRange,OvernightVolumeRatio,OvernightDropRatio," +
                        "RegularHoursRange,FirstHourRange,LastHourRange," +
                        "LowTimeRatio,PrevOpenToLowRatio,LastHourClosePosition," +
                        "TargetSessionLow"
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

    private IntradayFeatures computeIntradayFeatures(List<BarData> bars, double atr20)
    {
        if (!bars.Any()) return new IntradayFeatures();

        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        var sessionOpen = ordered.First().Open;
        var totalVolume = ordered.Sum(b => b.Volume);

        var overnight = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= SessionOpenTime ||
            b.Timestamp.TimeOfDay <  RthStartTime).ToList();
        var rth = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= RthStartTime &&
            b.Timestamp.TimeOfDay <  RthEndTime).ToList();
        var firstHour = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= RthStartTime &&
            b.Timestamp.TimeOfDay <  RthStartTime + TimeSpan.FromHours(1)).ToList();
        var lastHour = ordered.Where(b => 
            b.Timestamp.TimeOfDay >= RthEndTime - TimeSpan.FromHours(1) &&
            b.Timestamp.TimeOfDay <  RthEndTime).ToList();

        var sessionLow = ordered.Min(b => b.Low);
        var lowBar = ordered.MinBy(b => b.Low)!;
        var lowBarIndex = ordered.IndexOf(lowBar);
        var lowTimeRatio = ordered.Count > 1 ? (double)lowBarIndex / (ordered.Count - 1) : 0.5;

        // How far overnight dropped below the session open (downside measure)
        var overnightLow      = overnight.Any() ? overnight.Min(b => b.Low) : sessionOpen;
        var overnightDropRatio = Operations.SafeDiv(sessionOpen - overnightLow, atr20);

        // Where price closed within the last hour's range (0 = at low, 1 = at high)
        double lastHourClosePosition = 0.5;
        if (lastHour.Any())
        {
            var lastHourHigh = lastHour.Max(b => b.High);
            var lastHourLow = lastHour.Min(b => b.Low);
            var lastHourClose = lastHour.OrderBy(b => b.Timestamp).Last().Close;
            lastHourClosePosition = lastHourHigh == lastHourLow
                ? 0.5
                : (lastHourClose - lastHourLow) / (lastHourHigh - lastHourLow);
        }

        return new IntradayFeatures
        {
            OvernightRange        = overnight.Any()
                ? Operations.SafeDiv(overnight.Max(b => b.High) - overnight.Min(b => b.Low), atr20) : 0,
            OvernightVolumeRatio  = Operations.SafeDiv(overnight.Sum(b => b.Volume), totalVolume),
            OvernightDropRatio    = overnightDropRatio,
            RegularHoursRange     = rth.Any()
                ? Operations.SafeDiv(rth.Max(b => b.High) - rth.Min(b => b.Low), atr20) : 0,
            FirstHourRange        = firstHour.Any()
                ? Operations.SafeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr20) : 0,
            LastHourRange         = lastHour.Any()
                ? Operations.SafeDiv(lastHour.Max(b => b.High) - lastHour.Min(b => b.Low), atr20) : 0,
            LowTimeRatio          = lowTimeRatio,
            PrevOpenToLowRatio    = Operations.SafeDiv(sessionOpen - sessionLow, atr20),
            LastHourClosePosition = lastHourClosePosition,
        };
    }

    private static BarData aggregateSession(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        return new BarData
        {
            Timestamp = ordered.First().Timestamp,
            SessionDate = ordered.First().SessionDate,
            Open = ordered.First().Open,
            Close = ordered.Last().Close,
            High = ordered.Max(b => b.High),
            Low = ordered.Min(b => b.Low),
            Volume = ordered.Sum(b => b.Volume),
        };
    }

    private class IntradayFeatures
    {
        public double OvernightRange { get; set; }
        public double OvernightVolumeRatio { get; set; }
        public double OvernightDropRatio { get; set; }
        public double RegularHoursRange { get; set; }
        public double FirstHourRange { get; set; }
        public double LastHourRange { get; set; }
        public double LowTimeRatio { get; set; }
        public double PrevOpenToLowRatio { get; set; }
        public double LastHourClosePosition { get; set; }
    }
}
