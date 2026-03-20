using AutomatedTrading.Core.Framework.DataLayer.Repositories;
using AutomatedTrading.Core.Framework.Utility;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Export.Feature.Exporters;

public class SessionOpenHighFeatureExporter
{
    // Minimum prior sessions required to compute ATR20, SMA20, RSI14, etc.
    private const int MinLookback = 20;

    // MES session times in CT (timestamps assumed to be in CT)
    // Session opens at 16:00 (5 PM ET = 4 PM CT), closes at 15:00 (4 PM ET = 3 PM CT)
    private static readonly TimeSpan SessionOpenTime = new(16, 0, 0);
    private static readonly TimeSpan RthStartTime   = new( 8, 30, 0); // 9:30 AM ET
    private static readonly TimeSpan RthEndTime     = new(15, 15, 0); // 4:15 PM ET

    private readonly string outputPath;
    private readonly IBarRepository barRepository;

    public SessionOpenHighFeatureExporter(string outputPath, IBarRepository barRepository)
    {
        this.outputPath    = outputPath;
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

        // Session dates are "yyyyMMdd" strings — alphabetical = chronological
        var sessionDates = minuteBarsBySession.Keys.OrderBy(x => x).ToList();

        // Build the daily bar sequence once — each entry is one session aggregated
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
            var prevMinuteBars    = minuteBarsBySession[sessionDates[j]];

            if (!currentMinuteBars.Any() || !prevMinuteBars.Any()) continue;

            try
            {
                var sessionOpen = currentMinuteBars.OrderBy(b => b.Timestamp).First().Open;

                var sessionHigh = currentMinuteBars.Max(b => b.High);

                var atr5      = Computations.ATR(dailyBars, j, 5);
                var atr10     = Computations.ATR(dailyBars, j, 10);
                var atr20     = Computations.ATR(dailyBars, j, 20);
                if (atr20 == 0) continue;

                var sma5      = Computations.SMA(dailyBars, j, 5,  b => b.Close);
                var sma10     = Computations.SMA(dailyBars, j, 10, b => b.Close);
                var sma20     = Computations.SMA(dailyBars, j, 20, b => b.Close);
                var avgRange5 = Computations.AverageRange(dailyBars, j, 5);
                var avgRange10= Computations.AverageRange(dailyBars, j, 10);
                var rsi14     = Computations.RSI(dailyBars, j, 14);
                var current   = dailyBars[j];

                var sessionDate = DateTime.ParseExact(sessionDates[i], "yyyyMMdd", null);
                var intraday    = computeIntradayFeatures(prevMinuteBars, atr20);
                var target      = (sessionHigh - sessionOpen) / atr20;

                var features = new double[]
                {
                    // --- Previous complete session shape ---
                    safeDiv(current.High, sessionOpen),              // PrevHighRatio
                    safeDiv(current.Low,  sessionOpen),              // PrevLowRatio
                    Computations.HighLowRange(current, atr20),       // PrevRange
                    Computations.BodySize(current, atr20),           // PrevBodySize
                    Computations.UpperWick(current, atr20),          // PrevUpperWick
                    Computations.LowerWick(current, atr20),          // PrevLowerWick
                    Computations.ClosePosition(current),             // PrevClosePosition

                    // --- Volatility / trend context ---
                    safeDiv(atr5,  sessionOpen),                     // ATR5
                    safeDiv(atr10, sessionOpen),                     // ATR10
                    safeDiv(atr20, sessionOpen),                     // ATR20
                    avgRange5,                                        // AvgRange5
                    avgRange10,                                       // AvgRange10
                    safeDiv(current.High - current.Low, avgRange10), // RangeRatio
                    Computations.Return(dailyBars, j, 1),            // Return1D
                    Computations.Return(dailyBars, j, 3),            // Return3D
                    Computations.Return(dailyBars, j, 5),            // Return5D
                    safeDiv(sma5,  sma20),                           // SMA5SMA20Ratio
                    safeDiv(sma10, sma20),                           // SMA10SMA20Ratio
                    safeDiv(sessionOpen, sma5),                      // OpenSMA5Ratio
                    safeDiv(sessionOpen, sma10),                     // OpenSMA10Ratio
                    safeDiv(sessionOpen, sma20),                     // OpenSMA20Ratio

                    // --- Volume ---
                    Computations.SMA(dailyBars, j, 20, b => b.Volume), // VolumeMA20
                    Computations.VolumeZScore(dailyBars, j, 20),        // VolumeZScore20

                    // --- Gap / distance from key levels ---
                    safeDiv(sessionOpen - current.High, atr20),      // DistanceFromPrevHigh
                    safeDiv(sessionOpen - current.Low,  atr20),      // DistanceFromPrevLow
                    safeDiv(sessionOpen - current.Close, atr20),     // GapVsATR

                    // --- Regime ---
                    safeDiv(atr10, atr20),                           // VolatilityRegime
                    rsi14,                                            // RSI14

                    // --- Calendar ---
                    (int)sessionDate.DayOfWeek,                      // DayOfWeek
                    sessionDate.Day,                                  // DayOfMonth

                    // --- Previous session intraday structure ---
                    intraday.OvernightRange,                         // OvernightRange
                    intraday.OvernightVolumeRatio,                   // OvernightVolumeRatio
                    intraday.RegularHoursRange,                      // RegularHoursRange
                    intraday.FirstHourRange,                         // FirstHourRange
                    intraday.LastHourRange,                          // LastHourRange
                    intraday.HighTimeRatio,                          // HighTimeRatio
                    intraday.PrevOpenToHighRatio,                    // PrevOpenToHighRatio
                    intraday.PrevOpenToLowRatio,                     // PrevOpenToLowRatio
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
                        "OvernightRange,OvernightVolumeRatio,RegularHoursRange,FirstHourRange,LastHourRange," +
                        "HighTimeRatio,PrevOpenToHighRatio,PrevOpenToLowRatio," +
                        "TargetSessionHigh"
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
        var sessionOpen  = ordered.First().Open;
        var totalVolume  = ordered.Sum(b => b.Volume);

        // Overnight = session open (16:00) through pre-RTH (< 08:30)
        // RTH = 08:30 to 15:15
        var overnight  = ordered.Where(b => b.Timestamp.TimeOfDay >= SessionOpenTime ||
                                            b.Timestamp.TimeOfDay <  RthStartTime).ToList();
        var rth        = ordered.Where(b => b.Timestamp.TimeOfDay >= RthStartTime &&
                                            b.Timestamp.TimeOfDay <  RthEndTime).ToList();
        var firstHour  = ordered.Where(b => b.Timestamp.TimeOfDay >= RthStartTime &&
                                            b.Timestamp.TimeOfDay <  RthStartTime + TimeSpan.FromHours(1)).ToList();
        var lastHour   = ordered.Where(b => b.Timestamp.TimeOfDay >= RthEndTime - TimeSpan.FromHours(1) &&
                                            b.Timestamp.TimeOfDay <  RthEndTime).ToList();

        var sessionHigh    = ordered.Max(b => b.High);
        var sessionLow     = ordered.Min(b => b.Low);
        var highBar        = ordered.MaxBy(b => b.High)!;
        var highBarIndex   = ordered.IndexOf(highBar);
        var highTimeRatio  = ordered.Count > 1 ? (double)highBarIndex / (ordered.Count - 1) : 0.5;

        return new IntradayFeatures
        {
            OvernightRange       = overnight.Any()
                ? safeDiv(overnight.Max(b => b.High) - overnight.Min(b => b.Low), atr20) : 0,
            OvernightVolumeRatio = safeDiv(overnight.Sum(b => b.Volume), totalVolume),
            RegularHoursRange    = rth.Any()
                ? safeDiv(rth.Max(b => b.High) - rth.Min(b => b.Low), atr20) : 0,
            FirstHourRange       = firstHour.Any()
                ? safeDiv(firstHour.Max(b => b.High) - firstHour.Min(b => b.Low), atr20) : 0,
            LastHourRange        = lastHour.Any()
                ? safeDiv(lastHour.Max(b => b.High) - lastHour.Min(b => b.Low), atr20) : 0,
            HighTimeRatio        = highTimeRatio,
            PrevOpenToHighRatio  = safeDiv(sessionHigh - sessionOpen, atr20),
            PrevOpenToLowRatio   = safeDiv(sessionOpen - sessionLow, atr20),
        };
    }

    private static BarData aggregateSession(List<BarData> bars)
    {
        var ordered = bars.OrderBy(b => b.Timestamp).ToList();
        return new BarData
        {
            Timestamp   = ordered.First().Timestamp,
            SessionDate = ordered.First().SessionDate,
            Open        = ordered.First().Open,
            Close       = ordered.Last().Close,
            High        = ordered.Max(b => b.High),
            Low         = ordered.Min(b => b.Low),
            Volume      = ordered.Sum(b => b.Volume),
        };
    }

    private static double safeDiv(double a, double b) => b == 0 ? 0 : a / b;

    private class IntradayFeatures
    {
        public double OvernightRange       { get; set; }
        public double OvernightVolumeRatio { get; set; }
        public double RegularHoursRange    { get; set; }
        public double FirstHourRange       { get; set; }
        public double LastHourRange        { get; set; }
        public double HighTimeRatio        { get; set; }
        public double PrevOpenToHighRatio  { get; set; }
        public double PrevOpenToLowRatio   { get; set; }
    }
}
