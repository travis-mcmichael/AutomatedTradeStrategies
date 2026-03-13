using AutomatedTrading.Core.Framework;
using AutomatedTrading.Core.Framework.DataLayer;
using AutomatedTrading.Core.Framework.Interfaces;
using AutomatedTrading.Core.Models;

namespace AutomatedTrading.Export.Feature.Exporters.Bar.v20260313;

public class BarExporter
{
    private const int sequenceLength = 100;
    private const int predictionHorizon = 20;
    private const int bufferSize = 30;
    private List<ITradeSpan> bars = new List<ITradeSpan>();

    public async Task GenerateFeature(string outputPath, DateTime start, DateTime end)
    {
        using var writer = new StreamWriter(outputPath);
        var buffer = new List<ITradeSpan>(sequenceLength + predictionHorizon + 1);
        var sampleCount = 0;
        var headerWritten = false;
        var currentStart = start;
        while (currentStart < end)
        {
            var currentEnd = currentStart.AddDays(7);
            if (currentEnd > end)
                currentEnd = end;
            Console.WriteLine($"Loading bars {currentStart} → {currentEnd}");
            using var reader = await SqliteDataLayer.GetBarDataReader(currentStart, currentEnd);
            while (await reader.ReadAsync())
            {
                var x = 0;
                var bar = new BarData
                {
                    Instrument = reader.GetString(x++),
                    Timeframe = reader.GetString(x++),
                    Timestamp = reader.GetDateTime(x++),
                    SessionDate = reader.GetString(x++),
                    Open = reader.GetDouble(x++),
                    High = reader.GetDouble(x++),
                    Low = reader.GetDouble(x++),
                    Close = reader.GetDouble(x++),
                    Volume = reader.GetDouble(x++)
                };
                buffer.Add(bar);
                if (buffer.Count < sequenceLength + predictionHorizon)
                    continue;
                if (!headerWritten)
                {
                    writer.WriteLine("label");
                    headerWritten = true;
                }
                sampleCount++;
                if (buffer.Count > bufferSize)
                    buffer.RemoveAt(0);

                if (sampleCount % 10000 == 0)
                    Console.WriteLine($"Generated {sampleCount} samples...");

                currentStart = currentEnd;
                Console.WriteLine($"Range exported: {start} → {end}");
                Console.WriteLine($"Generated {sampleCount} samples total.");
            }
        }
    }

    class FeatureBar : BarData
    {
        // ===== Previous Day Features =====
        public double PrevDayHigh { get; set; }
        public double PrevDayLow { get; set; }
        public double PrevDayRange { get; set; }
        public double PrevDayBody { get; set; }
        public double PrevDayUpperWick { get; set; }
        public double PrevDayLowerWick { get; set; }
        public double PrevDayClosePosition { get; set; }

        // ===== Gap Features =====
        public double Gap { get; set; }
        public double GapPercent { get; set; }

        // ===== Volatility =====
        public double ATR5 { get; set; }
        public double ATR10 { get; set; }
        public double ATR20 { get; set; }

        // ===== Moving Averages =====
        public double SMA5 { get; set; }
        public double SMA10 { get; set; }
        public double SMA20 { get; set; }

        // ===== Range Compression =====
        public double AvgRange10 { get; set; }
        public double RangeRatio { get; set; }

        // ===== Volume Features =====
        public double VolumeZScore20 { get; set; }
        public double VolumeMA20 { get; set; }

        // ===== Momentum =====
        public double Return1Day { get; set; }
        public double Return3Day { get; set; }
        public double Return5Day { get; set; }

        // ===== Intraday Position =====
        public double MinutesFromOpen { get; set; }
        public double MinutesToClose { get; set; }
        public double IntradayProgress { get; set; }

        // ===== Labels for Training =====
        public double TargetNextDayHigh { get; set; }
        public double TargetNextDayLow { get; set; }
        public double TargetHighTime { get; set; }
        public double TargetLowTime { get; set; }
    }
}